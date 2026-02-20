namespace eFinance.Importing
{
    public sealed class ImportPipeline
    {
        private readonly List<IImporter> _importers;

        public ImportPipeline(IEnumerable<IImporter> importers)
        {
            _importers = importers?.ToList() ?? throw new ArgumentNullException(nameof(importers));
        }

        public async Task<ImportResult> ImportAsync(string filePath)
        {
            var importer = _importers.FirstOrDefault(i => i.CanImport(filePath));
            if (importer is null)
                throw new InvalidOperationException($"No importer found for: {filePath}");

            return await importer.ImportAsync(filePath);
        }
    }
}
