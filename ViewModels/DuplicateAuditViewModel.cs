using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Models;
using eFinance.Data.Repositories;

namespace eFinance.ViewModels;

public partial class DuplicateAuditViewModel : ObservableObject
{
    private readonly TransactionRepository _transactions;

    public ObservableCollection<DuplicateAuditResultRow> Results { get; } = new();

    [ObservableProperty]
    private string summaryText = "";

    public DuplicateAuditViewModel(TransactionRepository transactions)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        SummaryText = "Click 'Run Duplicate Audit' to scan for duplicates.";
    }

    [RelayCommand]
    private async Task ReturnAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task RunAuditAsync()
    {
        var rows = await _transactions.RunDuplicateAuditAsync();

        Results.Clear();
        foreach (var r in rows)
            Results.Add(r);

        SummaryText = Results.Count == 0
            ? "No duplicates found."
            : $"{Results.Count} suspicious duplicate(s) found.";
    }

    [RelayCommand]
    private async Task KeepTopAsync(DuplicateAuditResultRow row)
    {
        if (row is null) return;

        // keep A/top, soft-delete B/bottom
        await _transactions.SoftDeleteAsync(row.B_Id);

        // ALSO persist ignore so the same pair never shows again
        await _transactions.IgnoreDuplicatePairAsync(row.A_Id, row.B_Id, "User kept top; bottom soft-deleted");

        Results.Remove(row);
        SummaryText = $"{Results.Count} suspicious duplicate(s) remaining.";
    }

    [RelayCommand]
    private async Task KeepBottomAsync(DuplicateAuditResultRow row)
    {
        if (row is null) return;

        // keep B/bottom, soft-delete A/top
        await _transactions.SoftDeleteAsync(row.A_Id);

        // ALSO persist ignore so the same pair never shows again
        await _transactions.IgnoreDuplicatePairAsync(row.A_Id, row.B_Id, "User kept bottom; top soft-deleted");

        Results.Remove(row);
        SummaryText = $"{Results.Count} suspicious duplicate(s) remaining.";
    }

    [RelayCommand]
    private async Task AcceptAsync(DuplicateAuditResultRow row)
    {
        if (row is null) return;

        await _transactions.IgnoreDuplicatePairAsync(
            row.A_Id,
            row.B_Id,
            "User accepted as not a duplicate");

        Results.Remove(row);
        SummaryText = $"{Results.Count} suspicious duplicate(s) remaining.";
    }
}
