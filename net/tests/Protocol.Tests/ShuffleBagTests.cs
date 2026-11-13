using Online.Protocol;

namespace Protocol.Tests;

/// <summary>The shuffle bag (ShuffleBag): every item once a pass, a weight's worth of times; what a take passes over;
/// the next pass joining; a list that changes between takes.</summary>
public sealed class ShuffleBagTests
{
    private static List<T> Taken<T>(ShuffleBag<T> bag, IReadOnlyCollection<T> items, Random random, int count, Func<T, bool>? allowed = null)
        where T : notnull
    {
        var taken = new List<T>();
        for (var i = 0; i < count; i++)
        {
            Assert.True(bag.TryTake(items, random.Next, allowed, out var item));
            taken.Add(item);
        }
        return taken;
    }

    [Fact]
    public void EveryItemComesOnceBeforeAnyComesAgain()
    {
        var (bag, random) = (new ShuffleBag<int>(), new Random(1));
        int[] items = [.. Enumerable.Range(0, 10)];
        var orders = new HashSet<string>();
        for (var pass = 0; pass < 50; pass++)
        {
            var taken = Taken(bag, items, random, 10);
            Assert.Equal(items, taken.Order());
            orders.Add(string.Join(' ', taken));
        }
        Assert.True(orders.Count > 40, $"{orders.Count} orders in 50 passes");
    }

    [Fact]
    public void AnItemComesItsWeightsWorthOfTimesAPass()
    {
        var (bag, random) = (new ShuffleBag<string>(), new Random(2));
        string[] items = ["a", "b", "b", "c", "c", "c"];
        for (var pass = 0; pass < 50; pass++)
        {
            Assert.Equal(items, Taken(bag, items, random, 6).Order());
        }
    }

    [Fact]
    public void AnItemPassedOverKeepsItsPlaceAndComesNext()
    {
        var (bag, random) = (new ShuffleBag<int>(), new Random(3));
        int[] items = [0, 1, 2, 3, 4];
        Assert.Equal([0, 1, 2, 4], Taken(bag, items, random, 4, x => x != 3).Order());
        Assert.Equal([3], Taken(bag, items, random, 1));
    }

    [Fact]
    public void WithNothingAllowedLeftTheNextPassJoinsBehindThisOne()
    {
        var (bag, random) = (new ShuffleBag<int>(), new Random(4));
        int[] items = [0, 1];
        var first = Taken(bag, items, random, 1)[0];
        Assert.Equal([first], Taken(bag, items, random, 1, x => x == first));   // (the next pass's)
        Assert.Equal([1 - first], Taken(bag, items, random, 1));                // (this pass's last, still ahead)
        Assert.Equal(items, Taken(bag, items, random, 2).Order());
    }

    [Fact]
    public void AnItemTheListDropsLeavesAndANewOneJoinsThePassUnderWay()
    {
        var (bag, random) = (new ShuffleBag<int>(), new Random(5));
        int[] items = [0, 1, 2, 3, 4];
        var taken = Taken(bag, items, random, 2);
        var left = items.Except(taken).ToList();
        int[] changed = [.. taken, left[0], left[1], 9];   // (left[2] is gone, 9 is new)
        Assert.Equal(new[] { left[0], left[1], 9 }.Order(), Taken(bag, changed, random, 3).Order());
        Assert.Equal(changed.Order(), Taken(bag, changed, random, 5).Order());
    }

    [Fact]
    public void NothingAllowedIsNoTake()
    {
        var (bag, random) = (new ShuffleBag<int>(), new Random(6));
        Assert.False(bag.TryTake([], random.Next, null, out _));
        Assert.False(bag.TryTake([1, 2], random.Next, _ => false, out _));
        Assert.Equal([1, 2], Taken(bag, [1, 2], random, 2).Order());   // (and the bag is as it was)
    }
}
