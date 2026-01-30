using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Helpers;
using SmartInspectConsole.Services;
using SmartInspectConsole.ViewModels;
using SmartInspectConsole.Views;

namespace SmartInspectConsole;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Restore full application state
        var state = AppState.Load();
        state.ApplyWindowSettings(this);
        _viewModel.RestoreStateFrom(state);
        App.IsDarkTheme = state.IsDarkTheme;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Apply dark title bar via DWM
        SourceInitialized += (_, _) => ApplyDarkTitleBar(App.IsDarkTheme);
    }

    private void ApplyDarkTitleBar(bool dark)
    {
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
        {
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwndSource.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Update theme menu checkmarks
        DarkThemeMenuItem.IsChecked = App.IsDarkTheme;
        LightThemeMenuItem.IsChecked = !App.IsDarkTheme;

        // Auto-start listening
        await _viewModel.StartAsync();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Save full application state
        var state = new AppState();
        state.CaptureWindowSettings(this);
        state.IsDarkTheme = App.IsDarkTheme;
        _viewModel.SaveStateTo(state);
        state.Save();

        await _viewModel.StopAsync();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void IconLegend_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IconLegendDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e)
    {
        App.IsDarkTheme = true;
        DarkThemeMenuItem.IsChecked = true;
        LightThemeMenuItem.IsChecked = false;
        ApplyDarkTitleBar(true);
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e)
    {
        App.IsDarkTheme = false;
        DarkThemeMenuItem.IsChecked = false;
        LightThemeMenuItem.IsChecked = true;
        ApplyDarkTitleBar(false);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_viewModel.TcpPort, _viewModel.PipeName, _viewModel.WebSocketPort, _viewModel.DebugMode, _viewModel.MaxLogEntries, _viewModel.ConfirmBeforeClear)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var portChanged = dialog.TcpPort != _viewModel.TcpPort;
            var pipeChanged = dialog.PipeName != _viewModel.PipeName;
            var wsPortChanged = dialog.WebSocketPort != _viewModel.WebSocketPort;

            // Apply settings that don't require restart
            _viewModel.DebugMode = dialog.DebugMode;
            _viewModel.MaxLogEntries = dialog.MaxLogEntries;
            _viewModel.ConfirmBeforeClear = dialog.ConfirmBeforeClear;

            if (portChanged || pipeChanged || wsPortChanged)
            {
                var wasListening = _viewModel.IsListening;

                if (wasListening)
                {
                    await _viewModel.StopAsync();
                }

                _viewModel.TcpPort = dialog.TcpPort;
                _viewModel.PipeName = dialog.PipeName;
                _viewModel.WebSocketPort = dialog.WebSocketPort;

                if (wasListening)
                {
                    await _viewModel.StartAsync();
                }
            }
        }
    }

    private void ExportLayout_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Layout",
            Filter = "Layout Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "SmartInspectLayout.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var state = new AppState();
                state.CaptureWindowSettings(this);
                state.IsDarkTheme = App.IsDarkTheme;
                _viewModel.SaveStateTo(state);
                state.SaveTo(dialog.FileName);
                MessageBoxHelper.Show("Layout exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBoxHelper.Show($"Failed to export layout:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ImportLayout_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Layout",
            Filter = "Layout Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var state = AppState.LoadFrom(dialog.FileName);
                state.ApplyWindowSettings(this);
                _viewModel.RestoreStateFrom(state);
                App.IsDarkTheme = state.IsDarkTheme;
                DarkThemeMenuItem.IsChecked = App.IsDarkTheme;
                LightThemeMenuItem.IsChecked = !App.IsDarkTheme;
                MessageBoxHelper.Show("Layout imported successfully.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBoxHelper.Show($"Failed to import layout:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToolBar toolBar)
        {
            // Hide the overflow button/grip
            if (toolBar.Template.FindName("OverflowGrid", toolBar) is FrameworkElement overflow)
                overflow.Visibility = Visibility.Collapsed;
            if (toolBar.Template.FindName("MainPanelBorder", toolBar) is FrameworkElement mainPanel)
                mainPanel.Margin = new Thickness(0);
        }
    }

    private void LogEntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is LogEntry logEntry)
        {
            _viewModel.OpenLogEntryDetailCommand.Execute(logEntry);
        }
    }

    private void LogEntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView && listView.DataContext is LogViewViewModel viewModel)
        {
            viewModel.SelectedLogEntries = listView.SelectedItems;
        }
    }
}
