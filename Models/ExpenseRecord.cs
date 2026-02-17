namespace eFinance.Models
{
    public class ExpenseRecord
    {
        public string Date { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}