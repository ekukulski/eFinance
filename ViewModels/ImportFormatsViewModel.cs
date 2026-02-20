using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Importing;
using Microsoft.Maui.Storage;

namespace eFinance.ViewModels;

public sealed partial class ImportFormatsViewModel : ObservableObject
{
    private readonly ImportPipeline _pipeline;
    private readonly ReadOnlyCollection<IImporter> _importers;

    public ObservableCollection<ImporterRow> Importers { get; } = new();

    [ObservableProperty]
    private string lastTestResult = "";

    public ImportFormatsViewModel(ImportPipeline pipeline, IEnumerable<IImporter> importers)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _importers = (importers ?? throw new ArgumentNullException(nameof(importers))).ToList().AsReadOnly();

        Refresh();
    }

    private void Refresh()
    {
        Importers.Clear();

        foreach (var i in _importers.OrderBy(x => x.SourceName))
        {
            Importers.Add(new ImporterRow
            {
                SourceName = i.SourceName,
                AmountPolicy = i.AmountPolicy?.ToString() ?? "n/a",
                HeaderHint = string.IsNullOrWhiteSpace(i.HeaderHint) ? "(not provided)" : i.HeaderHint
            });
        }
    }

    [RelayCommand]
    private async Task TestCsvAsync()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a CSV file to test",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".csv" } },
                { DevicePlatform.MacCatalyst, new[] { "public.comma-separated-values-text" } },
                { DevicePlatform.iOS, new[] { "public.comma-separated-values-text" } },
                { DevicePlatform.Android, new[] { "text/csv" } },
            })
            });

            if (pick is null)
                return;

            var path = pick.FullPath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                LastTestResult = "File not found.";
                return;
            }

            var importer = _pipeline.FindImporter(path);
            var header = File.ReadLines(path).FirstOrDefault() ?? "";

            if (importer is null)
            {
                LastTestResult = $"No importer matched.\n\nHeader:\n{header}";
                return;
            }

            LastTestResult =
                $"Matched importer: {importer.SourceName}\n" +
                $"AmountPolicy: {importer.AmountPolicy?.ToString() ?? "n/a"}\n\n" +
                $"Header:\n{header}";
        }
        catch (Exception ex)
        {
            LastTestResult = "Test failed: " + ex.Message;
        }
    }
}

public sealed class ImporterRow
{
    public string SourceName { get; set; } = "";
    public string AmountPolicy { get; set; } = "";
    public string HeaderHint { get; set; } = "";
}