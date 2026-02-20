using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace eFinance.Importing
{
    public sealed class ImportWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly ImportPipeline _pipeline;
        private readonly IImportTargetContext _target;

        private readonly ConcurrentDictionary<string, DateTime> _pending = new();
        private readonly Timer _timer;

        private readonly string _folderPath;
        private readonly string _archiveFolderPath;

        public ImportWatcher(string folderPath, ImportPipeline pipeline, IImportTargetContext target)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentNullException(nameof(folderPath));

            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _target = target ?? throw new ArgumentNullException(nameof(target));

            _folderPath = folderPath;
            _archiveFolderPath = Path.Combine(_folderPath, "Archive");

            Directory.CreateDirectory(_folderPath);
            Directory.CreateDirectory(_archiveFolderPath);

            _watcher = new FileSystemWatcher(_folderPath)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = false,
                Filter = "*.csv"
            };

            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Renamed += OnRenamed;

            // Debounce: every 1s, process files quiet for >= 2s
            _timer = new Timer(async _ => await ProcessPendingAsync(),
                null, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(1));
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
            _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            // Ignore anything already in Archive (defensive)
            if (IsUnderArchive(e.FullPath)) return;

            _pending[e.FullPath] = DateTime.UtcNow;
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (IsUnderArchive(e.FullPath)) return;

            _pending[e.FullPath] = DateTime.UtcNow;
        }

        private bool IsUnderArchive(string fullPath)
        {
            try
            {
                var archiveFull = Path.GetFullPath(_archiveFolderPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                var pathFull = Path.GetFullPath(fullPath);

                return pathFull.StartsWith(archiveFull, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async Task ProcessPendingAsync()
        {
            try
            {
                var now = DateTime.UtcNow;

                foreach (var kvp in _pending)
                {
                    var path = kvp.Key;
                    var lastSeen = kvp.Value;

                    // Wait until quiet for 2 seconds
                    if ((now - lastSeen) < TimeSpan.FromSeconds(2))
                        continue;

                    // Remove first so we don't double-process
                    if (!_pending.TryRemove(path, out _))
                        continue;

                    // Ensure file is ready (not locked / still writing)
                    if (!await WaitForFileReadyAsync(path))
                        continue;

                    // NEW: Require an active target account (currently open register)
                    var accountId = _target.CurrentAccountId;
                    if (accountId is null || accountId <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Import skipped for '{Path.GetFileName(path)}' because no account is currently selected. File left in ImportDrop.");
                        continue;
                    }

                    // Run pipeline
                    try
                    {
                        // NOTE: This requires ImportPipeline to have an overload:
                        //   Task<ImportResult> ImportAsync(string filePath, long accountId)
                        var result = await _pipeline.ImportAsync(path, accountId.Value);

                        System.Diagnostics.Debug.WriteLine(
                            $"Imported '{Path.GetFileName(path)}' into accountId={accountId.Value}: inserted={result.Inserted}, ignored={result.Ignored}, failed={result.Failed}");

                        // Archive only if we actually processed anything (inserted or ignored).
                        // If everything failed, leave it in place for inspection.
                        if (result.Inserted + result.Ignored > 0)
                        {
                            TryArchiveFile(path);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"Not archiving '{Path.GetFileName(path)}' because nothing was processed (all failed or empty).");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Import failed for '{path}': {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ImportWatcher.ProcessPendingAsync error: " + ex);
            }
        }

        private void TryArchiveFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                Directory.CreateDirectory(_archiveFolderPath);

                var fileName = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path);

                // Timestamped archive name to avoid collisions and preserve history
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var archivedName = $"{fileName}-{stamp}{ext}";
                var destPath = Path.Combine(_archiveFolderPath, archivedName);

                // In the rare case of same-second collision, add a counter
                int counter = 1;
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(_archiveFolderPath, $"{fileName}-{stamp}-{counter}{ext}");
                    counter++;
                }

                File.Move(path, destPath);

                System.Diagnostics.Debug.WriteLine(
                    $"Archived '{Path.GetFileName(path)}' -> '{destPath}'");
            }
            catch (Exception ex)
            {
                // If archive fails, don't crash the watcher — just log it.
                System.Diagnostics.Debug.WriteLine(
                    $"Archive failed for '{path}': {ex}");
            }
        }

        private static async Task<bool> WaitForFileReadyAsync(string path)
        {
            // Try for ~5 seconds
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (!File.Exists(path))
                        return false;

                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > 0)
                        return true;
                }
                catch
                {
                    // still locked
                }

                await Task.Delay(250);
            }

            return false;
        }

        public void Dispose()
        {
            Stop();
            _watcher.Dispose();
            _timer.Dispose();
        }
    }
}