using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using eFinance.Data.Models;

namespace eFinance.Data.Repositories
{
    public sealed class AccountRepository
    {
        private readonly SqliteDatabase _db;

        public AccountRepository(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<long> InsertAsync(Account account)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Accounts (Name, AccountType, IsActive, CreatedUtc)
VALUES ($name, $type, $active, $createdUtc);
SELECT last_insert_rowid();
";
            cmd.Parameters.AddWithValue("$name", account.Name);
            cmd.Parameters.AddWithValue("$type", account.AccountType);
            cmd.Parameters.AddWithValue("$active", account.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$createdUtc", account.CreatedUtc.ToString("O"));

            var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            account.Id = id;
            return id;
        }

        public async Task<List<Account>> GetAllAsync(bool includeInactive = false)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = includeInactive
                ? "SELECT Id, Name, AccountType, IsActive, CreatedUtc FROM Accounts ORDER BY Name;"
                : "SELECT Id, Name, AccountType, IsActive, CreatedUtc FROM Accounts WHERE IsActive = 1 ORDER BY Name;";

            var results = new List<Account>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                results.Add(new Account
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    AccountType = r.GetString(2),
                    IsActive = r.GetInt64(3) == 1,
                    CreatedUtc = DateTime.Parse(
                        r.GetString(4),
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind)
                });
            }
            return results;
        }

        // ------------------------------------------------------------
        // NEW METHOD (used by Importers to lookup AccountId by name)
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
WHERE LOWER(Name) = LOWER($name)
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$name", name.Trim());

            var result = await cmd.ExecuteScalarAsync();
            if (result is null || result == DBNull.Value)
                return null;

            return Convert.ToInt64(result);
        }

        public async Task SeedDefaultsIfEmptyAsync()
        {
            using var conn = _db.OpenConnection();

            // Check count
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM Accounts;";
                var count = (long)(await check.ExecuteScalarAsync() ?? 0L);
                if (count > 0) return;
            }

            var now = DateTime.UtcNow;

            using var tx = conn.BeginTransaction();

            async Task InsertSeed(string name, string type)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO Accounts (Name, AccountType, IsActive, CreatedUtc)
VALUES ($name, $type, 1, $createdUtc);
";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$type", type);
                cmd.Parameters.AddWithValue("$createdUtc", now.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }

            await InsertSeed("AMEX", "CreditCard");
            await InsertSeed("Checking", "Checking");

            await tx.CommitAsync();
        }
    }
}
