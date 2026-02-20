using System.Globalization;

namespace eFinance.Data.Repositories;

public sealed class OpeningBalanceRepository
{
    private readonly SqliteDatabase _db;

    public OpeningBalanceRepository(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<(DateOnly date, decimal balance)> GetOpeningBalanceInfoAsync(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return (DateOnly.FromDateTime(DateTime.Today), 0m);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        // TRIM + NOCASE prevents “Amex” vs “AMEX ” mismatches
        cmd.CommandText = @"
SELECT BalanceDate, Balance
FROM OpeningBalances
WHERE TRIM(AccountName) = TRIM($name) COLLATE NOCASE
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$name", accountName);

        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (DateOnly.FromDateTime(DateTime.Today), 0m);

        var dateText = r.IsDBNull(0) ? "" : r.GetString(0);
        var balObj = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);

        var date = DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrWhiteSpace(dateText) &&
            DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }

        return (date, (decimal)balObj);
    }

    // Optional: keep your old API for other callers
    public async Task<decimal> GetOpeningBalanceAsync(string accountName)
    {
        var (_, bal) = await GetOpeningBalanceInfoAsync(accountName);
        return bal;
    }
}
