namespace Online.Protocol;

/// <summary>Course names by the game's course number, as players know them; a
/// stage of the story by its chapter, as the game numbers them: stages 37 to 45 are chapters 1 to 9, and "Story" stands
/// where a cup course has its venue, so a story stage is told from a cup course by its name alone; "Course N" for
/// the others. The four story stages in the vote's pool have names of their own, still
/// with "Story" in front. Shared by the launcher and the server's status page.</summary>
public static class CourseNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "Mute City: Twist Road",
        [3] = "Mute City: Serial Gaps",
        [5] = "Aeropolis: Multiplex",
        [7] = "Port Town: Aero Dive",
        [8] = "Lightning: Loop Cross",
        [9] = "Lightning: Half Pipe",
        [10] = "Green Plant: Intersection",
        [11] = "Green Plant: Mobius Ring",
        [13] = "Port Town: Long Pipe",
        [14] = "Big Blue: Drift Highway",
        [15] = "Fire Field: Cylinder Knot",
        [16] = "Casino Palace: Split Oval",
        [17] = "Fire Field: Undulation",
        [21] = "Aeropolis: Dragon Slope",
        [24] = "Cosmo Terminal: Trident",
        [25] = "Sand Ocean: Lateral Shift",
        [26] = "Sand Ocean: Surface Slide",
        [27] = "Big Blue: Ordeal",
        [28] = "Phantom Road: Slim-Line Slits",
        [29] = "Casino Palace: Double Branches",
        [31] = "Aeropolis: Screw Drive",
        [32] = "Outer Space: Meteor Stream",
        [33] = "Port Town: Cylinder Wave",
        [34] = "Lightning: Thunder Road",
        [35] = "Green Plant: Spiral",
        [36] = "Mute City: Sonic Oval",
        [39] = "Story: High-Stakes Jumps",      // chapter 3
        [43] = "Story: Championship Circuit",   // chapter 7
        [44] = "Story: Emperor's Trial",        // chapter 8
        [45] = "Story: Dimension's Edge",       // chapter 9
    };

    /// <summary>The story's first stage; its nine stages are numbered on from it.</summary>
    public const int FirstStoryStage = 37;

    public const int StoryChapters = 9;

    /// <summary>The story's chapter a course is the stage of, 1 to 9; null for a course that is none.</summary>
    public static int? StoryChapter(int course) =>
        course >= FirstStoryStage && course < FirstStoryStage + StoryChapters ? course - FirstStoryStage + 1 : null;

    public static string Name(int course) =>
        Names.TryGetValue(course, out var name) ? name
        : StoryChapter(course) is { } chapter ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Story: Chapter {chapter}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Course {course}");
}
