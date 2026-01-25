using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartInspectConsole.Behaviors;

/// <summary>
/// Attached behavior that enables drag-and-drop reordering of tabs in a TabControl.
/// </summary>
public static class TabReorderBehavior
{
    private static TabItem? _draggedTab;
    private static Point _startPoint;
    private static bool _isDragging;

    public static readonly DependencyProperty EnableReorderProperty =
        DependencyProperty.RegisterAttached(
            "EnableReorder",
            typeof(bool),
            typeof(TabReorderBehavior),
            new PropertyMetadata(false, OnEnableReorderChanged));

    public static bool GetEnableReorder(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnableReorderProperty);
    }

    public static void SetEnableReorder(DependencyObject obj, bool value)
    {
        obj.SetValue(EnableReorderProperty, value);
    }

    private static void OnEnableReorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TabControl tabControl)
            return;

        if ((bool)e.NewValue)
        {
            tabControl.PreviewMouseLeftButtonDown += TabControl_PreviewMouseLeftButtonDown;
            tabControl.PreviewMouseMove += TabControl_PreviewMouseMove;
            tabControl.PreviewMouseLeftButtonUp += TabControl_PreviewMouseLeftButtonUp;
            tabControl.DragOver += TabControl_DragOver;
            tabControl.Drop += TabControl_Drop;
            tabControl.AllowDrop = true;
        }
        else
        {
            tabControl.PreviewMouseLeftButtonDown -= TabControl_PreviewMouseLeftButtonDown;
            tabControl.PreviewMouseMove -= TabControl_PreviewMouseMove;
            tabControl.PreviewMouseLeftButtonUp -= TabControl_PreviewMouseLeftButtonUp;
            tabControl.DragOver -= TabControl_DragOver;
            tabControl.Drop -= TabControl_Drop;
            tabControl.AllowDrop = false;
        }
    }

    private static void TabControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
        _draggedTab = FindTabItem(e.OriginalSource as DependencyObject);
        _isDragging = false;
    }

    private static void TabControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPosition = e.GetPosition(null);
        Vector diff = _startPoint - currentPosition;

        // Check if we've moved enough to start dragging
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(_draggedTab, _draggedTab.DataContext, DragDropEffects.Move);
            }
        }
    }

    private static void TabControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedTab = null;
        _isDragging = false;
    }

    private static void TabControl_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not TabControl tabControl)
            return;

        // Find the tab under the cursor
        var targetTab = FindTabItem(e.OriginalSource as DependencyObject);

        if (targetTab != null && _draggedTab != null && targetTab != _draggedTab)
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private static void TabControl_Drop(object sender, DragEventArgs e)
    {
        if (sender is not TabControl tabControl)
            return;

        var targetTab = FindTabItem(e.OriginalSource as DependencyObject);

        if (targetTab == null || _draggedTab == null || targetTab == _draggedTab)
            return;

        var sourceData = _draggedTab.DataContext;
        var targetData = targetTab.DataContext;

        if (tabControl.ItemsSource is IList itemsSource)
        {
            int sourceIndex = itemsSource.IndexOf(sourceData);
            int targetIndex = itemsSource.IndexOf(targetData);

            if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
            {
                // Remove from old position and insert at new position
                itemsSource.RemoveAt(sourceIndex);

                // Adjust target index if source was before target
                if (sourceIndex < targetIndex)
                    targetIndex--;

                itemsSource.Insert(targetIndex, sourceData);

                // Select the moved tab
                tabControl.SelectedItem = sourceData;
            }
        }

        _draggedTab = null;
        _isDragging = false;
        e.Handled = true;
    }

    private static TabItem? FindTabItem(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TabItem tabItem)
                return tabItem;

            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
}
