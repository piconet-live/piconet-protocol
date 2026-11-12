using System.Diagnostics.CodeAnalysis;

namespace Online.Protocol;

/// <summary>
/// A shuffle bag: its items come out in a drawn order, every one of them before any comes again. An item's weight is
/// how often the list given holds it: that many times in each pass. The list may change between takes (a lobby's pool,
/// a rotation put again): an item it no longer holds leaves the bag, and a new one joins the pass under way at a drawn
/// place.
/// </summary>
public sealed class ShuffleBag<T>
    where T : notnull
{
    private readonly List<T> _left = [];
    private HashSet<T> _known = [];

    /// <summary>The next item of the bag that <paramref name="allowed"/> lets through (null: any). One it passes over
    /// keeps its place, and comes as soon as it is allowed. With none left that is allowed, the next pass joins the
    /// bag behind what is left of this one. False: the list holds nothing allowed.
    /// <paramref name="draw"/>(n) draws a whole number from 0 to n - 1.</summary>
    public bool TryTake(IReadOnlyCollection<T> items, Func<int, int> draw, Func<T, bool>? allowed, [MaybeNullWhen(false)] out T item)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(draw);
        var order = items.Distinct().ToList();
        var weight = order.ToDictionary(x => x, x => items.Count(y => y.Equals(x)));
        _left.RemoveAll(x => !weight.ContainsKey(x));
        foreach (var joined in order.Where(x => !_known.Contains(x)))
        {
            for (var n = 0; n < weight[joined]; n++)
            {
                _left.Insert(draw(_left.Count + 1), joined);
            }
        }
        _known = [.. order];
        var at = _left.FindIndex(x => allowed?.Invoke(x) ?? true);
        if (at < 0)
        {
            var pass = order.SelectMany(x => Enumerable.Repeat(x, Math.Max(0, weight[x] - _left.Count(y => y.Equals(x))))).ToList();
            for (var i = pass.Count - 1; i > 0; i--)
            {
                var j = draw(i + 1);
                (pass[i], pass[j]) = (pass[j], pass[i]);
            }
            var from = _left.Count;
            _left.AddRange(pass);
            at = _left.FindIndex(from, x => allowed?.Invoke(x) ?? true);
        }
        if (at < 0)
        {
            item = default;
            return false;
        }
        item = _left[at];
        _left.RemoveAt(at);
        return true;
    }
}

/// <summary>A lobby's shuffle bags, kept from one vote to its next: one of the rotation's spicy modes (by file, each at
/// its weight) and one of courses for every mode (by the key its holder gives it: <see cref="RotationMode.File"/> for
/// the rotation's). What a vote offers on a card, or a draw lands on, is out of its bag until the rest have had their
/// turn.</summary>
public sealed class RotationBags
{
    private readonly Dictionary<string, ShuffleBag<int>> _courses = [];

    /// <summary>The bag of the rotation's spicy modes.</summary>
    public ShuffleBag<string> Spicy { get; } = new();

    /// <summary>The bag of a mode's courses.</summary>
    public ShuffleBag<int> Courses(string mode)
    {
        if (!_courses.TryGetValue(mode, out var bag))
        {
            bag = new ShuffleBag<int>();
            _courses[mode] = bag;
        }
        return bag;
    }
}
