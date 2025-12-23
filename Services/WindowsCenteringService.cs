using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Linq;

namespace KukiFinance.Services
{
    /// <summary>
    /// Provides a utility service for centering the main application window on the screen.
    /// This functionality is only supported on the WinUI (Windows) platform.
    /// </summary>
    public static class WindowCenteringService
    {
        /// <summary>
        /// Centers the main application window on the screen with the specified width and height.
        /// Only has an effect on the WinUI platform (Windows desktop).
        /// </summary>
        /// <param name="width">The desired width of the window in device-independent units (DIPs).</param>
        /// <param name="height">The desired height of the window in device-independent units (DIPs).</param>
        public static void CenterWindow(double width, double height)
        {
            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                var window = Application.Current?.Windows?.FirstOrDefault();
                if (window == null)
                    return;

                window.Width = width;
                window.Height = height;

                window.X = 0; // Position at left edge.  Comment out to center horizontally.
                window.Y = 0; // Position at top edge.  Comment out to center vertically.

                var displayInfo = DeviceDisplay.MainDisplayInfo;
                var screenWidth = displayInfo.Width / displayInfo.Density;
                var screenHeight = displayInfo.Height / displayInfo.Density;

                // window.X = (screenWidth - window.Width) / 2;  // Uncomment to center horizontally.
                // window.Y = (screenHeight - window.Height) / 2;  // Uncomment to center vertically.
            }
        }
    }
}