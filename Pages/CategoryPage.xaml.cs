using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using eFinance.Services;
using eFinance.Pages;

namespace eFinance.Pages
{
    public partial class CategoryPage : ContentPage
    {
        private readonly string categoryFile = FilePathHelper.GeteFinancePath("Category.csv");
        private readonly string categoryListFile = FilePathHelper.GeteFinancePath("CategoryList.csv");
        public ObservableCollection<CategoryEntry> Entries { get; set; } = new();
        public ObservableCollection<CategoryEntry> FilteredEntries { get; set; } = new();

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
        public CategoryPage()
        {
            InitializeComponent();
            LoadEntries();
            BindingContext = this;
        }

        private void LoadEntries()
        {
            Entries.Clear();
            if (File.Exists(categoryFile))
            {
                var lines = File.ReadAllLines(categoryFile)
                    .Skip(1)
                    .Select(line => line.Split(','))
                    .Where(parts => parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    .Select(parts => new CategoryEntry
                    {
                        Description = parts[0].Trim(),
                        Category = parts[1].Trim()
                    })
                    .OrderBy(e => e.Description)
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
                    (e.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Category?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            foreach (var entry in query)
                FilteredEntries.Add(entry);
        }
        private async void AddCategoryButton_Clicked(object sender, EventArgs e)
        {
            string description = await DisplayPromptAsync("Add Category", "Enter description:");
            if (string.IsNullOrWhiteSpace(description))
                return;

            // Load category list for dropdown
            var categoryList = File.Exists(categoryListFile)
                ? File.ReadAllLines(categoryListFile).Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l).ToList()
                : new List<string>();

            string category = await DisplayActionSheet("Select Category", "Cancel", null, categoryList.ToArray());
            if (string.IsNullOrWhiteSpace(category) || category == "Cancel")
                return;

            // Append to CSV
            bool fileExists = File.Exists(categoryFile);
            if (!fileExists)
            {
                File.WriteAllText(categoryFile, "Description,Category\n");
            }
            File.AppendAllText(categoryFile, $"{description.Trim()},{category.Trim()}\n");

            LoadEntries();
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (CategoryCollectionView.SelectedItem is CategoryEntry entry)
            {
                // Store the original description
                string originalDescription = entry.Description;

                string newDescription = await DisplayPromptAsync("Edit Description", "Enter new description:", initialValue: entry.Description);
                if (string.IsNullOrWhiteSpace(newDescription)) return;

                // Load category list for dropdown
                var categoryList = File.Exists(categoryListFile)
                    ? File.ReadAllLines(categoryListFile).Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l).ToList()
                    : new List<string>();

                string newCategory = await DisplayActionSheet("Select Category", "Cancel", null, categoryList.ToArray());
                if (string.IsNullOrWhiteSpace(newCategory) || newCategory == "Cancel") return;

                // Update in-memory entry
                entry.Description = newDescription.Trim();
                entry.Category = newCategory.Trim();

                // Update CSV: match on original description
                var allLines = File.ReadAllLines(categoryFile).ToList();
                for (int i = 0; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length >= 2 && parts[0].Trim() == originalDescription)
                    {
                        allLines[i] = $"{entry.Description},{entry.Category}";
                        break;
                    }
                }
                File.WriteAllLines(categoryFile, allLines);
                LoadEntries();
            }
            else
            {
                await DisplayAlert("Edit", "Please select a row to edit.", "OK");
            }
        }

        private async void DeleteButton_Clicked(object sender, EventArgs e)
        {
            if (CategoryCollectionView.SelectedItem is CategoryEntry entry)
            {
                var allLines = File.ReadAllLines(categoryFile).ToList();
                allLines.RemoveAll(line =>
                {
                    var parts = line.Split(',');
                    return parts.Length >= 2 && parts[0].Trim() == entry.Description;
                });
                File.WriteAllLines(categoryFile, allLines);
                LoadEntries();
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

        public class CategoryEntry
        {
            public required string Description { get; set; }
            public required string Category { get; set; }
        }
        private void CategoryCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // You can leave this empty or use it to handle selection logic
        }
    }
}