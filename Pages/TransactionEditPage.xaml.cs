using System;
using System.Collections.Generic;
using eFinance.ViewModels;
using Microsoft.Maui.Controls;

namespace eFinance.Pages;

public partial class TransactionEditPage : ContentPage, IQueryAttributable
{
    private readonly TransactionEditViewModel _vm;

    public TransactionEditPage(TransactionEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        BindingContext = _vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Shell passes query values as strings
        var accountId = ParseLong(query, "accountId");
        var transactionId = ParseNullableLong(query, "transactionId");

        System.Diagnostics.Debug.WriteLine(
            $"TransactionEditPage.ApplyQueryAttributes: accountId={accountId}, transactionId={(transactionId?.ToString() ?? "null")}");

        _ = _vm.InitializeAsync(accountId, transactionId);
    }

    private static long ParseLong(IDictionary<string, object> query, string key)
    {
        if (!query.TryGetValue(key, out var value) || value is null)
            return 0;

        if (value is long l)
            return l;

        return long.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static long? ParseNullableLong(IDictionary<string, object> query, string key)
    {
        if (!query.TryGetValue(key, out var value) || value is null)
            return null;

        if (value is long l)
            return l;

        var s = value.ToString();
        if (string.IsNullOrWhiteSpace(s))
            return null;

        return long.TryParse(s, out var parsed) ? parsed : null;
    }
}
