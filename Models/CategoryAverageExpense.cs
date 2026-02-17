namespace eFinance.Models
{
    public class CategoryAverageExpense
    {
        public string Category { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public decimal AverageExpense { get; set; }
    }
}