using System.Buffers.Binary;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using SlopArena.Shared.StageHazards;
using Xunit;

namespace SlopArena.Shared.Tests;

public class StageHazardSlice1aTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data", "arenas"))) dir = dir.Parent;
            return dir?.FullName ?? throw new Exception("Repository root unavailable.");
        }
    }
    private static ArenaDefinition Arena() => ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data/arenas/splash_deck.arena"))!.Value;
    private static StageHazardAnchors Anchors(in ArenaDefinition arena) => StageHazardAuthoring.Parse(
        File.ReadAllText(Path.Combine(Root, "tests/Shared.Tests/Fixtures/StageHazards/splash-deck-shell.json")), arena);
    private static KrakenHazard Family() { var arena = Arena(); return new KrakenHazard(Anchors(arena)); }
    private static byte[] Save(StageHazardSession session) { var w = new SnapshotWriter(); session.WriteSnapshot(w); return w.ToArray(); }

    [Fact]
    public void ShellFixtureParsesAgainstRealArenaAndBindsOnlyTheRegisteredStage()
    {
        var arena = Arena(); arena.HazardAnchors = Anchors(arena);
        Assert.Equal(6, arena.HazardAnchors.Entries.Count);
        Assert.Equal(ArenaPurpose.PvP, arena.Purpose);
        Assert.IsType<KrakenHazard>(StageHazardFamilies.Bind(arena));
        arena.Name = "unregistered"; Assert.Null(StageHazardFamilies.Bind(arena));
        arena.Name = "splash_deck"; arena.HazardAnchors = null;
        Assert.Throws<ArgumentException>(() => StageHazardFamilies.Bind(arena));
    }
    [Fact]
    public void GeometryIsBoundedFiniteAndOriginAloneMayLeaveBlastCorridor()
    {
        var arena = Arena(); var outside = new ArcadePoint(1000, -100, 0);
        Assert.Throws<ArgumentException>(() => new HazardAnchor.Point(outside, arena));
        Assert.Throws<ArgumentException>(() => new HazardAnchor.Capsule(new(0, 0, 0), new(0, 0, 0), 100, arena));
        Assert.Throws<ArgumentException>(() => new HazardAnchor.Box(new(0, 0, 0), new(100, 1, 1), 0, arena));
        Assert.IsType<HazardAnchor.Origin>(new HazardAnchor.Origin(outside));
        Assert.Throws<ArgumentException>(() => new HazardAnchor.Origin(new(float.NaN, 0, 0)));
        Assert.Throws<ArgumentException>(() => new HazardAnchor.Point(new(0, float.PositiveInfinity, 0), arena));
        var p = new HazardAnchor.Point(new(0, 0, 0), arena);
        Assert.Throws<ArgumentException>(() => new StageHazardAnchors(new[] { KeyValuePair.Create<string, HazardAnchor>("x", p), KeyValuePair.Create<string, HazardAnchor>("x", p) }, arena));
        Assert.Throws<ArgumentException>(() => new StageHazardAnchors(new[] { KeyValuePair.Create<string, HazardAnchor>(new string('a', 65), p) }, arena));
        Assert.Throws<ArgumentException>(() => new HazardAnchor.Loop(Enumerable.Repeat(new ArcadePoint(), 129), arena));
    }
    [Fact]
    public void AnchorCollectionsCopyTheirInputs()
    {
        var arena = Arena(); var points = new[] { new ArcadePoint(0, 0, 0), new ArcadePoint(1, 0, 0), new ArcadePoint(0, 0, 1) };
        var loop = new HazardAnchor.Loop(points, arena); points[0] = new(999, 0, 0);
        Assert.Equal(0, loop.Points[0].X);
    }
    [Fact]
    public void ExistingArenasRoundTripByteForByte()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(Root, "data/arenas"), "*.arena"))
        {
            var bytes = File.ReadAllBytes(path); var arena = ArenaBinaryFormat.Deserialize(bytes);
            Assert.True(arena.HasValue, path);
            Assert.Equal(bytes, ArenaBinaryFormat.Serialize(arena.Value));
        }
    }
    [Fact]
    public void AnchoredArenaRoundTripsAndMalformedSectionsFail()
    {
        var arena = Arena(); arena.HazardAnchors = null; var plain = ArenaBinaryFormat.Serialize(arena);
        arena.HazardAnchors = Anchors(arena); var bytes = ArenaBinaryFormat.Serialize(arena);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(plain.AsSpan(8).ToArray(), bytes.AsSpan(8, plain.Length - 8).ToArray());
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(ArenaBinaryFormat.Deserialize(bytes)!.Value));
        for (int n = plain.Length; n < bytes.Length; n++) Assert.Null(ArenaBinaryFormat.Deserialize(bytes[..n]));
        var oversized = bytes.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(oversized.AsSpan(plain.Length), 65537);
        Assert.Null(ArenaBinaryFormat.Deserialize(oversized));
        var invalid = bytes.ToArray(); invalid[plain.Length + 4] = 99; Assert.Null(ArenaBinaryFormat.Deserialize(invalid));
        var trailing = bytes.Concat(new byte[] { 0 }).ToArray(); Assert.Null(ArenaBinaryFormat.Deserialize(trailing));
    }
    [Fact]
    public void EveryAnchorKindRoundTrips()
    {
        var arena = Arena(); var entries = Anchors(arena).Entries.ToDictionary();
        entries.Add("point", new HazardAnchor.Point(new(0, 0, 0), arena));
        entries.Add("origin", new HazardAnchor.Origin(new(999, 999, 999)));
        entries.Add("cells", new HazardAnchor.Cells(new[] { new ArcadePoint(0, 0, 0) }, 1, arena));
        arena.HazardAnchors = new(entries, arena); var bytes = ArenaBinaryFormat.Serialize(arena);
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(ArenaBinaryFormat.Deserialize(bytes)!.Value));
    }
    [Fact]
    public void ScheduleIsSeededRouteCausesSideAndActionsNeverOverlap()
    {
        var f = Family(); var first = Schedule(f, 1); var repeat = Schedule(f, 1); var other = Schedule(f, 2);
        Assert.Equal(first, repeat); Assert.NotEqual(first, other);
        Assert.Contains(first, a => a.Side == KrakenSide.Port); Assert.Contains(first, a => a.Side == KrakenSide.Starboard);
    }
    private static (uint Lock, KrakenSide Side)[] Schedule(KrakenHazard f, uint seed)
    {
        var result = new List<(uint, KrakenSide)>(); uint from = 1;
        for (uint ordinal = 0; ordinal < 100; ordinal++)
        {
            uint locked = f.NextLock(seed, ordinal, from); var a = f.Plan(seed, ordinal, locked);
            Assert.True(locked >= from);
            Assert.Equal(f.Route(seed, locked).Z >= 0 ? KrakenSide.Port : KrakenSide.Starboard, a.Side);
            Assert.Equal(f.Route(seed, locked), a.LockedPose);
            Assert.True(a.Window.WarningTick - locked > NetplayPredictor.MaxPredictionTicks);
            Assert.True(a.Window.ImpactTick - a.Window.WarningTick > NetplayPredictor.MaxPredictionTicks);
            result.Add((locked, a.Side)); from = a.Window.RetireTick + 1;
        }
        return result.ToArray();
    }
    [Fact]
    public void BothPredictionWindowRulesAreEnforced()
    {
        uint p = NetplayPredictor.MaxPredictionTicks;
        Assert.Throws<ArgumentException>(() => new HazardWindow(1, 1 + p, 3 + 2 * p, 4 + 2 * p));
        Assert.Throws<ArgumentException>(() => new HazardWindow(1, 2 + p, 2 + 2 * p, 4 + 2 * p));
        Assert.Throws<ArgumentException>(() => new HazardWindow(5, 4, 3, 2));
    }
    [Fact]
    public void RestoreAtEveryTickMatchesContinuousSpawnsAndRetirement()
    {
        var f = Family(); var continuous = new StageHazardSession(f, 123, 0); var restored = new StageHazardSession(f, 123, 0);
        var r1 = new SpellResolver(); var r2 = new SpellResolver(); int spawns = 0, retires = 0;
        uint firstLock = f.NextLock(123, 0, 1); var first = f.Plan(123, 0, firstLock);
        for (uint tick = 1; tick <= 5000; tick++)
        {
            var before = Save(restored); var reader = new SnapshotReader(before);
            var resolverWriter = new SnapshotWriter(); r2.WriteSnapshot(resolverWriter, _ => (null, null));
            var resolverState = SpellResolver.ReadSnapshot(new SnapshotReader(resolverWriter.ToArray()), _ => (null, null));
            r2 = new SpellResolver(); r2.ApplySnapshot(resolverState);
            restored = new StageHazardSession(f, 0, 0);
            var prepared = restored.PrepareSnapshot(reader, tick - 1); Assert.True(reader.AtEnd);
            int count = r2.GetActiveHitboxes().Count; restored.Apply(prepared); Assert.Equal(count, r2.GetActiveHitboxes().Count);
            continuous.Tick(tick, r1); restored.Tick(tick, r2);
            Assert.Equal(Save(continuous), Save(restored));
            var h1 = r1.GetActiveHitboxes(); var h2 = r2.GetActiveHitboxes();
            Assert.Equal(h1.Select(h => (h.EmissionId, h.ActivationId, h.X, h.EndZ)), h2.Select(h => (h.EmissionId, h.ActivationId, h.X, h.EndZ)));
            if (h1.Count > count) spawns++;
            if (h1.Count < count) retires++;
            Assert.InRange(h1.Count, 0, first.Attacks.Count);
            if (tick < first.Window.WarningTick) Assert.Empty(continuous.View.Cues);
            if (tick == first.Window.WarningTick)
            {
                var cue = continuous.View.Cues[0];
                Assert.Equal(first.Attack.Definition, cue.Geometry);
            }
            if (tick == first.Attack.SpawnTick)
            {
                var h = h1[0]; Assert.Equal(StageHazardOwner.EntityId, h.OwnerId);
                Assert.False(h.Unblockable); Assert.False(h.TracksBone); Assert.False(h.FreezesOwner);
            }
        }
        Assert.True(spawns >= 5); Assert.True(retires >= 5);
    }
    [Fact]
    public void DuplicateOrSkippedTickCannotDuplicateAnOperation()
    {
        var f = Family(); var session = new StageHazardSession(f, 1, 0); var resolver = new SpellResolver();
        session.Tick(1, resolver); var before = Save(session);
        Assert.Throws<ArgumentException>(() => session.Tick(1, resolver));
        Assert.Throws<ArgumentException>(() => session.Tick(3, resolver)); Assert.Equal(before, Save(session));
    }
    [Fact]
    public void MalformedRestoreLeavesSessionAndResolverUntouched()
    {
        var f = Family(); var session = new StageHazardSession(f, 1, 0); var resolver = new SpellResolver();
        uint locked = f.NextLock(1, 0, 1);
        for (uint tick = 1; tick <= locked; tick++) session.Tick(tick, resolver);
        var before = Save(session);
        for (int length = 0; length < before.Length; length++)
            Assert.ThrowsAny<Exception>(() => session.PrepareSnapshot(new SnapshotReader(before[..length]), locked));
        var bad = before.ToArray(); bad[^1] ^= 1;
        Assert.ThrowsAny<Exception>(() => session.PrepareSnapshot(new SnapshotReader(bad), locked));
        Assert.Equal(before, Save(session)); Assert.Empty(resolver.GetActiveHitboxes());
        Assert.ThrowsAny<Exception>(() => session.PrepareSnapshot(new SnapshotReader(before), locked + 1));
    }
    [Fact]
    public void RetirementClearsHostEffectsAndPreservesOtherOwners()
    {
        var f = Family(); var session = new StageHazardSession(f, 1, 0); var resolver = new SpellResolver();
        var action = f.Plan(1, 0, f.NextLock(1, 0, 1));
        resolver.Spawn(new Hitbox { OwnerId = 42, Radius = 1, DurationTicks = 60000 });
        for (uint tick = 1; tick <= action.Window.RetireTick; tick++) session.Tick(tick, resolver);
        Assert.Equal(42UL, Assert.Single(resolver.GetActiveHitboxes()).OwnerId);
    }
    [Fact]
    public void MissingAndWrongKindAnchorsFailBinding()
    {
        var arena = Arena(); var entries = Anchors(arena).Entries.ToDictionary();
        entries["water_loop"] = new HazardAnchor.Point(new(0, 0, 0), arena);
        Assert.Throws<ArgumentException>(() => new KrakenHazard(new StageHazardAnchors(entries, arena)));
        entries.Remove("water_loop");
        Assert.Throws<ArgumentException>(() => new KrakenHazard(new StageHazardAnchors(entries, arena)));
    }
    [Fact]
    public void StageOwnerIsDistinctFromMayhemHost() => Assert.NotEqual(1UL << 48, StageHazardOwner.EntityId);
}
