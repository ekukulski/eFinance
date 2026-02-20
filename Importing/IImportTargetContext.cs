namespace eFinance.Importing;

public interface IImportTargetContext
{
    long? CurrentAccountId { get; set; }
}