using System;

namespace eFinance.Importing
{
    public sealed class DuplicateCandidate
    {
        public long AccountId { get; init; }
        public string AccountName { get; init; } = string.Empty;

        public long A_Id { get; init; }
        public DateOnly A_Date { get; init; }
        public decimal A_Amount { get; init; }
        public string A_Description { get; init; } = string.Empty;
        public string? A_FitId { get; init; }

        public long B_Id { get; init; }
        public DateOnly B_Date { get; init; }
        public decimal B_Amount { get; init; }
        public string B_Description { get; init; } = string.Empty;
        public string? B_FitId { get; init; }

        public DuplicateType Type { get; init; }
        public double Score { get; init; } // 0..1 (higher = more likely duplicate)

        public string Reason { get; init; } = string.Empty;
    }

    public enum DuplicateType
    {
        Exact,      // same date + amount + (normalized) description (or same FitId)
        Near        // same amount + close dates + similar description
    }
}
