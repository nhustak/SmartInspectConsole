using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.ViewModels;

namespace SmartInspectConsole;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-start listening
        await _viewModel.StartAsync();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        await _viewModel.StopAsync();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "SmartInspect Console\n\nA replacement console for SmartInspectCore logging.\n\nVersion 1.0",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e)
    {
        App.IsDarkTheme = true;
        DarkThemeMenuItem.IsChecked = true;
        LightThemeMenuItem.IsChecked = false;
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e)
    {
        App.IsDarkTheme = false;
        DarkThemeMenuItem.IsChecked = false;
        LightThemeMenuItem.IsChecked = true;
    }

    private void LogEntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is LogEntry logEntry)
        {
            _viewModel.OpenLogEntryDetailCommand.Execute(logEntry);
        }
    }
}
