using System.Windows;
using System.Windows.Controls;

namespace SmartInspectConsole.Behaviors;

/// <summary>
/// Attached behavior that makes a specific GridViewColumn fill remaining space.
/// Attach to a ListView with a GridView. Set FillColumnIndex to the column that should stretch.
/// </summary>
public static class GridViewFillColumnBehavior
{
    public static readonly DependencyProperty FillColumnIndexProperty =
        DependencyProperty.RegisterAttached(
            "FillColumnIndex",
            typeof(int),
            typeof(GridViewFillColumnBehavior),
            new PropertyMetadata(-1, OnFillColumnIndexChanged));

    public static int GetFillColumnIndex(DependencyObject obj) =>
        (int)obj.GetValue(FillColumnIndexProperty);

    public static void SetFillColumnIndex(DependencyObject obj, int value) =>
        obj.SetValue(FillColumnIndexProperty, value);

    /// <summary>
    /// Minimum width for the fill column.
    /// </summary>
    public static readonly DependencyProperty MinFillWidthProperty =
        DependencyProperty.RegisterAttached(
            "MinFillWidth",
            typeof(double),
            typeof(GridViewFillColumnBehavior),
            new PropertyMetadata(100.0));

    public static double GetMinFillWidth(DependencyObject obj) =>
        (double)obj.GetValue(MinFillWidthProperty);

    public static void SetMinFillWidth(DependencyObject obj, double value) =>
        obj.SetValue(MinFillWidthProperty, value);

    private static void OnFillColumnIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView) return;

        if ((int)e.NewValue >= 0)
        {
            listView.SizeChanged += OnListViewSizeChanged;
            listView.Loaded += OnListViewLoaded;
        }
        else
        {
            listView.SizeChanged -= OnListViewSizeChanged;
            listView.Loaded -= OnListViewLoaded;
        }
    }

    private static void OnListViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListView listView)
            AdjustFillColumn(listView);
    }

    private static void OnListViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ListView listView)
            AdjustFillColumn(listView);
    }

    private static void AdjustFillColumn(ListView listView)
    {
        if (listView.View is not GridView gridView) return;

        var fillIndex = GetFillColumnIndex(listView);
        if (fillIndex < 0 || fillIndex >= gridView.Columns.Count) return;

        var minWidth = GetMinFillWidth(listView);

        // Calculate total width of all other columns
        double otherColumnsWidth = 0;
        for (var i = 0; i < gridView.Columns.Count; i++)
        {
            if (i != fillIndex)
                otherColumnsWidth += gridView.Columns[i].ActualWidth;
        }

        // Account for scrollbar (~20px) and some padding
        var availableWidth = listView.ActualWidth - otherColumnsWidth - 30;
        var newWidth = Math.Max(minWidth, availableWidth);

        gridView.Columns[fillIndex].Width = newWidth;
    }
}
