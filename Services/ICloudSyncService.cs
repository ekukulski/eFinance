namespace eFinance.Services;

public interface ICloudSyncService
{
    Task<(bool ok, string message, string? snapshotName)> ExportToCloudAsync();
    Task<(bool ok, string message, string? snapshotName)> ImportFromCloudAsync();
    Task<(bool ok, string? snapshotName, DateTime? snapshotWriteUtc, string message)> GetLatestSnapshotInfoAsync();
}