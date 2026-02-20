using eFinance.ViewModels;

namespace eFinance.Pages;

public partial class ImportFormatsPage : ContentPage
{
    public ImportFormatsPage(ImportFormatsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}