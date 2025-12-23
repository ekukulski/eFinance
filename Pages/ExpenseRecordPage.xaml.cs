using KukiFinance.Models;
using System.Collections.ObjectModel;
using KukiFinance.Services;

namespace KukiFinance.Pages;

public partial class ExpenseRecordPage : ContentPage
{
    public string Category { get; set; }
    public ObservableCollection<ExpenseRecord> Expenses { get; set; } = new();
    public decimal TotalExpense { get; set; }

    public ExpenseRecordPage(string category, IEnumerable<ExpenseRecord> expenses)
    {
        InitializeComponent();
        Category = category;
        foreach (var exp in expenses)
            Expenses.Add(exp);
        TotalExpense = Expenses.Sum(e => e.Amount);
        BindingContext = this;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        WindowCenteringService.CenterWindow(1000, 1400);
    }

    private async void ReturnButton_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}