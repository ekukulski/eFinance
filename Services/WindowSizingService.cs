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

            // ✅ Per-monitor: this resolves the display that the window is currently on
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);

            // ✅ WorkArea excludes taskbar / docked bars and has an X/Y that can be non-zero on multi-monitor setups
            var work = displayArea.WorkArea;

            int targetWidth = Math.Max(600, (int)(work.Width * 0.56));
            int targetHeight = work.Height;

            // ✅ Left-aligned on the current monitor (not global screen origin)
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
