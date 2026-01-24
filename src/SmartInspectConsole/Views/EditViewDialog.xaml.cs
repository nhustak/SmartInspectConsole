using System.Windows;
using SmartInspectConsole.ViewModels;

namespace SmartInspectConsole.Views;

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
}
