using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Data.Models;
using Microsoft.Data.Sqlite;

namespace eFinance.Data.Repositories
{
    /// <summary>
    /// Account-agnostic transaction repository.
    /// - Register UI always queries by AccountId.
    /// - Importers parse bank-specific formats into Transaction objects
    ///   and then call InsertTransactionsAsync(accountId, txns).
    ///
    /// NOTE:
    /// INSERT OR IGNORE requires a UNIQUE constraint to be meaningful.
    /// Recommended: UNIQUE(AccountId, FitId) where FitId is present.
    ///
    /// Soft delete:
    /// - Transactions have IsDeleted INTEGER NOT NULL DEFAULT 0
    /// - SoftDeleteAsync sets IsDeleted = 1
    /// - GetTransactionsAsync excludes deleted rows by default
    /// - GetDeletedTransactionsAsync returns only deleted rows
    /// - RestoreAsync / RestoreAllForAccountAsync revert IsDeleted to 0
    /// </summary>
    public sealed partial class TransactionRepository
    {
        private readonly SqliteDatabase _db;

        public TransactionRepository(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // ============================================================
        // Accounts Page Balances (FIXED)
        // ============================================================

        /// <summary>
        /// Returns the account Balance for each accountId:
        ///     OpeningBalances.Balance + SUM(Transactions.Amount)
        /// Excludes soft-deleted rows (IsDeleted = 0).
        ///
        /// Any accountId not present in the result set will map to 0.
        /// </summary>
        public async Task<Dictionary<long, decimal>> GetSumByAccountIdsAsync(IEnumerable<long> accountIds)
        {
            if (accountIds is null) throw new ArgumentNullException(nameof(accountIds));

            var ids = accountIds
                .Distinct()
                .Where(id => id > 0)
                .ToArray();

            // Default all requested ids to 0
            var result = ids.ToDictionary(id => id, _ => 0m);

            if (ids.Length == 0)
                return result;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            // IN ($p0,$p1,...) with parameters
            var paramNames = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                paramNames[i] = $"$p{i}";
                cmd.Parameters.AddWithValue(paramNames[i], ids[i]);
            }

            // IMPORTANT:
            // We compute balances from Accounts (a) to ensure:
            // - accounts with zero transactions still return a row
            // - opening balance (if present) is included
            //
            // Balance = COALESCE(ob.Balance,0) + COALESCE(SUM(active txns),0)
            cmd.CommandText = $@"
SELECT
    a.Id AS AccountId,
    COALESCE(ob.Balance, 0) + COALESCE(SUM(CASE WHEN t.IsDeleted = 0 THEN t.Amount ELSE 0 END), 0) AS Balance
FROM Accounts a
LEFT JOIN OpeningBalances ob ON ob.AccountId = a.Id
LEFT JOIN Transactions t ON t.AccountId = a.Id
WHERE a.Id IN ({string.Join(",", paramNames)})
GROUP BY a.Id, ob.Balance;
";

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var accountId = r.GetInt64(0);

                // Amount stored as REAL in this project (bound via (double)t.Amount),
                // so SUM returns a floating value. Convert carefully.
                var balanceAsDouble = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                result[accountId] = (decimal)balanceAsDouble;
            }

            return result;
        }

        // ============================================================
        // Phase 1: Account-agnostic APIs
        // ============================================================

        /// <summary>
        /// Get transactions for an account, optionally filtered by date range.
        /// Dates are inclusive. Results are sorted newest-first (PostedDate DESC, Id DESC).
        ///
        /// IMPORTANT:
        /// By default, soft-deleted rows are excluded (IsDeleted = 0).
        /// </summary>
        public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
            long accountId,
            DateOnly? from = null,
            DateOnly? to = null,
            int? take = null,
            bool includeDeleted = false)
        {
            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            var where = "t.AccountId = $accountId";
            cmd.Parameters.AddWithValue("$accountId", accountId);

            if (!includeDeleted)
                where += " AND t.IsDeleted = 0";

            if (from is not null)
            {
                where += " AND t.PostedDate >= $from";
                cmd.Parameters.AddWithValue("$from", from.Value.ToString("yyyy-MM-dd"));
            }

            if (to is not null)
            {
                where += " AND t.PostedDate <= $to";
                cmd.Parameters.AddWithValue("$to", to.Value.ToString("yyyy-MM-dd"));
            }

            var limitSql = "";
            if (take is not null && take.Value > 0)
            {
                limitSql = "LIMIT $take";
                cmd.Parameters.AddWithValue("$take", take.Value);
            }

            cmd.CommandText = $@"
SELECT
  t.Id,
  t.AccountId,
  t.PostedDate,
  t.Description,
  t.Amount,

  -- Display category name:
  -- Prefer legacy Transactions.Category if non-empty, else Categories.Name
  COALESCE(NULLIF(TRIM(t.Category), ''), c.Name) AS Category,

  t.CategoryId,
  t.MatchedRuleId,
  t.MatchedRulePattern,
  t.CategorizedUtc,
  t.FitId,
  t.Source,
  t.CreatedUtc
FROM Transactions t
LEFT JOIN Categories c ON c.Id = t.CategoryId
WHERE {where}
ORDER BY t.PostedDate DESC, t.Id DESC
{limitSql};
";

            var results = new List<Transaction>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                results.Add(ReadTransactionWithCategoryJoin(r));

            return results;
        }

        /// <summary>
        /// Gets deleted (soft-deleted) transactions for an account.
        /// Sorted newest-first.
        /// </summary>
        public async Task<IReadOnlyList<Transaction>> GetDeletedTransactionsAsync(long accountId, int? take = null)
        {
            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.Parameters.AddWithValue("$accountId", accountId);

            var limitSql = "";
            if (take is not null && take.Value > 0)
            {
                limitSql = "LIMIT $take";
                cmd.Parameters.AddWithValue("$take", take.Value);
            }

            cmd.CommandText = $@"
SELECT
  t.Id,
  t.AccountId,
  t.PostedDate,
  t.Description,
  t.Amount,

  COALESCE(NULLIF(TRIM(t.Category), ''), c.Name) AS Category,

  t.CategoryId,
  t.MatchedRuleId,
  t.MatchedRulePattern,
  t.CategorizedUtc,
  t.FitId,
  t.Source,
  t.CreatedUtc
FROM Transactions t
LEFT JOIN Categories c ON c.Id = t.CategoryId
WHERE t.AccountId = $accountId
  AND t.IsDeleted = 1
ORDER BY t.PostedDate DESC, t.Id DESC
{limitSql};
";

            var results = new List<Transaction>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                results.Add(ReadTransactionWithCategoryJoin(r));

            return results;
        }

        /// <summary>
        /// Restores a soft-deleted transaction (IsDeleted=1 -> 0).
        /// </summary>
        public async Task RestoreAsync(long id)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Transactions
SET IsDeleted = 0
WHERE Id = $id;
";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Restores all soft-deleted transactions for an account.
        /// </summary>
        public async Task RestoreAllForAccountAsync(long accountId)
        {
            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Transactions
SET IsDeleted = 0
WHERE AccountId = $accountId
  AND IsDeleted = 1;
";
            cmd.Parameters.AddWithValue("$accountId", accountId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// OPTIONAL (recommended):
        /// If you import a transaction that already exists but is soft-deleted, restore it.
        /// Returns number of rows restored (0 or 1).
        /// </summary>
        public async Task<int> RestoreIfDeletedByFitIdAsync(long accountId, string fitId)
        {
            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
            if (string.IsNullOrWhiteSpace(fitId)) return 0;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Transactions
SET IsDeleted = 0
WHERE AccountId = $accountId
  AND FitId = $fitId
  AND IsDeleted = 1;
SELECT changes();
";
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$fitId", fitId.Trim());

            var changed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            return (int)changed;
        }

        /// <summary>
        /// Inserts a batch for a specific account using INSERT OR IGNORE.
        /// Returns the number of rows inserted (duplicates ignored).
        ///
        /// IMPORTANT:
        /// Requires UNIQUE(AccountId, FitId) (or another de-dupe key) for IGNORE to work.
        /// </summary>
        public async Task<int> InsertTransactionsAsync(long accountId, IEnumerable<Transaction> transactions)
        {
            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
            if (transactions is null) throw new ArgumentNullException(nameof(transactions));

            var list = transactions as IList<Transaction> ?? transactions.ToList();
            if (list.Count == 0) return 0;

            using var conn = _db.OpenConnection();
            using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
INSERT OR IGNORE INTO Transactions
(AccountId, PostedDate, Description, Amount, Category, CategoryId, Memo, MatchedRuleId, MatchedRulePattern, CategorizedUtc, FitId, Source, CreatedUtc)
VALUES
($accountId, $postedDate, $desc, $amount, $cat, $categoryId, $memo, $matchedRuleId, $matchedRulePattern, $categorizedUtc, $fitId, $source, $createdUtc);
";

            var pAccountId = cmd.CreateParameter(); pAccountId.ParameterName = "$accountId"; cmd.Parameters.Add(pAccountId);
            var pPostedDate = cmd.CreateParameter(); pPostedDate.ParameterName = "$postedDate"; cmd.Parameters.Add(pPostedDate);
            var pDesc = cmd.CreateParameter(); pDesc.ParameterName = "$desc"; cmd.Parameters.Add(pDesc);
            var pAmount = cmd.CreateParameter(); pAmount.ParameterName = "$amount"; cmd.Parameters.Add(pAmount);
            var pCat = cmd.CreateParameter(); pCat.ParameterName = "$cat"; cmd.Parameters.Add(pCat);
            var pCategoryId = cmd.CreateParameter(); pCategoryId.ParameterName = "$categoryId"; cmd.Parameters.Add(pCategoryId);
            var pMemo = cmd.CreateParameter(); pMemo.ParameterName = "$memo"; cmd.Parameters.Add(pMemo);
            var pMatchedRuleId = cmd.CreateParameter(); pMatchedRuleId.ParameterName = "$matchedRuleId"; cmd.Parameters.Add(pMatchedRuleId);
            var pMatchedRulePattern = cmd.CreateParameter(); pMatchedRulePattern.ParameterName = "$matchedRulePattern"; cmd.Parameters.Add(pMatchedRulePattern);
            var pCategorizedUtc = cmd.CreateParameter(); pCategorizedUtc.ParameterName = "$categorizedUtc"; cmd.Parameters.Add(pCategorizedUtc);
            var pFitId = cmd.CreateParameter(); pFitId.ParameterName = "$fitId"; cmd.Parameters.Add(pFitId);
            var pSource = cmd.CreateParameter(); pSource.ParameterName = "$source"; cmd.Parameters.Add(pSource);
            var pCreatedUtc = cmd.CreateParameter(); pCreatedUtc.ParameterName = "$createdUtc"; cmd.Parameters.Add(pCreatedUtc);

            int inserted = 0;

            foreach (var t in list)
            {
                if (t is null) continue;

                t.AccountId = accountId;

                if (t.CreatedUtc == default)
                    t.CreatedUtc = DateTime.UtcNow;

                pAccountId.Value = t.AccountId;
                pPostedDate.Value = t.PostedDate.ToString("yyyy-MM-dd");
                pDesc.Value = t.Description ?? string.Empty;
                pAmount.Value = (double)t.Amount;

                pCat.Value = (object?)t.Category ?? DBNull.Value;
                pCategoryId.Value = (object?)t.CategoryId ?? DBNull.Value;
                pMemo.Value = (object?)t.Memo ?? DBNull.Value;
                pMatchedRuleId.Value = (object?)t.MatchedRuleId ?? DBNull.Value;
                pMatchedRulePattern.Value = (object?)t.MatchedRulePattern ?? DBNull.Value;
                pCategorizedUtc.Value = t.CategorizedUtc is null ? DBNull.Value : t.CategorizedUtc.Value.ToString("O");

                pFitId.Value = (object?)t.FitId ?? DBNull.Value;
                pSource.Value = (object?)t.Source ?? DBNull.Value;

                if (t.CreatedUtc.HasValue)
                    pCreatedUtc.Value = t.CreatedUtc.Value.ToString("O");
                else
                    pCreatedUtc.Value = DBNull.Value;

                inserted += await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return inserted;
        }

        // ============================================================
        // Existing APIs (kept for compatibility)
        // ============================================================

        public async Task<long> InsertAsync(Transaction t)
        {
            if (t is null) throw new ArgumentNullException(nameof(t));
            if (t.AccountId <= 0) throw new ArgumentOutOfRangeException(nameof(t.AccountId));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT INTO Transactions
(AccountId, PostedDate, Description, Amount, Category, CategoryId, Memo, MatchedRuleId, MatchedRulePattern, CategorizedUtc, FitId, Source, CreatedUtc)
VALUES
($accountId, $postedDate, $desc, $amount, $cat, $categoryId, $memo, $matchedRuleId, $matchedRulePattern, $categorizedUtc, $fitId, $source, $createdUtc);
SELECT last_insert_rowid();
";
            BindParameters(cmd, t);

            var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            t.Id = id;
            return id;
        }

        public async Task<bool> InsertOrIgnoreByFitIdAsync(Transaction t)
        {
            if (t is null) throw new ArgumentNullException(nameof(t));
            if (t.AccountId <= 0) throw new ArgumentOutOfRangeException(nameof(t.AccountId));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT OR IGNORE INTO Transactions
(AccountId, PostedDate, Description, Amount, Category, CategoryId, Memo, MatchedRuleId, MatchedRulePattern, CategorizedUtc, FitId, Source, CreatedUtc)
VALUES
($accountId, $postedDate, $desc, $amount, $cat, $categoryId, $memo, $matchedRuleId, $matchedRulePattern, $categorizedUtc, $fitId, $source, $createdUtc);
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

        public async Task<bool> ExistsByAnyFitIdAsync(params string[] fitIds)
        {
            if (fitIds is null || fitIds.Length == 0)
                return false;

            var cleaned = fitIds
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct()
                .ToArray();

            if (cleaned.Length == 0)
                return false;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            var paramNames = new string[cleaned.Length];
            for (int i = 0; i < cleaned.Length; i++)
            {
                paramNames[i] = $"$p{i}";
                cmd.Parameters.AddWithValue(paramNames[i], cleaned[i]);
            }

            cmd.CommandText = $@"
SELECT 1
FROM Transactions
WHERE FitId IN ({string.Join(",", paramNames)})
LIMIT 1;
";
            var result = await cmd.ExecuteScalarAsync();
            return result is not null && result != DBNull.Value;
        }

        public async Task<bool> InsertOrIgnoreByAnyFitIdsAsync(Transaction t, params string[] additionalFitIdsToCheck)
        {
            if (t is null) throw new ArgumentNullException(nameof(t));
            if (t.AccountId <= 0) throw new ArgumentOutOfRangeException(nameof(t.AccountId));

            var toCheck = new List<string>();

            if (!string.IsNullOrWhiteSpace(t.FitId))
                toCheck.Add(t.FitId!);

            if (additionalFitIdsToCheck is not null && additionalFitIdsToCheck.Length > 0)
            {
                foreach (var f in additionalFitIdsToCheck)
                {
                    if (!string.IsNullOrWhiteSpace(f) && !toCheck.Contains(f))
                        toCheck.Add(f);
                }
            }

            if (toCheck.Count == 0)
                return await InsertOrIgnoreByFitIdAsync(t);

            using var conn = _db.OpenConnection();
            using var tx = conn.BeginTransaction();

            using (var existsCmd = conn.CreateCommand())
            {
                existsCmd.Transaction = tx;

                var paramNames = new string[toCheck.Count];
                for (int i = 0; i < toCheck.Count; i++)
                {
                    paramNames[i] = $"$p{i}";
                    existsCmd.Parameters.AddWithValue(paramNames[i], toCheck[i]);
                }

                existsCmd.CommandText = $@"
SELECT 1
FROM Transactions
WHERE FitId IN ({string.Join(",", paramNames)})
LIMIT 1;
";
                var exists = await existsCmd.ExecuteScalarAsync();
                if (exists is not null && exists != DBNull.Value)
                {
                    tx.Rollback();
                    return false;
                }
            }

            using (var insertCmd = conn.CreateCommand())
            {
                insertCmd.Transaction = tx;

                insertCmd.CommandText = @"
INSERT OR IGNORE INTO Transactions
(AccountId, PostedDate, Description, Amount, Category, CategoryId, MatchedRuleId, MatchedRulePattern, CategorizedUtc, FitId, Source, CreatedUtc)
VALUES
($accountId, $postedDate, $desc, $amount, $cat, $categoryId, $matchedRuleId, $matchedRulePattern, $categorizedUtc, $fitId, $source, $createdUtc);
SELECT changes();
";
                BindParameters(insertCmd, t);

                var changed = (long)(await insertCmd.ExecuteScalarAsync() ?? 0L);
                if (changed > 0)
                {
                    using var idCmd = conn.CreateCommand();
                    idCmd.Transaction = tx;
                    idCmd.CommandText = "SELECT last_insert_rowid();";
                    t.Id = (long)(await idCmd.ExecuteScalarAsync() ?? 0L);

                    tx.Commit();
                    return true;
                }

                tx.Commit();
                return false;
            }
        }

        /// <summary>
        /// Gets a transaction by Id.
        /// By default, soft-deleted rows are excluded.
        /// </summary>
        public async Task<Transaction?> GetByIdAsync(long id, bool includeDeleted = false)
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
    Category,
    CategoryId,
    Memo,
    FitId,
    Source,
    CreatedUtc
FROM Transactions
WHERE Id = $id
" + (includeDeleted ? "" : "  AND IsDeleted = 0\n") + @"
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$id", id);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return null;

            return new Transaction
            {
                Id = r.GetInt64(0),
                AccountId = r.GetInt64(1),
                PostedDate = DateOnly.Parse(r.GetString(2)),
                Description = r.IsDBNull(3) ? "" : r.GetString(3),
                Amount = (decimal)r.GetDouble(4),

                Category = r.IsDBNull(5) ? null : r.GetString(5),
                CategoryId = r.IsDBNull(6) ? null : r.GetInt64(6),
                Memo = r.IsDBNull(7) ? null : r.GetString(7),

                FitId = r.IsDBNull(8) ? null : r.GetString(8),
                Source = r.IsDBNull(9) ? null : r.GetString(9),

                CreatedUtc = r.IsDBNull(10)
                    ? null
                    : DateTime.Parse(r.GetString(10), null, DateTimeStyles.RoundtripKind)
            };
        }

        public async Task UpdateAsync(Transaction t)
        {
            if (t is null) throw new ArgumentNullException(nameof(t));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Transactions
SET
    PostedDate = $date,
    Description = $desc,
    Amount = $amount,
    Category = $cat,
    CategoryId = $categoryId,
    Memo = $memo
WHERE Id = $id;
";
            cmd.Parameters.AddWithValue("$id", t.Id);
            cmd.Parameters.AddWithValue("$date", t.PostedDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$desc", t.Description ?? "");
            cmd.Parameters.AddWithValue("$amount", (double)t.Amount);
            cmd.Parameters.AddWithValue("$cat", (object?)t.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$categoryId", (object?)t.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$memo", (object?)t.Memo ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteAsync(long id)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Transactions
SET IsDeleted = 1
WHERE Id = $id;
";
            cmd.Parameters.AddWithValue("$id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // Duplicate Audit Ignore List (restored)
        // ============================================================

        public async Task IgnoreDuplicatePairAsync(long aId, long bId, string? reason = null)
        {
            var x = Math.Min(aId, bId);
            var y = Math.Max(aId, bId);

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT OR IGNORE INTO DuplicateAuditIgnores
(A_TransactionId, B_TransactionId, Reason, CreatedUtc)
VALUES
($a, $b, $reason, $utc);
";
            cmd.Parameters.AddWithValue("$a", x);
            cmd.Parameters.AddWithValue("$b", y);
            cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> IsIgnoredDuplicatePairAsync(long aId, long bId)
        {
            var x = Math.Min(aId, bId);
            var y = Math.Max(aId, bId);

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT 1
FROM DuplicateAuditIgnores
WHERE A_TransactionId = $a AND B_TransactionId = $b
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$a", x);
            cmd.Parameters.AddWithValue("$b", y);

            var result = await cmd.ExecuteScalarAsync();
            return result is not null && result != DBNull.Value;
        }

        public async Task<List<DuplicateAuditResultRow>> RunDuplicateAuditAsync()
        {
            var candidates = await AuditDuplicatesAsync();

            var results = new List<DuplicateAuditResultRow>(candidates.Count);

            foreach (var c in candidates)
            {
                results.Add(new DuplicateAuditResultRow
                {
                    AccountName = c.AccountName,
                    Type = c.Type == DuplicateType.Exact ? "Exact" : "Near",
                    Reason = c.Reason,

                    A_Id = c.A_Id,
                    A_Date = c.A_Date.ToString("yyyy-MM-dd"),
                    A_Description = c.A_Description,
                    A_Amount = c.A_Amount.ToString("C"),

                    B_Id = c.B_Id,
                    B_Date = c.B_Date.ToString("yyyy-MM-dd"),
                    B_Description = c.B_Description,
                    B_Amount = c.B_Amount.ToString("C")
                });
            }

            return results;
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static void BindParameters(SqliteCommand cmd, Transaction t)
        {
            cmd.Parameters.AddWithValue("$accountId", t.AccountId);
            cmd.Parameters.AddWithValue("$postedDate", t.PostedDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$desc", t.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$amount", (double)t.Amount);

            cmd.Parameters.AddWithValue("$cat", (object?)t.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$categoryId", (object?)t.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$memo", (object?)t.Memo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$matchedRuleId", (object?)t.MatchedRuleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$matchedRulePattern", (object?)t.MatchedRulePattern ?? DBNull.Value);

            cmd.Parameters.AddWithValue(
                "$categorizedUtc",
                t.CategorizedUtc is null ? DBNull.Value : t.CategorizedUtc.Value.ToString("O"));

            cmd.Parameters.AddWithValue("$fitId", (object?)t.FitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$source", (object?)t.Source ?? DBNull.Value);

            var created = t.CreatedUtc == default ? DateTime.UtcNow : t.CreatedUtc;
            cmd.Parameters.AddWithValue(
                "$createdUtc",
                created.HasValue ? created.Value.ToString("O") : (object)DBNull.Value);
        }

        private static Transaction ReadTransactionWithCategoryJoin(SqliteDataReader r)
        {
            return new Transaction
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
            };
        }
    }
}