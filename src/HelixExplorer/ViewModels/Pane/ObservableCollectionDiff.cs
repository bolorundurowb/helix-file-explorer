using System.Collections.ObjectModel;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Applies an in-place, reference-identity diff from a desired sequence onto an
/// <see cref="ObservableCollection{T}"/>: removes items no longer present, then moves/inserts the rest
/// into their new positions. This preserves the identity of surviving items (and any state bound to
/// them, such as selection) instead of clearing and re-adding the whole collection.
/// </summary>
public static class ObservableCollectionDiff
{
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        // Skip work when already in the desired order by reference (common after a no-op refresh).
        if (target.Count == desired.Count)
        {
            var sameOrder = true;
            for (var i = 0; i < desired.Count; i++)
            {
                if (ReferenceEquals(target[i], desired[i]))
                    continue;

                sameOrder = false;
                break;
            }

            if (sameOrder)
                return;
        }

        if (target.Count == 0 || desired.Count == 0)
        {
            target.Clear();
            foreach (var item in desired)
                target.Add(item);
            return;
        }

        var wanted = new HashSet<T>(desired, ReferenceComparer<T>.Instance);

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];

            if (i < target.Count && ReferenceEquals(target[i], item))
                continue;

            var currentIndex = IndexOfReference(target, item, i + 1);
            if (currentIndex >= 0)
                target.Move(currentIndex, i);
            else
                target.Insert(i, item);
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
    }

    private static int IndexOfReference<T>(IList<T> list, T target, int startIndex) where T : class
    {
        for (var i = startIndex; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target))
                return i;
        }

        return -1;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
