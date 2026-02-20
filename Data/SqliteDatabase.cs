using Microsoft.Data.Sqlite;

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
    CreatedUtc      TEXT NOT NULL,
    AccountType     TEXT NOT NULL DEFAULT 'Checking',
    IsActive        INTEGER NOT NULL DEFAULT 1
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
    Memo            TEXT NULL,
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

            // ------------------------------------------------------------
            // Duplicate audit ignores (persist "Accept (not a duplicate)")
            // ------------------------------------------------------------
            await ExecuteNonQueryAsync(conn, @"
CREATE TABLE IF NOT EXISTS DuplicateAuditIgnores (
    A_TransactionId INTEGER NOT NULL,
    B_TransactionId INTEGER NOT NULL,
    Reason          TEXT NULL,
    CreatedUtc      TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),

    PRIMARY KEY (A_TransactionId, B_TransactionId)
);

CREATE INDEX IF NOT EXISTS IX_DuplicateAuditIgnores_A
ON DuplicateAuditIgnores(A_TransactionId);

CREATE INDEX IF NOT EXISTS IX_DuplicateAuditIgnores_B
ON DuplicateAuditIgnores(B_TransactionId);
");

            // ---- Migrations / upgrades for existing DBs ----
            // Add new columns to Transactions if missing.
            await EnsureColumnAsync(conn, "Transactions", "CategoryId", "INTEGER NULL");
            await EnsureColumnAsync(conn, "Transactions", "MatchedRuleId", "INTEGER NULL");
            await EnsureColumnAsync(conn, "Transactions", "MatchedRulePattern", "TEXT NULL");
            await EnsureColumnAsync(conn, "Transactions", "CategorizedUtc", "TEXT NULL");
            await EnsureColumnAsync(conn, "Transactions", "Memo", "TEXT NULL");
            await EnsureColumnAsync(conn, "Transactions", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, "Accounts", "AccountType", "TEXT NOT NULL DEFAULT 'Checking'");
            await EnsureColumnAsync(conn, "Accounts", "IsActive", "INTEGER NOT NULL DEFAULT 1");

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

            // ---- One-time seeding / migrations from legacy CSV files ----
            await SeedCategoriesFromLegacyCsvIfNeededAsync(conn);
            await BackfillTransactionCategoryIdsFromLegacyCategoryTextAsync(conn);
        }

        private static async Task SeedCategoriesFromLegacyCsvIfNeededAsync(SqliteConnection conn)
        {
            // If Categories already has data, don't touch it.
            using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM Categories;";
                var count = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);
                if (count > 0)
                    return;
            }

            // Attempt to load legacy CategoryList.csv into Categories.
            var legacyPath = FilePathHelper.GeteFinancePath("CategoryList.csv");
            if (!File.Exists(legacyPath))
                return;

            var lines = File.ReadAllLines(legacyPath)
                .Select(l => (l ?? string.Empty).Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l)
                .ToList();

            if (lines.Count == 0)
                return;

            using var tx = conn.BeginTransaction();
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"
INSERT OR IGNORE INTO Categories (Name, IsActive, CreatedUtc)
VALUES ($name, 1, $utc);
";
            var pName = insert.CreateParameter(); pName.ParameterName = "$name"; insert.Parameters.Add(pName);
            var pUtc = insert.CreateParameter(); pUtc.ParameterName = "$utc"; insert.Parameters.Add(pUtc);

            foreach (var name in lines)
            {
                pName.Value = name;
                pUtc.Value = DateTime.UtcNow.ToString("O");
                await insert.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        private static async Task BackfillTransactionCategoryIdsFromLegacyCategoryTextAsync(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Transactions
SET CategoryId = (
    SELECT c.Id
    FROM Categories c
    WHERE LOWER(TRIM(c.Name)) = LOWER(TRIM(Transactions.Category))
    LIMIT 1
)
WHERE CategoryId IS NULL
  AND Category IS NOT NULL
  AND TRIM(Category) <> ''
  AND EXISTS (
    SELECT 1
    FROM Categories c
    WHERE LOWER(TRIM(c.Name)) = LOWER(TRIM(Transactions.Category))
  );
";
            await cmd.ExecuteNonQueryAsync();
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
        private static async Task EnsureColumnAsync(SqliteConnection conn, string table, string column, string definition)
        {
            // Check existing columns
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";

            var exists = false;
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(reader.GetOrdinal("name"));
                    if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists) return;

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync();
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
