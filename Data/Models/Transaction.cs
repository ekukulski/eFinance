using System;

namespace eFinance.Data.Models
{
    public sealed class Transaction
    {
        public long Id { get; set; }
        public long AccountId { get; set; }

        // Store as DateOnly in code, persist as yyyy-MM-dd
        public DateOnly PostedDate { get; set; }

        public string Description { get; set; } = "";
        public decimal Amount { get; set; }

        // Legacy / transitional: keep if you already store category text today.
        // Going forward, prefer CategoryId + Categories table.
        public string? Category { get; set; }

        // New: normalized categorization via Categories table
        public long? CategoryId { get; set; }

        // New: audit/debug - which rule assigned the category
        public long? MatchedRuleId { get; set; }
        public string? MatchedRulePattern { get; set; }

        // New: when categorization was applied (UTC)
        public DateTime? CategorizedUtc { get; set; }

        public string? FitId { get; set; }
        public string? Source { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
