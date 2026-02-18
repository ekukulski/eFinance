using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using eFinance.Data.Models;
using eFinance.Data.Repositories;

namespace eFinance.Importing
{
    public sealed class DuplicateAuditService
    {
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _txRepo;

        public DuplicateAuditService(AccountRepository accounts, TransactionRepository txRepo)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _txRepo = txRepo ?? throw new ArgumentNullException(nameof(txRepo));
        }

        public async Task<List<DuplicateCandidate>> AuditAllAccountsAsync(
            DuplicateAuditOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= new DuplicateAuditOptions();

            var accounts = await _accounts.GetAllAsync(); // assumes you have this; if not, use your existing method
            var results = new List<DuplicateCandidate>();

            foreach (var acct in accounts)
            {
                ct.ThrowIfCancellationRequested();

                var acctResults = await AuditSingleAccountAsync(acct, options, ct);
                results.AddRange(acctResults);

                if (results.Count >= options.MaxResults)
                    break;
            }

            // Sort: Exact first, then higher score
            return results
                .OrderByDescending(r => r.Type == DuplicateType.Exact)
                .ThenByDescending(r => r.Score)
                .Take(options.MaxResults)
                .ToList();
        }

        private async Task<List<DuplicateCandidate>> AuditSingleAccountAsync(
            Account acct,
            DuplicateAuditOptions options,
            CancellationToken ct)
        {
            // Pull recent transactions for the account
            var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-options.LookbackDays));
            var txns = await _txRepo.GetByAccountForAuditAsync(acct.Id, start);

            // Precompute normalized descriptions for similarity checks
            var items = txns
                .Select(t => new AuditItem(t, FitIdHelper.NormalizeDescription(t.Description)))
                .ToList();

            // 1) Exact duplicates by FitId (fast and definitive)
            var exactByFitId = items
                .Where(x => !string.IsNullOrWhiteSpace(x.T.FitId))
                .GroupBy(x => x.T.FitId!)
                .Where(g => g.Count() > 1)
                .ToList();

            var results = new List<DuplicateCandidate>();

            foreach (var g in exactByFitId)
            {
                ct.ThrowIfCancellationRequested();

                // Pair them (A vs B) but avoid spamming results:
                // report only adjacent pairs when sorted.
                var ordered = g.OrderBy(x => x.T.PostedDate).ThenBy(x => x.T.Id).ToList();
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    var a = ordered[i].T;
                    var b = ordered[i + 1].T;

                    results.Add(MakeCandidate(acct, a, b, DuplicateType.Exact, 1.0,
                        "Same FitId already exists multiple times (definite duplicate)."));

                    if (results.Count >= options.MaxResults) return results;
                }
            }

            // 2) Near duplicates:
            // Same amount + within date window + similar normalized description.
            // Approach: bucket by amount to keep it fast.
            var byAmount = items.GroupBy(x => x.T.Amount).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in byAmount)
            {
                ct.ThrowIfCancellationRequested();

                var group = kvp.Value;
                if (group.Count < 2) continue;

                // Sort by date for sliding window comparisons
                group.Sort((x, y) =>
                {
                    var c = x.T.PostedDate.CompareTo(y.T.PostedDate);
                    return c != 0 ? c : x.T.Id.CompareTo(y.T.Id);
                });

                for (int i = 0; i < group.Count; i++)
                {
                    var a = group[i];

                    for (int j = i + 1; j < group.Count; j++)
                    {
                        var b = group[j];

                        var dayDiff = Math.Abs(b.T.PostedDate.DayNumber - a.T.PostedDate.DayNumber);
                        if (dayDiff > options.NearDuplicateDateWindowDays)
                            break; // because sorted by date

                        // If already exact by FitId, skip (we already reported)
                        if (!string.IsNullOrWhiteSpace(a.T.FitId) && a.T.FitId == b.T.FitId)
                            continue;

                        // Optionally require exact amount match (recommended true)
                        if (options.RequireExactAmountMatch && a.T.Amount != b.T.Amount)
                            continue;

                        var score = Similarity(a.NormDesc, b.NormDesc);
                        if (score < options.SimilarityThreshold)
                            continue;

                        // Avoid reporting obvious legitimate repeats: identical merchant + same amount can be real.
                        // We keep it as "Near" and let you review.
                        results.Add(MakeCandidate(
                            acct,
                            a.T, b.T,
                            DuplicateType.Near,
                            score,
                            $"Same amount, within {dayDiff} day(s), similar description (score {score:0.00})."));

                        if (results.Count >= options.MaxResults) return results;
                    }
                }
            }

            return results;
        }

        private static DuplicateCandidate MakeCandidate(
            Account acct,
            Transaction a,
            Transaction b,
            DuplicateType type,
            double score,
            string reason)
        {
            return new DuplicateCandidate
            {
                AccountId = acct.Id,
                AccountName = acct.Name ?? $"Account {acct.Id}",

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
        }

        private sealed record AuditItem(Transaction T, string NormDesc);

        // Simple token-based Jaccard similarity (fast, stable, no heavy dependencies)
        // 0..1
        private static double Similarity(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return 0;

            if (a == b) return 1.0;

            var setA = Tokenize(a);
            var setB = Tokenize(b);

            if (setA.Count == 0 || setB.Count == 0) return 0;

            var inter = setA.Intersect(setB).Count();
            var union = setA.Union(setB).Count();

            return union == 0 ? 0 : (double)inter / union;
        }

        private static HashSet<string> Tokenize(string s)
        {
            // tokens: words length >= 2
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts)
            {
                if (p.Length >= 2)
                    set.Add(p);
            }
            return set;
        }
    }
}
