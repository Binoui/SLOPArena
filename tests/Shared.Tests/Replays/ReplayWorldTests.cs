using System.Reflection;
using System.Security.Cryptography;
using SlopArena.Shared.AI;
using SlopArena.Shared.Replays;
using Xunit;
namespace SlopArena.Shared.Tests.Replays;

public class ReplayWorldTests
{
    internal static string Root
    {
        get
        {
            var p = new DirectoryInfo(AppContext.BaseDirectory);
            while (p != null && !File.Exists(Path.Combine(p.FullName, "SlopArena.sln"))) p = p.Parent;
            return p!.FullName;
        }
    }

    /// <summary>The whole shipped roster, built the way the GameHost builds its catalog and handle map.</summary>
    static readonly Lazy<MatchContentCatalog> Roster = new(() =>
    {
        string cooked = Path.Combine(Root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster", "manifest.json"));
        var packages = manifest.Entries.ToDictionary(e => e.PackageId,
            e => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, e.PackageId), e.Requirement), StringComparer.Ordinal);
        return new MatchContentCatalogBuilder().Build(manifest, packages).Catalog!;
    });

    static ReplayArena? LoadArena(string name)
    {
        string path = Path.Combine(Root, "data/arenas", name + ".arena");
        if (!File.Exists(path)) return null;
        return new(ArenaBinaryFormat.LoadFromFile(path)!.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    internal static ReplayContent Content(MatchContentCatalog? catalog = null) => new(catalog ?? Roster.Value, LoadArena, "fightguy");

    static MatchContentEntry Entry(CharacterClass c) => Roster.Value.Resolve(c)!;

    internal sealed record Recording(byte[] Bytes, ReplayHeader Header, List<ReplayViewTick> Views);

    /// <summary>A same-build CPU stock fight recorded through the real capture and writer, as the GameHost records it.</summary>
    static Recording RecordBots(CharacterClass[] fighters, int ticks, int seed = 7, string? alternate = null)
    {
        var arena = LoadArena("slop_pit")!;
        var entries = fighters.Select(Entry).ToArray();
        var roster = entries.Select((e, i) => new ReplayParticipant((ulong)i + 1, "cpu", i.ToString(), e.Identity,
            e.Identity.PackageId == alternate ? "alternate" : "base", "", i.ToString())).ToArray();
        var records = Roster.Value.Entries.Where(e => e.LegacySelector.HasValue)
            .Select(e => new MatchContentHandleRecord(e.Handle, e.LegacySelector!.Value, e.Identity, e.DisplayName)).ToList();
        string hash = SteamMatchDescriptor.HashContent(new MatchContentHandleMap(MatchContentHandleMap.CurrentSchemaVersion, records,
            new MatchArenaRecord(arena.Definition.Name, arena.Digest)));
        var header = new ReplayHeader("bots", arena.Definition.Name, new("stock", "off", 250, false), 1, roster,
            ReplayBuildIdentity.Current("test", hash), arena.Digest);
        var sim = new ServerSimulation(arena.Definition, MatchFormat.Stock.CreateRule(250, FriendlyFireMode.Off));
        for (int i = 0; i < entries.Length; i++)
        {
            var spawn = arena.Definition.SpawnPoints[i % arena.Definition.SpawnPoints.Length];
            var state = new CharacterState { EntityId = (ulong)i + 1, PX = spawn.X + i * .5f, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, IsGrounded = true, JumpsLeft = entries[i].Definition.Movement.MaxJumps, AirDodgesLeft = 1, DamagePercent = 150 };
            if (roster[i].FormId == "alternate") state.Form.Active = 1;
            sim.RegisterEntity(state.EntityId, entries[i].Definition, state, entries[i].BakedAnimation);
        }
        var capture = new ReplayCapture(header, Roster.Value, arena.Definition);
        using var stream = new MemoryStream();
        using var writer = new ReplayWriter(stream, header);
        var policy = new HeuristicBotPolicy();
        var rng = new Random(seed);
        var memories = entries.Select(_ => new BotMemory { Difficulty = CpuDifficulty.Normal }).ToArray();
        var views = new List<ReplayViewTick>();
        for (int tick = 0; tick < ticks; tick++)
        {
            var inputs = new Dictionary<ulong, InputState>();
            for (int i = 0; i < entries.Length; i++)
            {
                ulong id = (ulong)i + 1, other = (ulong)((i + 1 + tick / 300) % entries.Length) + 1;
                if (other == id) other = (ulong)((i + 1) % entries.Length) + 1;
                policy.ObserveOpponent(memories[i], sim.GetState(other), sim.GetDefinition(other), entries[(int)other - 1].BakedAnimation, sim.GetActiveMove(other)?.Slot);
                memories[i].ChooseOpponent(sim.GetState(id));
                inputs[id] = policy.Decide(sim.GetState(id), sim.GetDefinition(id), rng, memories[i], arena.Definition, entries[i].BakedAnimation, activeMove: sim.GetActiveMove(id)?.Slot);
            }
            sim.Tick(inputs);
            var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(), sim.GetPresentationEvents(true).Where(e => WorldFramePacket.CanCarry(e)).ToArray(), sim.LastTickHits.ToArray());
            var captured = capture.Capture(sim, frame);
            Assert.Equal(ReplayCaptureStatus.Accepted, captured.Status);
            views.Add(captured.Capture!.View);
            if (writer.Append(captured.Capture) == ReplayAppend.Stopped) Assert.Fail(writer.Complete(new(ReplayEndReason.Cancelled, "", null)).Conclusion.Detail);
        }
        writer.Complete(new(ReplayEndReason.Cancelled, "test", null));
        return new(stream.ToArray(), header, views);
    }

    internal static readonly Lazy<Recording> Duel = new(() => RecordBots(new[] { CharacterClass.FightGuy, CharacterClass.Nyx }, 1800, alternate: "nyx"));
    static readonly Lazy<Recording> FourWay = new(() => RecordBots(new[] { CharacterClass.Bonk, CharacterClass.Manki, CharacterClass.Nyx, CharacterClass.Bashie }, 1800, seed: 11));

    sealed class Probe : IReplayPresenter
    {
        public readonly List<uint> Resets = new();
        public readonly List<ReplayFrame> Frames = new();
        public Action<ReplayFrame>? OnPresent;
        public void Reset(uint targetTick) => Resets.Add(targetTick);
        public void Present(in ReplayFrame frame)
        {
            Frames.Add(frame);
            OnPresent?.Invoke(frame);
        }
    }

    /// <summary>Projects each presented display world the way the GameHost projects its live world.</summary>
    sealed class Reprojector
    {
        readonly ReplayCapture capture;
        public Reprojector(ReplayHeader header, ArenaDefinition arena) => capture = new ReplayCapture(header, Roster.Value, arena);
        public ReplayViewTick Project(ServerSimulation world)
        {
            var frame = new WorldFramePacket(MatchState.Playing, world.CaptureSnapshot(), Array.Empty<RelayedInput>(), world.GetPresentationEvents().ToArray(), world.LastTickHits.ToArray());
            var result = capture.Capture(world, frame);
            Assert.True(result.Status == ReplayCaptureStatus.Accepted, result.Detail);
            return result.Capture!.View;
        }
    }

    // Floats the decoder must invert arithmetically (FuseProgress, LaunchDirection) are compared within this bound.
    const float Tolerance = 1e-4f;

    /// <summary>Every recorded field through the codec's own field tables, so a new field is compared without edits here.</summary>
    static List<string> Diff(ReplayViewTick expected, ReplayViewTick actual)
    {
        var diffs = new List<string>();
        Rows("Fighters", expected.Fighters, actual.Fighters, ReplayViewCodec.ReplayFighterTable, diffs);
        Rows("Shapes", expected.Shapes, actual.Shapes, ReplayViewCodec.ReplayShapeTable, diffs);
        Rows("Hits", expected.Hits, actual.Hits, ReplayViewCodec.ReplayHitTable, diffs);
        Rows("Deaths", expected.Deaths, actual.Deaths, ReplayViewCodec.ReplayDeathTable, diffs);
        if (expected.Events.Count != actual.Events.Count) diffs.Add($"Events.Count {expected.Events.Count} != {actual.Events.Count}");
        else for (int i = 0; i < expected.Events.Count; i++)
                if (!ReplayViewCodec.ReplayEventKinds.Write(expected.Events[i]).SequenceEqual(ReplayViewCodec.ReplayEventKinds.Write(actual.Events[i])))
                    diffs.Add($"Events[{i}]");
        if (expected.MatchPhase != actual.MatchPhase) diffs.Add("MatchPhase");
        return diffs;
    }

    static void Rows<T>(string list, IReadOnlyList<T> expected, IReadOnlyList<T> actual, ReplayFields.Table<T> table, List<string> diffs)
    {
        if (expected.Count != actual.Count) { diffs.Add($"{list}.Count {expected.Count} != {actual.Count}"); return; }
        for (int i = 0; i < expected.Count; i++)
            foreach (var field in table.Fields)
            {
                byte[] a = Field(field, expected[i]), b = Field(field, actual[i]);
                if (!a.SequenceEqual(b) && !Close(list, field.Name, field.ValueType, a, b)) diffs.Add($"{list}[{i}].{field.Name}");
            }
    }

    static byte[] Field<T>(ReplayFields.FieldEntry<T> field, T row)
    {
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        field.Write(w, row);
        return s.ToArray();
    }

    static bool Close(string list, string field, Type type, byte[] a, byte[] b)
    {
        if ((list, field) is not (("Shapes", "FuseProgress") or ("Hits", "LaunchDirection"))) return false;
        if (type != typeof(float) && type != typeof(ReplayVector)) return false;
        if (a.Length != b.Length || a.Length != (type == typeof(float) ? 4 : 12)) return false;
        for (int i = 0; i < a.Length; i += 4)
        {
            float x = BitConverter.ToSingle(a, i), y = BitConverter.ToSingle(b, i);
            if (!float.IsFinite(x) || !float.IsFinite(y) || MathF.Abs(x - y) > Tolerance * MathF.Max(1f, MathF.Abs(x))) return false;
        }
        return true;
    }

    [Fact]
    public void EqualRemainingCooldownsKeepTheirResolvedSlotDurations()
    {
        using var replay = Open(Duel.Value.Bytes, ReplayTrackChoice.ViewOnly);
        var slots = replay.Cast[0].Content.Definition.CookedSlots!.Where(s => s != null && !s.IsAir).Take(2).ToArray();
        Assert.Equal(2, slots.Length);
        var fighter = new ReplayFighter
        {
            EntityId = replay.Cast[0].EntityId,
            Cooldowns = slots.Select((s, i) => new ReplayCooldown
            {
                PackageId = replay.Cast[0].Recorded.Package.PackageId, FormId = "base",
                MoveName = s.Name, SlotId = slots[1 - i].Id, TicksLeft = 30,
                DurationTicks = (ushort)(60 + 60 * i)
            }).ToArray()
        };
        var cast = ReplayCastIndex.Build(replay.Header, Content())!;
        var restored = ReplayViewDecoder.Prepare(replay.World,
            new ReplayViewTick { Fighters = new[] { fighter } }, cast);
        replay.World.ApplySnapshot(restored);
        for (int i = 0; i < slots.Length; i++)
        {
            byte wire = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[slots[i].Ordinal]);
            Assert.Equal(30, replay.World.GetState(fighter.EntityId).GetCooldown(wire));
            Assert.Equal((ushort)(60 + 60 * i), replay.CooldownDuration(fighter, wire));
        }
    }

    [Fact]
    public void ComparatorRejectsIntegerCorruption()
    {
        var fighter = new ReplayFighter { ChargeTier = 2 };
        var altered = new ReplayFighter { ChargeTier = 0 };
        var diffs = new List<string>();
        Rows("Fighters", new[] { fighter }, new[] { altered }, ReplayViewCodec.ReplayFighterTable, diffs);
        Assert.Contains("Fighters[0].ChargeTier", diffs);
        diffs.Clear();
        Rows("Shapes", new[] { new ReplayShape { VisualOperationIndex = 2 } },
            new[] { new ReplayShape { VisualOperationIndex = 0 } }, ReplayViewCodec.ReplayShapeTable, diffs);
        Assert.Contains("Shapes[0].VisualOperationIndex", diffs);
    }

    static ReplayWorld Open(byte[] bytes, ReplayTrackChoice choice, ReplayContent? content = null)
    {
        var opened = ReplayWorld.Open(new MemoryStream(bytes), content ?? Content(), choice);
        Assert.Null(opened.Failure);
        return opened.World!;
    }

    [Theory]
    [InlineData(2, ReplayTrackChoice.ViewOnly)]
    [InlineData(2, ReplayTrackChoice.Best)]
    [InlineData(4, ReplayTrackChoice.ViewOnly)]
    [InlineData(4, ReplayTrackChoice.Best)]
    public void DisplayWorldProjectsBackToTheRecordedViewOnEveryTick(int fighters, ReplayTrackChoice choice)
    {
        var recording = fighters == 2 ? Duel.Value : FourWay.Value;
        using var replay = Open(recording.Bytes, choice);
        Assert.Equal(choice == ReplayTrackChoice.Best ? ReplayTrack.Exact : ReplayTrack.View, replay.Track);
        Assert.Null(replay.ExactRefusal);
        Assert.All(replay.Cast, m => Assert.Equal(ReplayCastKind.Recorded, m.Kind));
        var reprojector = new Reprojector(replay.Header, replay.Arena);
        var mismatches = new Dictionary<string, int>();
        int ticks = 0, moves = 0, shapes = 0, hits = 0, deaths = 0, events = 0, perches = 0;
        var probe = new Probe
        {
            OnPresent = frame =>
            {
                Assert.Same(replay.World, frame.World);
                var expected = recording.Views[(int)(frame.View.Tick - replay.Range.First)];
                foreach (var d in Diff(expected, reprojector.Project(frame.World)))
                    mismatches[d.Split('.')[^1]] = mismatches.GetValueOrDefault(d.Split('.')[^1]) + 1;
                ticks++;
                moves += expected.Fighters.Count(f => f.SlotId != "");
                shapes += expected.Shapes.Count;
                perches += expected.Shapes.Count(s => s.VisualKindId == "perching");
                hits += expected.Hits.Count;
                deaths += expected.Deaths.Count;
                events += expected.Events.Count;
            }
        };
        while (replay.Step(probe).Status == ReplayStep.Presented) { }
        Assert.Equal(recording.Views.Count, ticks);
        Assert.True(mismatches.Count == 0, string.Join(", ", mismatches.Select(m => $"{m.Key} x{m.Value}")));
        Assert.True(moves > 0 && shapes > 0 && hits > 0 && events > 0, $"moves {moves} shapes {shapes} hits {hits} events {events}");
        if (fighters == 4) Assert.True(deaths > 0 && perches > 0, $"deaths {deaths} perches {perches}");
    }

    [Fact]
    public void EveryReleasedGoldenSeeksAndStepsEveryTickThroughTheDisplayWorld()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "tests/Shared.Tests/Golden/Replays"), "*.slopreplay");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var replay = Open(File.ReadAllBytes(file), ReplayTrackChoice.Best);
            Assert.Equal(ReplayTrack.View, replay.Track);
            Assert.NotNull(replay.ExactRefusal);
            var probe = new Probe();
            for (uint tick = replay.Range.First; tick <= replay.Range.Last; tick++)
            {
                Assert.Null(replay.Seek(tick, probe));
                Assert.Equal(tick, probe.Frames[^1].View.Tick);
                Assert.Equal(tick, replay.World.CurrentTick);
            }
            Assert.Null(replay.Seek(replay.Range.First, probe));
            int steps = 0;
            ReplayStepResult result;
            while ((result = replay.Step(probe)).Status == ReplayStep.Presented) steps++;
            Assert.Null(result.Failure);
            Assert.Equal((int)(replay.Range.Last - replay.Range.First), steps);
        }
    }

    [Fact]
    public void SeekPresentsTheHalfSecondBeforeTheTargetAsWarmup()
    {
        using var replay = Open(Duel.Value.Bytes, ReplayTrackChoice.ViewOnly);
        var probe = new Probe();
        uint target = replay.Range.First + 120;
        Assert.Null(replay.Seek(target, probe));
        Assert.Equal(new[] { target }, probe.Resets);
        Assert.Equal(Enumerable.Range(0, 31).Select(i => (uint)(30 - i)), probe.Frames.Select(f => f.TicksBeforeTarget));
        Assert.All(probe.Frames, f => Assert.Equal(ReplayDelivery.Warmup, f.Delivery));
        Assert.Equal(ReplayStep.Presented, replay.Step(probe).Status);
        Assert.Equal(ReplayDelivery.Live, probe.Frames[^1].Delivery);
        Assert.Equal(target + 1, probe.Frames[^1].View.Tick);
    }

    [Theory]
    [InlineData(ReplayTrackChoice.ViewOnly)]
    [InlineData(ReplayTrackChoice.Best)]
    public void DisplayWorldNeverTicks(ReplayTrackChoice choice)
    {
        using var replay = Open(Duel.Value.Bytes, choice);
        Assert.Null(replay.Seek(replay.Range.First + 10, new Probe()));
        Assert.Throws<InvalidOperationException>(() => replay.World.Tick(new()));
    }

    /// <summary>Rewrites a recording with a changed header and View records, as an older build's file would look.</summary>
    static byte[] Doctor(Recording recording, Func<ReplayHeader, ReplayHeader> header, Action<ReplayViewTick> view)
    {
        using var reader = ReplayReader.Open(new MemoryStream(recording.Bytes)).Reader!;
        var doctored = header(reader.Header);
        var binding = ReplayStorage.Preamble(doctored);
        using var output = new MemoryStream();
        using var writer = new ReplayWriter(output, doctored);
        for (uint tick = reader.Range!.Value.First; tick <= reader.Range.Value.Last; tick++)
        {
            var v = reader.ReadView(tick).Tick!;
            view(v);
            Assert.Equal(ReplayAppend.BufferedOrCommitted, writer.Append(new ReplayTickCapture(reader.ReadExact(tick), v, binding)));
        }
        writer.Complete(reader.Conclusion!);
        return output.ToArray();
    }

    [Fact]
    public void MissingPackagePlaysWithAStandInAndNeutralPoses()
    {
        var recording = Duel.Value;
        var bytes = Doctor(recording,
            h => h with { Roster = h.Roster.Select(p => p.EntityId == 2 ? p with { Package = p.Package with { PackageId = "ghost" } } : p).ToArray() },
            v =>
            {
                foreach (var f in v.Fighters.Where(f => f.EntityId == 2)) f.PackageId = "ghost";
                foreach (var s in v.Shapes.Where(s => s.OwnerId == 2)) s.PackageId = "ghost";
            });
        using var replay = Open(bytes, ReplayTrackChoice.Best);
        Assert.Equal(ReplayTrack.View, replay.Track);
        Assert.Equal(ReplayFailureCode.PackageUnavailable, replay.ExactRefusal!.Code);
        var standIn = replay.Cast.Single(m => m.EntityId == 2);
        Assert.Equal(ReplayCastKind.StandIn, standIn.Kind);
        Assert.Equal("fightguy", standIn.Content.Identity.PackageId);
        Assert.Equal(ReplayCastKind.Recorded, replay.Cast.Single(m => m.EntityId == 1).Kind);
        int missing = 0;
        var probe = new Probe
        {
            OnPresent = frame =>
            {
                var recorded = frame.View.Fighters.Single(f => f.EntityId == 2);
                var state = frame.World.GetState(2);
                Assert.Equal(recorded.Position.X, state.PX);
                Assert.Null(frame.World.GetActiveMove(2));
                Assert.NotEqual(ActionState.Attacking, state.State);
                if (frame.Moves.Single(m => m.EntityId == 2).Match == ReplayMoveMatch.Missing) missing++;
            }
        };
        while (replay.Step(probe).Status == ReplayStep.Presented) { }
        Assert.True(missing > 0);
    }

    [Fact]
    public void RenamedMoveResolvesBySlotAndRemovedMoveIsMissingAndIdle()
    {
        var recording = Duel.Value;
        var bytes = Doctor(recording, h => h, v =>
        {
            foreach (var f in v.Fighters.Where(f => f.SlotId != ""))
                if (f.EntityId == 1) f.MoveName = "Renamed Since";
                else { f.MoveName = "Removed Since"; f.SlotId = "ground.Z"; }
        });
        using var replay = Open(bytes, ReplayTrackChoice.ViewOnly);
        int bySlot = 0, missing = 0;
        var probe = new Probe
        {
            OnPresent = frame =>
            {
                foreach (var label in frame.Moves)
                {
                    var recorded = frame.View.Fighters.Single(f => f.EntityId == label.EntityId);
                    if (recorded.SlotId == "") { Assert.Equal(ReplayMoveMatch.None, label.Match); continue; }
                    Assert.Equal(recorded.MoveName, label.RecordedName);
                    if (label.EntityId == 1)
                    {
                        Assert.Equal(ReplayMoveMatch.BySlot, label.Match);
                        Assert.Equal(recorded.SlotId, frame.World.GetActiveMove(1)!.Value.Slot.Id);
                        bySlot++;
                    }
                    else
                    {
                        Assert.Equal(ReplayMoveMatch.Missing, label.Match);
                        Assert.Null(frame.World.GetActiveMove(2));
                        Assert.Equal(0, frame.World.GetState(2).AttackSlot);
                        Assert.False(frame.World.GetState(2).State is ActionState.Attacking or ActionState.Aiming);
                        missing++;
                    }
                }
            }
        };
        while (replay.Step(probe).Status == ReplayStep.Presented) { }
        Assert.True(bySlot > 0 && missing > 0, $"bySlot {bySlot} missing {missing}");
    }

    [Fact]
    public void RecordedPoseClockIsWhatTheDisplayWorldShows()
    {
        var bytes = Doctor(Duel.Value, h => h, v =>
        {
            foreach (var f in v.Fighters.Where(f => f.SlotId != "")) { f.PoseElapsedTicks = 3.5f; f.PoseDurationTicks = 99; }
        });
        using var replay = Open(bytes, ReplayTrackChoice.ViewOnly);
        int checkedMoves = 0;
        var probe = new Probe
        {
            OnPresent = frame =>
            {
                foreach (var f in frame.View.Fighters.Where(f => f.SlotId != ""))
                {
                    var pose = frame.World.GetActiveMove(f.EntityId)!.Value.Pose;
                    Assert.Equal((f.PoseAnimationId, 3.5f, (ushort)99), (pose.AnimationId, pose.ElapsedTicks, pose.DurationTicks));
                    checkedMoves++;
                }
            }
        };
        while (replay.Step(probe).Status == ReplayStep.Presented) { }
        Assert.True(checkedMoves > 0);
    }

    [Fact]
    public void TruncatedAndCorruptFilesNeverThrow()
    {
        var bytes = Duel.Value.Bytes;
        var lengths = Enumerable.Range(0, 64).Select(i => bytes.Length * i / 64).Append(bytes.Length - 1).Append(24).Append(0);
        foreach (int length in lengths)
        {
            var cut = bytes[..length];
            var opened = ReplayWorld.Open(new MemoryStream(cut), Content(), ReplayTrackChoice.Best);
            if (opened.World is not { } replay) { Assert.NotNull(opened.Failure); continue; }
            using (replay)
            {
                var probe = new Probe();
                while (replay.Step(probe).Status == ReplayStep.Presented) { }
                Assert.Null(replay.Seek(replay.Range.Last, probe));
            }
        }
        var flipped = bytes.ToArray();
        flipped[flipped.Length / 2] ^= 0xff;
        var corrupt = ReplayWorld.Open(new MemoryStream(flipped), Content(), ReplayTrackChoice.Best);
        Assert.True(corrupt.World != null || corrupt.Failure != null);
        corrupt.World?.Dispose();
    }

    [Fact]
    public void MissingArenaAndEmptyFilesAreTypedFailures()
    {
        var noArena = new ReplayContent(Roster.Value, _ => null, "fightguy");
        Assert.Equal(ReplayFailureCode.ArenaUnavailable, ReplayWorld.Open(new MemoryStream(Duel.Value.Bytes), noArena).Failure!.Code);
        Assert.Equal(ReplayFailureCode.InvalidHeader, ReplayWorld.Open(new MemoryStream(new byte[10]), Content()).Failure!.Code);
    }
}
