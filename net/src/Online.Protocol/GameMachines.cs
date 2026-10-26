namespace Online.Protocol;

/// <summary>
/// The game's machines by id, as every message and the match file name one: 0 to 40. Ids 0 to 29 are the thirty of
/// the Grand Prix, 30 is Dark Schneider and 31 to 40 are the ten that came from the arcade game (F-Zero AX). The
/// game takes all 41 in a race (<c>protocol/formats.md</c>: a seat's <c>machine</c> is 0 to 40) and a race
/// started from a match file does not ask the player's save which ones are unlocked. An id of 41 or more is a custom
/// machine built from parts in the game, which no message can describe.
/// </summary>
public static class GameMachines
{
    /// <summary>How many machines the game has: ids 0 to <c>Count - 1</c>.</summary>
    public const int Count = 41;

    /// <summary>The machines' names by id: the one list of them, for the launcher and the server's status page. They
    /// are the disc's own, in its own order (its car data file lists them and its name plates show them),
    /// kept here so that a list can name a machine without the disc.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Red Gazelle", "White Cat", "Golden Fox", "Iron Tiger", "Fire Stingray", "Wild Goose", "Blue Falcon",
        "Deep Claw", "Great Star", "Little Wyvern", "Mad Wolf", "Super Piranha", "Death Anchor", "Astro Robin",
        "Big Fang", "Sonic Phantom", "Green Panther", "Hyper Speeder", "Space Angler", "King Meteor", "Queen Meteor",
        "Twin Noritta", "Night Thunder", "Wild Boar", "Blood Hawk", "Wonder Wasp", "Mighty Typhoon",
        "Mighty Hurricane", "Crazy Bear", "Black Bull",
        "Dark Schneider", "Fat Shark", "Cosmic Dolphin", "Pink Spider", "Magic Seagull", "Silver Rat", "Spark Moon",
        "Bunny Flash", "Groovy Taxi", "Rolling Turtle", "Rainbow Phoenix",
    ];

    /// <summary>The machines a server gives the computer's racers: ids 0 to <c>CpuCount - 1</c>, which is all of them
    /// (the computer may drive the eleven added machines too, not only the Grand Prix's thirty). The one place that says so: the server's draw and the bots' field read it.</summary>
    public const int CpuCount = Count;

    /// <summary>Whether the game has a machine of this id.</summary>
    public static bool Has(int id) => id is >= 0 and < Count;
}
