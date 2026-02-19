using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using eFinance.Data.Models;
using TransactionModel = eFinance.Data.Models.Transaction;


namespace eFinance.Data.Repositories
{
    public sealed partial class TransactionRepository
    {
        // Pull minimal data for audit (faster than full join)
        public async Task<List<Transaction>> GetByAccountForAuditAsync(long accountId, DateOnly start)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT
  Id,
  AccountId,
  PostedDate,
  Description,
  Amount,
  FitId
FROM Transactions
WHERE AccountId = $accountId
  AND PostedDate >= $start
ORDER BY PostedDate ASC, Id ASC;
";
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));

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
                    FitId = r.IsDBNull(5) ? null : r.GetString(5),

                    // not needed for audit, but keep safe defaults
                    CreatedUtc = DateTime.UtcNow
                });
            }

            return results;
        }
    }
}
