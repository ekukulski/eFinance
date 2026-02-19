using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using eFinance.Services;

namespace eFinance.ViewModels;

public sealed partial class CategoriesViewModel : ObservableObject
{
    private readonly CategoryRepository _categories;
    private readonly IDialogService _dialogs;

    private readonly ObservableCollection<Category> _all = new();

    public ObservableCollection<Category> FilteredCategories { get; } = new();

    [ObservableProperty]
    private string title = "Categories";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private Category? selectedCategory;

    public CategoriesViewModel(CategoryRepository categories, IDialogService dialogs)
    {
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        _all.Clear();

        var list = await _categories.GetAllAsync(activeOnly: true);
        foreach (var c in list)
            _all.Add(c);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredCategories.Clear();

        var query = string.IsNullOrWhiteSpace(SearchText)
            ? _all
            : new ObservableCollection<Category>(_all.Where(c =>
                c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        foreach (var c in query)
            FilteredCategories.Add(c);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var name = await _dialogs.PromptAsync("Add Category", "Enter new category name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            await _dialogs.AlertAsync("Invalid", "Please enter a category name.");
            return;
        }

        var trimmed = name.Trim();

        var existing = await _categories.GetByNameAsync(trimmed);
        if (existing is not null)
        {
            await _dialogs.AlertAsync("Duplicate", "This category already exists.");
            return;
        }

        try
        {
            await _categories.InsertAsync(trimmed);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedCategory is null)
        {
            await _dialogs.AlertAsync("Edit", "Please select a row to edit.");
            return;
        }

        var name = await _dialogs.PromptAsync("Edit Category", "Enter new category name:", SelectedCategory.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var trimmed = name.Trim();

        if (!string.Equals(trimmed, SelectedCategory.Name, StringComparison.OrdinalIgnoreCase))
        {
            var dup = await _categories.GetByNameAsync(trimmed);
            if (dup is not null)
            {
                await _dialogs.AlertAsync("Duplicate", "That category name already exists.");
                return;
            }
        }

        try
        {
            SelectedCategory.Name = trimmed;
            await _categories.UpdateAsync(SelectedCategory);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedCategory is null)
        {
            await _dialogs.AlertAsync("Delete", "Please select a row to delete.");
            return;
        }

        var inUse = await _categories.IsInUseAsync(SelectedCategory.Id);
        if (inUse)
        {
            await _dialogs.AlertAsync(
                "Cannot Delete",
                "This category is used by one or more transactions.\n\n" +
                "To protect your history, it will be archived instead (hidden from lists).");

            await _categories.SoftDeleteAsync(SelectedCategory.Id);
            SelectedCategory = null;
            await LoadAsync();
            return;
        }

        var confirm = await _dialogs.ConfirmAsync(
            "Delete",
            $"Delete category '{SelectedCategory.Name}'?",
            accept: "Delete",
            cancel: "Cancel");

        if (!confirm)
            return;

        await _categories.DeleteAsync(SelectedCategory.Id); // hard delete, safe because not in use
        SelectedCategory = null;
        await LoadAsync();
    }

    [RelayCommand]
    private Task ReturnAsync()
    {
        // Shell back
        return Shell.Current.GoToAsync("..");
    }
}
