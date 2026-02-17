using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Pages;
using eFinance.Services;

namespace eFinance.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly INavigationService _nav;

    public MainPageViewModel(INavigationService nav) => _nav = nav;

    [RelayCommand] private Task OpenFinanceSummary() => _nav.GoToAsync(nameof(FinanceSummaryPage));
    [RelayCommand] private Task OpenCalendar() => _nav.GoToAsync(nameof(CalendarPage));
    [RelayCommand] private Task OpenExpense() => _nav.GoToAsync(nameof(ExpensePage));
    [RelayCommand] private Task OpenAverageExpense() => _nav.GoToAsync(nameof(AverageExpenses));
    [RelayCommand] private Task OpenCashFlow() => _nav.GoToAsync(nameof(CashFlowPage));

    [RelayCommand] private Task OpenMidland() => _nav.GoToAsync(nameof(MidlandRegisterPage));
    [RelayCommand] private Task OpenCharlesSchwabContributory() => _nav.GoToAsync(nameof(CharlesSchwabContributoryRegisterPage));
    [RelayCommand] private Task OpenCharlesSchwabJointTenant() => _nav.GoToAsync(nameof(CharlesSchwabJointTenantRegisterPage));
    [RelayCommand] private Task OpenCharlesSchwabRothIRAEd() => _nav.GoToAsync(nameof(CharlesSchwabRothIraEdRegisterPage));
    [RelayCommand] private Task OpenCharlesSchwabRothIRAPatti() => _nav.GoToAsync(nameof(CharlesSchwabRothIraPattiRegisterPage));

    [RelayCommand] private Task OpenNetX() => _nav.GoToAsync(nameof(NetXRegisterPage));
    [RelayCommand] private Task OpenHealthPro() => _nav.GoToAsync(nameof(HealthProRegisterPage));
    [RelayCommand] private Task OpenSelect401K() => _nav.GoToAsync(nameof(Select401KRegisterPage));
    [RelayCommand] private Task OpenGold() => _nav.GoToAsync(nameof(GoldRegisterPage));
    [RelayCommand] private Task OpenHouse() => _nav.GoToAsync(nameof(HouseRegisterPage));
    [RelayCommand] private Task OpenChevroletImpala() => _nav.GoToAsync(nameof(ChevroletImpalaRegisterPage));
    [RelayCommand] private Task OpenNissanSentra() => _nav.GoToAsync(nameof(NissanSentraRegisterPage));

    [RelayCommand] private Task OpenCash() => _nav.GoToAsync(nameof(CashRegisterPage));
    [RelayCommand] private Task OpenBmoCheckRegister() => _nav.GoToAsync(nameof(BmoCheckRegisterPage));
    [RelayCommand] private Task OpenBmoMoneyMarketRegister() => _nav.GoToAsync(nameof(BmoMoneyMarketRegisterPage));
    [RelayCommand] private Task OpenBmoCdRegister() => _nav.GoToAsync(nameof(BmoCdRegisterPage));

    [RelayCommand] private Task OpenAMEX() => _nav.GoToAsync(nameof(AmexRegisterPage));
    [RelayCommand] private Task OpenVisa() => _nav.GoToAsync(nameof(VisaRegisterPage));
    [RelayCommand] private Task OpenMasterCard() => _nav.GoToAsync(nameof(MasterCardRegisterPage));

    [RelayCommand] private Task OpenCheckNumber() => _nav.GoToAsync(nameof(CheckNumberPage));
    [RelayCommand] private Task OpenCategory() => _nav.GoToAsync(nameof(CategoryPage));
    [RelayCommand] private Task OpenCategoryList() => _nav.GoToAsync(nameof(CategoryListPage));
    [RelayCommand] private Task OpenOpeningBalances() => _nav.GoToAsync(nameof(OpeningBalancesPage));
    [RelayCommand] private Task OpenExcludedCategories() => _nav.GoToAsync(nameof(ExcludedCategoriesPage));
    [RelayCommand] private Task OpenForecastExpenses() => _nav.GoToAsync(nameof(ForecastExpensesPage));

    [RelayCommand] private Task OpenDataSync() => _nav.GoToAsync(nameof(DataSyncPage));

    [RelayCommand]
    private void Exit()
    {
#if WINDOWS
        Application.Current?.Quit();
#endif
    }
}
