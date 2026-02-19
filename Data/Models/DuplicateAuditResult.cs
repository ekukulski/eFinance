namespace eFinance.Data.Models
{
    public sealed class DuplicateAuditResult
    {
        public long OriginalTransactionId { get; set; }
        public long SuspectedDuplicateTransactionId { get; set; }

        public DateTime OriginalDate { get; set; }
        public DateTime DuplicateDate { get; set; }

        public string? Description { get; set; }

        public decimal OriginalAmount { get; set; }
        public decimal DuplicateAmount { get; set; }

        public bool IsExactAmountMatch { get; set; }
    }
}
