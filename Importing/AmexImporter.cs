using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using eFinance.Services;

namespace eFinance.Importing
{
    public sealed class AmexImporter : IImporter
    {
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _transactions;
        private readonly CategorizationService _categorizer;

        public AmexImporter(AccountRepository accounts, TransactionRepository transactions, CategorizationService categorizer)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
            _categorizer = categorizer ?? throw new ArgumentNullException(nameof(categorizer));
        }

        public string SourceName => "AMEX";

        public bool CanImport(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            if (!filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(filePath)) return false;

            var fileName = Path.GetFileName(filePath);

            // Convenience: allow historical naming AND the AMEX default download name.
            var nameLooksLikeAmex =
                fileName.Contains("amex", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("activity.csv", StringComparison.OrdinalIgnoreCase);

            // Strong check: detect AMEX by header columns.
            // Your AMEX activity.csv header is:
            // Date,Description,Card Member,Account #,Amount
            var header = File.ReadLines(filePath).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(header))
            {
                var cols = header.Split(',')
                                 .Select(c => c.Trim().Trim('"'))
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

                bool hasAmexHeaders =
                    cols.Contains("Date") &&
                    cols.Contains("Description") &&
                    cols.Contains("Card Member") &&
                    cols.Contains("Account #") &&
                    cols.Contains("Amount");

                if (hasAmexHeaders)
                    return true;
            }

            // Fallback: filename heuristic.
            return nameLooksLikeAmex;
        }

        public async Task<ImportResult> ImportAsync(string filePath)
        {
            var accountId = await _accounts.GetIdByNameAsync("AMEX");
            if (accountId is null)
                throw new InvalidOperationException("AMEX account not found.");

            int inserted = 0;
            int ignored = 0;
            int failed = 0;

            foreach (var row in CsvReader.Read(filePath))
            {
                try
                {
                    var dateText = FirstNonNull(row,
                        "Date", "Posted Date", "POSTED DATE", "Transaction Date", "Trans Date");

                    var desc = FirstNonNull(row,
                        "Description", "DESCRIPTION", "Merchant", "Payee");

                    var amountText = FirstNonNull(row,
                        "Amount", "AMOUNT", "Charge Amount", "Transaction Amount");

                    var accountNo = FirstNonNull(row,
                        "Account #", "Account", "Account Number");

                    var creditDebitText = FirstNonNull(row,
                        "Credit/Debit", "Credit or Debit", "CreditOrDebit", "Type", "Transaction Type");

                    if (dateText is null || desc is null || amountText is null)
                    {
                        failed++;
                        continue;
                    }

                    var date = ParseDate(dateText);
                    var parsedAmount = ParseDecimal(amountText);

                    var amount = NormalizeAmexAmount(parsedAmount, creditDebitText, desc);

                    var fitId = $"{date:yyyyMMdd}|{desc}|{accountNo}|{amount.ToString(CultureInfo.InvariantCulture)}";

                    var transaction = new Transaction
                    {
                        AccountId = accountId.Value,
                        PostedDate = date,
                        Description = desc,
                        Amount = amount,
                        Category = null, // legacy; keep null
                        FitId = fitId,
                        Source = "AMEX",
                        CreatedUtc = DateTime.UtcNow
                    };

                    // ✅ AUTO-CATEGORIZE BEFORE INSERT
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
                catch
                {
                    failed++;
                }
            }

            return new ImportResult(inserted, ignored, failed);
        }

        private static decimal NormalizeAmexAmount(decimal parsedAmount, string? creditDebitText, string description)
        {
            if (!string.IsNullOrWhiteSpace(creditDebitText))
            {
                var t = creditDebitText.Trim();

                if (IsCreditIndicator(t))
                    return Math.Abs(parsedAmount);

                if (IsDebitIndicator(t))
                    return -Math.Abs(parsedAmount);
            }

            if (LooksLikeCredit(description))
                return Math.Abs(parsedAmount);

            return -Math.Abs(parsedAmount);
        }

        private static bool IsCreditIndicator(string text)
        {
            var t = text.Trim().ToLowerInvariant();
            return t is "credit" or "cr" or "c"
                   or "payment" or "refund" or "reversal" or "return";
        }

        private static bool IsDebitIndicator(string text)
        {
            var t = text.Trim().ToLowerInvariant();
            return t is "debit" or "dr" or "d"
                   or "charge" or "purchase";
        }

        private static bool LooksLikeCredit(string description)
        {
            var d = (description ?? "").Trim().ToLowerInvariant();
            return d.Contains("payment")
                   || d.Contains("autopay")
                   || d.Contains("thank you")
                   || d.Contains("refund")
                   || d.Contains("reversal")
                   || d.Contains("credit");
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

        private static DateOnly ParseDate(string text)
        {
            text = text.Trim();

            string[] fmts = new[]
            {
                "M/d/yyyy", "MM/dd/yyyy",
                "M/d/yy", "MM/dd/yy",
                "yyyy-MM-dd",
                "yyyyMMdd"
            };

            if (DateTime.TryParseExact(text, fmts, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var dt))
                return DateOnly.FromDateTime(dt);

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
                return DateOnly.FromDateTime(dt);

            throw new FormatException($"Unrecognized date: '{text}'");
        }

        private static decimal ParseDecimal(string text)
        {
            text = text.Trim();
            text = text.Replace("$", "").Replace(",", "");

            if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses,
                    CultureInfo.InvariantCulture, out var d))
                return d;

            if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses,
                    CultureInfo.CurrentCulture, out d))
                return d;

            throw new FormatException($"Unrecognized amount: '{text}'");
        }
    }
}
