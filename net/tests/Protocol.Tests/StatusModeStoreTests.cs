using System.Text.Json;
using Online.Protocol;

namespace Protocol.Tests;

/// <summary>The mode store in the public status: where the official modes came from and which are raced, written only
/// when the server has a rotation, and a status from before it reads as none.</summary>
public sealed class StatusModeStoreTests
{
    private static ServiceStatus Status(ModeStoreView? store) =>
        new("Online", DateTimeOffset.UnixEpoch, 0, [], [], [], [], ModeStore: store);

    [Fact]
    public void TheStatusCarriesTheModesAndWhereTheyCameFrom()
    {
        var store = new ModeStoreView("local copy", "the bucket cannot be reached", [
            new ModeView("competitive_gx_unleashed.fzmode", "Quick Play with GX Unleashed stats", "ab12", "normal"),
            new ModeView("knockout.fzmode", "Knockout", "cd34", "spicy", 1)]);
        var text = JsonSerializer.Serialize(Status(store), ControlProtocol.Json);
        Assert.Contains("\"modeStore\":{\"source\":\"local copy\",\"fault\"", text, StringComparison.Ordinal);
        Assert.Contains("\"file\":\"knockout.fzmode\",\"name\":\"Knockout\",\"hash\":\"cd34\",\"role\":\"spicy\",\"weight\":1", text, StringComparison.Ordinal);
        var read = JsonSerializer.Deserialize<ServiceStatus>(text, ControlProtocol.Json)!.ModeStore!;
        Assert.Equal(store with { Modes = [] }, read with { Modes = [] });
        Assert.Equal(store.Modes, read.Modes);
    }

    [Fact]
    public void AStatusWithoutOneReadsAsBefore()
    {
        Assert.DoesNotContain("modeStore", JsonSerializer.Serialize(Status(null), ControlProtocol.Json), StringComparison.Ordinal);
        var older = JsonSerializer.Deserialize<ServiceStatus>(
            """{"name":"Online","time":"1970-01-01T00:00:00+00:00","playersOnline":0,"lobbies":[],"starting":[],"races":[],"relays":[]}""",
            ControlProtocol.Json)!;
        Assert.Null(older.ModeStore);
    }
}
