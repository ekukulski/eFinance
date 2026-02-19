using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using eFinance.Data.Models;
using TransactionModel = eFinance.Data.Models.Transaction;


namespace eFinance.Data.Repositories
{
    /// <summary>
    /// Accounts repository matching your current DB schema:
    ///
    /// CREATE TABLE Accounts (
    ///   Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ///   Name TEXT NOT NULL,
    ///   CreatedUtc TEXT NOT NULL
    /// );
    ///
    /// (No AccountType, no IsActive)
    /// </summary>
    public sealed class AccountRepository
    {
        private readonly SqliteDatabase _db;

        public AccountRepository(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // ------------------------------------------------------------
        // INSERT
        // ------------------------------------------------------------
        public async Task<long> InsertAsync(Account account)
        {
            if (account is null) throw new ArgumentNullException(nameof(account));
            if (string.IsNullOrWhiteSpace(account.Name))
                throw new ArgumentException("Account.Name is required.", nameof(account));

            if (account.CreatedUtc == default)
                account.CreatedUtc = DateTime.UtcNow;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT INTO Accounts (Name, CreatedUtc)
VALUES ($name, $createdUtc);
SELECT last_insert_rowid();
";
            cmd.Parameters.AddWithValue("$name", account.Name.Trim());
            cmd.Parameters.AddWithValue("$createdUtc", account.CreatedUtc.ToString("O"));

            var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            account.Id = id;
            return id;
        }

        // ------------------------------------------------------------
        // GET ALL
        // ------------------------------------------------------------
        public async Task<List<Account>> GetAllAsync()
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id, Name, CreatedUtc
FROM Accounts
ORDER BY Name COLLATE NOCASE;
";

            var results = new List<Account>();
            using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
                results.Add(ReadAccount(r));

            return results;
        }

        // ------------------------------------------------------------
        // GET BY ID
        // ------------------------------------------------------------
        public async Task<Account?> GetByIdAsync(long id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id, Name, CreatedUtc
FROM Accounts
WHERE Id = $id
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$id", id);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return null;

            return ReadAccount(r);
        }

        // ------------------------------------------------------------
        // GET BY NAME (case-insensitive)
        // ------------------------------------------------------------
        public async Task<Account?> GetByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id, Name, CreatedUtc
FROM Accounts
WHERE Name = $name COLLATE NOCASE
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$name", name.Trim());

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return null;

            return ReadAccount(r);
        }

        // ------------------------------------------------------------
        // GET ID BY NAME (case-insensitive) - handy for importers
        // ------------------------------------------------------------
        public async Task<long?> GetIdByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id
FROM Accounts
WHERE Name = $name COLLATE NOCASE
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$name", name.Trim());

            var result = await cmd.ExecuteScalarAsync();
            if (result is null || result == DBNull.Value)
                return null;

            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------
        // UPDATE NAME
        // ------------------------------------------------------------
        public async Task<bool> UpdateNameAsync(long accountId, string newName)
        {
            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name is required.", nameof(newName));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Accounts
SET Name = $name
WHERE Id = $id;
SELECT changes();
";
            cmd.Parameters.AddWithValue("$id", accountId);
            cmd.Parameters.AddWithValue("$name", newName.Trim());

            var changed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            return changed > 0;
        }

        // ------------------------------------------------------------
        // DELETE
        // ------------------------------------------------------------
        public async Task<bool> DeleteByIdAsync(long id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
DELETE FROM Accounts
WHERE Id = $id;
SELECT changes();
";
            cmd.Parameters.AddWithValue("$id", id);

            var changed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            return changed > 0;
        }

        // ------------------------------------------------------------
        // SEED DEFAULTS
        // ------------------------------------------------------------
        public async Task SeedDefaultsIfEmptyAsync()
        {
            using var conn = _db.OpenConnection();

            // 1) Is empty?
            using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM Accounts;";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
                if (count > 0) return;
            }

            // 2) Seed
            var defaults = new[]
            {
                "AMEX",
                "Visa",
                "Mastercard",
                "Checking"
            };

            using var tx = conn.BeginTransaction();
            var now = DateTime.UtcNow.ToString("O");

            foreach (var name in defaults)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                cmd.CommandText = @"
INSERT INTO Accounts (Name, CreatedUtc)
VALUES ($name, $utc);
";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$utc", now);

                await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static Account ReadAccount(SqliteDataReader r)
        {
            // Column order: Id, Name, CreatedUtc
            return new Account
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                CreatedUtc = DateTime.Parse(r.GetString(2), null, DateTimeStyles.RoundtripKind)
            };
        }
    }
}
