using eFinance.ViewModels;

namespace eFinance.Pages;

public partial class DuplicateAuditPage : ContentPage
{
    public DuplicateAuditPage(DuplicateAuditViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
