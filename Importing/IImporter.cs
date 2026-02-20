namespace eFinance.Importing
{
    public interface IImporter
    {
        string SourceName { get; }
        bool CanImport(string filePath);

        Task<ImportResult> ImportAsync(string filePath, long accountId);

        AmountSignPolicy? AmountPolicy => null;

        string HeaderHint => "";
    }
}