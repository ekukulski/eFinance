using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using eFinance.Services;

namespace eFinance.Pages
{
    public partial class CheckNumberPage : ContentPage
    {
        private readonly string checkNumberFile = FilePathHelper.GeteFinancePath("CheckNumber.csv");

        public ObservableCollection<CheckNumberEntry> Entries { get; set; } = new();
        public ObservableCollection<CheckNumberEntry> FilteredEntries { get; set; } = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    FilterEntries();
                    OnPropertyChanged(nameof(SearchText));
                }
            }
        }

        public CheckNumberPage()
        {
            InitializeComponent();
            BindingContext = this;
            LoadCheckNumbers();
        }

        private void LoadCheckNumbers()
        {
            Entries.Clear();
            if (File.Exists(checkNumberFile))
            {
                var lines = File.ReadAllLines(checkNumberFile)
                    .Skip(1)
                    .Select(line => line.Split(','))
                    .Where(parts => parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    .Select(parts => new CheckNumberEntry
                    {
                        CheckNumber = parts[0].Trim(),
                        Description = parts[1].Trim()
                    })
                    .OrderBy(e => int.TryParse(e.CheckNumber, out var num) ? num : int.MaxValue)
                    .ToList();

                foreach (var entry in lines)
                    Entries.Add(entry);
            }
            FilterEntries();
        }

        private void FilterEntries()
        {
            FilteredEntries.Clear();
            var query = string.IsNullOrWhiteSpace(SearchText)
                ? Entries
                : Entries.Where(e =>
                    (e.CheckNumber?.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Description?.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) ?? false));
            foreach (var entry in query)
                FilteredEntries.Add(entry);
        }

        public class CheckNumberEntry
        {
            public required string CheckNumber { get; set; }
            public required string Description { get; set; }
        }

        private async void AddCheckNumberButton_Clicked(object sender, EventArgs e)
        {
            string checkNumber = await DisplayPromptAsync("Enter New Check Number", "Check Number:");
            if (string.IsNullOrWhiteSpace(checkNumber))
                return;

            string description = await DisplayPromptAsync("Enter Description", "Description:");
            if (string.IsNullOrWhiteSpace(description))
                return;

            bool fileExists = File.Exists(checkNumberFile);
            if (!fileExists)
            {
                File.WriteAllText(checkNumberFile, "CheckNumber,Description\n");
            }
            File.AppendAllText(checkNumberFile, $"{checkNumber.Trim()},{description.Trim()}\n");

            LoadCheckNumbers();
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (CheckNumberCollectionView.SelectedItem is CheckNumberEntry entry)
            {
                string originalCheckNumber = entry.CheckNumber;

                string newNumber = await DisplayPromptAsync("Edit Check Number", "Enter new check number:", initialValue: entry.CheckNumber);
                if (string.IsNullOrWhiteSpace(newNumber)) return;

                string newDescription = await DisplayPromptAsync("Edit Description", "Enter new description:", initialValue: entry.Description);
                if (string.IsNullOrWhiteSpace(newDescription)) return;

                entry.CheckNumber = newNumber.Trim();
                entry.Description = newDescription.Trim();

                var allLines = File.ReadAllLines(checkNumberFile).ToList();
                for (int i = 0; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length >= 2 && parts[0].Trim() == originalCheckNumber)
                    {
                        allLines[i] = $"{entry.CheckNumber},{entry.Description}";
                        break;
                    }
                }
                File.WriteAllLines(checkNumberFile, allLines);
                LoadCheckNumbers();
            }
            else
            {
                await DisplayAlert("Edit", "Please select a row to edit.", "OK");
            }
        }

        private async void DeleteButton_Clicked(object sender, EventArgs e)
        {
            if (CheckNumberCollectionView.SelectedItem is CheckNumberEntry entry)
            {
                var allLines = File.ReadAllLines(checkNumberFile).ToList();
                allLines.RemoveAll(line =>
                {
                    var parts = line.Split(',');
                    return parts.Length >= 2 && parts[0].Trim() == entry.CheckNumber;
                });
                File.WriteAllLines(checkNumberFile, allLines);
                LoadCheckNumbers();
            }
            else
            {
                await DisplayAlert("Delete", "Please select a row to delete.", "OK");
            }
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private void CheckNumberCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optional: handle selection logic here
        }
    }
}