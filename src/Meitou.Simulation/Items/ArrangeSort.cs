namespace Meitou.Simulation.Items;

/// <summary>
/// The order an inventory section's ARRANGE puts its items in (the original's <c>std::_Sort</c>, <c>0x14075bb40</c>, docs/game/economy.md "Window copy"; <b>Verified</b> against the
/// order the original exe's sort gives a 40-item input, TradeTests): an introsort
/// that is not stable, so items the comparison ties keep a particular order rather than their input order.
/// <list type="bullet">
/// <item>While more than 32 items are left and the depth budget (starting at the item count: the arrange's call at <c>0x14074a02b</c> passes end - begin, <b>Verified</b>) is above 0: partition (<see cref="Partition"/>), the budget becomes half of itself
/// plus half of that half (two integer divisions), then sort the smaller side in a nested call and keep going on the larger.</item>
/// <item>A run still above 32 when the budget is spent is heap sorted (<see cref="HeapSort"/>); a shorter one is insertion sorted (<see cref="Insertion"/>).</item>
/// </list>
/// <paramref name="less"/> is the comparison (true: the first goes before the second).
/// </summary>
internal static class ArrangeSort
{
    const int InsertionMax = 32, NinePointAbove = 40;

    public static void Sort<T>(T[] items, Func<T, T, bool> less) => Sort(items, 0, items.Length, items.Length, less);

    static void Sort<T>(T[] a, int lo, int hi, int budget, Func<T, T, bool> less)
    {
        int count;
        while ((count = hi - lo) > InsertionMax && budget > 0)
        {
            var (equalLo, equalHi) = Partition(a, lo, hi, less);
            budget /= 2;
            budget += budget / 2;
            if (equalLo - lo < hi - equalHi)
            {
                Sort(a, lo, equalLo, budget, less);
                lo = equalHi;
            }
            else
            {
                Sort(a, equalHi, hi, budget, less);
                hi = equalLo;
            }
        }
        if (count > InsertionMax) HeapSort(a, lo, hi, less);
        else if (count > 1) Insertion(a, lo, hi, less);
    }

    /// <summary>Orders three items in place by three compare-and-swaps: first and middle, middle and last, first and middle again.</summary>
    static void Three<T>(T[] a, int first, int middle, int last, Func<T, T, bool> less)
    {
        if (less(a[middle], a[first])) Swap(a, middle, first);
        if (less(a[last], a[middle])) Swap(a, last, middle);
        if (less(a[middle], a[first])) Swap(a, middle, first);
    }

    /// <summary>
    /// Brings the pivot guess to <paramref name="middle"/> (<paramref name="last"/> is the last item, not one past it): more than 40 apart, the median of the medians of three spaced
    /// triples (step (last - first + 1) / 8, around the first, the middle and the last item), else the median of the three.
    /// </summary>
    static void Pivot<T>(T[] a, int first, int middle, int last, Func<T, T, bool> less)
    {
        if (last - first > NinePointAbove)
        {
            int step = (last - first + 1) / 8;
            Three(a, first, first + step, first + 2 * step, less);
            Three(a, middle - step, middle, middle + step, less);
            Three(a, last - 2 * step, last - step, last, less);
            Three(a, first + step, middle, last - step, less);
        }
        else Three(a, first, middle, last, less);
    }

    /// <summary>
    /// A three-way partition of [lo, hi) around the item at the middle ((hi - lo) / 2 in), after <see cref="Pivot"/>: the run of items equal to it around the middle is widened both
    /// ways first, then scans go outward from it, the upper one moving items equal to the pivot onto the run's upper end and stopping at one that goes before it, the lower one
    /// likewise downward; a pair of stopped scans swap, and a scan that ran out moves the run toward the other side by a rotation of its end items. Returns the run of equal items.
    /// </summary>
    static (int Lo, int Hi) Partition<T>(T[] a, int lo, int hi, Func<T, T, bool> less)
    {
        int middle = lo + (hi - lo) / 2;
        Pivot(a, lo, middle, hi - 1, less);
        int equalLo = middle, equalHi = middle + 1;
        while (lo < equalLo && !less(a[equalLo - 1], a[equalLo]) && !less(a[equalLo], a[equalLo - 1])) equalLo--;
        while (equalHi < hi && !less(a[equalHi], a[equalLo]) && !less(a[equalLo], a[equalHi])) equalHi++;
        int up = equalHi, down = equalLo;
        while (true)
        {
            for (; up < hi; up++)
            {
                if (less(a[equalLo], a[up])) continue;
                if (less(a[up], a[equalLo])) break;
                Swap(a, equalHi++, up);
            }
            for (; lo < down; down--)
            {
                if (less(a[down - 1], a[equalLo])) continue;
                if (less(a[equalLo], a[down - 1])) break;
                Swap(a, --equalLo, down - 1);
            }
            if (down == lo && up == hi) return (equalLo, equalHi);
            if (down == lo)
            {
                if (equalHi != up) Swap(a, equalLo, equalHi);
                equalHi++;
                Swap(a, equalLo++, up++);
            }
            else if (up == hi)
            {
                if (--down != --equalLo) Swap(a, down, equalLo);
                Swap(a, equalLo, --equalHi);
            }
            else Swap(a, up++, --down);
        }
    }

    /// <summary>
    /// Insertion: each next item that goes before the first shifts the whole run up and becomes the first; any other moves down past the items it goes before (a strict comparison,
    /// so it stops at an equal one).
    /// </summary>
    static void Insertion<T>(T[] a, int lo, int hi, Func<T, T, bool> less)
    {
        for (int next = lo + 1; next < hi; next++)
        {
            var value = a[next];
            if (less(value, a[lo]))
            {
                Array.Copy(a, lo, a, lo + 1, next - lo);
                a[lo] = value;
                continue;
            }
            int hole = next;
            for (; less(value, a[hole - 1]); hole--) a[hole] = a[hole - 1];
            a[hole] = value;
        }
    }

    /// <summary>
    /// A heap sort: the heap is built from the last parent ((count / 2) - 1) down to the root, each sifted down (<see cref="Sift"/>); then the root is swapped out to the end of a
    /// shrinking heap, the old last item sifted in from the root.
    /// </summary>
    static void HeapSort<T>(T[] a, int lo, int hi, Func<T, T, bool> less)
    {
        int count = hi - lo;
        for (int hole = count / 2; hole > 0;)
        {
            hole--;
            Sift(a, lo, hole, count, a[lo + hole], less);
        }
        for (; hi - lo > 1; hi--)
        {
            var value = a[hi - 1];
            a[hi - 1] = a[lo];
            Sift(a, lo, 0, hi - 1 - lo, value, less);
        }
    }

    /// <summary>
    /// Puts <paramref name="value"/> into the heap of <paramref name="bottom"/> items from <paramref name="hole"/>: the hole first walks down to a leaf, each step to the right child
    /// (2 x hole + 2) unless it goes before the left one, then to a lone left child at the bottom; then the value climbs back up past parents that go before it, no higher than where
    /// it started.
    /// </summary>
    static void Sift<T>(T[] a, int lo, int hole, int bottom, T value, Func<T, T, bool> less)
    {
        int top = hole, child = 2 * hole + 2;
        for (; child < bottom; child = 2 * child + 2)
        {
            if (less(a[lo + child], a[lo + child - 1])) child--;
            a[lo + hole] = a[lo + child];
            hole = child;
        }
        if (child == bottom)
        {
            a[lo + hole] = a[lo + bottom - 1];
            hole = bottom - 1;
        }
        for (int parent = (hole - 1) / 2; top < hole && less(a[lo + parent], value); parent = (hole - 1) / 2)
        {
            a[lo + hole] = a[lo + parent];
            hole = parent;
        }
        a[lo + hole] = value;
    }

    static void Swap<T>(T[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);
}
