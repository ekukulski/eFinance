using CsvHelper.Configuration;
using KukiFinance.Models;

namespace KukiFinance.Helpers;

public sealed class CurrentRegisterEntryMap : ClassMap<RegistryEntry>
{
    public CurrentRegisterEntryMap()
    {
        Map(m => m.Date).Name("DATE");
        Map(m => m.Description).Name("DESCRIPTION");
        Map(m => m.Category).Name("CATEGORY");
        Map(m => m.Amount).Name("AMOUNT");
        Map(m => m.Balance).Name("BALANCE");
    }
}
