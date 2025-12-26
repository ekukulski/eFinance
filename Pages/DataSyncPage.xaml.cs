using System;
using KukiFinance.Services;
using Microsoft.Maui.Storage;

namespace KukiFinance.Pages;

public partial class DataSyncPage : ContentPage
{
    private readonly IOneDriveSyncService _sync;


private const string PrefAutoImport = "KukiFinance.AutoImportOnStartup";
private const string PrefAutoExport = "KukiFinance.AutoExportOnExit";
private const string PrefLastImported = "KukiFinance.LastImportedSnapshot";
private const string PrefLastExported = "KukiFinance.LastExportedSnapshot";


    public DataSyncPage(IOneDriveSyncService sync)
    {
        InitializeComponent();
        _sync = sync;

        AutoImportSwitch.IsToggled = Preferences.Get(PrefAutoImport, true);
        AutoExportSwitch.IsToggled = Preferences.Get(PrefAutoExport, true);
    }

    private async void OnExportClicked(object sender, EventArgs e)
    {
        SetStatus("Exporting to OneDrive...");
        var (ok, message, snapshotName) = await _sync.ExportToOneDriveAsync();
        if (ok && !string.IsNullOrWhiteSpace(snapshotName))
            Preferences.Set(PrefLastExported, snapshotName);
        await DisplayAlert(ok ? "Export" : "Export Failed", message, "OK");
        SetStatus(message);
    }

    private async void OnImportClicked(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Import from OneDrive",
            "This will replace your local CSV files with the latest OneDrive snapshot.\n\nA local backup zip will be created first.\n\nContinue?",
            "Yes", "Cancel");

        if (!confirm)
            return;

        SetStatus("Importing from OneDrive...");
        var (ok, message, snapshotName) = await _sync.ImportFromOneDriveAsync();
        if (ok && !string.IsNullOrWhiteSpace(snapshotName))
            Preferences.Set(PrefLastImported, snapshotName);
        await DisplayAlert(ok ? "Import" : "Import Failed", message, "OK");
        SetStatus(message);
    }

    
private void OnAutoImportToggled(object sender, ToggledEventArgs e)
    => Preferences.Set(PrefAutoImport, e.Value);

private void OnAutoExportToggled(object sender, ToggledEventArgs e)
    => Preferences.Set(PrefAutoExport, e.Value);

    private async void OnReturnClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void SetStatus(string msg) => StatusLabel.Text = msg ?? "";
}
