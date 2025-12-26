#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
#endif

namespace KukiFinance.Services;

public interface IWindowSizingService
{
    void ApplyDefaultSizing();
}

public sealed class WindowSizingService : IWindowSizingService
{
#if WINDOWS
    private AppWindow? _appWindow;
    private bool _isApplying;

    // Heuristic threshold: if the current monitor's work-area width is at or below this,
    // treat it like a laptop/internal display and use the full width.
    // Common laptop widths: 1366, 1440, 1536 (scaled), 1600, etc.
    private const int LaptopLikeWorkAreaWidthThreshold = 1600;

    // External monitor behavior: use this fraction of the current monitor work-area width.
    private const double ExternalMonitorWidthFraction = 0.56;

    // Minimum width safeguard for external monitor layout
    private const int MinimumWindowWidth = 600;
#endif

    public void ApplyDefaultSizing()
    {
#if WINDOWS
        if (_isApplying) return;

        try
        {
            _isApplying = true;

            EnsureAppWindow();
            if (_appWindow is null) return;

            // Per-monitor: resolves the display the window is currently on
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);

            // WorkArea excludes taskbar/docked bars, and X/Y can be non-zero on multi-monitor
            var work = displayArea.WorkArea;

            // Determine whether we should treat this as a "laptop/internal monitor" situation.
            // Heuristics:
            //  1) If only one display area is detected, it's commonly an undocked laptop.
            //  2) If the work-area width is relatively small, it’s commonly an internal panel.
            bool singleDisplay = false;
            try
            {
                var all = DisplayArea.FindAll();
                singleDisplay = all is not null && all.Count <= 1;
            }
            catch
            {
                // If FindAll isn't available for any reason, fall back to width heuristic only.
                singleDisplay = false;
            }

            bool looksLikeLaptopPanel =
                singleDisplay ||
                work.Width <= LaptopLikeWorkAreaWidthThreshold;

            int targetWidth;
            int targetHeight = work.Height;

            if (looksLikeLaptopPanel)
            {
                // Laptop/internal monitor: use the entire work area
                targetWidth = work.Width;
            }
            else
            {
                // External monitor: preserve your "left dock" layout
                targetWidth = Math.Max(MinimumWindowWidth, (int)(work.Width * ExternalMonitorWidthFraction));
            }

            // Left-aligned on the current monitor
            _appWindow.MoveAndResize(new RectInt32(work.X, work.Y, targetWidth, targetHeight));

            // Optional: allow normal window controls
            if (_appWindow.Presenter is OverlappedPresenter p)
            {
                p.IsMaximizable = true;
                p.IsMinimizable = true;
                p.IsResizable = true;
            }
        }
        finally
        {
            _isApplying = false;
        }
#endif
    }

#if WINDOWS
    private void EnsureAppWindow()
    {
        if (_appWindow is not null) return;

        // Grab the current MAUI window handle once
        var mauiWindow = Application.Current?.Windows?.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is null) return;

        var hwnd = WindowNative.GetWindowHandle(mauiWindow.Handler.PlatformView);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
    }
#endif
}
