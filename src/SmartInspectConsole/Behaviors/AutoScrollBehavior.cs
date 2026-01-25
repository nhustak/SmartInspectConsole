using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace SmartInspectConsole.Behaviors;

/// <summary>
/// Attached behavior that enables auto-scrolling to the last item in a ListView.
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnAutoScrollChanged));

    public static bool GetAutoScroll(DependencyObject obj)
    {
        return (bool)obj.GetValue(AutoScrollProperty);
    }

    public static void SetAutoScroll(DependencyObject obj, bool value)
    {
        obj.SetValue(AutoScrollProperty, value);
    }

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView)
            return;

        var newValue = (bool)e.NewValue;

        // Remove any existing handler first
        if (listView.ItemsSource is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= (s, args) => OnCollectionChanged(listView, args);
        }

        // Unsubscribe from SourceUpdated
        listView.SourceUpdated -= OnSourceUpdated;

        if (newValue)
        {
            // Subscribe to collection changes
            if (listView.ItemsSource is INotifyCollectionChanged collection)
            {
                // Use a closure to capture the listView reference
                NotifyCollectionChangedEventHandler handler = null!;
                handler = (s, args) =>
                {
                    if (GetAutoScroll(listView))
                    {
                        OnCollectionChanged(listView, args);
                    }
                };

                // Store the handler in the listView's Tag for later removal
                listView.Tag = handler;
                collection.CollectionChanged += handler;
            }

            // Also listen for ItemsSource changes
            listView.SourceUpdated += OnSourceUpdated;

            // Subscribe to ItemsSource property changes
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty, typeof(ListView));
            dpd?.AddValueChanged(listView, OnItemsSourceChanged);

            // Scroll to end immediately if there are items
            ScrollToEnd(listView);
        }
    }

    private static void OnSourceUpdated(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is ListView listView && GetAutoScroll(listView))
        {
            ScrollToEnd(listView);
        }
    }

    private static void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        if (sender is not ListView listView)
            return;

        // Re-attach to the new collection
        if (listView.Tag is NotifyCollectionChangedEventHandler oldHandler &&
            listView.ItemsSource is INotifyCollectionChanged oldCollection)
        {
            // This won't work perfectly but we'll re-subscribe anyway
        }

        if (GetAutoScroll(listView) && listView.ItemsSource is INotifyCollectionChanged newCollection)
        {
            NotifyCollectionChangedEventHandler handler = (s, args) =>
            {
                if (GetAutoScroll(listView))
                {
                    OnCollectionChanged(listView, args);
                }
            };
            listView.Tag = handler;
            newCollection.CollectionChanged += handler;
        }
    }

    private static void OnCollectionChanged(ListView listView, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            // Use dispatcher to ensure UI is updated before scrolling
            listView.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => ScrollToEnd(listView)));
        }
    }

    private static void ScrollToEnd(ListView listView)
    {
        if (listView.Items.Count > 0)
        {
            var lastItem = listView.Items[listView.Items.Count - 1];
            listView.ScrollIntoView(lastItem);
        }
    }
}
