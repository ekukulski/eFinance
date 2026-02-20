namespace eFinance.Data.Models;

public sealed class Account
{
    /// <summary>Primary key (AUTOINCREMENT).</summary>
    public long Id { get; set; }

    /// <summary>Display name for the account (NOT NULL).</summary>
    public string Name { get; set; } = "";

    /// <summary>When the account was created (UTC, stored as ISO-8601 string).</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Type/category of account (stored in DB as TEXT).
    /// Examples: Checking, Savings, CreditCard, Investment, Loan
    /// </summary>
    public string AccountType { get; set; } = "Checking";

    /// <summary>
    /// Whether the account is active (stored in DB as INTEGER 0/1).
    /// </summary>
    public bool IsActive { get; set; } = true;
}
