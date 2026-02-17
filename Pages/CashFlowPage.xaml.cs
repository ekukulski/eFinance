namespace eFinance.Pages;

public partial class CashFlowPage : ContentPage
{
    public CashFlowPage()
    {
        InitializeComponent();
    }

    private async void ReturnButton_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
