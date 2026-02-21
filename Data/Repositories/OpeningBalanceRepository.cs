using System;
using System.Globalization;
using System.Threading.Tasks;
using eFinance.Data;
using Microsoft.Data.Sqlite;

namespace eFinance.Data.Repositories;

public sealed class OpeningBalanceRepository
{
    private readonly SqliteDatabase _db;

    public OpeningBalanceRepository(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Preferred: lookup by AccountId (stable).
    /// If not found and fallbackAccountName is provided, falls back to name lookup for legacy rows.
    /// </summary>
    public async Task<(DateOnly date, decimal balance)> GetOpeningBalanceInfoAsync(long accountId, string? fallbackAccountName = null)
    {
        if (accountId <= 0)
            return (DateOnly.FromDateTime(DateTime.Today), 0m);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT BalanceDate, Balance
FROM OpeningBalances
WHERE AccountId = $id
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$id", accountId);

        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return ReadDateBalance(r);
        }

        // Temporary fallback while legacy rows are being cleaned up / mapped
        if (!string.IsNullOrWhiteSpace(fallbackAccountName))
            return await GetOpeningBalanceInfoByNameAsync(fallbackAccountName);

        return (DateOnly.FromDateTime(DateTime.Today), 0m);
    }

    /// <summary>
    /// Legacy: lookup by AccountName (fragile). Kept for backward compatibility and fallback.
    /// </summary>
    public async Task<(DateOnly date, decimal balance)> GetOpeningBalanceInfoByNameAsync(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return (DateOnly.FromDateTime(DateTime.Today), 0m);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT BalanceDate, Balance
FROM OpeningBalances
WHERE TRIM(AccountName) = TRIM($name) COLLATE NOCASE
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$name", accountName.Trim());

        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (DateOnly.FromDateTime(DateTime.Today), 0m);

        return ReadDateBalance(r);
    }
    public async Task<Dictionary<long, decimal>> GetOpeningByAccountIdsAsync(IEnumerable<long> accountIds)
    {
        if (accountIds is null)
            throw new ArgumentNullException(nameof(accountIds));

        var ids = accountIds
            .Distinct()
            .Where(id => id > 0)
            .ToArray();

        var result = ids.ToDictionary(id => id, _ => 0m);

        if (ids.Length == 0)
            return result;

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        var paramNames = new string[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            paramNames[i] = $"$p{i}";
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);
        }

        cmd.CommandText = $@"
SELECT AccountId, Amount
FROM OpeningBalances
WHERE AccountId IN ({string.Join(",", paramNames)});
";

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var accountId = r.GetInt64(0);

            // Your OpeningBalances table likely stores REAL
            var amount = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1);

            result[accountId] = amount;
        }

        return result;
    }
    /// <summary>
    /// Convenience: balance only, by AccountId.
    /// </summary>
    public async Task<decimal> GetOpeningBalanceAsync(long accountId, string? fallbackAccountName = null)
    {
        var (_, bal) = await GetOpeningBalanceInfoAsync(accountId, fallbackAccountName);
        return bal;
    }

    public async Task<Dictionary<long, decimal>> GetSumByAccountIdsAsync(IEnumerable<long> accountIds)
    {
        if (accountIds is null)
            throw new ArgumentNullException(nameof(accountIds));

        var ids = accountIds
            .Distinct()
            .Where(id => id > 0)
            .ToArray();

        var result = ids.ToDictionary(id => id, _ => 0m);

        if (ids.Length == 0)
            return result;

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        var paramNames = new string[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            paramNames[i] = $"$p{i}";
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);
        }

        cmd.CommandText = $@"
SELECT AccountId, COALESCE(SUM(Amount), 0)
FROM Transactions
WHERE AccountId IN ({string.Join(",", paramNames)})
  AND IsDeleted = 0
GROUP BY AccountId;
";

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var accountId = r.GetInt64(0);
            var sum = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1);
            result[accountId] = sum;
        }

        return result;
    }
    private static (DateOnly date, decimal balance) ReadDateBalance(Microsoft.Data.Sqlite.SqliteDataReader r)
    {
        var dateText = r.IsDBNull(0) ? null : r.GetString(0);

        // Your schema stores Balance as REAL => GetDouble
        var balanceDouble = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
        var balance = Convert.ToDecimal(balanceDouble);

        var date = DateOnly.FromDateTime(DateTime.Today);

        if (!string.IsNullOrWhiteSpace(dateText))
        {
            if (DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExact))
                date = parsedExact;
            else if (DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedAny))
                date = parsedAny;
        }

        return (date, balance);
    }
}