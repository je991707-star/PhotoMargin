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
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-data" or "--verify-data-restore"))
            {
                var restore = Environment.GetCommandLineArgs().Contains("--verify-data-restore");
                var marker = Path.Combine(AppContext.BaseDirectory, "data-regression-location.txt");
                var testRoot = restore ? File.ReadAllText(marker) : Path.Combine(AppContext.BaseDirectory, "data-regression-" + Guid.NewGuid().ToString("N"));
                if (!Path.GetFullPath(testRoot).StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(testRoot).StartsWith("data-regression-", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid test data path");
                if (!restore) File.WriteAllText(marker, testRoot);
                var testWindow = new MainWindow(new Services.SettingsStore(new Services.DataLocation(Path.Combine(testRoot, "Data")))); window = testWindow; window.Activate();
                _ = testWindow.RunDataManagementRegressionAsync(restore); return;
            }
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-export" or "--verify-export-restore"))
            {
                var restore = Environment.GetCommandLineArgs().Contains("--verify-export-restore");
                var settings = Path.Combine(AppContext.BaseDirectory, "ui-export-settings.json");
                if (!restore && File.Exists(settings)) File.Delete(settings);
                var testWindow = new MainWindow(new Services.SettingsStore(settings)); window = testWindow; window.Activate();
                _ = testWindow.RunExportRegressionAsync(restore); return;
            }
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-raw-information" or "--verify-raw-information-restore"))
            {
                var restore = Environment.GetCommandLineArgs().Contains("--verify-raw-information-restore");
                var testSettings = Path.Combine(AppContext.BaseDirectory, restore ? "ui-raw-restore.json" : "ui-raw-information.json");
                if (!restore && File.Exists(testSettings)) File.Delete(testSettings);
                if (restore && !File.Exists(testSettings)) throw new InvalidOperationException("請先執行 --verify-raw-information。");
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings)); window = testWindow; window.Activate();
                _ = testWindow.RunRawInformationRegressionAsync(restore); return;
            }
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-builtin-update" or "--verify-builtin-update-restore"))
            {
                var restore = Environment.GetCommandLineArgs().Contains("--verify-builtin-update-restore");
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-built-in-update-settings.json");
                var store = new Services.SettingsStore(testSettings);
                if (!restore)
                {
                    var old = new Services.MarginRule { Top = 100, Gap = 50, Bottom = 50, BuiltInRevision = 1 }.Validate();
                    Task.Run(() => store.SaveAsync(new Services.PersistedSettings { MarginRules = [old],
                        Layout = old.ForAspect(Core.SourceAspect.FourByThree).ApplyTo(new() { LensFontSize = 18,
                            Arrangement = Core.Models.InformationArrangement.TwoColumns }) })).GetAwaiter().GetResult();
                }
                if (restore && !File.Exists(testSettings)) throw new InvalidOperationException("請先執行 --verify-builtin-update。");
                var testWindow = new MainWindow(store); window = testWindow; window.Activate();
                _ = testWindow.RunBuiltInCatalogUpdateRegressionAsync(restore); return;
            }
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-builtin-lens" or "--verify-builtin-lens-restore"))
            {
                var restore = Environment.GetCommandLineArgs().Contains("--verify-builtin-lens-restore");
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-built-in-lens-settings.json");
                if (!restore && File.Exists(testSettings)) File.Delete(testSettings);
                if (restore && !File.Exists(testSettings)) throw new InvalidOperationException("請先執行 --verify-builtin-lens。");
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings)); window = testWindow; window.Activate();
                _ = testWindow.RunBuiltInLensRegressionAsync(restore); return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-bottom-margins"))
            {
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-bottom-margin-settings.json");
                if (File.Exists(testSettings)) File.Delete(testSettings);
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow; window.Activate(); _ = testWindow.RunBottomMarginRegressionAsync(); return;
            }
            if (Environment.GetCommandLineArgs().Any(arg => arg is "--verify-settings-recovery" or "--verify-settings-readonly"))
            {
                var skipRetry = Environment.GetCommandLineArgs().Contains("--verify-settings-readonly");
                var testSettings = Path.Combine(AppContext.BaseDirectory, skipRetry ? "ui-settings-readonly.json" : "ui-settings-recovery.json");
                var store = new Services.SettingsStore(testSettings);
                Task.Run(() => store.SaveAsync(new Services.PersistedSettings
                {
                    MarginRules = [new() { Id = "recovery-a", Name = "Recovery A", Side = 61 }, new() { Id = "recovery-b", Name = "Recovery B", Side = 75 }],
                    SelectedMarginRuleId = "recovery-b", Layout = new() { PhotoSideMargin = 91 }
                })).GetAwaiter().GetResult();
                var original = File.ReadAllBytes(testSettings);
                var heldLock = new FileStream(testSettings, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var testWindow = new MainWindow(store); window = testWindow; window.Activate();
                _ = testWindow.RunSettingsRecoveryAsync(heldLock, testSettings, original, skipRetry); return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-column-fallback"))
            {
                var testSettings = Path.Combine(AppContext.BaseDirectory, "ui-column-fallback-settings.json");
                if (File.Exists(testSettings)) File.Delete(testSettings);
                var testWindow = new MainWindow(new Services.SettingsStore(testSettings));
                window = testWindow; window.Activate(); _ = testWindow.RunColumnFallbackAsync(); return;
            }
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
