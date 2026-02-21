using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace eFinance.Data;

public sealed class SqliteDatabase
{
    private readonly string _dbPath;

    public SqliteDatabase(string dbPath)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
    }

    // Used by other parts of your app
    public string DatabasePath => _dbPath;

    public static string DefaultDbPath(string appName)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);

        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "eFinance.db");
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    public async Task InitializeAsync()
    {
        using var conn = OpenConnection();

        // ------------------------------------------------------------
        // Base schema
        // ------------------------------------------------------------
        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS Accounts (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT NOT NULL,
    CreatedUtc      TEXT NOT NULL,
    AccountType     TEXT NOT NULL DEFAULT 'Checking',
    IsActive        INTEGER NOT NULL DEFAULT 1
);");

        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS Transactions (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId            INTEGER NOT NULL,
    PostedDate           TEXT NOT NULL,
    Description          TEXT NOT NULL,
    Amount               REAL NOT NULL,
    Category             TEXT NULL,
    CategoryId           INTEGER NULL,
    Memo                 TEXT NULL,
    FitId                TEXT NULL,
    Source               TEXT NULL,
    CreatedUtc           TEXT NOT NULL,
    IsDeleted            INTEGER NOT NULL DEFAULT 0,
    DeletedUtc           TEXT NULL,
    MatchedRuleId        INTEGER NULL,
    MatchedRulePattern   TEXT NULL,
    CategorizedUtc       TEXT NULL
);");

        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS OpeningBalances (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountName TEXT NOT NULL,
    BalanceDate TEXT NOT NULL,
    Balance     REAL NOT NULL,
    CreatedUtc  TEXT NOT NULL
);");

        // ------------------------------------------------------------
        // CategoryRules
        // NOTE: Keep ONLY if this is truly your schema/table.
        // If your project already created this table elsewhere, you can remove this block safely.
        // ------------------------------------------------------------
        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS CategoryRules (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    DescriptionPattern   TEXT NOT NULL,
    CategoryId           INTEGER NOT NULL,
    MatchType            TEXT NOT NULL DEFAULT 'Contains',
    Priority             INTEGER NOT NULL DEFAULT 0,
    IsEnabled            INTEGER NOT NULL DEFAULT 1
);");

        // ------------------------------------------------------------
        // Migrations / upgrades (Option A)
        // ------------------------------------------------------------

        // 1) Add AccountId column if missing (nullable is safest for existing DBs)
        await EnsureColumnAsync(conn, "OpeningBalances", "AccountId", "INTEGER NULL");

        // 2) Backfill AccountId from AccountName (only for rows missing it)
        var updated = await BackfillOpeningBalanceAccountIdsAsync(conn);

        // 3) Index for fast lookups
        await EnsureIndexAsync(conn, "IX_OpeningBalances_AccountId", "OpeningBalances(AccountId)");

        System.Diagnostics.Debug.WriteLine(
            $"DB init complete. OpeningBalances.AccountId backfilled rows={updated}");
    }

    // ------------------------------------------------------------
    // CategoryRules API used by CategorizationService
    // ------------------------------------------------------------
    public async Task<List<CategoryRuleRow>> GetCategoryRulesAsync(bool enabledOnly)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = enabledOnly
            ? @"SELECT Id, DescriptionPattern, CategoryId, MatchType, Priority, IsEnabled
                FROM CategoryRules
                WHERE IsEnabled = 1
                ORDER BY Priority DESC, Id ASC;"
            : @"SELECT Id, DescriptionPattern, CategoryId, MatchType, Priority, IsEnabled
                FROM CategoryRules
                ORDER BY Priority DESC, Id ASC;";

        var list = new List<CategoryRuleRow>();

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new CategoryRuleRow
            {
                Id = r.GetInt32(0),
                DescriptionPattern = r.IsDBNull(1) ? "" : r.GetString(1),
                CategoryId = r.GetInt32(2),
                MatchType = r.IsDBNull(3) ? "Contains" : r.GetString(3),
                Priority = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                IsEnabled = r.IsDBNull(5) ? 1 : r.GetInt32(5),
            });
        }

        return list;
    }
    public sealed class CategoryRuleRow
    {
        public int Id { get; set; }
        public string DescriptionPattern { get; set; } = "";
        public int CategoryId { get; set; }
        public string MatchType { get; set; } = "Contains";
        public int Priority { get; set; }
        public int IsEnabled { get; set; }
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureColumnAsync(SqliteConnection conn, string table, string column, string definition)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var add = conn.CreateCommand();
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await add.ExecuteNonQueryAsync();
    }

    private static async Task EnsureIndexAsync(SqliteConnection conn, string indexName, string onClause)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {indexName} ON {onClause};";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Returns number of rows updated.
    /// </summary>
    private static async Task<int> BackfillOpeningBalanceAccountIdsAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE OpeningBalances
SET AccountId = (
    SELECT a.Id
    FROM Accounts a
    WHERE TRIM(a.Name) = TRIM(OpeningBalances.AccountName) COLLATE NOCASE
    LIMIT 1
)
WHERE AccountId IS NULL
  AND AccountName IS NOT NULL
  AND TRIM(AccountName) <> '';
";
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows;
    }
}