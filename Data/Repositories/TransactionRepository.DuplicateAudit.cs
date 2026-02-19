using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Data.Models;
using eFinance.Importing; // FitIdHelper

namespace eFinance.Data.Repositories
{
    public sealed partial class TransactionRepository
    {
        private sealed record AuditRow(
            long Id,
            long AccountId,
            string AccountName,
            DateOnly PostedDate,
            decimal Amount,
            string Description,
            string? FitId,
            string NormDesc);

        public async Task<List<DuplicateCandidate>> AuditDuplicatesAsync(DuplicateAuditOptions? options = null)
        {
            options ??= new DuplicateAuditOptions();

            var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-options.LookbackDays));
            var rows = await LoadAuditRowsAsync(start);

            var results = new List<DuplicateCandidate>();

            // ============================================================
            // 1) Exact duplicates by FitId
            // ============================================================
            foreach (var g in rows.Where(r => !string.IsNullOrWhiteSpace(r.FitId))
                                  .GroupBy(r => r.FitId!)
                                  .Where(g => g.Count() > 1))
            {
                var ordered = g.OrderBy(r => r.PostedDate).ThenBy(r => r.Id).ToList();

                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    var a = ordered[i];
                    var b = ordered[i + 1];

                    // NEW: Skip if user already accepted this pair
                    if (await IsIgnoredDuplicatePairAsync(a.Id, b.Id))
                        continue;

                    results.Add(Make(
                        a,
                        b,
                        DuplicateType.Exact,
                        1.0,
                        "Same FitId exists multiple times (definite duplicate)."));

                    if (results.Count >= options.MaxResults)
                        return results;
                }
            }

            // ============================================================
            // 2) Near duplicates:
            // Same account + same amount + within N days + similar description
            // ============================================================
            foreach (var bucket in rows.GroupBy(r => (r.AccountId, r.Amount)))
            {
                var list = bucket.OrderBy(r => r.PostedDate).ThenBy(r => r.Id).ToList();
                if (list.Count < 2) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];

                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var b = list[j];

                        var dayDiff = Math.Abs(b.PostedDate.DayNumber - a.PostedDate.DayNumber);
                        if (dayDiff > options.NearDuplicateDateWindowDays)
                            break;

                        // Skip exact FitId duplicates (already handled above)
                        if (!string.IsNullOrWhiteSpace(a.FitId) && a.FitId == b.FitId)
                            continue;

                        var score = Jaccard(a.NormDesc, b.NormDesc);
                        if (score < options.SimilarityThreshold)
                            continue;

                        // NEW: Skip if user already accepted this pair
                        if (await IsIgnoredDuplicatePairAsync(a.Id, b.Id))
                            continue;

                        results.Add(Make(
                            a,
                            b,
                            DuplicateType.Near,
                            score,
                            $"Same amount, within {dayDiff} day(s), similar description (score {score:0.00})."));

                        if (results.Count >= options.MaxResults)
                            return results;
                    }
                }
            }

            return results
                .OrderByDescending(r => r.Type == DuplicateType.Exact)
                .ThenByDescending(r => r.Score)
                .Take(options.MaxResults)
                .ToList();
        }

        private async Task<List<AuditRow>> LoadAuditRowsAsync(DateOnly start)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT
  t.Id,
  t.AccountId,
  a.Name,
  t.PostedDate,
  t.Amount,
  t.Description,
  t.FitId
FROM Transactions t
JOIN Accounts a ON a.Id = t.AccountId
WHERE t.PostedDate >= $start
ORDER BY t.AccountId, t.Amount, t.PostedDate, t.Id;
";
            cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));

            var list = new List<AuditRow>();

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var id = r.GetInt64(0);
                var accountId = r.GetInt64(1);
                var accountName = r.GetString(2);
                var posted = DateOnly.Parse(r.GetString(3));
                var amount = (decimal)r.GetDouble(4);
                var desc = r.GetString(5);
                var fitId = r.IsDBNull(6) ? null : r.GetString(6);

                list.Add(new AuditRow(
                    id,
                    accountId,
                    accountName,
                    posted,
                    amount,
                    desc,
                    fitId,
                    FitIdHelper.NormalizeDescription(desc)));
            }

            return list;
        }

        private static DuplicateCandidate Make(
            AuditRow a,
            AuditRow b,
            DuplicateType type,
            double score,
            string reason)
            => new DuplicateCandidate
            {
                AccountId = a.AccountId,
                AccountName = a.AccountName,

                A_Id = a.Id,
                A_Date = a.PostedDate,
                A_Amount = a.Amount,
                A_Description = a.Description,
                A_FitId = a.FitId,

                B_Id = b.Id,
                B_Date = b.PostedDate,
                B_Amount = b.Amount,
                B_Description = b.Description,
                B_FitId = b.FitId,

                Type = type,
                Score = score,
                Reason = reason
            };

        private static double Jaccard(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
            if (a == b) return 1.0;

            var A = Tokenize(a);
            var B = Tokenize(b);

            var inter = A.Intersect(B).Count();
            var union = A.Union(B).Count();
            return union == 0 ? 0 : (double)inter / union;
        }

        private static HashSet<string> Tokenize(string s)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (tok.Length >= 2) set.Add(tok);
            return set;
        }
    }
}
