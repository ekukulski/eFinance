namespace eFinance.Importing
{
    public sealed class DuplicateAuditOptions
    {
        // How far back to scan
        public int LookbackDays { get; init; } = 120;

        // Consider potential duplicates only if amounts match exactly (recommended)
        public bool RequireExactAmountMatch { get; init; } = true;

        // Date window for near duplicates (same amount, within +/- N days)
        public int NearDuplicateDateWindowDays { get; init; } = 3;

        // Similarity threshold (0..1). 0.75 is conservative.
        public double SimilarityThreshold { get; init; } = 0.78;

        // Limit results so UI stays responsive
        public int MaxResults { get; init; } = 200;
    }
}
