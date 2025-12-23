using KukiFinance.Models;
using KukiFinance.Services;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using CsvHelper;
using KukiFinance.Constants;
using KukiFinance.Helpers;

namespace KukiFinance.Pages
{
    public partial class AmexRegisterPage : ContentPage
    {
        // File paths and opening balance
        private readonly string registerFile = FilePathHelper.GetKukiFinancePath("AMEX.csv");
        private readonly string currentFile = FilePathHelper.GetKukiFinancePath("AMEXCurrent.csv");
        private readonly string transactionsFile = FilePathHelper.GetKukiFinancePath("transactionsAMEX.csv");
        private readonly string categoryFile = FilePathHelper.GetKukiFinancePath("Category.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("Amex");
        private readonly DateTime? openingBalanceDate = OpeningBalances.GetDate("Amex");

        private readonly RegisterViewModel viewModel = new();

        public AmexRegisterPage()
        {
            InitializeComponent();
            WindowCenteringService.CenterWindow(1435, 1375);
            BindingContext = viewModel;
            LoadRegister();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadRegister();
        }

        private void LoadRegister()
        {
            if (!File.Exists(registerFile) || !File.Exists(categoryFile))
            {
                viewModel.Entries.Clear();
                viewModel.CurrentBalance = 0m;
                return;
            }

            var entries = RegisterService.LoadRegister<RegistryEntry>(
                registerFile,
                categoryFile,
                openingBalance,
                parts =>
                {
                    // parts[0]: Date, parts[1]: Description, parts[2]: Amount
                    return new RegistryEntry
                    {
                        Date = DateTime.TryParse(parts[0].Trim(), out var date) ? date : (DateTime?)null,
                        Description = parts[1].Trim(),
                        Amount = decimal.TryParse(parts[4].Trim(), out var amt) ? amt * -1 : 0
                    };
                },
                entry => entry.Date ?? DateTime.MinValue,
                entry => entry.Amount ?? 0,
                (entry, balance) => entry.Balance = balance,
                new RegistryEntry
                {
                    Date = openingBalanceDate ?? DateTime.Today.AddDays(-1),
                    Description = "OPENING BALANCE",
                    Category = "Equity",
                    Amount = openingBalance,
                    Balance = openingBalance
                }
            );

            // Set categories from category file
            var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
                    categoryMap[key] = value;
            }

            viewModel.Entries.Clear();
            foreach (var entry in entries)
            {
                entry.Category = categoryMap.TryGetValue(entry.Description ?? "", out var cat) ? cat : "";
                viewModel.Entries.Add(entry);
            }
            viewModel.CurrentBalance = viewModel.Entries.LastOrDefault()?.Balance ?? 0m;

            // Export the current, display-ready register to CashCurrent.csv using the in-memory list
            RegisterExporter.ExportRegisterWithBalance(
                viewModel.Entries.ToList(),
                currentFile,
                includeCheckNumber: false
            );
            viewModel.FilterEntries();
        }

        private async void AddTransactionsButton_Clicked(object sender, EventArgs e)
        {
            if (!File.Exists(transactionsFile))
            {
                await DisplayAlert("Error", "No new transactions file found.", "OK");
                return;
            }

            // Append new transactions to AMEX.csv
            var newLines = File.ReadAllLines(transactionsFile).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (newLines.Count > 0)
                File.AppendAllLines(registerFile, newLines);

            // Optionally clear the transactions file after appending
            File.WriteAllText(transactionsFile, File.ReadLines(transactionsFile).FirstOrDefault() ?? "");

            // Reload the register and update the UI
            LoadRegister();

            await DisplayAlert("Success", "New transactions added.", "OK");
        }

        private void RegisterCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No selection logic needed
        }

        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            // Prompt for Date
            string dateStr = await DisplayPromptAsync("Manual Entry", "Enter date (MM/dd/yyyy):");
            if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, out var date))
            {
                await DisplayAlert("Invalid", "Please enter a valid date.", "OK");
                return;
            }

            // Prompt for Description
            string description = await DisplayPromptAsync("Manual Entry", "Enter description:");
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlert("Invalid", "Please enter a description.", "OK");
                return;
            }

            // Prompt for Amount
            string amountStr = await DisplayPromptAsync("Manual Entry", "Enter amount:");
            if (string.IsNullOrWhiteSpace(amountStr) || !decimal.TryParse(amountStr, out var amount))
            {
                await DisplayAlert("Invalid", "Please enter a valid amount.", "OK");
                return;
            }

            // Compose the CSV line (adjust columns as needed for each register)
            string csvLine = $"{date:MM/dd/yyyy},{description},EDWARD KUKULSKI,-5006,{amount}";

            // If the file does not exist, add a header first
            if (!File.Exists(registerFile))
            {
                string header = "POSTED DATE,DESCRIPTION,NAME,ACCOUNT,AMOUNT";
                File.WriteAllText(registerFile, header + Environment.NewLine);
            }

            // Append the new transaction
            File.AppendAllText(registerFile, csvLine + Environment.NewLine);

            // Refresh the register view
            LoadRegister();

            await DisplayAlert("Success", "Manual transaction added.", "OK");
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is RegistryEntry selectedEntry)
            {
                string newDescription = await DisplayPromptAsync("Edit Description", "Enter new description:", initialValue: selectedEntry.Description);
                if (string.IsNullOrWhiteSpace(newDescription) || newDescription == selectedEntry.Description)
                    return;

                // Update the description in memory
                selectedEntry.Description = newDescription;

                // Lookup new category using only unique keys
                var categoryMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!categoryMap.ContainsKey(key))
                        categoryMap[key] = value;
                }

                categoryMap.TryGetValue(newDescription, out var newCategory);
                selectedEntry.Category = newCategory ?? "";

                // Refresh the CollectionView
                var idx = viewModel.Entries.IndexOf(selectedEntry);
                if (idx >= 0)
                {
                    viewModel.Entries.RemoveAt(idx);
                    viewModel.Entries.Insert(idx, selectedEntry);
                }

                // Save the change to AMEX.csv
                var allLines = File.ReadAllLines(registerFile).ToList();
                if (allLines.Count > 1)
                {
                    // Start at i = 1 to skip the header row
                    for (int i = 1; i < allLines.Count; i++)
                    {
                        var parts = allLines[i].Split(',');
                        if (parts.Length < 5) continue;

                        // Trim all fields for robust comparison
                        for (int j = 0; j < parts.Length; j++)
                            parts[j] = parts[j].Trim();

                        // Parse date and amount
                        if (DateTime.TryParse(parts[0], out var date) &&
                            decimal.TryParse(parts[4], out var amount))
                        {
                            if (date.Date == (selectedEntry.Date ?? DateTime.MinValue).Date &&
                                (amount * -1) == (selectedEntry.Amount ?? 0m))
                            {
                                parts[1] = newDescription;
                                allLines[i] = string.Join(",", parts);
                                break;
                            }
                        }
                    }
                    File.WriteAllLines(registerFile, allLines);
                }

                LoadRegister();
            }
            else
            {
                await DisplayAlert("Edit", "Please select a row to edit.", "OK");
            }
        }
        private async void CopyDescriptionButton_Clicked(object sender, EventArgs e)
        {
            var selectedEntry = RegisterCollectionView.SelectedItem;
            if (selectedEntry is RegistryEntry entry)
            {
                await Clipboard.Default.SetTextAsync(entry.Description ?? "");
                await DisplayAlert("Copied", "Description copied to clipboard.", "OK");
            }
            else
            {
                await DisplayAlert("Copy", "Please select a row to copy.", "OK");
            }
        }

        private async void DeleteTransactionButton_Clicked(object sender, EventArgs e)
        {
            var selectedEntry = RegisterCollectionView.SelectedItem;
            if (selectedEntry is RegistryEntry entry)
            {
                bool confirm = await DisplayAlert("Delete", "Are you sure you want to delete this transaction?", "Yes", "No");
                if (!confirm) return;

                // Remove from the UI
                viewModel.Entries.Remove(entry);

                // Remove from AMEX.csv
                var allLines = File.ReadAllLines(registerFile).ToList();
                int startIdx = allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION") ? 1 : 0;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length < 5) continue;

                    // Match by Date, Amount (with sign inversion), and Description
                    if (DateTime.TryParse(parts[0].Trim(), out var date) &&
                        decimal.TryParse(parts[4].Trim(), out var amt) &&
                        date == entry.Date &&
                        (amt * -1) == entry.Amount &&
                        parts[1].Trim() == entry.Description)
                    {
                        allLines.RemoveAt(i);
                        break;
                    }
                }
                File.WriteAllLines(registerFile, allLines);

                // Refresh the register view
                LoadRegister();
            }
            else
            {
                await DisplayAlert("Delete", "Please select a row to delete.", "OK");
            }
        }
        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}