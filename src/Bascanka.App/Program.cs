using System.Runtime.InteropServices;
using System.Text;

namespace Bascanka.App;

static class Program
{
    /// <summary>
    /// Gets whether the Plugins menu should be shown.
    /// Enabled by passing <c>--plugins</c> on the command line.
    /// </summary>
    public static bool EnablePlugins { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        // Register the code-pages provider so that Windows-1252, ISO-8859-1, GB18030,
        // and other legacy encodings are available on all platforms.
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Enable high DPI support and visual styles.
        ApplicationConfiguration.Initialize();

        // Enable per-monitor DPI awareness via Win32 API as a fallback.
        SetProcessDpiAwareness();

        // Set unhandled exception handlers.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Initialize localization (must be before any UI string access).
        LocalizationManager.Initialize();

        // Parse command-line arguments: each argument is treated as a file path to open.
        string[] filesToOpen = ParseCommandLineArgs(args);

        // Single-instance: only one Bascanka process may run at a time.
        var singleInstance = new SingleInstanceManager();
        if (!singleInstance.TryAcquire())
        {
            // Another instance is running — forward files (or just activate it) and exit.
            SingleInstanceManager.TrySignalExisting(filesToOpen);
            return;
        }

        // Create and run the main form.
        using var mainForm = new MainForm(filesToOpen, singleInstance);
        Application.Run(mainForm);
    }

    /// <summary>
    /// Parses command-line arguments, extracting file paths to open.
    /// Skips arguments that start with '-' (treated as flags).
    /// </summary>
    private static string[] ParseCommandLineArgs(string[] args)
    {
        var files = new List<string>();
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--plugins", StringComparison.OrdinalIgnoreCase))
            {
                EnablePlugins = true;
                continue;
            }

            if (string.Equals(arg, "-r", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--reset", StringComparison.OrdinalIgnoreCase))
            {
                SettingsManager.ResetToDefaults();
                RecoveryManager.ClearRecoveryData();
                // Delete recent files list.
                string recentPath = Path.Combine(
                    SettingsManager.AppDataFolder, "recent.json");
                try { if (File.Exists(recentPath)) File.Delete(recentPath); } catch { }
                continue;
            }

            if (arg.StartsWith('-'))
                continue;

            // Resolve to absolute path if relative.
            string fullPath = Path.GetFullPath(arg);
            files.Add(fullPath);
        }
        return files.ToArray();
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        string message = string.Format(
            Strings.ErrorUnhandledException,
            e.Exception.Message,
            e.Exception.StackTrace);

        MessageBox.Show(
            message,
            Strings.AppTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            string message = string.Format(
                Strings.ErrorUnhandledException,
                ex.Message,
                ex.StackTrace);

            MessageBox.Show(
                message,
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Sets process DPI awareness to Per-Monitor V2 on Windows 10+.
    /// </summary>
    private static void SetProcessDpiAwareness()
    {
        try
        {
            // Windows 10 1703+ (Creators Update): Per-Monitor V2
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // Fallback: the app.manifest handles this for older scenarios.
        }
    }

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
