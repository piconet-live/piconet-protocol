namespace Online.Protocol;

/// <summary>The courses a server races unless it is told others (its vote's pool), by the game's stage numbers, in
/// that order. Here, with the messages, because the launcher reads the list too (the backdrop behind its pages) and
/// shares nothing else with the server.</summary>
public static class CoursePool
{
    /// <summary>The 26 cup courses, Sonic Oval among them, and four of the story's stages: 39,
    /// 43, 44 and 45 (chapters 3, 7, 8 and 9). The game takes every stage here
    /// (<c>COURSES</c> in piconet-runner's port/core/src/files_match.cpp).
    /// A course joins this list only by this rule (two words of the game's restored .data are each game's own:
    /// the machine its camera follows and its viewport slot): its file has no story objects, no trigger that
    /// becomes a physics object, and no instance that the scene update moves for the followed machine and that has a collider. Courses 37, 38 and 41
    /// fail it: on 41 each game would close the gates for its own racer, and two games would differ.</summary>
    public static IReadOnlyList<int> Default { get; } =
        [1, 3, 5, 7, 8, 9, 10, 11, 13, 14, 15, 16, 17, 21, 24, 25, 26, 27, 28, 29, 31, 32, 33, 34, 35, 36, 39, 43, 44, 45];
}
