using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

            // Convenience: allow historical naming AND AMEX default download name.
            var nameLooksLikeAmex =
                fileName.Contains("amex", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("activity.csv", StringComparison.OrdinalIgnoreCase);

            // Strong check: detect AMEX by header columns.
            // Expected header example:
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
                    cols.Contains("Account #") &&
                    cols.Contains("Amount");

                if (hasAmexHeaders)
                    return true;
            }

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

            // Key fix: prevent legit duplicates from colliding on FitId.
            // We build a "base key" and then append an occurrence number (001, 002, ...).
            var occurrenceByBaseKey = new Dictionary<string, int>(StringComparer.Ordinal);

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

                    // Include Card Member if present (reduces collisions further).
                    var cardMember = FirstNonNull(row,
                        "Card Member", "CardMember", "CARD MEMBER");

                    if (dateText is null || desc is null || amountText is null)
                    {
                        failed++;
                        continue;
                    }

                    var date = ParseDate(dateText);

                    // AMEX CSV convention:
                    //   Charges are positive
                    //   Credits/payments are negative
                    // eFinance convention:
                    //   Charges should be negative
                    //   Credits should be positive
                    // Therefore: eFinanceAmount = -csvAmount
                    var csvAmount = ParseDecimal(amountText);
                    var amount = NormalizeAmexAmount(csvAmount);

                    // Build stable, collision-resistant FitId.
                    // Base key defines "what" the transaction is; occurrence makes duplicates unique.
                    var normDesc = NormalizeKeyText(desc);
                    var normAcct = NormalizeKeyText(accountNo);
                    var normMember = NormalizeKeyText(cardMember);

                    // Normalize amount text so 12.3 and 12.30 are identical in the key.
                    var normAmountText = amount.ToString("0.00", CultureInfo.InvariantCulture);

                    var baseKey = $"{date:yyyyMMdd}|{normAcct}|{normMember}|{normAmountText}|{normDesc}";

                    occurrenceByBaseKey.TryGetValue(baseKey, out var n);
                    n++;
                    occurrenceByBaseKey[baseKey] = n;

                    // Final FitId: stable across re-imports of the same content, but unique for legit duplicates.
                    var fitId = $"{baseKey}|{n:D3}";

                    var transaction = new Transaction
                    {
                        AccountId = accountId.Value,
                        PostedDate = date,
                        Description = desc,
                        Amount = amount,
                        Category = null, // legacy; keep null
                        FitId = fitId,
                        Source = SourceName,
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
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine("AMEX import row failed: " + ex);
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"AMEX import finished. Inserted={inserted}, Ignored={ignored}, Failed={failed}");

            return new ImportResult(inserted, ignored, failed);
        }

        /// <summary>
        /// Convert AMEX CSV amount into eFinance sign convention.
        /// AMEX: charges positive, credits negative.
        /// eFinance: charges negative, credits positive.
        /// </summary>
        private static decimal NormalizeAmexAmount(decimal csvAmount)
        {
            return -csvAmount;
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

        /// <summary>
        /// Normalizes text for key generation (FitId base key).
        /// </summary>
        private static string NormalizeKeyText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var s = text.Trim();

            // collapse whitespace
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
