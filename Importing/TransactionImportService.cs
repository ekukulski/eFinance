using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using eFinance.Data;
using eFinance.Data.Models;
using eFinance.Services;
using TransactionModel = eFinance.Data.Models.Transaction;

namespace eFinance.Importing
{
    public sealed class TransactionImportService
    {
        private readonly SqliteDatabase _db;
        private readonly CategorizationService _categorizer;

        public TransactionImportService(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _categorizer = new CategorizationService(db);
        }

        public async Task ImportAmexCsvAsync(long accountId, string filePath, CancellationToken ct = default)
        {
            foreach (var row in CsvReader.Read(filePath))
            {
                // TODO: adjust these column names to match your AMEX CSV headers
                var postedDateText = row.Get("Date") ?? row.Get("PostedDate");
                var description = row.Get("Description") ?? "";
                var amountText = row.Get("Amount") ?? "0";

                if (string.IsNullOrWhiteSpace(postedDateText) || string.IsNullOrWhiteSpace(description))
                    continue;

                // Parse date (adjust format if needed)
                var postedDate = DateOnly.Parse(postedDateText, CultureInfo.InvariantCulture);

                // Parse amount
                var amount = decimal.Parse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture);

                var tx = new Transaction
                {
                    AccountId = accountId,
                    PostedDate = postedDate,
                    Description = description,
                    Amount = amount,
                    Source = "AMEX",
                    CreatedUtc = DateTime.UtcNow
                };

                // ✅ AUTO-CATEGORIZE HERE
                var match = await _categorizer.CategorizeAsync(tx.Description, ct);
                if (match is not null)
                {
                    tx.CategoryId = match.CategoryId;
                    tx.MatchedRuleId = match.RuleId;
                    tx.MatchedRulePattern = match.Pattern;
                    tx.CategorizedUtc = DateTime.UtcNow;
                }

                // TODO: call your existing insert method here
                // await _db.InsertTransactionAsync(tx, ct);
            }
        }
    }
}
