namespace KukiFinance.Models
{
    public class ExpenseRecord
    {
        public string Date { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public decimal Amount { get; set; }
    }
}