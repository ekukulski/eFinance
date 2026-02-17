using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace eFinance.Services;

public record FileResult<T>(bool Success, string Message, T? Data);

public interface ICsvFileService
{
    FileResult<List<T>> TryRead<T>(string path, bool hasHeader = true);
    FileResult<bool> TryWrite<T>(string path, IEnumerable<T> records, bool includeHeader = true);
}

public sealed class CsvFileService : ICsvFileService
{
    private static CsvConfiguration CreateConfig(bool hasHeaderRecord)
        => new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeaderRecord,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim
        };

    public FileResult<List<T>> TryRead<T>(string path, bool hasHeader = true)
    {
        try
        {
            if (!File.Exists(path))
                return new(false, $"File not found: {path}", null);

            var config = CreateConfig(hasHeader);

            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, config);

            var records = csv.GetRecords<T>().ToList();
            return new(true, $"Loaded {records.Count} rows from {Path.GetFileName(path)}.", records);
        }
        catch (Exception ex)
        {
            return new(false, $"Failed to read {Path.GetFileName(path)}: {ex.Message}", null);
        }
    }

    public FileResult<bool> TryWrite<T>(string path, IEnumerable<T> records, bool includeHeader = true)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var config = CreateConfig(includeHeader);

            using var writer = new StreamWriter(path, false);
            using var csv = new CsvWriter(writer, config);

            csv.WriteRecords(records);

            // Avoid multiple enumeration surprises
            var count = records is ICollection<T> c ? c.Count : records.Count();
            return new(true, $"Wrote {count} rows to {Path.GetFileName(path)}.", true);
        }
        catch (Exception ex)
        {
            return new(false, $"Failed to write {Path.GetFileName(path)}: {ex.Message}", false);
        }
    }
}
