using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using eFinance.Data.Repositories;
using eFinance.Services;

namespace eFinance.Importing
{
    public sealed class ChaseVisaImporter : IImporter
    {
        private readonly TransactionRepository _transactions;
        private readonly CategorizationService _categorizer;

        // For Chase/Visa exports in your app: amounts already match your sign convention.
        private const AmountSignPolicy SignPolicy = AmountSignPolicy.AsIs;
        public AmountSignPolicy? AmountPolicy => SignPolicy;

        public string SourceName => "CHASE";
        public string HeaderHint => "Transaction Date, Post Date, Description, Category, Type, Amount, Memo";

        public ChaseVisaImporter(
            AccountRepository accounts, // kept for DI compatibility if you already register it
            TransactionRepository transactions,
            CategorizationService categorizer)
        {
            _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
            _categorizer = categorizer ?? throw new ArgumentNullException(nameof(categorizer));
        }

        public bool CanImport(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            if (!filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(filePath)) return false;

            var header = File.ReadLines(filePath).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(header)) return false;

            var cols = header.Split(',')
                             .Select(c => c.Trim().Trim('"'))
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // We only REQUIRE Transaction Date + Description + Amount for this importer.
            // Post Date may exist but is ignored for identity (and we don't require it).
            return cols.Contains("Transaction Date")
                && cols.Contains("Description")
                && cols.Contains("Amount");
        }

        public async Task<ImportResult> ImportAsync(string filePath, long accountId)
        {
            if (accountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(accountId));

            int inserted = 0;
            int ignored = 0;
            int failed = 0;

            foreach (var row in CsvReader.Read(filePath))
            {
                try
                {
                    // ----------------------------
                    // Read columns (tolerant names)
                    // ----------------------------
                    var txnDateText = FirstNonNull(row, "Transaction Date", "TRANSACTION DATE", "Trans Date", "Date");
                    _ = FirstNonNull(row, "Post Date", "POSTED DATE", "Posted Date"); // intentionally ignored for identity

                    var desc = FirstNonNull(row, "Description", "DESCRIPTION");
                    var amountText = FirstNonNull(row, "Amount", "AMOUNT");

                    var type = FirstNonNull(row, "Type", "TYPE");
                    var memo = FirstNonNull(row, "Memo", "MEMO");

                    // Optional: some exports include an id
                    var txnId = FirstNonNull(row, "Transaction ID", "TransactionId", "Id");

                    // ----------------------------
                    // Validate required fields
                    // ----------------------------
                    if (string.IsNullOrWhiteSpace(txnDateText) ||
                        string.IsNullOrWhiteSpace(desc) ||
                        string.IsNullOrWhiteSpace(amountText))
                    {
                        failed++;
                        continue;
                    }

                    // ----------------------------
                    // Parse Transaction Date (canonical for identity and storage)
                    // ----------------------------
                    var txnDate = ParseDate(txnDateText);

                    // IMPORTANT:
                    // Chase "Post Date" can change between downloads (pending -> posted).
                    // We IGNORE Post Date entirely for identity, and we store txnDate as PostedDate
                    // to keep the register stable across re-imports.
                    var dateToStore = txnDate;

                    // ----------------------------
                    // Parse + normalize amount
                    // ----------------------------
                    var csvAmount = ParseDecimal(amountText);
                    var amount = AmountNormalizer.Normalize(csvAmount, SignPolicy);

                    // ----------------------------
                    // Stable FitId: Transaction Date ONLY (Post Date ignored)
                    // ----------------------------
                    var fitId = BuildStableFitId(txnDate, amount, desc, type, memo, txnId);

                    var transaction = new eFinance.Data.Models.Transaction
                    {
                        AccountId = accountId,
                        PostedDate = dateToStore,
                        Description = desc,
                        Amount = amount,
                        Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
                        Category = null,
                        FitId = fitId,
                        Source = SourceName,
                        CreatedUtc = DateTime.UtcNow
                    };

                    // Auto-categorize before insert
                    var match = await _categorizer.CategorizeAsync(transaction.Description);
                    if (match is not null)
                    {
                        transaction.CategoryId = match.CategoryId;
                        transaction.MatchedRuleId = match.RuleId;
                        transaction.MatchedRulePattern = match.Pattern;
                        transaction.CategorizedUtc = DateTime.UtcNow;
                    }

                    var didInsert = await _transactions.InsertOrIgnoreByFitIdAsync(transaction);
                    if (didInsert) inserted++;
                    else ignored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine("CHASE import row failed: " + ex);
                }
            }

            return new ImportResult(inserted, ignored, failed);
        }

        private static string BuildStableFitId(
            DateOnly txnDate,
            decimal amount,
            string description,
            string? type,
            string? memo,
            string? transactionIdFromCsv)
        {
            // Best case: a real id from the bank export
            if (!string.IsNullOrWhiteSpace(transactionIdFromCsv))
                return $"CHASE|ID|{transactionIdFromCsv.Trim()}";

            // STRICT: Transaction Date only. Post Date is ignored because it can change between downloads.
            var keyDate = txnDate.ToString("yyyyMMdd");

            var amtPart = amount.ToString("0.00", CultureInfo.InvariantCulture);
            var descPart = NormalizeKeyText(description);
            var typePart = NormalizeKeyText(type);
            var memoPart = NormalizeKeyText(memo);

            var raw = $"{keyDate}|{amtPart}|{descPart}";

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            var hex = Convert.ToHexString(bytes);

            return $"CHASE|H|{hex}";
        }

        private static string? FirstNonNull(CsvRow row, params string[] names)
        {
            foreach (var n in names)
            {
                var v = row.GetFirst(n);
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
            return null;
        }

        private static string NormalizeKeyText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var s = text.Trim().ToLowerInvariant();

            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");

            return s;
        }

        private static DateOnly ParseDate(string text)
        {
            text = text.Trim();

            string[] fmts =
            {
                "M/d/yyyy", "MM/dd/yyyy",
                "M/d/yy", "MM/dd/yy",
                "yyyy-MM-dd",
                "yyyyMMdd"
            };

            if (DateTime.TryParseExact(text, fmts, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var dt))
                return DateOnly.FromDateTime(dt);

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces, out dt))
                return DateOnly.FromDateTime(dt);

            throw new FormatException($"Unrecognized date: '{text}'");
        }

        private static decimal ParseDecimal(string text)
        {
            text = text.Trim().Replace("$", "").Replace(",", "");

            if (decimal.TryParse(text,
                    NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses,
                    CultureInfo.InvariantCulture, out var d))
                return d;

            if (decimal.TryParse(text,
                    NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses,
                    CultureInfo.CurrentCulture, out d))
                return d;

            throw new FormatException($"Unrecognized amount: '{text}'");
        }
    }
}