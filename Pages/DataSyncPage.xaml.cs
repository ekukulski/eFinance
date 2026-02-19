using eFinance.Services;

namespace eFinance.Pages;

public partial class DataSyncPage : ContentPage
{
    private readonly ICloudSyncService _sync;

    private const string PrefAutoImport = "eFinance.AutoImportOnStartup";
    private const string PrefAutoExport = "eFinance.AutoExportOnExit";
    private const string PrefLastImported = "eFinance.LastImportedSnapshot";
    private const string PrefLastExported = "eFinance.LastExportedSnapshot";

    public DataSyncPage(ICloudSyncService sync)
    {
        InitializeComponent();
        _sync = sync;

        AutoImportSwitch.IsToggled = Preferences.Get(PrefAutoImport, false);
        AutoExportSwitch.IsToggled = Preferences.Get(PrefAutoExport, false);
    }

    private async void OnExportClicked(object sender, EventArgs e)
    {
        SetStatus("Exporting to Proton Drive...");
        var (ok, message, snapshotName) = await _sync.ExportToCloudAsync();
        if (ok && !string.IsNullOrWhiteSpace(snapshotName))
            Preferences.Set(PrefLastExported, snapshotName);
        await DisplayAlert(ok ? "Export" : "Export Failed", message, "OK");
        SetStatus(message);
    }

    private async void OnImportClicked(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Import from Proton Drive",
            "This will replace your local CSV files with the latest Proton Drive snapshot.\n\nA local backup zip will be created first.\n\nContinue?",
            "Yes", "Cancel");

        if (!confirm)
            return;

        SetStatus("Importing from Proton Drive...");
        var (ok, message, snapshotName) = await _sync.ImportFromCloudAsync();
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
