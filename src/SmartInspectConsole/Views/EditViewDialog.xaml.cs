using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SmartInspectConsole.ViewModels;

namespace SmartInspectConsole.Views;

/// <summary>
/// Converts a count to visibility (visible if count > 0).
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Interaction logic for EditViewDialog.xaml
/// </summary>
public partial class EditViewDialog : Window
{
    public EditViewDialog(EditViewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public EditViewViewModel ViewModel => (EditViewViewModel)DataContext;

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectAllLogTypes();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectNoLogTypes();
    }

    private void AppTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.DataContext is FilterOption option)
        {
            ViewModel.ToggleAppSelection(option);
        }
    }

    private void SessionTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.DataContext is FilterOption option)
        {
            ViewModel.ToggleSessionSelection(option);
        }
    }

    private void HostnameTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.DataContext is FilterOption option)
        {
            ViewModel.ToggleHostnameSelection(option);
        }
    }
}
