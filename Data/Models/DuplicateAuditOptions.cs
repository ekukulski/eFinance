namespace eFinance.Data.Models
{
    public sealed class DuplicateAuditOptions
    {
        public int LookbackDays { get; init; } = 120;
        public int NearDuplicateDateWindowDays { get; init; } = 3;
        public double SimilarityThreshold { get; init; } = 0.78;
        public int MaxResults { get; init; } = 200;
    }
}
