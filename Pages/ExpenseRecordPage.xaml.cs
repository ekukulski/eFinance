using System.Collections.ObjectModel;
using eFinance.Services;
using eFinance.Models;
using eFinance.Models;
using System.Collections.ObjectModel;
using eFinance.Services;

namespace eFinance.Pages;

public partial class ExpenseRecordPage : ContentPage, IQueryAttributable
{
    public string Category { get; set; } = string.Empty;
    public ObservableCollection<ExpenseRecord> Expenses { get; set; } = new();
    public decimal TotalExpense { get; set; }

    public ExpenseRecordPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Category = (string)query["Category"];
        var expenses = (IEnumerable<ExpenseRecord>)query["Expenses"];

        Expenses.Clear();
        foreach (var exp in expenses)
            Expenses.Add(exp);

        TotalExpense = Expenses.Sum(e => e.Amount);

        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Expenses));
        OnPropertyChanged(nameof(TotalExpense));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
    }

    private async void ReturnButton_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
