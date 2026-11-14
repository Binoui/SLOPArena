using SlopArena.Shared.AI;
using SlopArena.Shared.Replays;
using SlopArena.ReplayReport;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests.Replays;

public sealed class ReplayReportTests(ITestOutputHelper output)
{
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void GoldenHasKnownMoveAndEndingCounts()
    {
        var report = PatternReport.Read(Path.Combine(Root, "tests/Shared.Tests/Golden/Replays"));
        Assert.Empty(report.Failures);
        Assert.Equal(1, report.Files);
        Assert.Equal(0.05, report.Minutes, 6);
        Assert.Equal(3, report.Moves.Sum(m => m.Activations));
        Assert.Equal(new[] { "Cyclone Kick", "Jetpack Flight", "Ki Shot" }, report.Moves.Select(m => m.Key.Move).Order().ToArray());
        Assert.All(report.Moves, m => { Assert.Equal(1, m.Activations); Assert.Equal(0, m.Landed); });
        Assert.Empty(report.KOs);
        Assert.Equal("cancelled", Assert.Single(report.Endings).Reason);
        Assert.Equal(3, report.Endings[0].Seconds);
    }

    [Fact]
    public void RecordedCpuFightHasKnownMovesKOsAndEnding()
    {
        string dir = Path.Combine(Path.GetTempPath(), "replay-report-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var entries = new[] { BuiltInContentResolver.ResolvePackage("fightguy"), BuiltInContentResolver.ResolvePackage("manki") };
            var catalog = new MatchContentCatalog(entries);
            var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data/arenas/slop_pit.arena"))!.Value;
            var roster = entries.Select((e,i) => new ReplayParticipant((ulong)i+1, "cpu", i.ToString(), e.Identity, "base", "", "", i == 0 ? "Binoui" : "")).ToArray();
            var header = new ReplayHeader("combat", arena.Name, new("stock", "off", 250, false), 1, roster, ReplayBuildIdentity.Current("test", "test"));
            var sim = new ServerSimulation(arena, MatchFormat.Stock.CreateRule(250, FriendlyFireMode.Off));
            for (int i = 0; i < 2; i++)
            {
                var spawn = arena.SpawnPoints[i];
                var state = new CharacterState { EntityId = (ulong)i+1, PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, IsGrounded = true, JumpsLeft = entries[i].Definition.Movement.MaxJumps, AirDodgesLeft = 1, DamagePercent = 300 };
                sim.RegisterEntity(state.EntityId, entries[i].Definition, state, entries[i].BakedAnimation);
            }
            var capture = new ReplayCapture(header, catalog, arena);
            var policy = new HeuristicBotPolicy(); var rng = new Random(7);
            var memories = new[] { new BotMemory { Difficulty = CpuDifficulty.Normal }, new BotMemory { Difficulty = CpuDifficulty.Normal } };

            using (var stream = File.Create(Path.Combine(dir, "cpu.slopreplay")))
            using (var writer = new ReplayWriter(stream, header))
            {
                for (int tick = 0; tick < 3600; tick++)
                {
                    var inputs = new Dictionary<ulong, InputState>();
                    for (int i = 0; i < 2; i++)
                    {
                        ulong id = (ulong)i+1, other = (ulong)(1-i)+1;
                        policy.ObserveOpponent(memories[i], sim.GetState(other), sim.GetDefinition(other), entries[1-i].BakedAnimation, sim.GetActiveMove(other)?.Slot);
                        memories[i].ChooseOpponent(sim.GetState(id));
                        inputs[id] = policy.Decide(sim.GetState(id), sim.GetDefinition(id), rng, memories[i], arena, entries[i].BakedAnimation, activeMove: sim.GetActiveMove(id)?.Slot);
                    }
                    sim.Tick(inputs);

                    var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
                        sim.GetPresentationEvents(true).ToArray(), sim.LastTickHits);
                    Assert.Equal(ReplayAppend.BufferedOrCommitted, writer.Append(capture.Capture(sim, frame).Capture!));
                }
                writer.Complete(new(ReplayEndReason.Finished, "fixture", new ReplayMatchResult(new ulong[] { 1 }, "", false,
                    Array.Empty<ReplayStanding>(), 0, 0, 0, "")));
            }
            var report = PatternReport.Read(dir);
            Assert.Empty(report.Failures);
            output.WriteLine($"moves={report.Moves.Sum(m => m.Activations)} hits={report.Moves.Sum(m => m.Landed)} blocked={report.Moves.Sum(m => m.Blocked)} damage={report.Moves.Sum(m => m.Damage)} kos={report.KOs.Count}");
            Assert.Equal(1, report.Files);
            Assert.Equal(1, report.Minutes, 6);
            Assert.Equal(53, report.Moves.Sum(m => m.Activations));
            Assert.Equal(60, report.Moves.Sum(m => m.Landed));
            Assert.Equal(4, report.Moves.Sum(m => m.Blocked));
            Assert.Equal(299, report.Moves.Sum(m => m.Damage));
            Assert.Equal(3, report.KOs.Count);
            Assert.All(report.KOs, ko => { Assert.NotEqual(default, ko.Position); Assert.NotEmpty(ko.Victim); });
            Assert.Equal("finished", Assert.Single(report.Endings).Reason);
            Assert.Single(report.Endings[0].Winners);
            Assert.Contains("[Binoui]", report.Endings[0].Participants[0]);
            Assert.DoesNotContain("[", report.Endings[0].Participants[1]);
            Assert.Contains("1/1", report.Markdown());
            Assert.Contains("0/1", report.Markdown());
            // The .part suffix does not invent a trailer: a crash is reported as partial.
            var bytes = File.ReadAllBytes(Path.Combine(dir, "cpu.slopreplay"));
            File.WriteAllBytes(Path.Combine(dir, "crash.slopreplay.part"), bytes[..^10]);
            File.WriteAllText(Path.Combine(dir, "invalid.slopreplay"), "bad magic");
            var mixed = PatternReport.Read(dir);
            Assert.Single(mixed.Failures);
            Assert.Contains(mixed.Endings, e => e.Reason == "partial");
        }
        finally { Directory.Delete(dir, true); }
    }
}
