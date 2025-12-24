using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KukiFinance.Services;

namespace KukiFinance.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly INavigationService _nav;

    public MainPageViewModel(INavigationService nav)
    {
        _nav = nav;
    }

    [RelayCommand]
    private Task OpenFinanceSummary() =>
        _nav.GoToAsync(nameof(Pages.FinanceSummaryPage));

    [RelayCommand]
    private Task OpenExpenses() =>
        _nav.GoToAsync(nameof(Pages.ExpensePage));

    [RelayCommand]
    private Task OpenAverageExpenses() =>
        _nav.GoToAsync(nameof(Pages.AverageExpenses));

    [RelayCommand]
    private Task OpenCashFlow() =>
        _nav.GoToAsync(nameof(Pages.CashFlowPage));

    [RelayCommand]
    private Task OpenCalendar() =>
        _nav.GoToAsync(nameof(Pages.CalendarPage));

    [RelayCommand]
    private Task OpenMidland() =>
        _nav.GoToAsync(nameof(Pages.MidlandRegisterPage));

    [RelayCommand]
    private Task OpenCharlesSchwabContributory() =>
        _nav.GoToAsync(nameof(Pages.CharlesSchwabContributoryRegisterPage));
    [RelayCommand]
    private Task OpenCharlesSchwabJointTenant() => 
        _nav.GoToAsync(nameof(Pages.CharlesSchwabJointTenantRegisterPage));

    [RelayCommand]
    private Task OpenCharlesSchwabRothIRAEd() =>
        _nav.GoToAsync(nameof(Pages.CharlesSchwabRothIraEdRegisterPage));
    [RelayCommand]
    private Task OpenCharlesSchwabRothIRAPatti() =>
        _nav.GoToAsync(nameof(Pages.CharlesSchwabRothIraPattiRegisterPage));

    [RelayCommand]
    private Task OpenNetX() =>
        _nav.GoToAsync(nameof(Pages.NetXRegisterPage));
    [RelayCommand]
    private Task OpenHealthPro() =>
        _nav.GoToAsync(nameof(Pages.HealthProRegisterPage));
    [RelayCommand]

    private Task OpenSelect401K() => 
        _nav.GoToAsync(nameof(Pages.ExpensePage));
    [RelayCommand]
    private Task OpenGold() => 
        _nav.GoToAsync(nameof(Pages.Select401KRegisterPage));
    [RelayCommand]
    private Task OpenHouse() => 
        _nav.GoToAsync(nameof(Pages.HouseRegisterPage));
    [RelayCommand]
    private Task OpenChevroletImpala() => 
        _nav.GoToAsync(nameof(Pages.ChevroletImpalaRegisterPage));
    [RelayCommand]
    private Task OpenNissanSentra() =>
        _nav.GoToAsync(nameof(Pages.NissanSentraRegisterPage));
    [RelayCommand]
    private Task OpenCash() => 
        _nav.GoToAsync(nameof(Pages.CashRegisterPage));
    [RelayCommand]
    private Task OpenBmoCheckRegister() => 
        _nav.GoToAsync(nameof(Pages.ExpensePage));
    [RelayCommand]
    private Task OpenBmoMoneyMarketRegister() => 
        _nav.GoToAsync(nameof(Pages.BmoCheckRegisterPage));
    [RelayCommand]
    private Task OpenBmoCdRegister() => 
        _nav.GoToAsync(nameof(Pages.BmoCdRegisterPage));
    [RelayCommand]
    private Task OpenAMEX() => 
        _nav.GoToAsync(nameof(Pages.AmexRegisterPage));
    [RelayCommand]
    private Task OpenVisa() => 
        _nav.GoToAsync(nameof(Pages.VisaRegisterPage));
    [RelayCommand]
    private Task OpenMasterCard() => 
        _nav.GoToAsync(nameof(Pages.MasterCardRegisterPage));
    [RelayCommand]
    private Task OpenCheckNumber() => 
        _nav.GoToAsync(nameof(Pages.CheckNumberPage));
    [RelayCommand]
    private Task OpenCategory() => 
        _nav.GoToAsync(nameof(Pages.CategoryPage));
    [RelayCommand]
    private Task OpenCategoryList() => 
        _nav.GoToAsync(nameof(Pages.CategoryListPage));
    [RelayCommand]
    private Task OpenOpeningBalances() => 
        _nav.GoToAsync(nameof(Pages.OpeningBalancesPage));
    [RelayCommand]
    private Task OpenExcludedCategories() => 
        _nav.GoToAsync(nameof(Pages.ExcludedCategoriesPage));
    [RelayCommand]
    private Task OpenForecastExpenses() => 
        _nav.GoToAsync(nameof(Pages.ForecastExpensesPage));
    [RelayCommand]
    private void Exit()
    {
#if WINDOWS
    Application.Current?.Quit();
#endif
    }


}
