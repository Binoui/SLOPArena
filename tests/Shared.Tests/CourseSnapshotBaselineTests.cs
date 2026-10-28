using SlopArena.Shared;
using Xunit;
namespace SlopArena.Shared.Tests;
public class CourseSnapshotBaselineTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RaceAndCoopKeepPreScrambleSnapshotBytes(bool cooperative)
    {
        var course = new OnlineCourseSimulation(CharacterCourseTests.Arena().Arcade!.Course!, cooperative ? CourseFormat.Coop : CourseFormat.Race);
        course.Register(1); course.Register(2); course.Begin(); course.BeginTick(); course.Hit(2, 0);
        course.FinishTick(1, new Dictionary<ulong, ArcadePoint> { [1] = default, [2] = default },
            new Dictionary<ulong, CharacterState> { [1] = TestHelpers.PlayerState(), [2] = TestHelpers.PlayerState() }, Array.Empty<ulong>());
        var writer = new SnapshotWriter(); course.WriteSnapshot(writer);
        Assert.Equal(cooperative ? "AQABAAAAAAAAAAAAAAAAAAAAAAACAAAAAQAAAAAAAAAAAAIBAAAAAAAAAP////8AAAIAAAAAAQIAAAAAAAAAAAACAQAAAAAAAAD/////AAACAAAAAAE=" : "AQABAAAAAAAAAAAAAAAAAAAAAAACAAAAAQAAAAAAAAAAAAIBAAAAAAAAAP////8AAAIAAAABAQIAAAAAAAAAAAACAQAAAAAAAAD/////AAACAAAAAAE=", Convert.ToBase64String(writer.ToArray()));
    }
}
