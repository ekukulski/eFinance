using KukiFinance.Pages;

namespace KukiFinance
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(BmoCheckRegisterPage), typeof(KukiFinance.Pages.BmoCheckRegisterPage));
            Routing.RegisterRoute(nameof(CheckNumberPage), typeof(KukiFinance.Pages.CheckNumberPage));
            Routing.RegisterRoute(nameof(CategoryListPage), typeof(KukiFinance.Pages.CategoryListPage));
            Routing.RegisterRoute(nameof(CategoryPage), typeof(KukiFinance.Pages.CategoryPage));
            Routing.RegisterRoute(nameof(BmoMoneyMarketRegisterPage), typeof(BmoMoneyMarketRegisterPage));
            Routing.RegisterRoute(nameof(BmoCdRegisterPage), typeof(BmoCdRegisterPage));
            Routing.RegisterRoute(nameof(CashRegisterPage), typeof(CashRegisterPage));
            Routing.RegisterRoute(nameof(AmexRegisterPage), typeof(AmexRegisterPage));
            Routing.RegisterRoute(nameof(VisaRegisterPage), typeof(VisaRegisterPage));
            Routing.RegisterRoute(nameof(MasterCardRegisterPage), typeof(MasterCardRegisterPage));
            Routing.RegisterRoute(nameof(MidlandRegisterPage), typeof(MidlandRegisterPage));
            Routing.RegisterRoute(nameof(CharlesSchwabContributoryRegisterPage), typeof(CharlesSchwabContributoryRegisterPage));
            Routing.RegisterRoute(nameof(CharlesSchwabJointTenantRegisterPage), typeof(CharlesSchwabJointTenantRegisterPage));
            Routing.RegisterRoute(nameof(CharlesSchwabRothIraEdRegisterPage), typeof(CharlesSchwabRothIraEdRegisterPage));
            Routing.RegisterRoute(nameof(CharlesSchwabRothIraPattiRegisterPage), typeof(CharlesSchwabRothIraPattiRegisterPage));
            Routing.RegisterRoute(nameof(HealthProRegisterPage), typeof(HealthProRegisterPage));
            Routing.RegisterRoute(nameof(Select401KRegisterPage), typeof(Select401KRegisterPage));
            Routing.RegisterRoute(nameof(ChevroletImpalaRegisterPage), typeof(ChevroletImpalaRegisterPage));
            Routing.RegisterRoute(nameof(NissanSentraRegisterPage), typeof(NissanSentraRegisterPage));
            Routing.RegisterRoute(nameof(GoldRegisterPage), typeof(GoldRegisterPage));
            Routing.RegisterRoute(nameof(HouseRegisterPage), typeof(HouseRegisterPage));
            Routing.RegisterRoute(nameof(NetXRegisterPage), typeof(NetXRegisterPage));
            Routing.RegisterRoute(nameof(FinanceSummaryPage), typeof(FinanceSummaryPage));
            Routing.RegisterRoute(nameof(ExpensePage), typeof(ExpensePage));
            Routing.RegisterRoute(nameof(OpeningBalancesPage), typeof(OpeningBalancesPage));   
            Routing.RegisterRoute(nameof(AverageExpenses), typeof(AverageExpenses));
            Routing.RegisterRoute(nameof(CashFlowPage), typeof(CashFlowPage));
            Routing.RegisterRoute(nameof(ExcludedCategoriesPage), typeof(ExcludedCategoriesPage));
            Routing.RegisterRoute("calendar", typeof(CalendarPage));
            Routing.RegisterRoute(nameof(ForecastExpensesPage), typeof(ForecastExpensesPage));
            Routing.RegisterRoute(nameof(EditForecastExpensePage), typeof(EditForecastExpensePage));
        }
    }
}