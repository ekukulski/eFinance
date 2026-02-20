using System;
using System.Globalization;

namespace eFinance.Helpers;

public enum AmountSignPolicy
{
    /// <summary>CSV amount is already: purchases negative, payments positive.</summary>
    AsIs,

    /// <summary>CSV amount is opposite: purchases positive, payments negative.</summary>
    Invert,

    /// <summary>CSV provides separate Debit/Credit columns; derive sign.</summary>
    DebitCreditColumns
}

public static class AmountNormalizer
{
    /// <summary>
    /// Normalize a single signed amount value according to the policy.
    /// </summary>
    public static decimal Normalize(decimal csvAmount, AmountSignPolicy policy)
        => policy switch
        {
            AmountSignPolicy.AsIs => csvAmount,
            AmountSignPolicy.Invert => -csvAmount,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Use NormalizeDebitCredit for DebitCreditColumns.")
        };

    /// <summary>
    /// Normalize when the CSV has separate Debit/Credit columns.
    /// Debit becomes negative; Credit becomes positive.
    /// Exactly one must be present.
    /// </summary>
    public static decimal NormalizeDebitCredit(decimal? debit, decimal? credit)
    {
        var hasDebit = debit.HasValue && debit.Value != 0m;
        var hasCredit = credit.HasValue && credit.Value != 0m;

        if (hasDebit == hasCredit)
            throw new FormatException("Expected exactly one of Debit or Credit to have a value.");

        return hasDebit ? -Math.Abs(debit!.Value) : Math.Abs(credit!.Value);
    }

    /// <summary>
    /// Convenience: parse a decimal using invariant culture (CSV-friendly),
    /// then normalize using AsIs/Invert policies.
    /// </summary>
    public static decimal ParseAndNormalize(string value, AmountSignPolicy policy)
    {
        if (!decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amt))
            throw new FormatException($"Invalid amount: '{value}'");

        return Normalize(amt, policy);
    }
}