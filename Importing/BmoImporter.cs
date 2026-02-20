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
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _transactions;
        private readonly CategorizationService _categorizer;

        private const AmountSignPolicy SignPolicy = AmountSignPolicy.AsIs;
        public AmountSignPolicy? AmountPolicy => SignPolicy;

        public string SourceName => "BMO";
        public string HeaderHint => "POSTED DATE, DESCRIPTION, AMOUNT, FI TRANSACTION REFERENCE, ...";

        public BmoImporter(AccountRepository accounts, TransactionRepository transactions, CategorizationService categorizer)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
            _categorizer = categorizer ?? throw new ArgumentNullException(nameof(categorizer));
        }

        public bool CanImport(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            if (!filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(filePath)) return false;

            // Strong check: detect by header columns.
            // POSTED DATE,DESCRIPTION,AMOUNT,CURRENCY,TRANSACTION REFERENCE NUMBER,FI TRANSACTION REFERENCE,TYPE,CREDIT/DEBIT,ORIGINAL AMOUNT
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

                    // Prefer FI TRANSACTION REFERENCE (best unique key)
                    // Fallback to a composite if missing.
                    var fitId = BuildFitId(date, desc, amount, fiRef, txnRef);

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
                    System.Diagnostics.Debug.WriteLine("BMO import row failed: " + ex);
                }
            }

            return new ImportResult(inserted, ignored, failed);
        }

        private static string BuildFitId(DateOnly date, string desc, decimal amount, string? fiRef, string? txnRef)
        {
            if (!string.IsNullOrWhiteSpace(fiRef))
                return $"BMO|FI|{fiRef.Trim()}";

            if (!string.IsNullOrWhiteSpace(txnRef))
                return $"BMO|TRN|{txnRef.Trim()}|{date:yyyyMMdd}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|{NormalizeKeyText(desc)}";

            return $"BMO|{date:yyyyMMdd}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|{NormalizeKeyText(desc)}";
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
                return string.Empty;

            var s = text.Trim();
            while (s.Contains("  ", StringComparison.Ordinal))
                s = s.Replace("  ", " ", StringComparison.Ordinal);

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