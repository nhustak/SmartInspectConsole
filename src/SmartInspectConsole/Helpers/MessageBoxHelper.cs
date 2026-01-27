using System.Windows;
using SmartInspectConsole.Views;

namespace SmartInspectConsole.Helpers;

/// <summary>
/// Helper for showing message dialogs centered on the main application window.
/// </summary>
public static class MessageBoxHelper
{
    public static MessageBoxResult Show(string messageBoxText)
    {
        return MessageDialog.Show(messageBoxText);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption)
    {
        return MessageDialog.Show(messageBoxText, caption);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
    {
        return MessageDialog.Show(messageBoxText, caption, button);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return MessageDialog.Show(messageBoxText, caption, button, icon);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        return MessageDialog.Show(messageBoxText, caption, button, icon);
    }
}
