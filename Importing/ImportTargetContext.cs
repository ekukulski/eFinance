namespace eFinance.Importing;

public sealed class ImportTargetContext : IImportTargetContext
{
    public long? CurrentAccountId { get; set; }
}