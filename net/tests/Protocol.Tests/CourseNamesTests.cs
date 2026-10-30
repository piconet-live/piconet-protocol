using Online.Protocol;

namespace Protocol.Tests;

/// <summary>The courses' names from protocol/courses.toml (CourseTable): the same names CourseNames gave when it held
/// them itself, and the story's named chapters at their stages.</summary>
public sealed class CourseNamesTests
{
    private static readonly int[] CupCourses = [1, 3, 5, 7, 8, 9, 10, 11, 13, 14, 15, 16, 17, 21, 24, 25, 26, 27, 28, 29, 31, 32, 33, 34, 35, 36];

    [Fact]
    public void EveryCourseIsNamedAsBefore()
    {
        Assert.Equal(
            ["Mute City: Twist Road", "Mute City: Serial Gaps", "Aeropolis: Multiplex", "Port Town: Aero Dive",
             "Lightning: Loop Cross", "Lightning: Half Pipe", "Green Plant: Intersection", "Green Plant: Mobius Ring",
             "Port Town: Long Pipe", "Big Blue: Drift Highway", "Fire Field: Cylinder Knot", "Casino Palace: Split Oval",
             "Fire Field: Undulation", "Aeropolis: Dragon Slope", "Cosmo Terminal: Trident", "Sand Ocean: Lateral Shift",
             "Sand Ocean: Surface Slide", "Big Blue: Ordeal", "Phantom Road: Slim-Line Slits",
             "Casino Palace: Double Branches", "Aeropolis: Screw Drive", "Outer Space: Meteor Stream",
             "Port Town: Cylinder Wave", "Lightning: Thunder Road", "Green Plant: Spiral", "Mute City: Sonic Oval"],
            CupCourses.Select(CourseNames.Name));
        Assert.Equal(
            ["Story: Chapter 1", "Story: Chapter 2", "Story: High-Stakes Jumps", "Story: Chapter 4", "Story: Chapter 5",
             "Story: Chapter 6", "Story: Championship Circuit", "Story: Emperor's Trial", "Story: Dimension's Edge"],
            Enumerable.Range(37, 9).Select(CourseNames.Name));
        Assert.Equal(("Course 2", "Course 46"), (CourseNames.Name(2), CourseNames.Name(46)));
        Assert.Equal((37, 9), (CourseNames.FirstStoryStage, CourseNames.StoryChapters));
        Assert.All(CourseTable.StoryChapterNames, c => Assert.Equal(c.Chapter, CourseNames.StoryChapter(c.Course)));
    }
}
