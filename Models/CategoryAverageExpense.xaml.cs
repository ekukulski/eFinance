namespace KukiFinance.Models
{
    public class CategoryAverageExpense
    {
        public string Category { get; set; }
        public int Frequency { get; set; }
        public decimal AverageExpense { get; set; }
    }
}