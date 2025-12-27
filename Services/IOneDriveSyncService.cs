using System;
using System.Threading.Tasks;

namespace KukiFinance.Services
{
    public interface IOneDriveSyncService
    {
        Task<(bool ok, string message, string? snapshotName)> ExportToOneDriveAsync();

        Task<(bool ok, string message, string? snapshotName)> ImportFromOneDriveAsync();

        Task<(bool ok, string? snapshotName, DateTime? snapshotWriteUtc, string message)>
            GetLatestSnapshotInfoAsync();
    }
}
