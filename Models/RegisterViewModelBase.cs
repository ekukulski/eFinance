using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace KukiFinance.Models;

public abstract class RegisterViewModelBase<TEntry> : INotifyPropertyChanged
{
    public ObservableCollection<TEntry> Entries { get; } = new();

    private decimal _currentBalance;
    public decimal CurrentBalance
    {
        get => _currentBalance;
        set { if (_currentBalance != value) { _currentBalance = value; OnPropertyChanged(); } }
    }

    public ICommand SortCommand { get; }

    private string _lastSortColumn;
    private bool _lastSortAscending = true;

    protected RegisterViewModelBase()
    {
        SortCommand = new Command<string>(SortByColumn);
    }

    private void SortByColumn(string column)
    {
        if (string.IsNullOrEmpty(column) || Entries.Count == 0)
            return;

        _lastSortAscending = _lastSortColumn == column ? !_lastSortAscending : true;
        _lastSortColumn = column;

        var sorted = _lastSortAscending
            ? Entries.OrderBy(e => GetPropertyValue(e, column)).ToList()
            : Entries.OrderByDescending(e => GetPropertyValue(e, column)).ToList();

        Entries.Clear();
        foreach (var entry in sorted)
            Entries.Add(entry);

        RecalculateBalance();
    }

    protected abstract object GetPropertyValue(TEntry entry, string propertyName);
    protected abstract void RecalculateBalance();

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}