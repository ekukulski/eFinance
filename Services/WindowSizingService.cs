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

    // Always use this fraction of the current monitor's work-area width
    private const double WindowWidthFraction = 0.56;

    // Safety minimum so the window never becomes too small
    private const int MinimumWindowWidth = 600;
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

            // Get the display the window is currently on
            var displayArea =
                DisplayArea.GetFromWindowId(
                    _appWindow.Id,
                    DisplayAreaFallback.Primary);

            var workArea = displayArea.WorkArea;

            int targetWidth = Math.Max(
                MinimumWindowWidth,
                (int)(workArea.Width * WindowWidthFraction));

            int targetHeight = workArea.Height;

            // Left-aligned, full height of the monitor
            _appWindow.MoveAndResize(
                new RectInt32(
                    workArea.X,
                    workArea.Y,
                    targetWidth,
                    targetHeight));

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
                presenter.IsResizable = true;
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
