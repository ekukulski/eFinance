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
    public sealed class CitiMastercardImporter : IImporter
    {
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _transactions;
        private readonly CategorizationService _categorizer;

        public string SourceName => "CITI";
        public string HeaderHint => "Status, Date, Description, Debit, Credit, Member Name";
        public AmountSignPolicy? AmountPolicy => AmountSignPolicy.DebitCreditColumns;

        public CitiMastercardImporter(AccountRepository accounts, TransactionRepository transactions, CategorizationService categorizer)
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

            var header = File.ReadLines(filePath).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(header)) return false;

            var cols = header.Split(',')
                             .Select(c => c.Trim().Trim('"'))
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Status,Date,Description,Debit,Credit,Member Name
            return cols.Contains("Status")
                && cols.Contains("Date")
                && cols.Contains("Description")
                && cols.Contains("Debit")
                && cols.Contains("Credit");
        }

        public async Task<ImportResult> ImportAsync(string filePath, long accountId)
        {
            if (accountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(accountId));

            int inserted = 0;
            int ignored = 0;
            int failed = 0;

            var occurrenceByBaseKey = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var row in CsvReader.Read(filePath))
            {
                try
                {
                    var dateText = FirstNonNull(row, "Date", "DATE");
                    var desc = FirstNonNull(row, "Description", "DESCRIPTION");
                    var debitText = FirstNonNull(row, "Debit", "DEBIT");
                    var creditText = FirstNonNull(row, "Credit", "CREDIT");
                    var status = FirstNonNull(row, "Status", "STATUS");

                    if (dateText is null || desc is null)
                    {
                        failed++;
                        continue;
                    }

                    var date = ParseDate(dateText);

                    decimal? debit = ParseNullableDecimal(debitText);
                    decimal? credit = ParseNullableDecimal(creditText);

                    var amount = AmountNormalizer.NormalizeDebitCredit(debit, credit);

                    var baseKey = $"{date:yyyyMMdd}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|{NormalizeKeyText(desc)}";
                    occurrenceByBaseKey.TryGetValue(baseKey, out var n);
                    n++;
                    occurrenceByBaseKey[baseKey] = n;

                    var fitId = $"CITI|{baseKey}|{n:D3}";

                    var transaction = new eFinance.Data.Models.Transaction
                    {
                        AccountId = accountId,
                        PostedDate = date,
                        Description = desc,
                        Amount = amount,
                        Memo = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                        Category = null,
                        FitId = fitId,
                        Source = SourceName,
                        CreatedUtc = DateTime.UtcNow
                    };

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
                    System.Diagnostics.Debug.WriteLine("CITI import row failed: " + ex);
                }
            }

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
            string[] fmts = { "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy", "yyyy-MM-dd", "yyyyMMdd" };

            if (DateTime.TryParseExact(text, fmts, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var dt))
                return DateOnly.FromDateTime(dt);

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces, out dt))
                return DateOnly.FromDateTime(dt);

            throw new FormatException($"Unrecognized date: '{text}'");
        }

        private static decimal? ParseNullableDecimal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

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