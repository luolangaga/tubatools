using System.Collections.ObjectModel;

namespace TubaWinUi3.Services;

// Keeps unchanged row objects (and realized containers) across native TCP snapshots.
internal static class ConnectionListUpdater
{
    internal static void Apply(ObservableCollection<ConnectionEntry> current, List<ConnectionEntry> connections)
    {
        var previous = current.ToDictionary(c => c.Identity);
        var desired = connections.DistinctBy(c => c.Identity)
            .Select(c => previous.TryGetValue(c.Identity, out var old) && old.SameDisplay(c) ? old : c).ToList();
        var identities = desired.Select(c => c.Identity).ToHashSet();
        for (int i = current.Count - 1; i >= 0; i--)
            if (!identities.Contains(current[i].Identity)) current.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < current.Count && current[i].Identity == desired[i].Identity)
            {
                if (!ReferenceEquals(current[i], desired[i])) current[i] = desired[i];
                continue;
            }
            int oldIndex = current.IndexOf(desired[i]);
            if (oldIndex >= 0) current.Move(oldIndex, i);
            else current.Insert(i, desired[i]);
        }
        while (current.Count > desired.Count) current.RemoveAt(current.Count - 1);
    }

}
