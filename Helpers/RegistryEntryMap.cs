using CsvHelper.Configuration;
using eFinance.Models;

namespace eFinance.Helpers;

public sealed class RegistryEntryMap : ClassMap<RegistryEntry>
{
    public RegistryEntryMap()
    {
        // Dates
        Map(m => m.Date).Name(
            "POSTED DATE", "DATE", "Date", "Post Date", "Transaction Date"
        );

        // Description
        Map(m => m.Description).Name("DESCRIPTION", "Description");

        // Amounts
        Map(m => m.Amount).Name("AMOUNT", "Amount");

        // AMEX-specific
        Map(m => m.CardMember).Name("Card Member");
        Map(m => m.AccountNumber).Name("Account #");

        // MasterCard-specific
        Map(m => m.Status).Name("Status");
        Map(m => m.Debit).Name("Debit");
        Map(m => m.Credit).Name("Credit");

        // Visa-specific
        Map(m => m.Category).Name("Category");
        Map(m => m.CheckNumber).Name("Check Number");
        Map(m => m.TransactionReferenceNumber).Name("Transaction Reference Number");

        // Schwab / generic CSV fields
        Map(m => m.Type).Name("Type");
        Map(m => m.Memo).Name("Memo");
        Map(m => m.Currency).Name("Currency");
        Map(m => m.FiTransactionReference).Name("Fi Transaction Reference");
        Map(m => m.OriginalAmount).Name("Original Amount");
        Map(m => m.CreditOrDebit).Name("Credit/Debit");
        Map(m => m.Account).Name("Account");
    }
}
