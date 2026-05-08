using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace SmartInspectConsole;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static bool _isDarkTheme = true;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public static bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme != value)
            {
                _isDarkTheme = value;
                ApplyTheme();
            }
        }
    }

    public static void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
    }

    private static void ApplyTheme()
    {
        var themeUri = _isDarkTheme
            ? new Uri("Resources/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Resources/LightTheme.xaml", UriKind.Relative);

        var newTheme = new ResourceDictionary { Source = themeUri };

        // Replace theme dictionaries - keep WPF UI dictionaries, replace our custom one
        var merged = Current.Resources.MergedDictionaries;

        // Remove existing custom theme (last dictionary)
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var dict = merged[i];
            if (dict.Source != null && (dict.Source.OriginalString.Contains("DarkTheme") || dict.Source.OriginalString.Contains("LightTheme")))
            {
                merged.RemoveAt(i);
            }
        }

        // Add new custom theme
        merged.Add(newTheme);

        // Switch WPF UI theme
        ApplicationThemeManager.Apply(_isDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteStartupFailureLog(e.Exception);
        MessageBox.Show(
            $"SmartInspect Console hit a startup error and could not continue.\n\nA diagnostic log was written to:\n{logPath}\n\n{e.Exception.Message}",
            "SmartInspect Console Startup Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteStartupFailureLog(exception);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteStartupFailureLog(e.Exception);
        e.SetObserved();
    }

    public static string WriteStartupFailureLog(Exception exception)
    {
        var logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartInspectConsole",
            "Logs");

        Directory.CreateDirectory(logFolder);

        var logPath = Path.Combine(logFolder, $"startup-error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(logPath, exception.ToString());
        return logPath;
    }
}
