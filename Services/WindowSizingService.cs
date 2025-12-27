#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using Microsoft.Maui.Storage;
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
    private bool _eventsHooked;

    // Safety minimum so the window never becomes too small
    private const int MinimumWindowWidth = 600;
    private const int MinimumWindowHeight = 500;

    // Preference keys (per-PC)
    private const string KeyX = "window_x";
    private const string KeyY = "window_y";
    private const string KeyW = "window_w";
    private const string KeyH = "window_h";
    private const string KeyMax = "window_is_maximized";
#endif

    public void ApplyDefaultSizing()
    {
#if WINDOWS
        if (_isApplying)
            return;

        try
        {
            _isApplying = true;

            EnsureAppWindow();
            if (_appWindow is null)
                return;

            HookWindowEventsOnce();

            // If we have saved sizing for this machine, restore it.
            if (TryRestoreSavedBounds())
                return;

            // Otherwise: first run / no saved bounds -> start "full screen" (maximized)
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
                presenter.IsResizable = true;

                presenter.Maximize();
            }
            else
            {
                // Fallback: move+resize to the monitor work area
                var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;

                _appWindow.MoveAndResize(new RectInt32(workArea.X, workArea.Y, workArea.Width, workArea.Height));
            }
        }
        finally
        {
            _isApplying = false;
        }
#endif
    }

#if WINDOWS
    private void HookWindowEventsOnce()
    {
        if (_appWindow is null || _eventsHooked)
            return;

        _eventsHooked = true;

        // Persist changes caused by the user (resize/move/maximize/restore).
        _appWindow.Changed += (_, args) =>
        {
            // Don’t persist while we're applying startup sizing.
            if (_isApplying || _appWindow is null)
                return;

            // Only write when something relevant changed
            if (!(args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange))
                return;

            SaveCurrentBounds();
        };
    }

    private bool TryRestoreSavedBounds()
    {
        if (_appWindow is null)
            return false;

        // If width/height aren't stored, treat as first run.
        if (!Preferences.Default.ContainsKey(KeyW) || !Preferences.Default.ContainsKey(KeyH))
            return false;

        int w = Preferences.Default.Get(KeyW, 0);
        int h = Preferences.Default.Get(KeyH, 0);

        if (w <= 0 || h <= 0)
            return false;

        int x = Preferences.Default.Get(KeyX, int.MinValue);
        int y = Preferences.Default.Get(KeyY, int.MinValue);
        bool wasMaximized = Preferences.Default.Get(KeyMax, false);

        // Enable normal window controls
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }

        // Clamp to current monitor work area so it always fits any monitor.
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        w = Math.Max(MinimumWindowWidth, Math.Min(w, workArea.Width));
        h = Math.Max(MinimumWindowHeight, Math.Min(h, workArea.Height));

        // If we never saved position (or it's invalid), center it.
        if (x == int.MinValue || y == int.MinValue)
        {
            x = workArea.X + (workArea.Width - w) / 2;
            y = workArea.Y + (workArea.Height - h) / 2;
        }
        else
        {
            // Keep it on-screen
            x = Math.Max(workArea.X, Math.Min(x, workArea.X + workArea.Width - w));
            y = Math.Max(workArea.Y, Math.Min(y, workArea.Y + workArea.Height - h));
        }

        _appWindow.MoveAndResize(new RectInt32(x, y, w, h));

        if (wasMaximized && _appWindow.Presenter is OverlappedPresenter p2)
            p2.Maximize();

        return true;
    }

    private void SaveCurrentBounds()
    {
        if (_appWindow is null)
            return;

        var presenter = _appWindow.Presenter as OverlappedPresenter;
        var state = presenter?.State ?? OverlappedPresenterState.Restored;

        // Always save maximized flag
        bool isMaximized = state == OverlappedPresenterState.Maximized;
        Preferences.Default.Set(KeyMax, isMaximized);

        // IMPORTANT:
        // Only save X/Y/W/H when the window is NOT maximized.
        // This preserves the user's last "restored" size even if they maximize.
        if (isMaximized)
            return;

        var rect = _appWindow.GetRect();

        // Optional: enforce minimums before saving
        int w = Math.Max(MinimumWindowWidth, rect.Width);
        int h = Math.Max(MinimumWindowHeight, rect.Height);

        Preferences.Default.Set(KeyX, rect.X);
        Preferences.Default.Set(KeyY, rect.Y);
        Preferences.Default.Set(KeyW, w);
        Preferences.Default.Set(KeyH, h);
    }

    private void EnsureAppWindow()
    {
        if (_appWindow is not null)
            return;

        var mauiWindow = Application.Current?.Windows?.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is null)
            return;

        var hwnd = WindowNative.GetWindowHandle(mauiWindow.Handler.PlatformView);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);

        _appWindow = AppWindow.GetFromWindowId(windowId);
    }
#endif
}

#if WINDOWS
internal static class AppWindowExtensions
{
    public static RectInt32 GetRect(this AppWindow w)
        => new RectInt32(w.Position.X, w.Position.Y, w.Size.Width, w.Size.Height);
}
#endif
