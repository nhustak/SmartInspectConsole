using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace SmartInspectConsole.Behaviors;

/// <summary>
/// Attached behavior that enables auto-scrolling to the last item in a ListView.
/// </summary>
public static class AutoScrollBehavior
{
    private static readonly DependencyProperty SubscriptionStateProperty =
        DependencyProperty.RegisterAttached(
            "SubscriptionState",
            typeof(AutoScrollSubscriptionState),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(null));

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

        var state = GetOrCreateState(listView);
        DetachFromCollection(state);
        listView.SourceUpdated -= OnSourceUpdated;
        state.ItemsSourceDescriptor?.RemoveValueChanged(listView, OnItemsSourceChanged);

        if ((bool)e.NewValue)
        {
            listView.SourceUpdated += OnSourceUpdated;
            state.ItemsSourceDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty, typeof(ListView));
            state.ItemsSourceDescriptor?.AddValueChanged(listView, OnItemsSourceChanged);

            AttachToCollection(state, listView.ItemsSource as INotifyCollectionChanged);
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

        var state = GetOrCreateState(listView);
        DetachFromCollection(state);
        AttachToCollection(state, listView.ItemsSource as INotifyCollectionChanged);
    }

    private static void OnCollectionChanged(ListView listView, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && GetAutoScroll(listView))
        {
            var state = GetOrCreateState(listView);
            if (state.ScrollScheduled)
                return;

            state.ScrollScheduled = true;
            listView.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    state.ScrollScheduled = false;
                    ScrollToEnd(listView);
                }));
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

    private static AutoScrollSubscriptionState GetOrCreateState(ListView listView)
    {
        if (listView.GetValue(SubscriptionStateProperty) is AutoScrollSubscriptionState existing)
            return existing;

        var created = new AutoScrollSubscriptionState(listView);
        listView.SetValue(SubscriptionStateProperty, created);
        return created;
    }

    private static void AttachToCollection(AutoScrollSubscriptionState state, INotifyCollectionChanged? collection)
    {
        state.Collection = collection;
        if (collection != null)
        {
            collection.CollectionChanged += state.Handler;
        }
    }

    private static void DetachFromCollection(AutoScrollSubscriptionState state)
    {
        if (state.Collection != null)
        {
            state.Collection.CollectionChanged -= state.Handler;
            state.Collection = null;
        }
    }

    private sealed class AutoScrollSubscriptionState
    {
        public AutoScrollSubscriptionState(ListView listView)
        {
            Handler = (_, args) => OnCollectionChanged(listView, args);
        }

        public INotifyCollectionChanged? Collection { get; set; }
        public NotifyCollectionChangedEventHandler Handler { get; }
        public System.ComponentModel.DependencyPropertyDescriptor? ItemsSourceDescriptor { get; set; }
        public bool ScrollScheduled { get; set; }
    }
}
