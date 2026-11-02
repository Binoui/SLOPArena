using SlopArena.Shared.Replays;
using Xunit;
namespace SlopArena.Shared.Tests.Replays;

public class ReplaySummariesTests
{
    static ReplaySummaryResult Read(byte[] bytes) => ReplaySummaries.Read(new MemoryStream(bytes));

    [Fact]
    public void CompleteFileSummarisesRangeAndConclusion()
    {
        var recording = ReplayWorldTests.Duel.Value;
        var summary = Read(recording.Bytes).Summary!;
        Assert.Equal(recording.Header.MatchId, summary.Header.MatchId);
        Assert.Equal(new ReplayRange(1, (uint)recording.Views.Count), summary.Range);
        Assert.Equal(ReplayEndReason.Cancelled, summary.Conclusion!.Reason);
        Assert.Equal(ReplayPrefixReason.None, summary.PrefixReason);
    }

    [Fact]
    public void PartialFileCutMidChunkKeepsItsCommittedPrefix()
    {
        var bytes = ReplayWorldTests.Duel.Value.Bytes;
        using var full = ReplayReader.Open(new MemoryStream(bytes)).Reader!;
        for (int cut = bytes.Length / 2; cut < bytes.Length / 2 + 4000; cut += 397)
        {
            var summary = Read(bytes[..cut]).Summary!;
            using var reader = ReplayReader.Open(new MemoryStream(bytes[..cut])).Reader!;
            Assert.Equal(reader.Range, summary.Range);
            Assert.Null(summary.Conclusion);
            Assert.Equal(reader.PrefixReason, summary.PrefixReason);
            Assert.NotEqual(ReplayPrefixReason.None, summary.PrefixReason);
            Assert.True(summary.Range!.Value.Last < full.Range!.Value.Last);
        }
    }

    [Fact]
    public void HeaderOnlyFileIsEmptyNotAFailure()
    {
        var bytes = ReplayWorldTests.Duel.Value.Bytes;
        int headerEnd = 24 + BitConverter.ToInt32(bytes, 12);
        var summary = Read(bytes[..headerEnd]).Summary!;
        Assert.Null(summary.Range);
        Assert.Equal(ReplayPrefixReason.CleanEofWithoutTrailer, summary.PrefixReason);
    }

    [Fact]
    public void NewerBuildAndGarbageAreTypedFailures()
    {
        var newer = ReplayWorldTests.Duel.Value.Bytes.ToArray();
        newer[8] = 2;
        Assert.Equal(ReplayFailureCode.PleaseUpdate, Read(newer).Failure!.Code);
        var garbage = new byte[4096];
        new Random(3).NextBytes(garbage);
        Assert.Equal(ReplayFailureCode.InvalidHeader, Read(garbage).Failure!.Code);
        Assert.Equal(ReplayFailureCode.InvalidHeader, Read(Array.Empty<byte>()).Failure!.Code);
    }

    [Fact]
    public void ForecastMatchesTheTrackOpenChooses()
    {
        var content = ReplayWorldTests.Content();
        var recording = ReplayWorldTests.Duel.Value;
        var same = ReplaySummaries.Forecast(Read(recording.Bytes).Summary!.Header, content);
        Assert.Equal(new ReplayTrackForecast(ReplayTrack.Exact, null), same);

        var golden = File.ReadAllBytes(Path.Combine(ReplayWorldTests.Root, "tests/Shared.Tests/Golden/Replays/v1-stock-bots.slopreplay"));
        var forecast = ReplaySummaries.Forecast(Read(golden).Summary!.Header, content);
        Assert.Equal(ReplayTrack.View, forecast.Track);
        Assert.Equal(ReplayFailureCode.ProtocolMismatch, forecast.ExactRefusal!.Code);
        using var opened = ReplayWorld.Open(new MemoryStream(golden), content).World!;
        Assert.Equal(forecast.ExactRefusal, opened.ExactRefusal);

        var noArena = ReplaySummaries.Forecast(Read(recording.Bytes).Summary!.Header, content with { LoadArena = _ => null });
        Assert.Equal(ReplayFailureCode.ArenaUnavailable, noArena.ExactRefusal!.Code);
    }
}
