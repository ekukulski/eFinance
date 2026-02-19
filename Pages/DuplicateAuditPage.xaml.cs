using eFinance.Data.Models;
using eFinance.Data.Repositories;

namespace eFinance.Pages;

public partial class DuplicateAuditPage : ContentPage
{
    private readonly TransactionRepository _txRepo;
    private List<DuplicateCandidate> _currentResults = new();

    public DuplicateAuditPage(TransactionRepository txRepo)
    {
        InitializeComponent();
        _txRepo = txRepo;
    }

    private async void RunClicked(object sender, EventArgs e)
    {
        SummaryLabel.Text = "Scanning for duplicates...";
        ResultsView.ItemsSource = null;

        _currentResults = await _txRepo.AuditDuplicatesAsync(new DuplicateAuditOptions());

        ResultsView.ItemsSource = _currentResults;

        UpdateSummary();
    }

    private async void AcceptClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is DuplicateCandidate candidate)
        {
            await _txRepo.IgnoreDuplicatePairAsync(
                candidate.A_Id,
                candidate.B_Id,
                "User accepted as legitimate repeat");

            // Remove immediately from current list
            _currentResults.Remove(candidate);
            ResultsView.ItemsSource = null;
            ResultsView.ItemsSource = _currentResults;

            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        SummaryLabel.Text = _currentResults.Count == 0
            ? "No suspicious duplicates found."
            : $"{_currentResults.Count} suspicious duplicate(s) found.";
    }
    private async void ReturnClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

}
