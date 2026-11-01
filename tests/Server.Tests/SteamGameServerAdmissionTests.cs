using System.Buffers.Binary;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class SteamGameServerAdmissionTests
{
    private const string ContentHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _utcTicks = now.UtcDateTime.Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
    }

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

    private static MatchInstance NewSteamMatch(string id, MatchContentCatalog catalog, TestClock clock,
        IReadOnlyList<MatchPlayer> roster, DateTimeOffset deadline,
        Action<Guid, string>? onCancel = null, Action<MatchResultReport>? onResult = null) =>
        new(0, id, ArenaRegistry.Get("slop_pit")!.Value, roster, catalog, _ => { }, onMatchResult: onResult,
            onMatchCancelled: onCancel, admissionDeadlineUtc: deadline, contentHash: ContentHash,
            steamSend: static (_, _, _, _) => { }, clock: clock);

    private static void MarkSteamReady(MatchInstance match, long connectionId, ulong entityId)
    {
        var ready = new NetplayControlPacket(NetplayControlKind.Ready, entityId, match.ServerTick, match.StartTick);
        Assert.True(match.TryMarkSteamReady(connectionId, ready));
    }

    private static byte[] ControlFrame(NetplayControlKind kind, ulong entityId, uint tick = 0, uint startTick = 0)
    {
        var frame = new byte[1 + NetplayControlPacket.Size];
        frame[0] = SteamGameplayWire.Control;
        new NetplayControlPacket(kind, entityId, tick, startTick).Serialize(frame.AsSpan(1));
        return frame;
    }

    [Fact]
    public void SteamStartupControlsRejectSpoofedUnreliableAndMalformedFrames()
    {
        bool readyCalled = false;
        bool MarkReady(NetplayControlPacket _) { readyCalled = true; return true; }

        foreach (var kind in new[] { NetplayControlKind.Bootstrap, NetplayControlKind.Ready })
        {
            var valid = ControlFrame(kind, 1);
            Assert.False(SteamGameplayControlRouter.TryHandle(valid, 1, false, MarkReady));
            Assert.False(SteamGameplayControlRouter.TryHandle(valid, 0, true, MarkReady));
            Assert.False(SteamGameplayControlRouter.TryHandle(valid, 2, true, MarkReady));
            Assert.False(SteamGameplayControlRouter.TryHandle(valid.AsSpan(0, valid.Length - 1), 1, true, MarkReady));
            Assert.False(SteamGameplayControlRouter.TryHandle(new byte[valid.Length + 1], 1, true, MarkReady));
            valid[2] = unchecked((byte)(NetplayControlPacket.Version + 1));
            Assert.False(SteamGameplayControlRouter.TryHandle(valid, 1, true, MarkReady));
        }
        var unknownKind = ControlFrame(NetplayControlKind.Bootstrap, 1);
        unknownKind[1] = byte.MaxValue;
        Assert.False(SteamGameplayControlRouter.TryHandle(unknownKind, 1, true, MarkReady));
        var wrongEnvelope = ControlFrame(NetplayControlKind.Ready, 1);
        wrongEnvelope[0] = SteamGameplayWire.Input;
        Assert.False(SteamGameplayControlRouter.TryHandle(wrongEnvelope, 1, true, MarkReady));
        Assert.False(SteamGameplayControlRouter.TryHandle(Array.Empty<byte>(), 1, true, MarkReady));
        Assert.False(SteamGameplayControlRouter.TryHandle(ControlFrame(NetplayControlKind.Clock, 1), 1, true, MarkReady));
        Assert.False(readyCalled, "Rejected startup frames must not reach match readiness.");
    }

    private static InputState[] OneInput(InputState input = default) => new[] { input };

    [Fact]
    public void SteamReadyRetainsConnectionAndAuthoritativeClockValidation()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-11T15:00:00Z"));
        var match = NewSteamMatch(Guid.NewGuid().ToString("D"), LoadCatalog(), clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1),
                new MatchPlayer(1002, CharacterClass.Manki, 2) }, clock.GetUtcNow().AddSeconds(60));
        Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
        Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));

        // The host routes each frame with its connection's bound entity.
        bool Deliver(long connectionId, byte[] frame) =>
            SteamGameplayControlRouter.TryHandle(frame, connectionId == 11 ? 1UL : 2UL, true,
                control => match.TryMarkSteamReady(connectionId, control));

        Assert.False(Deliver(22, ControlFrame(NetplayControlKind.Ready, 1)));            // spoofed entity closes
        // An early or stale Ready (e.g. sent across the countdown/GO edge) is ignored,
        // not treated as spoofing, and never marks the peer Ready.
        Assert.True(Deliver(11, ControlFrame(NetplayControlKind.Ready, 1, tick: 1)));
        Assert.True(Deliver(11, ControlFrame(NetplayControlKind.Ready, 1, startTick: 1)));
        Assert.False(match.TryQueueSteamInput(11, 1, OneInput()));
        Assert.True(Deliver(11, ControlFrame(NetplayControlKind.Bootstrap, 1)));
        Assert.False(match.TryQueueSteamInput(11, 1, OneInput()));
        Assert.True(Deliver(11, ControlFrame(NetplayControlKind.Ready, 1)));
        Assert.True(match.TryQueueSteamInput(11, 1, OneInput()));
    }

    [Fact]
    public async Task SteamBootstrapBeforeReadyPreservesBothConnectionsAndStartsCountdown()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-11T15:00:00Z"));
        var baselines = new[]
        {
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var receivedEntities = new[] { new HashSet<ulong>(), new HashSet<ulong>() };
        var countdown = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = LoadCatalog();
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), ArenaRegistry.Get("slop_pit")!.Value,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1),
                new MatchPlayer(1002, CharacterClass.Manki, 2) },
            catalog, _ => { }, admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60),
            contentHash: ContentHash,
            steamSend: (_, recipient, frame, _) =>
            {
                if (frame[0] != SteamGameplayWire.Frame) return;
                var world = WorldFramePacket.Deserialize(frame.AsSpan(1));
                int index = (int)recipient - 1;
                foreach (var relayed in world.Inputs)
                    receivedEntities[index].Add(relayed.EntityId);
                if (receivedEntities[index].Count == 2) baselines[index].TrySetResult(true);
                if (world.MatchState == MatchState.Countdown) countdown.TrySetResult(world.Tick);
            }, clock: clock);

        bool Deliver(long connectionId, ulong entityId, byte[] frame) =>
            SteamGameplayControlRouter.TryHandle(frame, entityId, reliable: true,
                control => match.TryMarkSteamReady(connectionId, control));

        match.Start();
        try
        {
            Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));
            Assert.True(Deliver(11, 1, ControlFrame(NetplayControlKind.Bootstrap, 1)),
                "The first client's normal Bootstrap would disconnect it before readiness.");
            Assert.True(Deliver(22, 2, ControlFrame(NetplayControlKind.Bootstrap, 2)),
                "The second client's normal Bootstrap would disconnect it before readiness.");
            await Task.WhenAll(baselines.Select(x => x.Task)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(match.HasStartedCountdown);
            Assert.Equal(0u, match.ServerTick);
            Assert.False(match.TryQueueSteamInput(11, 1, OneInput()));
            Assert.False(match.TryQueueSteamInput(22, 1, OneInput()));

            Assert.True(Deliver(11, 1, ControlFrame(NetplayControlKind.Ready, 1)));
            Assert.True(Deliver(11, 1, ControlFrame(NetplayControlKind.Bootstrap, 1)));
            Assert.True(Deliver(11, 1, ControlFrame(NetplayControlKind.Ready, 1)));
            Assert.False(match.HasStartedCountdown);
            Assert.True(Deliver(22, 2, ControlFrame(NetplayControlKind.Bootstrap, 2)));
            Assert.True(Deliver(22, 2, ControlFrame(NetplayControlKind.Ready, 2)));
            Assert.True(await countdown.Task.WaitAsync(TimeSpan.FromSeconds(2)) > 0,
                "Both peers completed the startup handshake but the authoritative countdown did not advance.");
            Assert.True(match.HasStartedCountdown);
            var diagnostics = match.Diagnostics.Snapshot();
            Assert.Contains(diagnostics.Events, entry => entry.Message.Contains("countdown started"));
            Assert.Equal(2, diagnostics.Peers.Count);
            Assert.All(diagnostics.Peers, peer => Assert.True(peer.Connected));
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public void SteamInputCodecRejectsEntitySpoofAndAcceptsOnlyTheBoundSlot()
    {
        var frame = new byte[1 + InputBundle.SizeFor(2)];
        frame[0] = SteamGameplayWire.Input;
        InputBundle.Write(frame.AsSpan(1), 41, 17, new[] { new InputState { Jump = true }, new InputState { Down = true } });
        var inputs = new InputState[InputBundle.MaxInputs];

        Assert.False(SteamGameplayInputCodec.TryParse(frame, 42, out _, inputs, out _));
        Assert.False(SteamGameplayInputCodec.TryParse(frame.AsSpan(0, frame.Length - 1), 41, out _, inputs, out _));
        Assert.True(SteamGameplayInputCodec.TryParse(frame, 41, out uint tick, inputs, out int count));
        Assert.Equal(17u, tick);
        Assert.Equal(2, count);
        Assert.True(inputs[0].Jump);
        Assert.True(inputs[1].Down);
    }

    [Fact]
    public void SteamMatchAdmissionIsRosterAndMatchScopedAndBlocksDuplicateWaitingHandles()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var deadline = clock.GetUtcNow().AddSeconds(60);
        var first = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) }, deadline);
        var second = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(2001, CharacterClass.FightGuy, 1), new MatchPlayer(2002, CharacterClass.Manki, 2) }, deadline);

        Assert.False(first.TryBindSteamPlayer(9999, 11, ContentHash, out _, out _, out _));
        Assert.False(first.TryBindSteamPlayer(1001, 11, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", out _, out _, out _));
        Assert.True(first.TryBindSteamPlayer(1001, 11, ContentHash, out ulong firstEntity, out _, out _));
        Assert.Equal(1UL, firstEntity);
        Assert.True(second.TryBindSteamPlayer(2001, 22, ContentHash, out ulong secondEntity, out _, out _));
        MarkSteamReady(first, 11, firstEntity);
        MarkSteamReady(second, 22, secondEntity);
        Assert.False(first.TryQueueSteamInput(22, 1, OneInput()));
        Assert.True(second.TryQueueSteamInput(22, 1, OneInput()));

        Assert.False(first.TryBindSteamPlayer(1001, 33, ContentHash, out _, out _, out byte denial));
        Assert.Equal((byte)5, denial);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(first.TryBindSteamPlayer(1002, 44, ContentHash, out _, out _, out denial));
        Assert.Equal((byte)3, denial);
    }

    [Fact]
    public void ActiveSameAccountReconnectReplacesOldHandleAndRejectsItsInput()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var match = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
            clock.GetUtcNow().AddSeconds(60));
        match.Start();
        try
        {
            bool firstAdmitted = match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out byte denial);
            Assert.True(firstAdmitted,
                $"First admission denied: code={denial}, countdown={match.HasStartedCountdown}, running={match.IsRunning}.");
            bool secondAdmitted = match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out denial);
            Assert.True(secondAdmitted,
                $"Second admission denied: code={denial}, countdown={match.HasStartedCountdown}, running={match.IsRunning}.");
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 11, 1);
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 22, 2);
            Assert.True(SpinWait.SpinUntil(() => match.HasStartedCountdown, 2000),
                $"Expected countdown, running={match.IsRunning}.");

            bool reconnected = match.TryBindSteamPlayer(1001, 33, ContentHash, out _, out long replaced, out denial);
            Assert.True(reconnected,
                $"Same-account reconnect denied: code={denial}, countdown={match.HasStartedCountdown}, running={match.IsRunning}.");
            Assert.Equal(11L, replaced);
            Assert.False(match.TryQueueSteamInput(11, match.ServerTick + 1, OneInput()));
            Assert.False(match.TryQueueSteamInput(33, match.ServerTick + 1, OneInput()));
            MarkSteamReady(match, 33, 1);
            Assert.True(match.TryQueueSteamInput(33, match.ServerTick + 1, OneInput()));
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public async Task SteamWaitingBroadcast_IsOneTaggedWorldFrameWithEveryFighter()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var matchId = Guid.NewGuid();
        var firstState = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var match = new MatchInstance(0, matchId.ToString("D"), ArenaRegistry.Get("slop_pit")!.Value,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1),
                new MatchPlayer(1002, CharacterClass.Manki, 2) },
            catalog, _ => { }, admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60),
            contentHash: ContentHash,
            steamSend: (_, _, frame, _) =>
            {
                if (frame[0] == SteamGameplayWire.Frame)
                    firstState.TrySetResult(frame);
            }, clock: clock);
        match.Start();
        try
        {
            Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));
            var frame = await firstState.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var world = WorldFramePacket.Deserialize(frame.AsSpan(1));
            Assert.Equal(MatchState.Waiting, world.MatchState);
            Assert.Equal(0u, world.Tick);
            Assert.Equal(new[] { 1UL, 2UL }, world.Inputs.Select(x => x.EntityId));
            // Nothing is consumed before the first playing tick; connected fighters relay neutral input.
            Assert.All(world.Inputs, relayed => Assert.Equal(default, relayed.Input));
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public async Task SteamOpponentReceivesWibouKunaiUntilServerRemovesThem()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var playing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawned = new TaskCompletionSource<Hitbox[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var swordActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var swordRemoved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = LoadCatalog();
        // The opponent's view: a simulation restored from every World Frame it receives.
        var view = new ServerSimulation(ArenaRegistry.GetPvP("slop_pit")!.Value);
        foreach (var (entity, character) in new[] { (1UL, CharacterClass.Wibou), (2UL, CharacterClass.FightGuy) })
        {
            var content = catalog.Resolve(character)!;
            view.RegisterEntity(entity, content.Definition, new CharacterState(), content.BakedAnimation);
        }
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), ArenaRegistry.Get("slop_pit")!.Value,
            new[] { new MatchPlayer(1001, CharacterClass.Wibou, 1),
                new MatchPlayer(1002, CharacterClass.FightGuy, 2) },
            catalog, _ => { }, contentHash: ContentHash,
            admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60),
            steamSend: (_, recipient, frame, _) =>
            {
                if (recipient != 2 || frame[0] != SteamGameplayWire.Frame) return;
                var world = WorldFramePacket.Deserialize(frame.AsSpan(1));
                if (world.MatchState == MatchState.Playing)
                    playing.TrySetResult(true);
                view.RestoreSnapshot(world.Snapshot);
                var kunai = view.Resolver.GetActiveHitboxes()
                    .Where(h => h.OwnerId == 1 && h.Active && h.AttackSlot == AbilitySlots.A).ToArray();
                if (kunai.Length == 3) spawned.TrySetResult(kunai);
                else if (kunai.Length == 0 && spawned.Task.IsCompleted)
                    removed.TrySetResult(true);
                bool sword = view.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == 1 && h.Active &&
                    h.SourceEvent.BoneName == "_weapon_hilt" && h.SourceEvent.EndBoneName == "_weapon_tip");
                if (sword)
                    swordActive.TrySetResult(true);
                else if (swordActive.Task.IsCompleted)
                    swordRemoved.TrySetResult(true);
            }, clock: clock);
        match.Start();
        try
        {
            Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 11, 1);
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 22, 2);
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(8));
            uint nextTick = match.ServerTick + 6;
            for (uint i = 0; i < 120; i++)
            {
                Assert.True(match.TryQueueSteamInput(11, nextTick++,
                    OneInput(i == 0 ? new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true } : default)));
                Thread.Sleep(16);
            }
            var kunai = await spawned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(3, kunai.Select(x => x.VisualOperationIndex).Distinct().Count());
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(4));
            nextTick = match.ServerTick + 6;
            for (uint i = 0; i < 80; i++)
            {
                Assert.True(match.TryQueueSteamInput(11, nextTick++,
                    OneInput(i == 0 ? new InputState { ActiveSlot = AbilitySlots.Normal1 } : default)));
                Thread.Sleep(16);
            }
            await swordActive.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await swordRemoved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public async Task SteamWaitingAndOpponentAbsenceAbortWithoutCompetitiveResult()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        int resultReports = 0;
        var waitingReason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
            clock.GetUtcNow().AddSeconds(60), (_, reason) => waitingReason.TrySetResult(reason),
            _ => Interlocked.Increment(ref resultReports));
        waiting.Start();
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("unfilled", await waitingReason.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        var absenceReason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(2001, CharacterClass.FightGuy, 1), new MatchPlayer(2002, CharacterClass.Manki, 2) },
            clock.GetUtcNow().AddSeconds(60), (_, reason) => absenceReason.TrySetResult(reason),
            _ => Interlocked.Increment(ref resultReports));
        active.Start();
        try
        {
            Assert.True(active.TryBindSteamPlayer(2001, 55, ContentHash, out _, out _, out _));
            Assert.True(active.TryBindSteamPlayer(2002, 66, ContentHash, out _, out _, out _));
            MarkSteamReady(active, 55, 1);
            MarkSteamReady(active, 66, 2);
            Assert.True(SpinWait.SpinUntil(() => active.HasStartedCountdown, 2000));
            active.DisconnectSteamPlayer(55);
            await Task.Delay(50); // Let the simulation observe absence before advancing the injected clock.
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal("absent", await absenceReason.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            active.Stop();
            Assert.True(SpinWait.SpinUntil(() => !active.IsRunning, 2000));
        }
        Assert.True(SpinWait.SpinUntil(() => !waiting.IsRunning, 2000));
        Assert.Equal(0, Volatile.Read(ref resultReports));
    }
    [Fact]
    public async Task DevelopmentRoomWaitingDeadlineReleasesUnfilledMatchWithoutWinner()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var canceled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int resultReports = 0;
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), ArenaRegistry.Get("slop_pit")!.Value,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1),
                new MatchPlayer(1002, CharacterClass.Manki, 2) }, catalog, _ => { },
            onMatchResult: _ => Interlocked.Increment(ref resultReports),
            onMatchCancelled: (_, reason) => canceled.TrySetResult(reason),
            admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60), clock: clock);
        match.Start();
        try
        {
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal("unfilled", await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
            Assert.Equal(0, Volatile.Read(ref resultReports));
        }
        finally
        {
            match.Stop();
        }
    }

}
