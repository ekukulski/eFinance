using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using KukiFinance.Services;

namespace KukiFinance.Pages
{
    public partial class CategoryListPage : ContentPage
    {
        private readonly string categoryListFile = FilePathHelper.GetKukiFinancePath("CategoryList.csv");

        // Holds all categories loaded from file
        private ObservableCollection<string> categories = new();

        // Holds filtered categories for display
        public ObservableCollection<string> FilteredCategories { get; set; } = new();

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    FilterCategories();
                    OnPropertyChanged(nameof(SearchText));
                }
            }
        }

        public CategoryListPage()
        {
            InitializeComponent();
            WindowCenteringService.CenterWindow(870, 1400);
            BindingContext = this;
            LoadCategories();
        }

        private void LoadCategories()
        {
            categories.Clear();
            if (File.Exists(categoryListFile))
            {
                var lines = File.ReadAllLines(categoryListFile)
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .OrderBy(line => line.Trim())
                                .ToList();
                foreach (var line in lines)
                    categories.Add(line.Trim());
            }
            FilterCategories();
        }

        private void FilterCategories()
        {
            FilteredCategories.Clear();
            var query = string.IsNullOrWhiteSpace(SearchText)
                ? categories
                : categories.Where(c => c.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase));
            foreach (var cat in query)
                FilteredCategories.Add(cat);
        }

        private async void AddCategoryButton_Clicked(object sender, EventArgs e)
        {
            string newCategory = await DisplayPromptAsync("Add Category", "Enter new category name:");
            if (string.IsNullOrWhiteSpace(newCategory))
            {
                await DisplayAlert("Invalid", "Please enter a category name.", "OK");
                return;
            }
            if (categories.Contains(newCategory.Trim()))
            {
                await DisplayAlert("Duplicate", "This category already exists.", "OK");
                return;
            }
            File.AppendAllText(categoryListFile, newCategory.Trim() + System.Environment.NewLine);
            LoadCategories();
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (CategoryListView.SelectedItem is string selectedCategory)
            {
                string newCategory = await DisplayPromptAsync("Edit Category", "Enter new category name:", initialValue: selectedCategory);
                if (string.IsNullOrWhiteSpace(newCategory)) return;

                var allLines = File.ReadAllLines(categoryListFile).ToList();
                int idx = allLines.FindIndex(line => line.Trim() == selectedCategory);
                if (idx >= 0)
                {
                    allLines[idx] = newCategory.Trim();
                    File.WriteAllLines(categoryListFile, allLines);
                    LoadCategories();
                }
            }
            else
            {
                await DisplayAlert("Edit", "Please select a row to edit.", "OK");
            }
        }

        private async void DeleteButton_Clicked(object sender, EventArgs e)
        {
            if (CategoryListView.SelectedItem is string selectedCategory)
            {
                var allLines = File.ReadAllLines(categoryListFile).ToList();
                allLines.RemoveAll(line => line.Trim() == selectedCategory);
                File.WriteAllLines(categoryListFile, allLines);
                LoadCategories();
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

        private void CategoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optionally handle selection logic here
        }
    }
}