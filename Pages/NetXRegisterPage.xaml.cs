using KukiFinance.Models;
using KukiFinance.Services;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using KukiFinance.Constants;
using KukiFinance.Helpers;

namespace KukiFinance.Pages
{
    public partial class NetXRegisterPage : ContentPage
    {
        private readonly string registerFile = FilePathHelper.GetKukiFinancePath("NetX.csv");
        private readonly string currentFile = FilePathHelper.GetKukiFinancePath("NetXCurrent.csv");
        private readonly string categoryFile = FilePathHelper.GetKukiFinancePath("Category.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("NetX");

        private readonly RegisterViewModel viewModel = new();

        public NetXRegisterPage()
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

            // Build category map
            var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
                    categoryMap[key] = value;
            }

            // Use RegisterService to load entries
            var entries = RegisterService.LoadRegister<RegistryEntry>(
                registerFile,
                categoryFile,
                openingBalance,
                parts => new RegistryEntry
                {
                    Date = DateTime.TryParse(parts[0].Trim(), out var date) ? date : (DateTime?)null,
                    Description = parts[1].Trim(),
                    Amount = decimal.TryParse(parts[2].Trim(), out var amt) ? amt : 0
                },
                entry => entry.Date ?? DateTime.MinValue,
                entry => entry.Amount ?? 0,
                entry => entry.Balance,
                (entry, balance) => entry.Balance = balance,
                new RegistryEntry
                {
                    Date = OpeningBalances.GetDate("NetX") ?? DateTime.Today.AddDays(-1),
                    Description = "OPENING BALANCE",
                    Category = "Equity",
                    Amount = openingBalance,
                    Balance = openingBalance
                }
            );

            viewModel.Entries.Clear();
            foreach (var entry in entries)
            {
                entry.Category = categoryMap.TryGetValue(entry.Description ?? "", out var cat) ? cat : "";
                viewModel.Entries.Add(entry);
            }
            viewModel.CurrentBalance = viewModel.Entries.LastOrDefault()?.Balance ?? 0m;

            RegisterExporter.ExportRegisterWithBalance(
                viewModel.Entries.ToList(),
                currentFile,
                includeCheckNumber: false
            );
            viewModel.FilterEntries();
        }

        private List<(string Security, decimal Amount)> ReadSecurities()
        {
            var path = FilePathHelper.GetKukiFinancePath("NetXSecurities.csv");
            var result = new List<(string, decimal)>();
            if (!File.Exists(path)) return result;
            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length == 2 && decimal.TryParse(parts[1], out var amt))
                    result.Add((parts[0].Trim(), amt));
            }
            return result;
        }

        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            var securities = ReadSecurities();

            // Show the modal SecurityReviewPage
            var reviewPage = new SecurityReviewPage(securities);
            await Navigation.PushModalAsync(reviewPage);

            // Wait for the modal to finish
            await reviewPage.ReviewCompleted.Task;

            // After modal closes, get the results
            var ridaAmount = reviewPage.RidaAmount;
            var ridaDescription = reviewPage.RidaDescription;
            var finalSecurities = reviewPage.FinalSecurities ?? new List<(string, decimal)>();

            // Calculate new reviewed balance
            decimal newBalance = finalSecurities.Sum(s => s.Amount) + ridaAmount;

            // Get prior transaction balance (last entry in register)
            decimal priorBalance = viewModel.Entries.LastOrDefault()?.Balance ?? openingBalance;

            // Calculate transaction amount as the difference (corrected)
            decimal transactionAmount = newBalance - priorBalance;

            // Compose transaction
            string dateStr = await DisplayPromptAsync("Manual Entry", "Enter date (MM/dd/yyyy):", initialValue: DateTime.Today.ToString("MM/dd/yyyy"));
            if (!DateTime.TryParse(dateStr, out var date))
            {
                await DisplayAlert("Invalid", "Please enter a valid date.", "OK");
                return;
            }
            string description = ridaDescription;
            string csvLine = $"{date:MM/dd/yyyy},{description},{transactionAmount}";

            // Write to register
            if (!File.Exists(registerFile))
            {
                string header = "DATE,DESCRIPTION,AMOUNT";
                File.WriteAllText(registerFile, header + Environment.NewLine);
            }
            File.AppendAllText(registerFile, csvLine + Environment.NewLine);

            // Optionally, update NetXSecurities.csv with edits
            var lines = new List<string> { "Security,Amount" };
            lines.AddRange(finalSecurities.Select(s => $"{s.Security},{s.Amount}"));
            File.WriteAllLines(FilePathHelper.GetKukiFinancePath("NetXSecurities.csv"), lines);

            LoadRegister();
            await DisplayAlert("Success", $"Manual transaction added. Amount: {transactionAmount:C}", "OK");
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

                // Lookup new category
                var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
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

                // Save the change to NetX.csv
                var allLines = File.ReadAllLines(registerFile).ToList();
                // Detect if file has header
                int startIdx = 0;
                if (allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION"))
                    startIdx = 1;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length < 3) continue;

                    // Match by Date and Amount (fields unlikely to change)
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

                // Optionally, reload to recalculate balances/categories
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