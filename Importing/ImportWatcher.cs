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

        private readonly ConcurrentDictionary<string, DateTime> _pending = new();
        private readonly Timer _timer;

        public ImportWatcher(string folderPath, ImportPipeline pipeline)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentNullException(nameof(folderPath));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

            Directory.CreateDirectory(folderPath);

            _watcher = new FileSystemWatcher(folderPath)
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
            _pending[e.FullPath] = DateTime.UtcNow;
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            _pending[e.FullPath] = DateTime.UtcNow;
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

                    // Run pipeline
                    try
                    {
                        var result = await _pipeline.ImportAsync(path);
                        System.Diagnostics.Debug.WriteLine(
                            $"Imported '{Path.GetFileName(path)}': inserted={result.Inserted}, ignored={result.Ignored}, failed={result.Failed}");
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
