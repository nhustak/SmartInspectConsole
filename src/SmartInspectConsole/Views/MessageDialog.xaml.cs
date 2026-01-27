using System.Windows;

namespace SmartInspectConsole.Views;

/// <summary>
/// Custom message dialog that centers over the owner window.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public MessageDialog()
    {
        InitializeComponent();
    }

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        var dialog = new MessageDialog
        {
            Title = caption,
            Owner = owner
        };

        dialog.MessageText.Text = message;
        dialog.SetIcon(icon);
        dialog.SetButtons(buttons);

        dialog.ShowDialog();
        return dialog.Result;
    }

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons)
    {
        return Show(message, caption, buttons, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string message, string caption)
    {
        return Show(message, caption, MessageBoxButton.OK, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string message)
    {
        return Show(message, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);
    }

    private void SetIcon(MessageBoxImage icon)
    {
        IconText.Text = icon switch
        {
            MessageBoxImage.Error => "\u274C",        // Red X
            MessageBoxImage.Warning => "\u26A0",      // Warning triangle
            MessageBoxImage.Information => "\u2139",  // Info circle
            MessageBoxImage.Question => "\u2753",     // Question mark
            _ => string.Empty
        };

        IconText.Foreground = icon switch
        {
            MessageBoxImage.Error => System.Windows.Media.Brushes.Red,
            MessageBoxImage.Warning => System.Windows.Media.Brushes.Orange,
            MessageBoxImage.Information => System.Windows.Media.Brushes.DodgerBlue,
            MessageBoxImage.Question => System.Windows.Media.Brushes.DodgerBlue,
            _ => Foreground
        };

        if (string.IsNullOrEmpty(IconText.Text))
        {
            IconText.Visibility = Visibility.Collapsed;
        }
    }

    private void SetButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                OkButton.Visibility = Visibility.Visible;
                OkButton.IsDefault = true;
                break;

            case MessageBoxButton.OKCancel:
                OkButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                OkButton.IsDefault = true;
                CancelButton.IsCancel = true;
                break;

            case MessageBoxButton.YesNo:
                YesButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Visible;
                YesButton.IsDefault = true;
                break;

            case MessageBoxButton.YesNoCancel:
                YesButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                YesButton.IsDefault = true;
                CancelButton.IsCancel = true;
                break;
        }
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        DialogResult = true;
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
    }
}
