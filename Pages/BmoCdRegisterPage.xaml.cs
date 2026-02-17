using eFinance.Models;
using eFinance.Services;
using eFinance.Helpers;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using CsvHelper;
using eFinance.Constants;
using eFinance.Models;

namespace eFinance.Pages
{
    public partial class BmoCdRegisterPage : ContentPage
    {
        private readonly string registerFile = FilePathHelper.GeteFinancePath("BMOCD.csv");
        private readonly string currentFile = FilePathHelper.GeteFinancePath("BMOCDCurrent.csv");
        private readonly string transactionsFile = FilePathHelper.GeteFinancePath("transactionsCD.csv");
        private readonly string categoryFile = FilePathHelper.GeteFinancePath("Category.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("BmoCd");

        private readonly RegisterViewModel viewModel = new();

        public BmoCdRegisterPage()
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
            if (!File.Exists(registerFile))
            {
                viewModel.Entries.Clear();
                viewModel.CurrentBalance = 0m;
                return;
            }

            List<RegistryEntry> records;
            using (var reader = new StreamReader(registerFile))
            using (var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            }))
            {
                csv.Context.RegisterClassMap<RegistryEntryMap>();
                records = csv.GetRecords<RegistryEntry>().ToList();
            }

            // Optionally, set category in code using your category file
            var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(categoryFile))
            {
                foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
                        categoryMap[key] = value;
                }
            }

            viewModel.Entries.Clear();

            // Insert the OPENING BALANCE row at the top
            viewModel.Entries.Add(new RegistryEntry
            {
                Date = records.Count > 0 ? (records[0].Date ?? DateTime.Today).AddDays(-1) : DateTime.Today,
                Description = "OPENING BALANCE",
                Category = "Equity",
                Amount = openingBalance,
                Balance = openingBalance
            });

            decimal runningBalance = openingBalance;
            foreach (var entry in records.OrderBy(e => e.Date))
            {
                entry.Category = categoryMap.TryGetValue(entry.Description ?? "", out var cat) ? cat : "";
                runningBalance += entry.Amount ?? 0;
                entry.Balance = runningBalance;
                viewModel.Entries.Add(entry);
            }
            viewModel.CurrentBalance = runningBalance;

            // Export the current, display-ready register to BMOCDCurrent.csv using the in-memory list
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

            var transactionLines = File.ReadAllLines(transactionsFile).ToList();
            if (transactionLines.Count <= 1)
            {
                await DisplayAlert("Info", "No new transactions to add.", "OK");
                return;
            }

            // Ensure BMOCD.csv exists with header if needed
            if (!File.Exists(registerFile))
            {
                var header = transactionLines.FirstOrDefault() ?? "POSTED DATE,DESCRIPTION,AMOUNT,CURRENCY,TRANSACTION REFERENCE NUMBER,FI TRANSACTION REFERENCE,TYPE,CREDIT/DEBIT,ORIGINAL AMOUNT";
                File.WriteAllText(registerFile, header + Environment.NewLine);
            }

            // Append new transactions (skip header)
            var newLines = transactionLines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (newLines.Count > 0)
                File.AppendAllLines(registerFile, newLines);

            // Optionally clear the transactions file after appending
            File.WriteAllText(transactionsFile, transactionLines.FirstOrDefault() ?? "");

            LoadRegister();

            await DisplayAlert("Success", "New transactions added.", "OK");
        }

        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            string dateStr = await DisplayPromptAsync("Manual Entry", "Enter date (MM/dd/yyyy):");
            if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, out var date))
            {
                await DisplayAlert("Invalid", "Please enter a valid date.", "OK");
                return;
            }

            string description = await DisplayPromptAsync("Manual Entry", "Enter description:");
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlert("Invalid", "Please enter a description.", "OK");
                return;
            }

            string amountStr = await DisplayPromptAsync("Manual Entry", "Enter amount:");
            if (string.IsNullOrWhiteSpace(amountStr) || !decimal.TryParse(amountStr, out var amount))
            {
                await DisplayAlert("Invalid", "Please enter a valid amount.", "OK");
                return;
            }

            string csvLine = $"{date:MM/dd/yyyy},{description},{amount},USD,,,,,";

            if (!File.Exists(registerFile))
            {
                string header = "POSTED DATE,DESCRIPTION,AMOUNT,CURRENCY,TRANSACTION REFERENCE NUMBER,FI TRANSACTION REFERENCE,TYPE,CREDIT/DEBIT,ORIGINAL AMOUNT";
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
                string newDescription = await DisplayPromptAsync("Edit Description", "Enter new description:", initialValue: selectedEntry.Description);
                if (string.IsNullOrWhiteSpace(newDescription) || newDescription == selectedEntry.Description)
                    return;

                selectedEntry.Description = newDescription;

                var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(categoryFile))
                {
                    foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
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
                int startIdx = 0;
                if (allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION"))
                    startIdx = 1;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length < 3) continue;

                    if (DateTime.TryParse(parts[0].Trim(), out var date) &&
                        decimal.TryParse(parts[2].Trim(), out var amt) &&
                        date == selectedEntry.Date &&
                        amt == selectedEntry.Amount)
                    {
                        parts[1] = newDescription;
                        allLines[i] = string.Join(",", parts);
                        break;
                    }
                }
                File.WriteAllLines(registerFile, allLines);

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

                viewModel.Entries.Remove(entry);

                string filePath = registerFile;
                var allLines = File.ReadAllLines(filePath).ToList();
                int startIdx = allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION") ? 1 : 0;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    bool match = false;
                    if (DateTime.TryParse(parts[0].Trim(), out var date) &&
                        decimal.TryParse(parts[2].Trim(), out var amt) &&
                        date == entry.Date &&
                        amt == entry.Amount &&
                        (parts.Length > 1 && parts[1].Trim() == entry.Description))
                    {
                        match = true;
                    }

                    if (match)
                    {
                        allLines.RemoveAt(i);
                        break;
                    }
                }
                File.WriteAllLines(filePath, allLines);

                LoadRegister();
            }
            else
            {
                await DisplayAlert("Delete", "Please select a row to delete.", "OK");
            }
        }

        private void RegisterCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Do not clear selection here; allow Edit to work.
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}