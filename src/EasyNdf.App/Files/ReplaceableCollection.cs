using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EasyNdf.App.Files;

/// <summary>
/// A list the screen follows, whose items can all be replaced at once with a single notice. Cleared and filled again
/// item by item, it sent the list on screen a notice for every item: thousands for a long file list, which made every
/// change to a mod (even folding a rule group) wait half a second.
/// </summary>
public sealed class ReplaceableCollection<T> : ObservableCollection<T>
{
    /// <summary>Puts these items in place of all the list's items, telling the screen once.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
