using System.Collections.Concurrent;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

/// <summary>Input redundancy, arrival feedback and match-loop containment on the Steam path.</summary>
public sealed class NetplayInputTimingTests
{
    private const string ContentHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static MatchContentCatalog LoadCatalog()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(catalog.IsValid, string.Join("; ", catalog.Diagnostics));
        return catalog.Catalog!;
    }

    private sealed class Capture
    {
        /// <summary>The input the GameHost relayed for entity 1, by frame tick.</summary>
        public readonly ConcurrentDictionary<uint, RelayedInput> SelfStates = new();
        public readonly ConcurrentQueue<NetplayControlPacket> SelfClocks = new();
        public volatile bool Playing;

        public void Record(ulong recipient, byte[] frame)
        {
            if (recipient != 1 || frame.Length < 2) return;
            if (frame[0] == SteamGameplayWire.Frame)
            {
                var world = WorldFramePacket.Deserialize(frame.AsSpan(1));
                if (world.MatchState == MatchState.Playing) Playing = true;
                foreach (var relayed in world.Inputs)
                    if (relayed.EntityId == 1) SelfStates[world.Tick] = relayed;
            }
            else if (frame[0] == SteamGameplayWire.Control &&
                     NetplayControlPacket.TryDeserialize(frame.AsSpan(1), out var control) &&
                     control.Kind == NetplayControlKind.Clock)
                SelfClocks.Enqueue(control);
        }
    }

    private static MatchInstance StartPlaying(Capture capture, Action<Guid, string>? onCancel = null)
    {
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), "slop_pit",
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
            LoadCatalog(), _ => { }, contentHash: ContentHash, onMatchCancelled: onCancel,
            admissionDeadlineUtc: DateTimeOffset.UtcNow.AddSeconds(60),
            steamSend: (_, recipient, frame, _) => capture.Record(recipient, frame));
        match.Start();
        Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
        Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));
        Assert.True(match.TryMarkSteamReady(11, new NetplayControlPacket(NetplayControlKind.Ready, 1, match.ServerTick, match.StartTick)));
        Assert.True(match.TryMarkSteamReady(22, new NetplayControlPacket(NetplayControlKind.Ready, 2, match.ServerTick, match.StartTick)));
        Assert.True(SpinWait.SpinUntil(() => capture.Playing, TimeSpan.FromSeconds(8)), "Match did not reach Playing.");
        return match;
    }

    private static void StopAndWait(MatchInstance match)
    {
        match.Stop();
        Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
    }

    [Fact]
    public void RedundantBundleDeliversAPressWhoseOwnDatagramWasLost()
    {
        var capture = new Capture();
        var match = StartPlaying(capture);
        try
        {
            uint pressTick = match.ServerTick + 12;
            // The datagram carrying only pressTick was lost; the next one repeats it.
            Assert.True(match.TryQueueSteamInput(11, pressTick + 1,
                new[] { new InputState { Jump = true, JumpHeld = true }, new InputState { JumpHeld = true } }));
            Assert.True(SpinWait.SpinUntil(() => capture.SelfStates.ContainsKey(pressTick), 3000));
            var consumed = capture.SelfStates[pressTick];
            Assert.True(consumed.HasInput);
            Assert.True(consumed.Input.Jump, "The server consumed a held input instead of the redundant press.");
        }
        finally { StopAndWait(match); }
    }

    [Fact]
    public void ClockControlReportsLateAndOnTimeInputArrival()
    {
        var capture = new Capture();
        var match = StartPlaying(capture);
        try
        {
            Assert.True(match.TryQueueSteamInput(11, match.ServerTick - 1, new[] { default(InputState) }));
            Assert.True(SpinWait.SpinUntil(() => capture.SelfClocks.Any(c => c.LateInputs > 0), 2000),
                "A late bundle was not reported on the next Clock control.");

            while (capture.SelfClocks.TryDequeue(out _)) { }
            uint newest = match.ServerTick + 5;
            Assert.True(match.TryQueueSteamInput(11, newest, new[] { default(InputState) }));
            NetplayControlPacket report = default;
            Assert.True(SpinWait.SpinUntil(() =>
            {
                while (capture.SelfClocks.TryDequeue(out var clock))
                    if (clock.InputSlack != NetplayControlPacket.NoInputSlack) { report = clock; return true; }
                return false;
            }, 2000), "An on-time bundle was not reported.");
            Assert.Equal((byte)0, report.LateInputs);
            Assert.InRange(report.InputSlack, (sbyte)1, (sbyte)5);
        }
        finally { StopAndWait(match); }
    }

    [Fact]
    public void MatchLoopFailureCancelsOnlyThatMatchWithADistinctReason()
    {
        string? reason = null;
        var capture = new Capture();
        var match = StartPlaying(capture, (_, why) => reason = why);
        var simulation = typeof(MatchInstance).GetField("_sim",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        simulation.SetValue(match, null);   // the next tick throws inside the match loop

        Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 3000), "A failing match kept running.");
        Assert.Equal(MatchInstance.MatchFailedReason, reason);
        Assert.Contains(match.Diagnostics.Snapshot().Events, e => e.Level == "error" && e.Message.StartsWith("Match loop failed"));
    }
}
