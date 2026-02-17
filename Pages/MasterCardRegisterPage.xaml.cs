using eFinance.Services;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using CsvHelper;
using eFinance.Constants;
using eFinance.Helpers;
using eFinance.Models;

namespace eFinance.Pages
{
    public partial class MasterCardRegisterPage : ContentPage
    {
        // File paths and opening balance
        private readonly string registerFile = FilePathHelper.GeteFinancePath("MasterCard.csv");
        private readonly string transactionsFile = FilePathHelper.GeteFinancePath("transactionsMasterCard.csv");
        private readonly string categoryFile = FilePathHelper.GeteFinancePath("Category.csv");
        private readonly string currentFile = FilePathHelper.GeteFinancePath("MasterCardCurrent.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("MasterCard");
        private readonly DateTime? openingBalanceDate = OpeningBalances.GetDate("MasterCard");

        private readonly RegisterViewModel viewModel = new();

        public MasterCardRegisterPage()
        {
            InitializeComponent();
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
                    // parts[1]: Date, parts[2]: Description, parts[3]: Debit, parts[4]: Credit
                    var date = DateTime.TryParse(parts[1].Trim(), out var d) ? d : (DateTime?)null;
                    var description = parts[2].Trim();
                    var debitStr = parts[3].Trim();
                    var creditStr = parts[4].Trim();

                    decimal amount = 0;
                    if (!string.IsNullOrWhiteSpace(debitStr) && decimal.TryParse(debitStr, out var debit))
                        amount = debit * -1;
                    else if (!string.IsNullOrWhiteSpace(creditStr) && decimal.TryParse(creditStr, out var credit))
                        amount = credit * -1;

                    return new RegistryEntry
                    {
                        Date = date,
                        Description = description,
                        Amount = amount
                    };
                },
                entry => entry.Date ?? DateTime.MinValue,
                entry => entry.Amount ?? 0,
                entry => entry.Balance,
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

            // Export the current, display-ready register to MasterCardCurrent.csv
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

            // Append new transactions to MasterCard.csv
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
            string amountStr = await DisplayPromptAsync("Manual Entry", "Enter amount (positive for charge, negative for payment):");
            if (string.IsNullOrWhiteSpace(amountStr) || !decimal.TryParse(amountStr, out var amount) || amount == 0)
            {
                await DisplayAlert("Invalid", "Please enter a non-zero amount.", "OK");
                return;
            }

            string status = ""; // Or set as needed
            string debit = "";
            string credit = "";

            if (amount > 0)
            {
                debit = amount.ToString("F2"); // Save as positive in Debit
            }
            else // amount < 0
            {
                credit = amount.ToString("F2"); // Save as negative in Credit
            }

            string csvLine = $"{status},{date:MM/dd/yyyy},{description},{debit},{credit}";

            if (!File.Exists(registerFile))
            {
                string header = "Status,Date,Description,Debit,Credit";
                File.WriteAllText(registerFile, header + Environment.NewLine);
            }

            File.AppendAllText(registerFile, csvLine + Environment.NewLine);

            LoadRegister();

            await DisplayAlert("Success", "Manual transaction added.", "OK");
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is RegistryEntry selectedEntry)
            {
                string originalDescription = selectedEntry.Description ?? string.Empty;
                DateTime originalDate = selectedEntry.Date ?? DateTime.MinValue;
                decimal originalAmount = selectedEntry.Amount ?? 0m;

                string? newDescription = await DisplayPromptAsync("Edit Description", "Enter new description:", initialValue: selectedEntry.Description);
                if (string.IsNullOrWhiteSpace(newDescription) || newDescription == selectedEntry.Description)
                    return;

                selectedEntry.Description = newDescription;

                var categoryMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                if (File.Exists(categoryFile))
                {
                    foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!categoryMap.ContainsKey(key))
                            categoryMap[key] = value;
                    }
                }

                categoryMap.TryGetValue(newDescription, out var newCategory);
                selectedEntry.Category = newCategory ?? "";

                var idx = viewModel.Entries.IndexOf(selectedEntry);
                if (idx >= 0)
                {
                    viewModel.Entries.RemoveAt(idx);
                    viewModel.Entries.Insert(idx, selectedEntry);
                }

                var allLines = File.ReadAllLines(registerFile).ToList();
                if (allLines.Count > 1)
                {
                    for (int i = 1; i < allLines.Count; i++)
                    {
                        var parts = allLines[i].Split(',');
                        if (parts.Length < 5) continue;

                        if (!DateTime.TryParse(parts[1].Trim(), out var date))
                            continue;

                        decimal amount = 0m;
                        var debitStr = parts[3].Trim();
                        var creditStr = parts[4].Trim();
                        if (!string.IsNullOrWhiteSpace(debitStr) && decimal.TryParse(debitStr, out var debit))
                            amount = -1 * debit;
                        else if (!string.IsNullOrWhiteSpace(creditStr) && decimal.TryParse(creditStr, out var credit))
                            amount = -1 * credit;

                        if (date.Date == originalDate.Date &&
                            amount == originalAmount &&
                            parts[2].Trim() == originalDescription)
                        {
                            parts[2] = newDescription;
                            allLines[i] = string.Join(",", parts);
                            break;
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

        private async void DeleteTransactionButton_Clicked(object sender, EventArgs e)
        {
            var selectedEntry = RegisterCollectionView.SelectedItem;
            if (selectedEntry is RegistryEntry entry)
            {
                bool confirm = await DisplayAlert("Delete", "Are you sure you want to delete this transaction?", "Yes", "No");
                if (!confirm) return;

                viewModel.Entries.Remove(entry);

                var allLines = File.ReadAllLines(registerFile).ToList();
                int startIdx = allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION") ? 1 : 0;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length < 5) continue;

                    if (!DateTime.TryParse(parts[1].Trim(), out var date))
                        continue;

                    decimal amount = 0m;
                    var debitStr = parts[3].Trim();
                    var creditStr = parts[4].Trim();
                    if (!string.IsNullOrWhiteSpace(debitStr) && decimal.TryParse(debitStr, out var debit))
                        amount = -1 * debit;
                    else if (!string.IsNullOrWhiteSpace(creditStr) && decimal.TryParse(creditStr, out var credit))
                        amount = -1 * credit;

                    if (date.Date == (entry.Date ?? DateTime.MinValue).Date &&
                        amount == (entry.Amount ?? 0m) &&
                        parts[2].Trim() == (entry.Description ?? ""))
                    {
                        allLines.RemoveAt(i);
                        break;
                    }
                }
                File.WriteAllLines(registerFile, allLines);

                LoadRegister();
            }
            else
            {
                await DisplayAlert("Delete", "Please select a row to delete.", "OK");
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

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}