using CsvHelper.Configuration;
using KukiFinance.Models;

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
        Map(m => m.Type).Name("Type");
       
        Map(m => m.Memo).Name("Memo");

        // Common/Other
        Map(m => m.Currency).Name("CURRENCY");
        Map(m => m.CheckNumber).Name("TRANSACTION REFERENCE NUMBER");
        Map(m => m.FiTransactionReference).Name("FI TRANSACTION REFERENCE");
        Map(m => m.OriginalAmount).Name("ORIGINAL AMOUNT");
        Map(m => m.CreditOrDebit).Name("CREDIT/DEBIT");
        Map(m => m.Account).Name("ACCOUNT");
        Map(m => m.Type).Name("TYPE");
    }
}