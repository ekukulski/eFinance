using System;
using TransactionModel = eFinance.Data.Models.Transaction;

namespace eFinance.Data.Models
{
    public sealed class Transaction
    {
        public long Id { get; set; }
        public long AccountId { get; set; }

        public DateOnly PostedDate { get; set; }

        public string Description { get; set; } = "";
        public decimal Amount { get; set; }

        public string? Category { get; set; }
        public long? CategoryId { get; set; }

        public string? Memo { get; set; } 

        public string? FitId { get; set; }
        public string? Source { get; set; }

        public DateTime? CreatedUtc { get; set; }
        public DateTime? CategorizedUtc { get; set; }

        public long? MatchedRuleId { get; set; }
        public string? MatchedRulePattern { get; set; }
        public int IsDeleted { get; set; }
    }

}
