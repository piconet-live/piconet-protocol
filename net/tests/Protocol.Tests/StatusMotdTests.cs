using System.Text.Json;
using Online.Protocol;

namespace Protocol.Tests;

/// <summary>The message of the day in the public status: carried as the welcome's notice while it stands, written
/// only when set, and a status from before it reads as none.</summary>
public sealed class StatusMotdTests
{
    private static ServiceStatus Status(RecentNotice? motd) =>
        new("Online", DateTimeOffset.UnixEpoch, 0, [], [], [], [], Motd: motd);

    [Fact]
    public void TheStatusCarriesTheMessageOfTheDay()
    {
        var motd = new RecentNotice("ab12-3", Notices.Notice, "Cup night Friday", DateTimeOffset.UnixEpoch);
        var text = JsonSerializer.Serialize(Status(motd), ControlProtocol.Json);
        Assert.Contains("\"motd\":{\"id\":\"ab12-3\",\"level\":\"notice\",\"text\":\"Cup night Friday\"", text, StringComparison.Ordinal);
        Assert.Equal(motd, JsonSerializer.Deserialize<ServiceStatus>(text, ControlProtocol.Json)!.Motd);
    }

    [Fact]
    public void AStatusWithoutOneReadsAsBefore()
    {
        Assert.DoesNotContain("motd", JsonSerializer.Serialize(Status(null), ControlProtocol.Json), StringComparison.Ordinal);
        var older = JsonSerializer.Deserialize<ServiceStatus>(
            """{"name":"Online","time":"1970-01-01T00:00:00+00:00","playersOnline":0,"lobbies":[],"starting":[],"races":[],"relays":[]}""",
            ControlProtocol.Json)!;
        Assert.Null(older.Motd);
    }
}
