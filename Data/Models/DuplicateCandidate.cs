namespace eFinance.Data.Models
{
    public enum DuplicateType
    {
        Exact,
        Near
    }

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
        public double Score { get; init; } // 0..1
        public string Reason { get; init; } = string.Empty;
    }
}
