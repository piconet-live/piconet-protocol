namespace Online.Protocol;

/// <summary>Course names by the game's course number, as players know them; a
/// stage of the story by its chapter, as the game numbers them: stages 37 to 45 are chapters 1 to 9, and "Story" stands
/// where a cup course has its venue, so a story stage is told from a cup course by its name alone; "Course N" for
/// the others. The story's chapters that have names of their own (the four in the vote's pool) are named so, still
/// with "Story" in front. The names are protocol/courses.toml's (CourseTable), which the game writes into its course
/// intro too. Shared by the launcher and the server's status page.</summary>
public static class CourseNames
{
    private static readonly Dictionary<int, string> Names =
        CourseTable.Courses.Select(c => (c.Course, Name: $"{c.Place}: {c.Track}"))
            .Concat(CourseTable.StoryChapterNames.Select(c => (c.Course, Name: $"Story: {c.Track}")))
            .ToDictionary(c => c.Course, c => c.Name);

    /// <summary>The story's first stage; its nine stages are numbered on from it.</summary>
    public const int FirstStoryStage = CourseTable.FirstStoryStage;

    public const int StoryChapters = CourseTable.StoryChapters;

    /// <summary>The story's chapter a course is the stage of, 1 to 9; null for a course that is none.</summary>
    public static int? StoryChapter(int course) =>
        course >= FirstStoryStage && course < FirstStoryStage + StoryChapters ? course - FirstStoryStage + 1 : null;

    public static string Name(int course) =>
        Names.TryGetValue(course, out var name) ? name
        : StoryChapter(course) is { } chapter ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Story: Chapter {chapter}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Course {course}");
}
