using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using eFinance.Data.Models;

namespace eFinance.Data.Repositories
{
    public sealed class TransactionRepository
    {
        private readonly SqliteDatabase _db;

        public TransactionRepository(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // ------------------------------------------------------------
        // STANDARD INSERT (will throw on unique constraint violation)
        // ------------------------------------------------------------
        public async Task<long> InsertAsync(Transaction t)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT INTO Transactions
(AccountId, PostedDate, Description, Amount, Category, CategoryId, MatchedRuleId, MatchedRulePattern, CategorizedUtc, FitId, Source, CreatedUtc)
VALUES
($accountId, $postedDate, $desc, $amount, $cat, $categoryId, $matchedRuleId, $matchedRulePattern, $categorizedUtc, $fitId, $source, $createdUtc);
SELECT last_insert_rowid();
";

            BindParameters(cmd, t);

            var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            t.Id = id;
            return id;
        }

        // ------------------------------------------------------------
        // INSERT OR IGNORE (Safe for Import Pipeline)
        // Returns true if inserted, false if ignored (duplicate FitId)
        // ------------------------------------------------------------
        public async Task<bool> InsertOrIgnoreByFitIdAsync(Transaction t)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT OR IGNORE INTO Transactions
(AccountId, PostedDate, Description, Amount, Category, CategoryId, MatchedRuleId, MatchedRulePattern, CategorizedUtc, FitId, Source, CreatedUtc)
VALUES
($accountId, $postedDate, $desc, $amount, $cat, $categoryId, $matchedRuleId, $matchedRulePattern, $categorizedUtc, $fitId, $source, $createdUtc);
SELECT changes();
";

            BindParameters(cmd, t);

            var changed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            if (changed > 0)
            {
                using var idCmd = conn.CreateCommand();
                idCmd.CommandText = "SELECT last_insert_rowid();";
                t.Id = (long)(await idCmd.ExecuteScalarAsync() ?? 0L);
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------
        // QUERY
        // ------------------------------------------------------------
        public async Task<List<Transaction>> GetByAccountAsync(
            long accountId,
            DateOnly? start = null,
            DateOnly? end = null)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            var where = "AccountId = $accountId";
            cmd.Parameters.AddWithValue("$accountId", accountId);

            if (start is not null)
            {
                where += " AND PostedDate >= $start";
                cmd.Parameters.AddWithValue("$start", start.Value.ToString("yyyy-MM-dd"));
            }

            if (end is not null)
            {
                where += " AND PostedDate <= $end";
                cmd.Parameters.AddWithValue("$end", end.Value.ToString("yyyy-MM-dd"));
            }

            cmd.CommandText = $@"
SELECT
  Id,
  AccountId,
  PostedDate,
  Description,
  Amount,
  Category,
  CategoryId,
  MatchedRuleId,
  MatchedRulePattern,
  CategorizedUtc,
  FitId,
  Source,
  CreatedUtc
FROM Transactions
WHERE {where}
ORDER BY PostedDate DESC, Id DESC;
";

            var results = new List<Transaction>();

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                results.Add(new Transaction
                {
                    Id = r.GetInt64(0),
                    AccountId = r.GetInt64(1),
                    PostedDate = DateOnly.Parse(r.GetString(2)),
                    Description = r.GetString(3),
                    Amount = (decimal)r.GetDouble(4),

                    Category = r.IsDBNull(5) ? null : r.GetString(5),
                    CategoryId = r.IsDBNull(6) ? null : r.GetInt64(6),

                    MatchedRuleId = r.IsDBNull(7) ? null : r.GetInt64(7),
                    MatchedRulePattern = r.IsDBNull(8) ? null : r.GetString(8),

                    CategorizedUtc = r.IsDBNull(9)
                        ? null
                        : DateTime.Parse(r.GetString(9), null, DateTimeStyles.RoundtripKind),

                    FitId = r.IsDBNull(10) ? null : r.GetString(10),
                    Source = r.IsDBNull(11) ? null : r.GetString(11),

                    CreatedUtc = DateTime.Parse(r.GetString(12), null, DateTimeStyles.RoundtripKind)
                });
            }

            return results;
        }

        private static void BindParameters(SqliteCommand cmd, Transaction t)
        {
            cmd.Parameters.AddWithValue("$accountId", t.AccountId);
            cmd.Parameters.AddWithValue("$postedDate", t.PostedDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$desc", t.Description);
            cmd.Parameters.AddWithValue("$amount", (double)t.Amount);

            cmd.Parameters.AddWithValue("$cat", (object?)t.Category ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$categoryId", (object?)t.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$matchedRuleId", (object?)t.MatchedRuleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$matchedRulePattern", (object?)t.MatchedRulePattern ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$categorizedUtc",
                t.CategorizedUtc is null ? DBNull.Value : t.CategorizedUtc.Value.ToString("O"));

            cmd.Parameters.AddWithValue("$fitId", (object?)t.FitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$source", (object?)t.Source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$createdUtc", t.CreatedUtc.ToString("O"));
        }
    }
}
