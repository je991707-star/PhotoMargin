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
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-aspects" or "--verify-aspects-restore"))
            {
                var restore = Environment.GetCommandLineArgs().Contains("--verify-aspects-restore");
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-aspect-settings.json");
                if (!restore && File.Exists(testSettings)) File.Delete(testSettings);
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow; window.Activate();
                _ = restore ? testWindow.RunAspectRestoreAsync() : testWindow.RunAspectPreviewAsync(); return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-presets-restore"))
            {
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-preset-settings.json");
                if (!File.Exists(testSettings)) throw new InvalidOperationException("請先執行 --verify-presets。");
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow; window.Activate(); _ = testWindow.RunPresetRestoreRegressionAsync(); return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-presets"))
            {
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-preset-settings.json");
                if (File.Exists(testSettings)) File.Delete(testSettings);
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow; window.Activate();
                _ = testWindow.RunPresetWorkflowRegressionAsync();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-close-save"))
            {
                var testWindow = new MainWindow(new Services.SettingsStore(Path.Combine(AppContext.BaseDirectory, "close-save-settings.json")));
                window = testWindow; window.Activate();
                _ = testWindow.RunCloseSaveRegressionAsync();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-arrangement-restore"))
            {
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-figma-regression-settings.json");
                if (!File.Exists(testSettings)) throw new InvalidOperationException("請先執行 --verify-settings 建立獨立的測試偏好。");
                var testResult = Path.Combine(AppContext.BaseDirectory, "arrangement-restore-result.json");
                if (File.Exists(testResult)) File.Delete(testResult);
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow; window.Activate();
                _ = testWindow.RunArrangementRestoreRegressionAsync();
                return;
            }
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
