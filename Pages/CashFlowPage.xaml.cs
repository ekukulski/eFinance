using KukiFinance.ViewModels;
using KukiFinance.Services;

namespace KukiFinance.Pages;

public partial class CashFlowPage : ContentPage
{
    public CashFlowPage()
    {
        InitializeComponent();
        WindowCenteringService.CenterWindow(820, 1400);
        BindingContext = new CashFlowViewModel();
    }

    private async void ReturnButton_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}