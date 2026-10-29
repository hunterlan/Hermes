using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>History entries that can drop many at once while telling listeners about it only once.</summary>
public sealed class HistoryCollection(IEnumerable<HistoryEntry> entries) : ObservableCollection<HistoryEntry>(entries)
{
    /// <summary>Removes every entry matching <paramref name="isExpired"/> and raises a single reset. Returns how many were removed.</summary>
    public int RemoveWhere(Func<HistoryEntry, bool> isExpired)
    {
        CheckReentrancy();
        var removed = 0;
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (!isExpired(Items[index]))
                continue;
            Items.RemoveAt(index);
            removed++;
        }
        if (removed > 0)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        return removed;
    }
}
