using eFinance.ViewModels;

namespace eFinance.Pages;

public partial class CategoriesPage : ContentPage
{
    private readonly CategoriesViewModel _vm;

    public CategoriesPage(CategoriesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_vm.LoadCommand.CanExecute(null))
            _vm.LoadCommand.Execute(null);
    }
}
