using Microsoft.UI.Xaml;

namespace PhotoMargin.App;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        UnhandledException += (_, args) => LogStartupException(args.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--verify-settings"))
            {
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-figma-regression-settings.json");
                if (File.Exists(testSettings)) File.Delete(testSettings);
                var testResult = Path.Combine(AppContext.BaseDirectory, "ui-regression-result.json");
                if (File.Exists(testResult)) File.Delete(testResult);
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow;
                window.Activate();
                _ = testWindow.RunSettingsNavigationRegressionAsync();
                return;
            }
#endif
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception exception)
        {
            LogStartupException(exception);
            throw;
        }
    }

    private static void LogStartupException(Exception exception)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), exception.ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
