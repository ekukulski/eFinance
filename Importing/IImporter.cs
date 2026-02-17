using System.Threading.Tasks;

namespace eFinance.Importing
{
    public interface IImporter
    {
        string SourceName { get; }      // "AMEX"
        bool CanImport(string filePath);
        Task<ImportResult> ImportAsync(string filePath);
    }
}
