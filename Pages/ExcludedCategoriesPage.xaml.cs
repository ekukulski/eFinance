using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Maui.Controls;
using eFinance.Services;
using System.Linq;
using System.Collections.Generic;

namespace eFinance.Pages;

public partial class ExcludedCategoriesPage : ContentPage
{
    private readonly string ExcludedCategoriesPath = FilePathHelper.GeteFinancePath("ExcludedCategories.csv");
    private readonly string CategoryListPath = FilePathHelper.GeteFinancePath("CategoryList.csv");

    public ObservableCollection<string> ExcludedCategories { get; set; } = new();

    public ExcludedCategoriesPage()
    {
        InitializeComponent();
        BindingContext = this;
        LoadExcludedCategories();
    }

    private void LoadExcludedCategories()
    {
        ExcludedCategories.Clear();
        if (!File.Exists(ExcludedCategoriesPath)) return;
        var lines = File.ReadAllLines(ExcludedCategoriesPath);
        var categories = lines.Skip(1) // Skip header
            .Select(line => line.Trim())
            .Where(category => !string.IsNullOrEmpty(category))
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase); // Sort alphabetically

        foreach (var category in categories)
            ExcludedCategories.Add(category);
    }

    private async void OnAddCategoryClicked(object sender, EventArgs e)
    {
        // Load all categories from CategoryList.csv
        if (!File.Exists(CategoryListPath))
        {
            await DisplayAlert("Error", "CategoryList.csv not found.", "OK");
            return;
        }
        var allCategories = File.ReadAllLines(CategoryListPath)
            .Skip(1)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase) // Sort alphabetically
            .ToList();
        var availableCategories = allCategories.Except(ExcludedCategories).ToList();

        string selected = await DisplayActionSheet("Select Category to Add", "Cancel", null, availableCategories.ToArray());
        if (string.IsNullOrEmpty(selected) || selected == "Cancel") return;

        ExcludedCategories.Add(selected);
        SaveExcludedCategories();
        LoadExcludedCategories();
    }

    private async void OnDeleteCategoryClicked(object sender, EventArgs e)
    {
        if (ExcludedCategoriesCollectionView.SelectedItem is not string selectedCategory)
        {
            await DisplayAlert("Error", "Please select a category to delete.", "OK");
            return;
        }
        ExcludedCategories.Remove(selectedCategory);
        SaveExcludedCategories();
        LoadExcludedCategories();
    }

    private void SaveExcludedCategories()
    {
        var lines = new List<string> { "Category" };
        lines.AddRange(ExcludedCategories);
        File.WriteAllLines(ExcludedCategoriesPath, lines);
    }

    // Utility for other pages/viewmodels
    public static HashSet<string> GetExcludedCategoriesFromCsv()
    {
        var path = FilePathHelper.GeteFinancePath("ExcludedCategories.csv");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;
        var lines = File.ReadAllLines(path);
        foreach (var line in lines.Skip(1)) // skip header
        {
            var category = line.Trim();
            if (!string.IsNullOrEmpty(category))
                set.Add(category);
        }
        return set;
    }

    private async void ReturnButton_Clicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}