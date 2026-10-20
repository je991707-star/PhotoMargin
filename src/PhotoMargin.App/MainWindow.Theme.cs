using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using System.Runtime.InteropServices;
using PhotoMargin.App.Services;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    private readonly UISettings systemColors = new();
    private WindowThemeListener? themeListener;
    private bool systemColorsSubscribed;
    private ElementTheme? chromeTheme;
    private bool? chromeHighContrast;
    private void StartThemeTracking()
    {
        try { systemColors.ColorValuesChanged += SystemColorsChanged; systemColorsSubscribed = true; }
        catch (COMException) { } // Unpackaged systems can lack the WinRT event source.
        themeListener = new(WinRT.Interop.WindowNative.GetWindowHandle(this), () => DispatcherQueue.TryEnqueue(() => { if (!closed) ApplyTheme(); }));
        RootGrid.ActualThemeChanged += RootThemeChanged;
    }
    private void StopThemeTracking()
    {
        if (systemColorsSubscribed) try { systemColors.ColorValuesChanged -= SystemColorsChanged; } catch (COMException) { }
        themeListener?.Dispose();
        RootGrid.ActualThemeChanged -= RootThemeChanged;
    }
    private void SystemColorsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (!closed) ApplyTheme(); });
    private void RootThemeChanged(FrameworkElement sender, object args) { if (!closed) SynchronizeThemeChrome(); }
    private void SynchronizeThemeChrome()
    {
        var title = AppWindow.TitleBar;
        var highContrast = WindowThemeListener.IsHighContrast;
        if (chromeTheme != RootGrid.ActualTheme || chromeHighContrast != highContrast)
        {
        chromeTheme = RootGrid.ActualTheme; chromeHighContrast = highContrast;
        if (highContrast)
        {
            title.BackgroundColor = null; title.ForegroundColor = null; title.InactiveBackgroundColor = null; title.InactiveForegroundColor = null;
            title.ButtonBackgroundColor = null; title.ButtonForegroundColor = null; title.ButtonHoverBackgroundColor = null;
            title.ButtonHoverForegroundColor = null; title.ButtonPressedForegroundColor = null;
            title.ButtonPressedBackgroundColor = null; title.ButtonInactiveBackgroundColor = null; title.ButtonInactiveForegroundColor = null;
        }
        else
        {
            var dark = RootGrid.ActualTheme == ElementTheme.Dark;
            var background = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 249, 249, 249);
            var foreground = dark ? Colors.White : Colors.Black;
            title.BackgroundColor = background; title.ForegroundColor = foreground;
            title.InactiveBackgroundColor = background; title.InactiveForegroundColor = Colors.Gray;
            title.ButtonBackgroundColor = background; title.ButtonForegroundColor = foreground;
            title.ButtonHoverForegroundColor = foreground; title.ButtonPressedForegroundColor = foreground;
            title.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 60, 60, 60) : Windows.UI.Color.FromArgb(255, 230, 230, 230);
            title.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 80, 80, 80) : Windows.UI.Color.FromArgb(255, 210, 210, 210);
            title.ButtonInactiveBackgroundColor = background; title.ButtonInactiveForegroundColor = Colors.Gray;
        }
        }
        if (RootGrid.XamlRoot is not null)
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot)) UpdateDialogTheme(popup.Child);
    }
    private void UpdateDialogTheme(DependencyObject? element)
    {
        if (element is null) return;
        if (element is ContentDialog dialog) dialog.RequestedTheme = RootGrid.ActualTheme;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) UpdateDialogTheme(VisualTreeHelper.GetChild(element, i));
    }
}
