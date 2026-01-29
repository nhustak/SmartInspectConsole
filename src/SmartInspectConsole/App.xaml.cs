using System.Windows;
using Wpf.Ui.Appearance;

namespace SmartInspectConsole;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static bool _isDarkTheme = true;

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
}
