using System;
using System.Collections.ObjectModel;
using System.Linq;
using eFinance.ViewModels;

namespace eFinance.Models
{
    public class RegisterViewModel : RegisterViewModelBase<RegistryEntry>
    {
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    FilterEntries();
                }
            }
        }

        public ObservableCollection<RegistryEntry> FilteredEntries { get; } = new();

        public RegisterViewModel()
        {
            FilterEntries();
        }

        public void FilterEntries()
        {
            FilteredEntries.Clear();
            var query = string.IsNullOrWhiteSpace(SearchText)
                ? Entries
                : Entries.Where(e =>
                    e.Date.HasValue && e.Date.Value.ToString("yyyy-MM-dd").Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (e.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Category?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.CheckNumber?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Amount?.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    e.Balance.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                );
            foreach (var entry in query)
                FilteredEntries.Add(entry);
        }

        protected override object? GetPropertyValue(RegistryEntry entry, string propertyName)
        {
            return propertyName switch
            {
                nameof(RegistryEntry.Date) => entry.Date,
                nameof(RegistryEntry.Description) => entry.Description,
                nameof(RegistryEntry.Category) => entry.Category,
                nameof(RegistryEntry.CheckNumber) => entry.CheckNumber,
                nameof(RegistryEntry.Amount) => entry.Amount,
                nameof(RegistryEntry.Balance) => entry.Balance,
                nameof(RegistryEntry.TransactionReferenceNumber) => entry.TransactionReferenceNumber,
                nameof(RegistryEntry.CardMember) => entry.CardMember,
                nameof(RegistryEntry.AccountNumber) => entry.AccountNumber,
                nameof(RegistryEntry.Debit) => entry.Debit,
                nameof(RegistryEntry.Credit) => entry.Credit,
                nameof(RegistryEntry.Status) => entry.Status,
                nameof(RegistryEntry.Type) => entry.Type,
                nameof(RegistryEntry.Memo) => entry.Memo,
                nameof(RegistryEntry.Currency) => entry.Currency,
                nameof(RegistryEntry.FiTransactionReference) => entry.FiTransactionReference,
                nameof(RegistryEntry.OriginalAmount) => entry.OriginalAmount,
                nameof(RegistryEntry.CreditOrDebit) => entry.CreditOrDebit,
                nameof(RegistryEntry.Account) => entry.Account,
                _ => null
            };
        }

        protected override void RecalculateBalance()
        {
            decimal runningBalance = 0m;
            foreach (var entry in Entries.OrderBy(e => e.Date))
            {
                runningBalance += entry.Amount ?? 0;
                entry.Balance = runningBalance;
            }
            CurrentBalance = runningBalance;
            FilterEntries();
        }
    }
}