using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace eFinance.Data
{
    /// <summary>
    /// Owns the SQLite file, opens connections, and ensures schema exists.
    /// Keep all schema creation/migrations here so it’s easy to audit.
    /// </summary>
    public sealed class SqliteDatabase
    {
        private readonly string _dbPath;

        public SqliteDatabase(string dbPath)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        }

        public string DatabasePath => _dbPath;

        public SqliteConnection OpenConnection()
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            var conn = new SqliteConnection(cs);
            conn.Open();

            // Good defaults for finance data: integrity + better concurrency.
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
";
                pragma.ExecuteNonQuery();
            }

            return conn;
        }

        public async Task InitializeAsync()
        {
            EnsureFolderExists(_dbPath);

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();

            // ---- Base schema (create tables if missing) ----
            cmd.CommandText = @"
-- ------------------------------------------------------------
-- Accounts
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Accounts (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT NOT NULL,
    AccountType     TEXT NOT NULL,          -- e.g., 'CreditCard', 'Checking'
    IsActive        INTEGER NOT NULL DEFAULT 1,
    CreatedUtc      TEXT NOT NULL
);

-- ------------------------------------------------------------
-- Transactions
-- NOTE: We keep your original Category TEXT column for compatibility,
-- but going forward you should use CategoryId (FK to Categories).
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Transactions (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId       INTEGER NOT NULL,
    PostedDate      TEXT NOT NULL,          -- ISO-8601 date string: yyyy-MM-dd
    Description     TEXT NOT NULL,
    Amount          REAL NOT NULL,          -- positive/negative per your convention
    Category        TEXT NULL,              -- legacy/temporary
    FitId           TEXT NULL,              -- import unique id, optional
    Source          TEXT NULL,              -- e.g., 'AMEX'
    CreatedUtc      TEXT NOT NULL,

    FOREIGN KEY(AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Transactions_AccountId_PostedDate
ON Transactions(AccountId, PostedDate);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Transactions_AccountId_FitId
ON Transactions(AccountId, FitId)
WHERE FitId IS NOT NULL AND FitId <> '';

-- ------------------------------------------------------------
-- Opening Balances
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS OpeningBalances (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountName   TEXT NOT NULL,        -- e.g. 'Amex'
    BalanceDate   TEXT NOT NULL,        -- yyyy-MM-dd
    Balance       REAL NOT NULL,
    CreatedUtc    TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_OpeningBalances_AccountName
ON OpeningBalances(AccountName);

-- ------------------------------------------------------------
-- Categories (master list)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Categories (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT NOT NULL,
    IsActive        INTEGER NOT NULL DEFAULT 1,
    CreatedUtc      TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    UpdatedUtc      TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Categories_Name
ON Categories(Name);

-- ------------------------------------------------------------
-- CategoryRules (auto-categorization)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryRules (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    DescriptionPattern   TEXT NOT NULL,
    CategoryId           INTEGER NOT NULL,
    MatchType            TEXT NOT NULL DEFAULT 'Contains', -- Exact / StartsWith / Contains
    Priority             INTEGER NOT NULL DEFAULT 100,
    IsEnabled            INTEGER NOT NULL DEFAULT 1,
    Notes                TEXT NULL,
    CreatedUtc           TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    UpdatedUtc           TEXT NULL,

    FOREIGN KEY(CategoryId) REFERENCES Categories(Id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_CategoryRules_Enabled_Priority
ON CategoryRules(IsEnabled, Priority);

CREATE INDEX IF NOT EXISTS IX_CategoryRules_CategoryId
ON CategoryRules(CategoryId);
";
            await cmd.ExecuteNonQueryAsync();

            // ---- Migrations / upgrades for existing DBs ----
            // Add new columns to Transactions if missing.
            await EnsureColumnAsync(conn, "Transactions", "CategoryId", "INTEGER NULL");
            await EnsureColumnAsync(conn, "Transactions", "MatchedRuleId", "INTEGER NULL");
            await EnsureColumnAsync(conn, "Transactions", "MatchedRulePattern", "TEXT NULL");
            await EnsureColumnAsync(conn, "Transactions", "CategorizedUtc", "TEXT NULL");

            // Helpful indexes for the new columns
            await ExecuteNonQueryAsync(conn, @"
CREATE INDEX IF NOT EXISTS IX_Transactions_CategoryId
ON Transactions(CategoryId);

CREATE INDEX IF NOT EXISTS IX_Transactions_MatchedRuleId
ON Transactions(MatchedRuleId);
");

            // Optional: prevent exact duplicates in rules (pattern + category + matchtype)
            // Keep this if you want the DB to enforce cleanliness.
            await ExecuteNonQueryAsync(conn, @"
CREATE UNIQUE INDEX IF NOT EXISTS UX_CategoryRules_UniqueRule
ON CategoryRules(DescriptionPattern, CategoryId, MatchType);
");
        }

        // -----------------------------
        // Data access: Category rules
        // -----------------------------
        public async Task<List<CategoryRuleRecord>> GetCategoryRulesAsync(bool enabledOnly = true)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = enabledOnly
                ? @"SELECT Id, DescriptionPattern, CategoryId, MatchType, Priority, IsEnabled
                    FROM CategoryRules
                    WHERE IsEnabled = 1
                    ORDER BY Priority DESC, LENGTH(DescriptionPattern) DESC;"
                : @"SELECT Id, DescriptionPattern, CategoryId, MatchType, Priority, IsEnabled
                    FROM CategoryRules
                    ORDER BY Priority DESC, LENGTH(DescriptionPattern) DESC;";

            var list = new List<CategoryRuleRecord>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new CategoryRuleRecord
                {
                    Id = reader.GetInt32(0),
                    DescriptionPattern = reader.GetString(1),
                    CategoryId = reader.GetInt32(2),
                    MatchType = reader.GetString(3),
                    Priority = reader.GetInt32(4),
                    IsEnabled = reader.GetInt32(5),
                });
            }

            return list;
        }

        // -----------------------------
        // Helpers / migrations
        // -----------------------------
        private static async Task EnsureColumnAsync(SqliteConnection conn, string table, string column, string ddlType)
        {
            if (await ColumnExistsAsync(conn, table, column))
                return;

            // SQLite doesn't support ADD COLUMN IF NOT EXISTS in older versions,
            // so we check first then ALTER.
            await ExecuteNonQueryAsync(conn, $"ALTER TABLE {table} ADD COLUMN {column} {ddlType};");
        }

        private static async Task<bool> ColumnExistsAsync(SqliteConnection conn, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var colName = reader.GetString(1); // PRAGMA table_info: name at index 1
                if (string.Equals(colName, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        private static void EnsureFolderExists(string dbPath)
        {
            var folder = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        public static string DefaultDbPath(string appName = "eFinance")
        {
            // Windows-friendly location: %LocalAppData%\eFinance\eFinance.db
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, appName, $"{appName}.db");
        }
    }

    // Simple DTO for reading rules from SQLite (used by CategorizationService)
    public sealed class CategoryRuleRecord
    {
        public int Id { get; set; }
        public string DescriptionPattern { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string MatchType { get; set; } = "Contains";
        public int Priority { get; set; } = 100;
        public int IsEnabled { get; set; } = 1;
    }
}
