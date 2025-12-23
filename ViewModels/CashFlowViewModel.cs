using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;

namespace KukiFinance.ViewModels;

public class CashFlowProjection
{
    public string Month { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal EndingBalance { get; set; }
}

public class CashFlowDetail
{
    public string Category { get; set; }
    public decimal Amount { get; set; }
}

public class CashFlowViewModel : INotifyPropertyChanged
{
    public ObservableCollection<CashFlowProjection> Projections { get; set; } = new();
    public ObservableCollection<CashFlowDetail> SelectedMonthIncome { get; set; } = new();
    public ObservableCollection<CashFlowDetail> SelectedMonthExpenses { get; set; } = new();

    private CashFlowProjection? _selectedProjection;
    public CashFlowProjection? SelectedProjection
    {
        get => _selectedProjection;
        set
        {
            if (_selectedProjection != value)
            {
                _selectedProjection = value;
                OnPropertyChanged(nameof(SelectedProjection));
                LoadDetailsForSelectedMonth();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CashFlowViewModel()
    {
        LoadProjections();
        // Debug output to verify data loading
        System.Diagnostics.Debug.WriteLine($"Loaded {Projections.Count} projections.");
        // Add a dummy item if no data loaded (for troubleshooting)
        if (Projections.Count == 0)
        {
            Projections.Add(new CashFlowProjection
            {
                Month = "Test",
                OpeningBalance = 1000,
                Income = 500,
                Expenses = -200,
                EndingBalance = 1300
            });
        }
    }

    private void LoadProjections()
    {
        string bmoFile = FilePathHelper.GetKukiFinancePath("BMOCheckCurrent.csv");
        string forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");
        decimal openingBalance = 0;
        DateTime now = DateTime.Today;
        DateTime firstOfMonth = new DateTime(now.Year, now.Month, 1);

        // Get opening balance
        if (File.Exists(bmoFile))
        {
            var lines = File.ReadAllLines(bmoFile);
            foreach (var line in lines.Skip(1).Reverse())
            {
                var parts = line.Split(',');
                if (parts.Length >= 5 &&
                    DateTime.TryParse(parts[0], out var date) &&
                    date <= firstOfMonth &&
                    decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
                {
                    openingBalance = bal;
                    break;
                }
            }
        }

        // Define income categories
        var incomeCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Income - Interest",
            "Income - Other",
            "Income - Reimbursement",
            "Salary - Ed",
            "Salary - Patti"
        };

        Projections.Clear();
        decimal prevEndingBalance = openingBalance;

        for (int i = 0; i < 12; i++)
        {
            var monthDate = firstOfMonth.AddMonths(i);
            var monthName = monthDate.ToString("MMMM", CultureInfo.InvariantCulture);
            var yearMonth = monthDate.ToString("yyyy-MM");

            decimal income = 0;
            decimal expenses = 0;

            if (File.Exists(forecastFile))
            {
                var lines = File.ReadAllLines(forecastFile);
                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 5)
                    {
                        var frequency = parts[0].Trim();
                        var forecastMonth = parts[1].Trim();
                        var category = parts[3].Trim();
                        var amountStr = parts[4].Trim();

                        if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                            continue;

                        // Frequency logic
                        bool include = false;
                        var forecastMonthName = forecastMonth.ToLowerInvariant();
                        var currentMonthName = monthName.ToLowerInvariant();

                        if (frequency == "Monthly" || frequency == "All")
                            include = true;
                        else if (frequency == "Annual" && forecastMonthName == currentMonthName)
                            include = true;
                        else if (frequency == "Once" && forecastMonthName == currentMonthName)
                            include = true;
                        else if (frequency.EndsWith("Months"))
                        {
                            int n;
                            if (int.TryParse(frequency.Split(' ')[0], out n))
                            {
                                int startMonthNum = MonthNameToNumber(forecastMonthName);
                                int globalMonthIndex = ((monthDate.Year - firstOfMonth.Year) * 12) + (monthDate.Month - firstOfMonth.Month);
                                if (startMonthNum > 0 && globalMonthIndex >= (startMonthNum - 1) && ((globalMonthIndex - (startMonthNum - 1)) % n == 0))
                                    include = true;
                            }
                        }

                        if (include)
                        {
                            if (incomeCategories.Contains(category) && amt > 0)
                                income += amt;
                            else if (amt < 0)
                                expenses += amt;
                        }
                    }
                }
            }

            decimal ending = prevEndingBalance + income + expenses;

            Projections.Add(new CashFlowProjection
            {
                Month = yearMonth,
                OpeningBalance = prevEndingBalance,
                Income = income,
                Expenses = expenses,
                EndingBalance = ending
            });

            prevEndingBalance = ending;
        }
    }

    private void LoadDetailsForSelectedMonth()
    {
        SelectedMonthIncome.Clear();
        SelectedMonthExpenses.Clear();

        if (SelectedProjection == null)
            return;

        string forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");
        var incomeCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Income - Interest",
        "Income - Other",
        "Income - Reimbursement",
        "Salary - Ed",
        "Salary - Patti"
    };

        var incomeList = new List<CashFlowDetail>();
        var expenseList = new List<CashFlowDetail>();

        if (File.Exists(forecastFile))
        {
            var lines = File.ReadAllLines(forecastFile);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length >= 5)
                {
                    var frequency = parts[0].Trim();
                    var forecastMonth = parts[1].Trim();
                    var category = parts[3].Trim();
                    var amountStr = parts[4].Trim();

                    if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                        continue;

                    var forecastMonthName = forecastMonth.ToLowerInvariant();
                    var selectedMonthDate = DateTime.ParseExact(SelectedProjection.Month, "yyyy-MM", CultureInfo.InvariantCulture);
                    var selectedMonthName = selectedMonthDate.ToString("MMMM", CultureInfo.InvariantCulture).ToLowerInvariant();

                    bool include = false;
                    if (frequency == "Monthly" || frequency == "All")
                        include = true;
                    else if (frequency == "Annual" && forecastMonthName == selectedMonthName)
                        include = true;
                    else if (frequency == "Once" && forecastMonthName == selectedMonthName)
                        include = true;
                    else if (frequency.EndsWith("Months"))
                    {
                        int n;
                        if (int.TryParse(frequency.Split(' ')[0], out n))
                        {
                            int startMonthNum = MonthNameToNumber(forecastMonthName);
                            var firstOfMonth = new DateTime(selectedMonthDate.Year, selectedMonthDate.Month, 1);
                            int globalMonthIndex = ((selectedMonthDate.Year - firstOfMonth.Year) * 12) + (selectedMonthDate.Month - firstOfMonth.Month);
                            if (startMonthNum > 0 && globalMonthIndex >= (startMonthNum - 1) && ((globalMonthIndex - (startMonthNum - 1)) % n == 0))
                                include = true;
                        }
                    }

                    if (include)
                    {
                        if (incomeCategories.Contains(category) && amt > 0)
                            incomeList.Add(new CashFlowDetail { Category = category, Amount = amt });
                        else if (amt < 0)
                            expenseList.Add(new CashFlowDetail { Category = category, Amount = amt });
                    }
                }
            }
        }

        foreach (var item in incomeList.OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase))
            SelectedMonthIncome.Add(item);

        foreach (var item in expenseList.OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase))
            SelectedMonthExpenses.Add(item);
    }

    private int MonthNameToNumber(string monthName)
    {
        switch (monthName)
        {
            case "january": return 1;
            case "february": return 2;
            case "march": return 3;
            case "april": return 4;
            case "may": return 5;
            case "june": return 6;
            case "july": return 7;
            case "august": return 8;
            case "september": return 9;
            case "october": return 10;
            case "november": return 11;
            case "december": return 12;
            default: return 0;
        }
    }

    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}