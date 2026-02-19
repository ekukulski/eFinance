using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace eFinance.Data.Repositories
{
    public sealed class OpeningBalanceRepository
    {
        private readonly SqliteDatabase _db;

        public OpeningBalanceRepository(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Returns the opening balance for an account (by AccountName).
        /// If none exists, returns 0.
        /// </summary>
        public async Task<decimal> GetOpeningBalanceAsync(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Balance
FROM OpeningBalances
WHERE AccountName = $name
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$name", accountName.Trim());

            var result = await cmd.ExecuteScalarAsync();
            if (result is null || result == DBNull.Value) return 0m;

            // SQLite may return double/string/decimal depending on how you stored it.
            return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
        }
    }
}
