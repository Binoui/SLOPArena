using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Server;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Server.Tests;

/// <summary>
/// Full-world rollback end to end over development UDP (ADR-0039, plan phase 4): a real
/// MatchInstance broadcasts one World Frame per tick, and two headless peers running the same
/// <see cref="NetplaySession"/> as the Unity client bootstrap, report Ready, start predicting on
/// the GO frame, send input bundles and stay in agreement with the GameHost.
/// </summary>
public class FullWorldNetplayTests
{
    [Fact]
    public void TwoUdpPeersPredictTheGameHostWorld()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var built = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(built.IsValid, string.Join("; ", built.Diagnostics));
        var catalog = built.Catalog!;
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var arena = ArenaRegistry.GetPvP("slop_pit")!.Value;

        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var ended = new ManualResetEventSlim();
        var roster = new[] { (1UL, CharacterClass.FightGuy), (2UL, CharacterClass.Manki) };
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), arena,
            roster.Select((p, i) => new MatchPlayer(101 + i, p.Item2, (int)p.Item1)).ToArray(), catalog, _ => ended.Set());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);

        var mover = new Peer(1, arena, catalog, roster, endpoint);
        var watcher = new Peer(2, arena, catalog, roster, endpoint);
        // The mover walks along +x for a second, jumps, then stands still; the watcher idles.
        mover.Input = tick => tick - mover.FirstPredictedTick is >= 20 and < 80
            ? new InputState { MoveX = 1f, Jump = tick - mover.FirstPredictedTick == 50, JumpHeld = tick - mover.FirstPredictedTick is >= 50 and < 56 }
            : default;
        try
        {
            match.Start();
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(14) &&
                (mover.PredictedTicks < 200 || watcher.PredictedTicks < 200))
            {
                double now = NetplayNow();
                mover.Step(now);
                watcher.Step(now);
                Thread.Sleep(15);
            }

            foreach (var peer in new[] { mover, watcher })
            {
                Assert.True(peer.PredictedTicks >= 200, $"peer {peer.Id} predicted only {peer.PredictedTicks} ticks; " +
                    $"local {peer.Session.LocalTick} anchor {peer.Session.Predictor.AnchorTick} newest frame {peer.Session.NewestFrameTick} " +
                    $"frames {peer.Totals.Frames} rejected {peer.Totals.RejectedFrames} anchors {peer.Totals.Anchors} starved {peer.Totals.StarvedTicks} " +
                    $"server tick {match.ServerTick}");
                // Prediction starts after the GO frame (StartTick − 1) or a later frame: no client
                // ever predicts a countdown tick the GameHost does not simulate.
                Assert.InRange(peer.FirstPredictedTick, match.StartTick, match.StartTick + 3);
                Assert.Equal(0, peer.Totals.RejectedFrames);
                Assert.Equal(0, peer.Totals.StarvedTicks);
                Assert.Equal(MatchState.Playing, peer.Session.MatchState);
            }
            // The mover's inputs reached the GameHost on time: it walked, and its own client never
            // needed a correction for it.
            var spawn = arena.SpawnPoints[0];
            var server = mover.LatestFrame!;
            var moved = mover.StateAt(server, 1);
            Assert.True(moved.PX - spawn.X > 1f, $"fighter 1 moved only {moved.PX - spawn.X:F2} m");
            Assert.Equal(0f, mover.Totals.MaxSelfCorrectionMeters);
            // The watcher learned of each input change a frame later, then saw the same world.
            Assert.True(watcher.Totals.Corrections > 0);
            Assert.True(watcher.Totals.ExactAnchors > watcher.Totals.Anchors / 2,
                $"watcher exact anchors {watcher.Totals.ExactAnchors} of {watcher.Totals.Anchors}");
            var seen = watcher.Session.Predictor.GetState(1);
            Assert.True(MathF.Abs(seen.PX - moved.PX) < 0.01f, $"watcher sees fighter 1 at {seen.PX:F3}, GameHost {moved.PX:F3}");
        }
        finally
        {
            mover.Dispose();
            watcher.Dispose();
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }

    /// <summary>
    /// ADR-0040 slice 2: a co-op Slopball run over the same transport. The GameHost seeds and serves;
    /// both peers learn the run from World Frames alone and predict the ball and rally state.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoUdpPeersPredictACoopSlopballRun(bool friendlyFire)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var built = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(built.IsValid, string.Join("; ", built.Diagnostics));
        var catalog = built.Catalog!;
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var arena = ArenaRegistry.Get("slopball")!.Value;

        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var ended = new ManualResetEventSlim();
        var roster = new[] { (1UL, CharacterClass.FightGuy), (2UL, CharacterClass.Bonk) };
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), arena,
            roster.Select((p, i) => new MatchPlayer(101 + i, p.Item2, (int)p.Item1)).ToArray(), catalog, _ => ended.Set(),
            format: MatchFormat.SlopballCoop, friendlyFire: friendlyFire ? FriendlyFireMode.On : FriendlyFireMode.Off);
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        Func<IMatchRule> rule = () => new SlopballCoopMatchRule(friendlyFire: friendlyFire ? FriendlyFireMode.On : FriendlyFireMode.Off);
        var first = new Peer(1, arena, catalog, roster, endpoint, rule);
        var second = new Peer(2, arena, catalog, roster, endpoint, rule);
        first.Input = tick => tick - first.FirstPredictedTick is >= 10 and < 40 ? new InputState { MoveX = -1f } : default;
        try
        {
            match.Start();
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(30) &&
                (first.ServedEvents < 5 || second.ServedEvents < 5))
            {
                double now = NetplayNow();
                first.Step(now);
                second.Step(now);
                Thread.Sleep(15);
            }

            var server = first.ViewOf(first.LatestFrame!);
            var run = server.Slopball!.Value;
            Assert.NotEqual(0u, run.SessionSeed);
            Assert.True(run.Misses >= 4, $"only {run.Misses} misses reached the GameHost");
            Assert.False(run.Ended);
            Assert.Equal(0, run.MissBudget);
            Assert.True(run.Phase == SlopballPhase.Live || run.Shot.Index > 0, $"no serve yet: {run.Phase}");
            foreach (var peer in new[] { first, second })
            {
                Assert.True(peer.PredictedTicks >= 300, $"peer {peer.Id} predicted only {peer.PredictedTicks} ticks");
                Assert.Equal(0, peer.Totals.RejectedFrames);
                Assert.Equal(MatchState.Playing, peer.Session.MatchState);
                var predicted = peer.Session.Predictor.Simulation.Slopball!.Value;
                Assert.Equal(run.SessionSeed, predicted.SessionSeed);
                Assert.InRange((long)predicted.Shot.Index - (long)run.Shot.Index, -1, 1);
                Assert.Equal(SlopballCoopMatchRule.DefaultMissBudget, predicted.MissBudget);
                // The serve reached presentation once through the predictor's Slopball ledger.
                Assert.True(peer.ServedEvents >= 5, $"peer {peer.Id} presented {peer.ServedEvents} serves");
            }
            // Fighters start on the co-op line, not stacked on the court's one start.
            var start = arena.SpawnPoints[0];
            Assert.True(server.GetState(2).PX - start.X > 0.5f, $"fighter 2 at {server.GetState(2).PX:F2}");
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(4, true)]
    public void UdpPeersPredictVersusServesPointsAndCamps(int count, bool ballOnly)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var built = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(built.IsValid, string.Join("; ", built.Diagnostics));
        var catalog = built.Catalog!;
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var arena = ArenaRegistry.Get("slopball")!.Value;
        var roster = Enumerable.Range(1, count).Select(i => (Id: (ulong)i, Character: CharacterClass.FightGuy)).ToArray();
        var camps = roster.ToDictionary(p => p.Id, p => p.Id % 2 == 1 ? SlopballCamp.Teal : SlopballCamp.Clay);
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var ended = new ManualResetEventSlim();
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), arena,
            roster.Select(p => new MatchPlayer(100 + (long)p.Id, p.Character, (int)p.Id, camps[p.Id])).ToArray(),
            catalog, _ => ended.Set(), format: MatchFormat.SlopballVersus, ballOnly: ballOnly);
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        var peers = roster.Select(p => new Peer(p.Id, arena, catalog, roster, endpoint,
            () => new SlopballVersusMatchRule(camps, ballOnly))).ToArray();
        foreach (var peer in peers)
            peer.Input = _ => peer.Session.Predictor.Simulation.SlopballVersus is
                { Phase: SlopballVersusPhase.HeldServe } state && state.ServerEntityId == peer.Id
                ? new InputState { ActiveSlot = AbilitySlots.Normal1, FacingYaw = (short)(camps[peer.Id] == SlopballCamp.Teal ? 0 : 18000),
                    AimYaw = (short)(camps[peer.Id] == SlopballCamp.Teal ? 0 : 18000) } : default;
        try
        {
            match.Start();
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(18) && peers.Any(p => p.PointEvents < 2
                || p.LatestFrame == null || p.ViewOf(p.LatestFrame).SlopballVersus is not { } score
                || score.TealScore + score.ClayScore < 2))
            {
                double now = NetplayNow();
                foreach (var peer in peers) peer.Step(now);
                Thread.Sleep(15);
            }
            foreach (var peer in peers)
            {
                Assert.Equal(0, peer.Totals.RejectedFrames);
                Assert.True(peer.ServedEvents >= 2, $"peer {peer.Id}: {peer.ServedEvents} serves, {peer.PointEvents} points");
                Assert.True(peer.PointEvents >= 2, $"peer {peer.Id}: {peer.PointEvents} points");
                var server = peer.ViewOf(peer.LatestFrame!);
                var score = server.SlopballVersus!.Value;
                Assert.True(score.TealScore + score.ClayScore >= 2,
                    $"peer {peer.Id} authoritative score {score.TealScore}:{score.ClayScore}");
                foreach (var camp in camps)
                {
                    Assert.Equal(camp.Value, server.CampOf(camp.Key));
                    Assert.True(camp.Value == SlopballCamp.Teal ? server.GetState(camp.Key).PZ < 0 : server.GetState(camp.Key).PZ > 0);
                }
                Assert.NotNull(peer.Session.Predictor.Simulation.SlopballVersus);
            }
        }
        finally
        {
            foreach (var peer in peers) peer.Dispose();
            match.Stop(); Assert.True(ended.Wait(3000), "Versus match did not stop.");
        }
    }

    [Fact]
    public void TwoNetplaySessionPeersFinishTheSameSeededMayhemMatch()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var provider = new MatchContentCatalogProvider();
        Assert.True(provider.TryBuild(out var catalog, out _, out var error), error);
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var arena = ArenaRegistry.Get("manki_mayhem")!.Value;
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port; reservation.Close();
        using var ended = new ManualResetEventSlim();
        var roster = new[] { (1UL, CharacterClass.FightGuy), (2UL, CharacterClass.Manki) };
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), arena,
            roster.Select((p, i) => new MatchPlayer(101 + i, p.Item2, (int)p.Item1)).ToArray(), catalog!, _ => ended.Set(),
            format: MatchFormat.MankiMayhem, friendlyFire: FriendlyFireMode.Shoves, mayhemSeed: 41);
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        using var first = new Peer(1, arena, catalog!, roster, endpoint, () => new MankiMayhemMatchRule());
        using var second = new Peer(2, arena, catalog!, roster, endpoint, () => new MankiMayhemMatchRule());
        first.Input = _ => first.Session.Predictor.Simulation.MankiMayhem is { RoundIndex: 0 }
            ? new InputState { MoveX = -1f } : default;
        try
        {
            match.Start(); var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(240) && (first.LatestResult == null || second.LatestResult == null))
            { double now = NetplayNow(); first.Step(now); second.Step(now); Thread.Sleep(10); }
            Assert.NotNull(first.LatestResult); Assert.NotNull(second.LatestResult);
            Assert.NotNull(first.LatestResult!.Mayhem);
            Assert.Equal(first.LatestResult.Mayhem!.Players, second.LatestResult!.Mayhem!.Players);
            Assert.True(first.MayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.Bench));
            foreach (var peer in new[] { first, second })
            {
                Assert.Equal(0, peer.Totals.RejectedFrames);
                Assert.True(peer.PredictedTicks > 1000);
                Assert.Equal(MatchState.Ended, peer.Session.MatchState);
                Assert.Equal(41u, peer.Session.Predictor.Simulation.MankiMayhem!.Value.Seed);
                Assert.Contains(peer.MayhemEvents, e => e.Kind == MankiMayhemEventKind.End);
                Assert.Contains(peer.MayhemEvents, e => e.Kind == MankiMayhemEventKind.Slam);
                Assert.Contains(peer.MayhemEvents, e => e.Kind == MankiMayhemEventKind.WallBroken);
                Assert.Equal(peer.LatestResult!.Mayhem!.Players, MayhemRunResult.From(peer.Session.Predictor.Simulation).Players);
            }
        }
        finally { match.Stop(); Assert.True(ended.Wait(3000), "Mayhem Match did not stop."); }
    }

    private static double NetplayNow() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>A headless client: the Unity bridge's protocol over a raw UDP socket.</summary>
    [Theory]
    [InlineData(MatchFormat.CourseRace)]
    [InlineData(MatchFormat.CourseCoop)]
    public void TwoUdpPeersPredictAnOnlineCharacterCourse(MatchFormat format)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages).Catalog!;
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var arena = ArenaRegistry.Get("fightguy_pier")!.Value;
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port; reservation.Close();
        using var ended = new ManualResetEventSlim();
        var roster = new[] { (1UL, CharacterClass.FightGuy), (2UL, CharacterClass.FightGuy) };
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), arena,
            roster.Select((p, i) => new MatchPlayer(101 + i, p.Item2, (int)p.Item1, Colour: (byte)i)).ToArray(),
            catalog, _ => ended.Set(), format: format);
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        using var first = new Peer(1, arena, catalog, roster, endpoint, () => format.CreateRule(3));
        using var second = new Peer(2, arena, catalog, roster, endpoint, () => format.CreateRule(3));
        try
        {
            match.Start(); var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(12) && (first.PredictedTicks < 90 || second.PredictedTicks < 90))
            { double now = NetplayNow(); first.Step(now); second.Step(now); Thread.Sleep(15); }
            foreach (var peer in new[] { first, second })
            {
                Assert.True(peer.PredictedTicks >= 90, $"peer {peer.Id}: {peer.PredictedTicks} ticks");
                Assert.Equal(0, peer.Totals.RejectedFrames);
                Assert.Equal(MatchState.Playing, peer.Session.MatchState);
                var host = peer.ViewOf(peer.LatestFrame!);
                Assert.Equal(format == MatchFormat.CourseCoop, host.OnlineCourse!.Value.Cooperative);
                Assert.Equal(2, host.CoursePlayers.Count);
                Assert.All(host.CoursePlayers, p => Assert.Equal(CharacterCoursePhase.Running, p.Course.Phase));
                Assert.True(host.OnlineCourse.Value.ElapsedTicks > 60);
                Assert.NotEqual(host.GetState(1).PX, host.GetState(2).PX);
            }
        }
        finally { match.Stop(); Assert.True(ended.Wait(3000)); }
    }

    private sealed class Peer : IDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly IPEndPoint _server;
        private readonly List<WorldFramePacket> _frames = new();
        private readonly ArenaDefinition _arena;
        private readonly MatchContentCatalog _catalog;
        private readonly (ulong Id, CharacterClass Character)[] _roster;
        private readonly Func<IMatchRule> _rule;
        private bool _clockReceived;
        private uint _startTick;
        private double _nextHandshake;
        private double _nextPing;

        public Peer(ulong id, ArenaDefinition arena, MatchContentCatalog catalog,
            (ulong Id, CharacterClass Character)[] roster, IPEndPoint server, Func<IMatchRule>? rule = null)
        {
            Id = id;
            _server = server;
            _arena = arena;
            _catalog = catalog;
            _roster = roster;
            _rule = rule ?? (() => new StockMatchRule(MatchDefaults.DefaultMaxStocks));
            Session = new NetplaySession(arena, id, _rule());
            foreach (var (entity, character) in roster)
            {
                var content = catalog.Resolve(character)!;
                Session.RegisterEntity(entity, content.Definition, new CharacterState(), content.BakedAnimation);
            }
        }

        public ulong Id { get; }
        public NetplaySession Session { get; }
        public Func<uint, InputState> Input { get; set; } = _ => default;
        public uint FirstPredictedTick { get; private set; }
        public int ServedEvents { get; private set; }
        public int PointEvents { get; private set; }
        private readonly List<SlopballTickEvent> _slopballStarted = new();
        private readonly List<SlopballTickEvent> _slopballRetracted = new();
        public int PredictedTicks { get; private set; }
        public MatchResultPacket? LatestResult { get; private set; }
        public List<MankiMayhemEvent> MayhemEvents { get; } = new();
        public WorldFramePacket? LatestFrame { get; private set; }
        public NetplayPredictorStats Totals;

        public void Step(double now)
        {
            _frames.Clear();
            while (_udp.Client.Poll(0, SelectMode.SelectRead))
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] datagram = _udp.Receive(ref remote);
                if (datagram.Length == NetplayControlPacket.Size && NetplayControlPacket.TryDeserialize(datagram, out var control))
                {
                    if (control.EntityId == Id && control.Kind == NetplayControlKind.Clock)
                    {
                        _clockReceived = true;
                        _startTick = control.StartTick;
                    }
                    Session.ObserveControl(control, now, 0);
                }
                else if (WorldFramePacket.TryDeserialize(datagram, out var frame))
                {
                    _frames.Add(frame!);
                    if (LatestFrame == null || frame!.Tick >= LatestFrame.Tick)
                        LatestFrame = frame;
                }
                else if (MatchResultPacket.TryDeserialize(datagram, out var result)) LatestResult = result;
            }
            Session.IngestFrames(_frames);

            if (now >= _nextPing)
            {
                // Like the Unity client: a ping each second keeps the UDP slot alive through the countdown.
                var ping = new byte[12];
                ping[0] = (byte)'P'; ping[1] = (byte)'I'; ping[2] = (byte)'N'; ping[3] = (byte)'G';
                _udp.Send(ping, ping.Length, _server);
                _nextPing = now + 1;
            }
            if (!_clockReceived && now >= _nextHandshake)
            {
                // Bootstrap until the GameHost answers; Ready once every fighter's state arrived.
                Send(new NetplayControlPacket(NetplayControlKind.Bootstrap, Id, 0, 0));
                if (Session.HasFrame)
                    Send(new NetplayControlPacket(NetplayControlKind.Ready, Id, Session.NewestFrameTick, _startTick));
                _nextHandshake = now + 0.25;
            }

            uint before = Session.LocalTick;
            if (Session.Update(Input(Session.LocalTick + 1), now))
            {
                if (FirstPredictedTick == 0)
                    FirstPredictedTick = before + 1;
                PredictedTicks += (int)(Session.LocalTick - before);
                var bundle = new byte[InputBundle.SizeFor(Session.RecentInputs.Length)];
                InputBundle.Write(bundle, Id, Session.LocalTick, Session.RecentInputs);
                _udp.Send(bundle, bundle.Length, _server);
            }
            _slopballStarted.Clear();
            _slopballRetracted.Clear();
            Session.Predictor.DrainSlopballEvents(_slopballStarted, _slopballRetracted);
            ServedEvents += _slopballStarted.Count(e => e.Event.Kind == SlopballEventKind.Served);
            PointEvents += _slopballStarted.Count(e => e.Event.Kind == SlopballEventKind.PointScored);
            // Judge prediction from the first predicted tick on; countdown frames are plain jumps.
            var mayhem = new List<MankiMayhemEvent>(); var retracted = new List<MankiMayhemEvent>();
            Session.Predictor.DrainMayhemEvents(mayhem, retracted);
            foreach (var item in retracted) MayhemEvents.RemoveAll(e => e.Key == item.Key);
            foreach (var item in mayhem)
            { Assert.DoesNotContain(MayhemEvents, e => e.Key == item.Key); MayhemEvents.Add(item); }
            var stats = Session.TakeStats(now).Prediction;
            if (FirstPredictedTick != 0)
                Accumulate(stats);
        }

        /// <summary>A fighter's state in a frame, decoded with this peer's own simulation setup.</summary>
        public CharacterState StateAt(WorldFramePacket frame, ulong fighter) => ViewOf(frame).GetState(fighter);

        /// <summary>The GameHost's world at a frame, decoded with this peer's own simulation setup.</summary>
        public ServerSimulation ViewOf(WorldFramePacket frame)
        {
            var view = new ServerSimulation(_arena, _rule());
            foreach (var (entity, character) in _roster)
            {
                var content = _catalog.Resolve(character)!;
                view.RegisterEntity(entity, content.Definition, new CharacterState(), content.BakedAnimation);
            }
            view.RestoreSnapshot(frame.Snapshot);
            return view;
        }

        private void Send(NetplayControlPacket control)
        {
            var bytes = new byte[NetplayControlPacket.Size];
            control.Serialize(bytes);
            _udp.Send(bytes, bytes.Length, _server);
        }

        private void Accumulate(in NetplayPredictorStats window)
        {
            Totals.Frames += window.Frames;
            Totals.RejectedFrames += window.RejectedFrames;
            Totals.Anchors += window.Anchors;
            Totals.ExactAnchors += window.ExactAnchors;
            Totals.Corrections += window.Corrections;
            Totals.StarvedTicks += window.StarvedTicks;
            Totals.MaxSelfCorrectionMeters = Math.Max(Totals.MaxSelfCorrectionMeters, window.MaxSelfCorrectionMeters);
        }

        public void Dispose() => _udp.Dispose();
    }
}
