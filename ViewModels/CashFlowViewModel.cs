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

    public CashFlowViewModel()
    {
        LoadProjections();
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

            var incomeCategories = GetIncomeCategories();

            // Read forecast rows once
            var forecastRows = ReadCsvRows(forecastFile).ToList();

            decimal prevEndingBalance = openingBalance;

            for (int i = 0; i < 12; i++)
            {
                var monthDate = firstOfThisMonth.AddMonths(i);
                var monthLabel = monthDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

                decimal income = 0;
                decimal expenses = 0;

                foreach (var row in forecastRows)
                {
                    if (row.Length < 5) continue;

                    var frequency = (row[0] ?? "").Trim();
                    var forecastMonth = (row[1] ?? "").Trim();
                    var category = (row[3] ?? "").Trim();
                    var amountStr = (row[4] ?? "").Trim();

                    if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                        continue;

                    if (!ShouldInclude(frequency, forecastMonth, monthDate, firstOfThisMonth))
                        continue;

                    if (incomeCategories.Contains(category) && amt > 0)
                        income += amt;
                    else if (amt < 0)
                        expenses += amt;
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
        catch (FileNotFoundException ex)
        {
            LoadError = ex.Message;
        }
        catch (InvalidDataException ex)
        {
            LoadError = ex.Message;
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
            var forecastRows = ReadCsvRows(forecastFile).ToList();

            var incomeCategories = GetIncomeCategories();

            // SelectedProjection.Month is "yyyy-MM"
            var selectedMonthDate = DateTime.ParseExact(
                SelectedProjection.Month,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            var selectedFirstOfMonth = new DateTime(selectedMonthDate.Year, selectedMonthDate.Month, 1);

            // Base month for relative calculations (must match projections generation)
            DateTime today = DateTime.Today;
            DateTime firstOfThisMonth = new(today.Year, today.Month, 1);

            foreach (var row in forecastRows)
            {
                if (row.Length < 5) continue;

                var frequency = (row[0] ?? "").Trim();
                var forecastMonth = (row[1] ?? "").Trim();
                var category = (row[3] ?? "").Trim();
                var amountStr = (row[4] ?? "").Trim();

                if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                    continue;

                if (!ShouldInclude(frequency, forecastMonth, selectedFirstOfMonth, firstOfThisMonth))
                    continue;

                if (incomeCategories.Contains(category) && amt > 0)
                {
                    SelectedMonthIncome.Add(new CashFlowDetail { Category = category, Amount = amt });
                }
                else if (amt < 0)
                {
                    SelectedMonthExpenses.Add(new CashFlowDetail { Category = category, Amount = amt });
                }
            }
        }
        catch (Exception ex)
        {
            // Don’t overwrite a file-level error from LoadProjections if it already exists
            LoadError ??= $"Cash flow detail load failed: {ex.Message}";
        }
    }

    private static HashSet<string> GetIncomeCategories() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Income - Interest",
            "Income - Other",
            "Income - Reimbursement",
            "Salary - Ed",
            "Salary - Patti"
        };

    private static decimal GetOpeningBalanceFromBmo(string bmoFile, DateTime firstOfMonth)
    {
        if (!File.Exists(bmoFile))
            throw new FileNotFoundException($"Required file not found: {bmoFile}", bmoFile);

        // Your original logic used column 0 = date, column 4 = balance.
        // We keep that assumption but parse using CsvHelper safely.
        decimal openingBalance = 0;

        // Read all rows, then walk backwards to find the last balance <= firstOfMonth
        var rows = ReadCsvRows(bmoFile).ToList();

        for (int i = rows.Count - 1; i >= 0; i--)
        {
            var r = rows[i];
            if (r.Length < 5) continue;

            if (!DateTime.TryParse(r[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                continue;

            if (date > firstOfMonth)
                continue;

            if (decimal.TryParse(r[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
            {
                openingBalance = bal;
                break;
            }
        }

        return openingBalance;
    }

    private static IEnumerable<string[]> ReadCsvRows(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required file not found: {path}", path);

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CsvConfig);

        while (csv.Read())
        {
            var row = csv.Parser.Record;
            if (row is null || row.Length == 0) continue;
            yield return row;
        }
    }

    /// <summary>
    /// Determines whether a forecast row should be applied to the target month.
    ///
    /// frequency examples expected:
    /// - "Monthly" or "All" => every month
    /// - "Annual" => only when target month == forecastMonth
    /// - "Once" => only when target month == forecastMonth (within 12-month window)
    /// - "N Months" (e.g., "3 Months") => every N months starting at forecastMonth
    /// </summary>
    private static bool ShouldInclude(string frequency, string forecastMonth, DateTime targetMonth, DateTime baseMonth)
    {
        var freq = (frequency ?? "").Trim();

        if (string.Equals(freq, "Monthly", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(freq, "All", StringComparison.OrdinalIgnoreCase))
            return true;

        int startMonthNum = MonthNameToNumber(forecastMonth);
        if (startMonthNum <= 0) return false;

        // Annual / Once: include when month matches (and the target month is within our projection window naturally)
        if (string.Equals(freq, "Annual", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(freq, "Once", StringComparison.OrdinalIgnoreCase))
        {
            return targetMonth.Month == startMonthNum;
        }

        // N Months: include if targetMonth is on the schedule starting at forecastMonth
        // Define a start date that is the first occurrence of forecastMonth on/after baseMonth.
        if (freq.EndsWith("Months", StringComparison.OrdinalIgnoreCase))
        {
            var firstToken = freq.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!int.TryParse(firstToken, out int n) || n <= 0) return false;

            var start = new DateTime(baseMonth.Year, startMonthNum, 1);
            if (start < baseMonth)
                start = start.AddYears(1); // next year’s occurrence

            if (targetMonth < start)
                return false;

            int monthsDiff = MonthsBetween(start, targetMonth);
            return monthsDiff % n == 0;
        }

        return false;
    }

    private static int MonthsBetween(DateTime start, DateTime end)
        => (end.Year - start.Year) * 12 + (end.Month - start.Month);

    private static int MonthNameToNumber(string month)
    {
        if (string.IsNullOrWhiteSpace(month))
            return 0;

        // Handles "January", "Jan", etc. by letting DateTime parse it
        // but we’ll also keep a safe fallback for odd formats.
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
