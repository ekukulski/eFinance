using System;

namespace KukiFinance.Models;

public class RegistryEntry
{
    // Common columns (BMO, Cash, AMEX, Visa, MasterCard, etc.)
    public DateTime? Date { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }

    // BMO-specific
    public string? TransactionReferenceNumber { get; set; }

    // AMEX-specific
    public string? CardMember { get; set; }
    public string? AccountNumber { get; set; }

    // MasterCard-specific
    public decimal? Debit { get; set; }
    public decimal? Credit { get; set; }
    public string? Status { get; set; }

    // Visa-specific
    public string? Type { get; set; }
    public string? Memo { get; set; }

    // Other possible columns
    public string? Currency { get; set; }
    public string? FiTransactionReference { get; set; }
    public decimal? OriginalAmount { get; set; }
    public string? CreditOrDebit { get; set; }

    // In-memory only properties (not mapped to CSV)
    public string? Category { get; set; }
    public string? CheckNumber { get; set; }
    public string? Account { get; set; }

    // Calculated in code, not mapped to CSV
    public decimal Balance { get; set; }
}