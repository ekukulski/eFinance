using System.Collections.ObjectModel;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using KukiFinance.Helpers;
using System.IO;

namespace KukiFinance.ViewModels;

public sealed class CashFlowProjection
{
    public string Month { get; set; } = "";
    public decimal OpeningBalance { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal EndingBalance { get; set; }
}

public sealed class CashFlowDetail
{
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
}

internal sealed class ForecastItem
{
    public string Frequency { get; set; } = "";
    public string ForecastMonth { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
}

public partial class CashFlowViewModel : ObservableObject
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        IgnoreBlankLines = true,
        TrimOptions = TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null
    };

    public ObservableCollection<CashFlowProjection> Projections { get; } = new();
    public ObservableCollection<CashFlowDetail> SelectedMonthIncome { get; } = new();
    public ObservableCollection<CashFlowDetail> SelectedMonthExpenses { get; } = new();

    [ObservableProperty]
    private CashFlowProjection? selectedProjection;

    [ObservableProperty]
    private string? loadError;

    public bool HasError => !string.IsNullOrWhiteSpace(LoadError);

    public CashFlowViewModel()
    {
        LoadProjections();
    }

    partial void OnLoadErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnSelectedProjectionChanged(CashFlowProjection? value)
    {
        LoadDetailsForSelectedMonth();
    }

    public void Reload()
    {
        LoadProjections();
        LoadDetailsForSelectedMonth();
    }

    private void LoadProjections()
    {
        LoadError = null;
        Projections.Clear();

        try
        {
            string bmoFile = FilePathHelper.GetKukiFinancePath("BMOCheckCurrent.csv");
            string forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");

            DateTime today = DateTime.Today;
            DateTime firstOfThisMonth = new(today.Year, today.Month, 1);

            decimal openingBalance = GetOpeningBalanceFromBmo(bmoFile, firstOfThisMonth);

            // Read forecast once, strongly and currency-safe
            var forecastItems = ReadForecastItems(forecastFile);

            decimal prevEndingBalance = openingBalance;

            for (int i = 0; i < 12; i++)
            {
                var monthDate = firstOfThisMonth.AddMonths(i);
                var monthLabel = monthDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

                decimal income = 0;
                decimal expenses = 0;

                foreach (var item in forecastItems)
                {
                    if (!ShouldInclude(item.Frequency, item.ForecastMonth, monthDate, firstOfThisMonth))
                        continue;

                    if (item.Amount > 0)
                        income += item.Amount;
                    else if (item.Amount < 0)
                        expenses += item.Amount;
                }

                var ending = prevEndingBalance + income + expenses;

                Projections.Add(new CashFlowProjection
                {
                    Month = monthLabel,
                    OpeningBalance = prevEndingBalance,
                    Income = income,
                    Expenses = expenses,
                    EndingBalance = ending
                });

                prevEndingBalance = ending;
            }

            SelectedProjection = Projections.FirstOrDefault();
        }
        catch (Exception ex)
        {
            LoadError = $"Cash flow load failed: {ex.Message}";
        }
    }

    private void LoadDetailsForSelectedMonth()
    {
        SelectedMonthIncome.Clear();
        SelectedMonthExpenses.Clear();

        if (SelectedProjection is null)
            return;

        try
        {
            string forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");

            var forecastItems = ReadForecastItems(forecastFile);

            var selectedMonthDate = DateTime.ParseExact(
                SelectedProjection.Month,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            var selectedFirstOfMonth = new DateTime(selectedMonthDate.Year, selectedMonthDate.Month, 1);

            DateTime today = DateTime.Today;
            DateTime firstOfThisMonth = new(today.Year, today.Month, 1);

            foreach (var item in forecastItems)
            {
                if (!ShouldInclude(item.Frequency, item.ForecastMonth, selectedFirstOfMonth, firstOfThisMonth))
                    continue;

                if (item.Amount > 0)
                    SelectedMonthIncome.Add(new CashFlowDetail { Category = item.Category, Amount = item.Amount });
                else if (item.Amount < 0)
                    SelectedMonthExpenses.Add(new CashFlowDetail { Category = item.Category, Amount = item.Amount });
            }
        }
        catch (Exception ex)
        {
            LoadError ??= $"Cash flow detail load failed: {ex.Message}";
        }
    }

    private static decimal GetOpeningBalanceFromBmo(string bmoFile, DateTime firstOfMonth)
    {
        if (!File.Exists(bmoFile))
            throw new FileNotFoundException($"Required file not found: {bmoFile}", bmoFile);

        decimal openingBalance = 0;

        using var reader = new StreamReader(bmoFile);
        using var csv = new CsvReader(reader, CsvConfig);

        // Column assumptions from your prior logic: col0 = date, col4 = balance
        var rows = new List<string[]>();
        while (csv.Read())
        {
            var row = csv.Parser.Record;
            if (row is null || row.Length == 0) continue;
            rows.Add(row);
        }

        for (int i = rows.Count - 1; i >= 0; i--)
        {
            var r = rows[i];
            if (r.Length < 5) continue;

            if (!DateTime.TryParse(r[0], CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var date) &&
                !DateTime.TryParse(r[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
                continue;

            if (date > firstOfMonth)
                continue;

            if (TryParseMoney(r[4], out var bal))
            {
                openingBalance = bal;
                break;
            }
        }

        return openingBalance;
    }

    private static List<ForecastItem> ReadForecastItems(string forecastFile)
    {
        if (!File.Exists(forecastFile))
            throw new FileNotFoundException($"Required file not found: {forecastFile}", forecastFile);

        using var reader = new StreamReader(forecastFile);
        using var csv = new CsvReader(reader, CsvConfig);

        // Read header so we can locate columns by name (robust)
        if (!csv.Read())
            return new List<ForecastItem>();

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        int idxFrequency = FindIndex(headers, "Frequency", "Freq");
        int idxMonth = FindIndex(headers, "Month", "StartMonth", "ForecastMonth");
        int idxCategory = FindIndex(headers, "Category");
        int idxAmount = FindIndex(headers, "Amount", "Value");

        // Fallback to your original index assumptions if headers are missing
        if (idxFrequency < 0) idxFrequency = 0;
        if (idxMonth < 0) idxMonth = 1;
        if (idxCategory < 0) idxCategory = 3;
        if (idxAmount < 0) idxAmount = 4;

        var list = new List<ForecastItem>();

        while (csv.Read())
        {
            string frequency = SafeGet(csv, idxFrequency);
            string month = SafeGet(csv, idxMonth);
            string category = SafeGet(csv, idxCategory);
            string amountStr = SafeGet(csv, idxAmount);

            if (!TryParseMoney(amountStr, out var amt))
                continue;

            list.Add(new ForecastItem
            {
                Frequency = frequency,
                ForecastMonth = month,
                Category = category,
                Amount = amt
            });
        }

        return list;
    }

    private static string SafeGet(CsvReader csv, int index)
    {
        try { return (csv.GetField(index) ?? "").Trim(); }
        catch { return ""; }
    }

    private static int FindIndex(string[] headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var h = (headers[i] ?? "").Trim();
            foreach (var c in candidates)
            {
                if (string.Equals(h, c, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    private static bool TryParseMoney(string? s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Try currency in current culture first (handles $ and local formatting)
        if (decimal.TryParse(s, NumberStyles.Currency, CultureInfo.CurrentCulture, out value))
            return true;

        // Try currency in invariant (handles $ in many cases too)
        if (decimal.TryParse(s, NumberStyles.Currency, CultureInfo.InvariantCulture, out value))
            return true;

        // Last resort: strip common symbols and retry
        var cleaned = s.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static bool ShouldInclude(string frequency, string forecastMonth, DateTime targetMonth, DateTime baseMonth)
    {
        var freq = (frequency ?? "").Trim();

        // Treat blank as monthly
        if (string.IsNullOrEmpty(freq) ||
            string.Equals(freq, "Monthly", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(freq, "All", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(freq, "Every Month", StringComparison.OrdinalIgnoreCase))
            return true;

        int startMonthNum = MonthNameToNumber(forecastMonth);
        if (startMonthNum <= 0) return false;

        if (string.Equals(freq, "Annual", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(freq, "Once", StringComparison.OrdinalIgnoreCase))
        {
            return targetMonth.Month == startMonthNum;
        }

        if (freq.EndsWith("Months", StringComparison.OrdinalIgnoreCase))
        {
            var firstToken = freq.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!int.TryParse(firstToken, out int n) || n <= 0) return false;

            var start = new DateTime(baseMonth.Year, startMonthNum, 1);
            if (start < baseMonth)
                start = start.AddYears(1);

            if (targetMonth < start)
                return false;

            int monthsDiff = (targetMonth.Year - start.Year) * 12 + (targetMonth.Month - start.Month);
            return monthsDiff % n == 0;
        }

        return false;
    }

    private static int MonthNameToNumber(string month)
    {
        if (string.IsNullOrWhiteSpace(month))
            return 0;

        if (DateTime.TryParseExact(month.Trim(),
            new[] { "MMMM", "MMM" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dt))
        {
            return dt.Month;
        }

        return month.Trim().ToLowerInvariant() switch
        {
            "january" => 1,
            "february" => 2,
            "march" => 3,
            "april" => 4,
            "may" => 5,
            "june" => 6,
            "july" => 7,
            "august" => 8,
            "september" => 9,
            "october" => 10,
            "november" => 11,
            "december" => 12,
            _ => 0
        };
    }
}
