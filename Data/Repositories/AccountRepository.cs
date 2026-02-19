using System.Globalization;
using eFinance.Data.Models;
using Microsoft.Data.Sqlite;

namespace eFinance.Data.Repositories
{
    /// <summary>
    /// Accounts repository matching the NEW DB schema:
    ///
    /// CREATE TABLE Accounts (
    ///   Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ///   Name TEXT NOT NULL,
    ///   CreatedUtc TEXT NOT NULL,
    ///   AccountType TEXT NOT NULL DEFAULT 'Checking',
    ///   IsActive INTEGER NOT NULL DEFAULT 1
    /// );
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

            // Defaults
            account.AccountType = string.IsNullOrWhiteSpace(account.AccountType) ? "Checking" : account.AccountType.Trim();

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT INTO Accounts (Name, CreatedUtc, AccountType, IsActive)
VALUES ($name, $createdUtc, $type, $active);
SELECT last_insert_rowid();
";
            cmd.Parameters.AddWithValue("$name", account.Name.Trim());
            cmd.Parameters.AddWithValue("$createdUtc", account.CreatedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$type", account.AccountType);
            cmd.Parameters.AddWithValue("$active", account.IsActive ? 1 : 0);

            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
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
SELECT Id, Name, CreatedUtc, AccountType, IsActive
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
        // GET ALL ACTIVE
        // ------------------------------------------------------------
        public async Task<List<Account>> GetAllActiveAsync()
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id, Name, CreatedUtc, AccountType, IsActive
FROM Accounts
WHERE IsActive = 1
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
SELECT Id, Name, CreatedUtc, AccountType, IsActive
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
SELECT Id, Name, CreatedUtc, AccountType, IsActive
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
        // UPDATE (Full)
        // ------------------------------------------------------------
        public async Task<bool> UpdateAsync(Account account)
        {
            if (account is null) throw new ArgumentNullException(nameof(account));
            if (account.Id <= 0) throw new ArgumentOutOfRangeException(nameof(account.Id));
            if (string.IsNullOrWhiteSpace(account.Name))
                throw new ArgumentException("Account.Name is required.", nameof(account));

            var name = account.Name.Trim();
            var type = string.IsNullOrWhiteSpace(account.AccountType) ? "Checking" : account.AccountType.Trim();
            var active = account.IsActive ? 1 : 0;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Accounts
SET Name = $name,
    AccountType = $type,
    IsActive = $active
WHERE Id = $id;
SELECT changes();
";
            cmd.Parameters.AddWithValue("$id", account.Id);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$active", active);

            var changed = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
            return changed > 0;
        }

        // ------------------------------------------------------------
        // UPDATE NAME (kept for compatibility)
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

            var changed = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
            return changed > 0;
        }

        // ------------------------------------------------------------
        // SOFT DELETE (recommended): IsActive = 0
        // ------------------------------------------------------------
        public async Task<bool> DeactivateByIdAsync(long id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Accounts
SET IsActive = 0
WHERE Id = $id;
SELECT changes();
";
            cmd.Parameters.AddWithValue("$id", id);

            var changed = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
            return changed > 0;
        }

        // ------------------------------------------------------------
        // HARD DELETE (kept for compatibility)
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

            var changed = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
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
                new Account { Name = "AMEX",       AccountType = "CreditCard", IsActive = true },
                new Account { Name = "Visa",       AccountType = "CreditCard", IsActive = true },
                new Account { Name = "Mastercard", AccountType = "CreditCard", IsActive = true },
                new Account { Name = "Checking",   AccountType = "Checking",   IsActive = true }
            };

            using var tx = conn.BeginTransaction();
            var now = DateTime.UtcNow.ToString("O");

            foreach (var a in defaults)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                cmd.CommandText = @"
INSERT INTO Accounts (Name, CreatedUtc, AccountType, IsActive)
VALUES ($name, $utc, $type, $active);
";
                cmd.Parameters.AddWithValue("$name", a.Name);
                cmd.Parameters.AddWithValue("$utc", now);
                cmd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(a.AccountType) ? "Checking" : a.AccountType.Trim());
                cmd.Parameters.AddWithValue("$active", a.IsActive ? 1 : 0);

                await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static Account ReadAccount(SqliteDataReader r)
        {
            // Using ordinals by name is safer if column order changes.
            var id = r.GetInt64(r.GetOrdinal("Id"));
            var name = r.GetString(r.GetOrdinal("Name"));
            var createdUtc = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedUtc")), null, DateTimeStyles.RoundtripKind);

            var typeOrdinal = r.GetOrdinal("AccountType");
            var activeOrdinal = r.GetOrdinal("IsActive");

            var accountType = r.IsDBNull(typeOrdinal) ? "Checking" : r.GetString(typeOrdinal);
            var isActive = !r.IsDBNull(activeOrdinal) && r.GetInt64(activeOrdinal) == 1;

            return new Account
            {
                Id = id,
                Name = name,
                CreatedUtc = createdUtc,
                AccountType = accountType,
                IsActive = isActive
            };
        }
    }
}
