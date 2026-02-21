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
    public sealed class AmexImporter : IImporter
    {
        private readonly AccountRepository _accounts; // kept for DI compatibility / future use
        private readonly TransactionRepository _transactions;
        private readonly CategorizationService _categorizer;

        // AMEX CSV convention is opposite of eFinance, so we invert.
        private const AmountSignPolicy SignPolicy = AmountSignPolicy.Invert;

        public AmexImporter(
            AccountRepository accounts,
            TransactionRepository transactions,
            CategorizationService categorizer)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
            _categorizer = categorizer ?? throw new ArgumentNullException(nameof(categorizer));
        }

        public AmountSignPolicy? AmountPolicy => SignPolicy;

        public string SourceName => "AMEX";

        public string HeaderHint => "Date, Description, Card Member, Account #, Amount";

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
                    var dateText = FirstNonNull(row,
                        "Date", "Posted Date", "POSTED DATE", "Transaction Date", "Trans Date");

                    var desc = FirstNonNull(row,
                        "Description", "DESCRIPTION", "Merchant", "Payee");

                    var amountText = FirstNonNull(row,
                        "Amount", "AMOUNT", "Charge Amount", "Transaction Amount");

                    var accountNo = FirstNonNull(row,
                        "Account #", "Account", "Account Number");

                    // Include Card Member if present (reduces collisions).
                    var cardMember = FirstNonNull(row,
                        "Card Member", "CardMember", "CARD MEMBER");

                    // Optional extra fields if they exist in some AMEX exports
                    var memo = FirstNonNull(row, "Memo", "Notes", "Extended Details", "Additional Information");
                    var txnId = FirstNonNull(row, "Transaction ID", "TransactionId", "Reference Number", "Ref", "Id");

                    if (dateText is null || desc is null || amountText is null)
                    {
                        failed++;
                        continue;
                    }

                    var date = ParseDate(dateText);

                    // Parse CSV amount then normalize to eFinance convention using policy (Invert).
                    var csvAmount = ParseDecimal(amountText);
                    var amount = AmountNormalizer.Normalize(csvAmount, SignPolicy);

                    // Build a STABLE FitId (deterministic across re-imports).
                    // IMPORTANT: include accountId to ensure uniqueness across accounts.
                    var baseKey = BuildBaseKey(accountId, date, amount, desc, accountNo, cardMember, memo, txnId);
                    var fitId = "AMEX|" + Sha256Hex(baseKey);

                    // Fail fast: if FitId is ever blank, uniqueness protections won't work.
                    if (string.IsNullOrWhiteSpace(fitId))
                        throw new InvalidOperationException("FitId must not be blank.");

                    var transaction = new eFinance.Data.Models.Transaction
                    {
                        AccountId = accountId,
                        PostedDate = date,
                        Description = desc,
                        Amount = amount,
                        Category = null, // legacy; keep null
                        FitId = fitId,
                        Source = SourceName,
                        CreatedUtc = DateTime.UtcNow,
                        Memo = memo
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
                    System.Diagnostics.Debug.WriteLine("AMEX import row failed: " + ex);
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"AMEX import finished. Inserted={inserted}, Ignored={ignored}, Failed={failed}");

            return new ImportResult(inserted, ignored, failed);
        }

        private static string BuildBaseKey(
            long accountId,
            DateOnly date,
            decimal amount,
            string description,
            string? accountNo,
            string? cardMember,
            string? memo,
            string? txnId)
        {
            var normDesc = NormalizeKeyText(description);
            var normAcct = NormalizeKeyText(accountNo);
            var normMember = NormalizeKeyText(cardMember);
            var normMemo = NormalizeKeyText(memo);
            var normTxnId = NormalizeKeyText(txnId);

            var normAmountText = amount.ToString("0.00", CultureInfo.InvariantCulture);

            // Keep the key purely data-based and deterministic.
            // Include accountId to avoid collisions across accounts.
            return $"{accountId}|{date:yyyyMMdd}|{normAmountText}|{normDesc}|{normAcct}|{normMember}|{normMemo}|{normTxnId}";
        }

        private static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);

            // Full hex avoids collisions best.
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
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