using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SmartInspectConsole.Collections;

/// <summary>
/// ObservableCollection with a bulk front-trim operation that raises a single reset notification.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var addedAny = false;

        CheckReentrancy();

        foreach (var item in items)
        {
            Items.Add(item);
            addedAny = true;
        }

        if (!addedAny)
            return;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void RemoveFirstRange(int count)
    {
        if (count <= 0 || Count == 0)
            return;

        var removeCount = Math.Min(count, Count);

        CheckReentrancy();

        for (var i = 0; i < removeCount; i++)
        {
            Items.RemoveAt(0);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
