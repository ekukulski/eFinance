using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Data.Repositories;
using eFinance.Services;

namespace eFinance.Importing
{
    public sealed class BmoImporter : IImporter
    {
        private readonly TransactionRepository _transactions;
        private readonly CategorizationService _categorizer;

        private const AmountSignPolicy SignPolicy = AmountSignPolicy.AsIs;
        public AmountSignPolicy? AmountPolicy => SignPolicy;

        public string SourceName => "BMO";

        public BmoImporter(TransactionRepository transactions, CategorizationService categorizer)
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

            return cols.Contains("POSTED DATE")
                && cols.Contains("DESCRIPTION")
                && cols.Contains("AMOUNT")
                && cols.Contains("FI TRANSACTION REFERENCE");
        }

        public async Task<ImportResult> ImportAsync(string filePath, long accountId)
        {
            if (accountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(accountId));

            int inserted = 0;
            int ignored = 0;
            int failed = 0;

            // For legitimate duplicates: base key + occurrence number (001, 002, ...)
            var occurrenceByBaseKey = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var row in CsvReader.Read(filePath))
            {
                try
                {
                    var dateText = FirstNonNull(row, "POSTED DATE", "Posted Date", "Date");
                    var desc = FirstNonNull(row, "DESCRIPTION", "Description");
                    var amountText = FirstNonNull(row, "AMOUNT", "Amount");

                    var fiRef = FirstNonNull(row, "FI TRANSACTION REFERENCE", "FI Transaction Reference");
                    var txnRef = FirstNonNull(row, "TRANSACTION REFERENCE NUMBER", "Transaction Reference Number");

                    if (dateText is null || desc is null || amountText is null)
                    {
                        failed++;
                        continue;
                    }

                    var date = ParseDate(dateText);
                    var csvAmount = ParseDecimal(amountText);
                    var amount = AmountNormalizer.Normalize(csvAmount, SignPolicy);

                    // ✅ Build a collision-resistant base key.
                    // We include FI ref if present, but also include date/amount/desc so
                    // rows that share FI ref don't collide.
                    var normDesc = NormalizeKeyText(desc);
                    var normAmount = amount.ToString("0.00", CultureInfo.InvariantCulture);
                    var normFiRef = NormalizeKeyText(fiRef);
                    var normTxnRef = NormalizeKeyText(txnRef);

                    string baseKey;
                    if (!string.IsNullOrWhiteSpace(normFiRef))
                    {
                        baseKey = $"BMO|FI|{normFiRef}|{date:yyyyMMdd}|{normAmount}|{normDesc}";
                    }
                    else if (!string.IsNullOrWhiteSpace(normTxnRef))
                    {
                        baseKey = $"BMO|TRN|{normTxnRef}|{date:yyyyMMdd}|{normAmount}|{normDesc}";
                    }
                    else
                    {
                        baseKey = $"BMO|{date:yyyyMMdd}|{normAmount}|{normDesc}";
                    }

                    occurrenceByBaseKey.TryGetValue(baseKey, out var n);
                    n++;
                    occurrenceByBaseKey[baseKey] = n;

                    var fitId = $"{baseKey}|{n:D3}";

                    var transaction = new eFinance.Data.Models.Transaction
                    {
                        AccountId = accountId,
                        PostedDate = date,
                        Description = desc,
                        Amount = amount,
                        Category = null, // legacy
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
                    System.Diagnostics.Debug.WriteLine($"BMO import row failed: {ex}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"BMO import finished. Inserted={inserted}, Ignored={ignored}, Failed={failed}");
            return new ImportResult(inserted, ignored, failed);
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
            text = text.Trim();
            text = text.Replace("$", "").Replace(",", "");

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