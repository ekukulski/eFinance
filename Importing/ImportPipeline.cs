using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace eFinance.Importing
{
    public sealed class ImportPipeline
    {
        private readonly List<IImporter> _importers;

        public ImportPipeline(IEnumerable<IImporter> importers)
        {
            _importers = importers?.ToList() ?? throw new ArgumentNullException(nameof(importers));
        }
        public IImporter? FindImporter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            return _importers.FirstOrDefault(i => i.CanImport(filePath));
        }

        /// <summary>
        /// Legacy overload is intentionally NOT supported now that importers require accountId.
        /// Keep this method only so any old call sites fail with a clear error message.
        /// </summary>
        public Task<ImportResult> ImportAsync(string filePath)
        {
            throw new InvalidOperationException(
                "ImportPipeline.ImportAsync(filePath) is no longer supported. " +
                "Call ImportAsync(filePath, accountId) so imports go into the currently open register.");
        }

        /// <summary>
        /// Import into a specific account (the currently-open register accountId).
        /// </summary>
        public async Task<ImportResult> ImportAsync(string filePath, long accountId)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required.", nameof(filePath));

            if (accountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(accountId));

            var importer = _importers.FirstOrDefault(i => i.CanImport(filePath));
            if (importer is null)
                throw BuildNoImporterFoundException(filePath);

            return await importer.ImportAsync(filePath, accountId);
        }

        private InvalidOperationException BuildNoImporterFoundException(string filePath)
        {
            string header = "";
            try
            {
                header = File.ReadLines(filePath).FirstOrDefault() ?? "";
            }
            catch { /* ignore */ }

            var importers = string.Join(", ", _importers.Select(i =>
            {
                var pol = i.AmountPolicy?.ToString() ?? "n/a";
                return $"{i.SourceName}(policy:{pol})";
            }));

            var msg =
                $"No importer found for: {filePath}\n" +
                $"Header: {header}\n" +
                $"Available importers: {importers}";

            return new InvalidOperationException(msg);
        }
    }
}