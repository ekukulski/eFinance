using System.IO;
using System.Text;
using KukiFinance.Converters;
using KukiFinance.Models;
using KukiFinance.Services;
using KukiFinance.ViewModels;

namespace KukiFinance.Pages;
public partial class AverageExpenses : ContentPage
{
    private AverageExpenseViewModel _viewModel;

    public AverageExpenses()
    {
        InitializeComponent();
        _viewModel = new AverageExpenseViewModel();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ExportAverageExpensesToCsv();
    }

    private async void OnCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CategoryAverageExpense selected)
        {
            _viewModel.UpdateSelectedCategoryTotal(selected.Category);

            var expenses = _viewModel.AllExpenseRecords
                .Where(r => string.Equals(
                    r.Category?.Trim(),
                    selected.Category?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => DateTime.Parse(r.Date))
                .ToList();

            await Shell.Current.GoToAsync(
                nameof(ExpenseRecordPage),
                new Dictionary<string, object>
                {
                    ["Category"] = selected.Category,
                    ["Expenses"] = expenses
                });
        }
    }

    private void ExportAverageExpensesToCsv()
    {
        if (_viewModel.CategoryAverages.Count == 0)
            return;

        var filePath = FilePathHelper.GetKukiFinancePath("AverageExpenses.csv");

        var sb = new StringBuilder();
        sb.AppendLine("Category,Frequency,AverageExpense");

        foreach (var item in _viewModel.CategoryAverages)
        {
            var category = item.Category.Contains(',') ? $"\"{item.Category}\"" : item.Category;
            sb.AppendLine($"{category},{item.Frequency},{item.AverageExpense}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    private async void ReturnButton_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }
}