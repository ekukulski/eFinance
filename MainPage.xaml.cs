using KukiFinance.Models;
using KukiFinance.Services;
using KukiFinance.Pages;
using Microsoft.Maui.Controls;

namespace KukiFinance
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }
        
        protected override void OnAppearing()
        {
            base.OnAppearing();
            WindowCenteringService.CenterWindow(1500, 900);
        }
        private async void CashButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(CashRegisterPage));
        }
        private async void BmoCheckRegisterButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(BmoCheckRegisterPage));
        }
        private async void BmoMoneyMarketRegisterButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(BmoMoneyMarketRegisterPage));
        }
        private async void CheckNumberButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new KukiFinance.Pages.CheckNumberPage());
        }
        private async void BmoCdRegisterButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(KukiFinance.Pages.BmoCdRegisterPage));
        }
        private async void AMEXButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(KukiFinance.Pages.AmexRegisterPage));
        }
        private async void VisaButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(VisaRegisterPage));
        }
        private async void MasterCardButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(MasterCardRegisterPage));
        }
        private async void MidlandButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new MidlandRegisterPage());
        }
        private async void CharlesSchwabContributoryButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CharlesSchwabContributoryRegisterPage());
        }
        private async void CharlesSchwabJointTenantButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CharlesSchwabJointTenantRegisterPage());
        }
        private async void CharlesSchwabRothIRAEdButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CharlesSchwabRothIraEdRegisterPage());
        }
        private async void CharlesSchwabRothIRAPattiButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CharlesSchwabRothIraPattiRegisterPage());
        }
        private async void HealthProButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new HealthProRegisterPage());
        }
        private async void Select401KButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Select401KRegisterPage());
        }
        private async void ChevroletImpalaButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ChevroletImpalaRegisterPage());
        }
        private async void NissanSentraButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new NissanSentraRegisterPage());
        }
        private async void GoldButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new GoldRegisterPage());
        }
        private async void HouseButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new HouseRegisterPage());
        }
        private async void NetXButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new NetXRegisterPage());
        }
        private async void FinanceSummaryButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new FinanceSummaryPage());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.ToString(), "OK");
            }
        }
        private async void ExpenseButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ExpensePage());
        }
        private async void AverageExpenseButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new AverageExpenses());
        }
        private async void CashFlowButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CashFlowPage());
        }
        private async void CalendarButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("calendar");
        }
        private async void CategoryButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new KukiFinance.Pages.CategoryPage());
        }
        private async void CategoryListButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new KukiFinance.Pages.CategoryListPage());
        }
        private async void OpeningBalancesButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new OpeningBalancesPage());
        }
        private async void OnExcludedCategoriesClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ExcludedCategoriesPage());
        }
        private async void ForecastExpensesButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ForecastExpensesPage());
        }
        private void ExitButton_Clicked(object sender, EventArgs e)
        {
            #if WINDOWS
                Application.Current.Quit();
            #endif
        }
    }
}