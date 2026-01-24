using System.Windows;

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

        // Replace the theme dictionary
        Current.Resources.MergedDictionaries.Clear();
        Current.Resources.MergedDictionaries.Add(newTheme);
    }
}
