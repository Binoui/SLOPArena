using SlopArena.Client.UI;
using SlopArena.Client.Workshop;
using SlopArena.Shared.AI;
using SlopArena.Shared.Replays;
using SlopArena.Shared.Telemetry;
using SlopArena.Shared.Tests.Replays;
using Xunit;

namespace SlopArena.Shared.Tests;

public partial class PackageFighterContentTests
{
    [Theory]
    [InlineData(GameMode.Arcade, LocalPlayMode.CharacterCourse, 1)]
    [InlineData(GameMode.Arcade, LocalPlayMode.CharacterCourse, 2)]
    [InlineData(GameMode.Arcade, LocalPlayMode.CharacterCourse, 3)]
    [InlineData(GameMode.Arcade, LocalPlayMode.CharacterCourse, 4)]
    [InlineData(GameMode.Solo, LocalPlayMode.MankiMayhem, 3)]
    public void RestrictedModesRefuseMadeFightersAtResolution(GameMode mode, LocalPlayMode localMode, int seat)
    {
        var library = Library(Local());
        var player = MatchConfig.Selection; var seats = MatchConfig.SoloSeats.ToArray();
        var oldMode = MatchConfig.Mode; var oldLocal = MatchConfig.LocalMode;
        try
        {
            MatchConfig.Mode = mode; MatchConfig.LocalMode = localMode;
            MatchConfig.SetPlayerFighter(library.Choose(seat == 1 ? "local.tumbleweed" : "fightguy"));
            for (int number = 2; number <= 4; number++)
            {
                MatchConfig.SoloSeats[number - 2].Kind = SeatKind.LocalPlayer;
                MatchConfig.SoloSeats[number - 2].SetFighter(library.Choose(seat == number ? "local.tumbleweed" : "fightguy"));
            }
            var stage = new ArcadeStageDefinition("fixture", 1, new[] { "fightguy" }, new ArcadeBallDefinition(default, default, 0.5f, 9f, 0.5f, 1f));
            Assert.False(MatchConfig.TryResolveLocalChoices(library, out _, out var failure, modeStage: stage));
            Assert.Contains("content.mode.fighter", failure);
            MatchConfig.ResetUnavailableSeats(library, MatchConfig.VisibleFighters(library, stage));
            Assert.Equal("fightguy", MatchConfig.PlayerPackageId);
            Assert.All(MatchConfig.SoloSeats, s => Assert.Equal("fightguy", s.Fighter));
            Assert.True(MatchConfig.TryResolveLocalChoices(library, out _, out failure, modeStage: stage), failure);
        }
        finally { MatchConfig.SetPlayerFighter(player); Array.Copy(seats, MatchConfig.SoloSeats, 3); MatchConfig.Mode = oldMode; MatchConfig.LocalMode = oldLocal; }
    }
    [Fact]
    public void RefreshResetsPlayerOneEvenWhenAnotherSeatIsBeingEdited()
    {
        var before = Library(Local()); var after = Library(Bundled());
        var player = MatchConfig.Selection; var seats = MatchConfig.SoloSeats.ToArray();
        try
        {
            MatchConfig.SetPlayerFighter(before.Choose("local.tumbleweed"));
            MatchConfig.SoloSeats[1].Kind = SeatKind.Cpu;
            MatchConfig.SoloSeats[1].SetFighter(before.Choose("local.tumbleweed"));
            MatchConfig.ResetUnavailableSeats(after, after.PackageIds);
            Assert.Contains(MatchConfig.PlayerPackageId, after.PackageIds);
            Assert.All(MatchConfig.SoloSeats, s => Assert.Contains(s.Fighter, after.PackageIds));
        }
        finally { MatchConfig.SetPlayerFighter(player); Array.Copy(seats, MatchConfig.SoloSeats, 3); }
    }
    [Fact]
    public void PublishingASnapshotDoesNotReadBuiltInPackagesAgain()
    {
        int reads = 0; var stamp = new object();
        var cache = new LocalFighterLibrary.SourceCache(() => { reads++; return Library(Bundled()); }, () => stamp);
        var before = cache.Read(); var fixture = Fixture();
        cache.Publish(before.WithMadeFighters(new[] { AdmittedFighter.TestFixture(fixture.Files, fixture.Requirement) }));
        Assert.True(cache.Read().Contains("local.tumbleweed")); Assert.Equal(1, reads);
        cache.Publish(cache.Read().WithMadeFighters(Array.Empty<AdmittedFighter>()));
        Assert.False(cache.Read().Contains("local.tumbleweed")); Assert.Equal(1, reads);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReplayOfRemovedOrUpdatedMadeFighterReturnsNamedFailure(bool update)
    {
        var catalog = Local(); var content = ReplayWorldTests.Content(catalog); var arena = content.LoadArena("slop_pit")!;
        var picked = new[] { catalog.ResolvePackage("local.tumbleweed")!, catalog.ResolvePackage("fightguy")! };
        var roster = picked.Select((e, i) => new ReplayParticipant((ulong)i + 1, "cpu", "", e.Identity, "base", "", "", e.DisplayName)).ToArray();
        var header = new ReplayHeader("made", arena.Definition.Name, new("stock", "off", 3, false), 1, roster, ReplayBuildIdentity.Current("test", "pending"), arena.Digest).WithLocalContent(catalog);
        var sim = new ServerSimulation(arena.Definition, MatchFormat.Stock.CreateRule(3));
        foreach (var (entry, i) in picked.Select((e, i) => (e, i))) sim.RegisterEntity((ulong)i + 1, entry.Definition, new CharacterState { EntityId = (ulong)i + 1 }, entry.BakedAnimation);
        sim.Tick(new Dictionary<ulong, InputState>());
        var tick = new ReplayCapture(header, catalog, arena.Definition).Capture(sim, new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>()));
        using var bytes = new MemoryStream(); using (var writer = new ReplayWriter(bytes, header)) { writer.Append(tick.Capture!); writer.Complete(new(ReplayEndReason.Cancelled, "test", null)); }
        var today = Library(Bundled());
        if (update) { var f = Fixture("local.tumbleweed", "99.0.0"); today = today.WithMadeFighters(new[] { AdmittedFighter.TestFixture(f.Files, f.Requirement) }); }
        today.TryGetCatalog(out var current, out _);
        var result = ReplayWorld.Open(new MemoryStream(bytes.ToArray()), ReplayWorldTests.Content(current));
        Assert.Null(result.World); Assert.Equal(ReplayFailureCode.PackageUnavailable, result.Failure!.Code);
        Assert.Contains("local.tumbleweed", result.Failure.ToString());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CpuDegradesWithMissingSlotsCaptureAndForms(bool allSlotsMissing)
    {
        var entry = Local().ResolvePackage("local.tumbleweed")!;
        var def = entry.Definition with { CaptureGeometry = null, CookedSlots = allSlotsMissing ? Array.Empty<CookedSlotDefinition>() : entry.Definition.CookedSlots!.Take(1).ToArray() };
        var policy = new HeuristicBotPolicy(); var memory = new BotMemory(); var rng = new Random(52);
        var self = TestHelpers.PlayerState(); var target = TestHelpers.NpcState(0, 1);
        for (int tick = 0; tick < 240; tick++)
        {
            self.IsGrounded = tick % 2 == 0;
            var input = policy.Decide(self, target, def, rng, memory);
            if (input.ActiveSlot != 0) Assert.NotNull(def.GetCookedSlotAbility(input.ActiveSlot, !self.IsGrounded));
            Assert.False(input.GrabPressed);
        }
    }
}

public sealed class Unit5RevisionTests
{
    sealed class Lease : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    [Fact]
    public async Task SlowAdmissionKeepsOldSnapshotAliveUntilPublication()
    {
        using var set = new FighterRevisionSet<Lease>(); var old = new Lease();
        await set.Replace((_, next) => { next["a"] = old; return Task.CompletedTask; }, _ => { }, default);
        var first = new Lease(); var second = new Lease(); var blocked = new TaskCompletionSource();
        var refresh = set.Replace(async (_, next) => { next["a"] = first; await blocked.Task; next["b"] = second; }, next =>
        { Assert.False(old.Disposed); Assert.Equal(2, next.Count); }, default);
        Assert.False(old.Disposed); Assert.Same(old, set.Current["a"]); Assert.False(refresh.IsCompleted);
        blocked.SetResult(); await refresh; Assert.True(old.Disposed); Assert.False(first.Disposed);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FailedOrCancelledRefreshCannotPublishOrDestroyPreviousSnapshot(bool cancel)
    {
        using var set = new FighterRevisionSet<Lease>(); var old = new Lease(); var staged = new Lease();
        await set.Replace((_, n) => { n["a"] = old; return Task.CompletedTask; }, _ => { }, default);
        using var stop = new CancellationTokenSource();
        var operation = set.Replace((_, n) => { n["a"] = staged; if (cancel) stop.Cancel(); else throw new IOException("source stamp"); return Task.CompletedTask; }, _ => Assert.Fail("published"), stop.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => operation);
        Assert.False(old.Disposed); Assert.True(staged.Disposed); Assert.Same(old, set.Current["a"]);
    }
    [Fact]
    public void DownloadPollsUseBackoffAndResetOnSteamChange()
    {
        var retry = new WorkshopDownloadRetry();
        Assert.True(retry.Request(12, "install1", 0, false));
        Assert.False(retry.Request(12, "install1", 3, false));
        Assert.True(retry.Request(12, "install1", 6, false));
        Assert.False(retry.Request(12, "install1", 12, false));
        Assert.False(retry.Request(12, "install2", 13, true));
        Assert.True(retry.Request(12, "install2", 14, false));
        retry.Complete(12); Assert.True(retry.Request(12, "install2", 15, false));
    }
    [Fact]
    public void FirstSuccessfulSubmitSendsEveryFieldEvenAfterCreateSucceeded()
    {
        var retry = WorkshopPublishEdits.ForItem(false, "title", "desc", 2, "title", "desc", 2);
        retry.RequireFirstSubmit(false); Assert.Equal("title", retry.Title); Assert.Equal("desc", retry.Description); Assert.Equal(2, retry.Visibility);
        var later = WorkshopPublishEdits.ForItem(true, "title", "desc", 2, "title", "desc", 2);
        Assert.Null(later.Title); Assert.Null(later.Description); Assert.Null(later.Visibility);
        Assert.Throws<InvalidOperationException>(() => later.RequireFirstSubmit(false));
    }
    [Fact]
    public void UnchangedAssetsAreOmittedAndChangedAssetsAreExplicit()
    {
        Assert.False(WorkshopPublishEdits.AssetChanged("portrait", "portrait"));
        Assert.True(WorkshopPublishEdits.AssetChanged("portrait1", "portrait2"));
        Assert.False(WorkshopPublishEdits.AssetChanged("", "legacy"));
        var edits = new WorkshopPublishEdits(null, null, null).Assets(false, false);
        Assert.False(edits.UpdatePreview); Assert.False(edits.UpdateTags);
        edits.Assets(true, true); Assert.True(edits.UpdatePreview); Assert.True(edits.UpdateTags);
    }
    [Fact]
    public void MovedDeletedAndBrokenWorkspaceRecordsDoNotHideOtherSources()
    {
        string root = Path.Combine(Path.GetTempPath(), "unit5-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "workspace"); Directory.CreateDirectory(source);
            string caches = Path.Combine(root, "caches"); Directory.CreateDirectory(caches);
            void Cache(string key, string path) { string cache = Path.Combine(caches, key); Directory.CreateDirectory(cache); File.WriteAllText(Path.Combine(cache, "source.txt"), path); File.WriteAllText(Path.Combine(cache, "latest.txt"), "revision-123"); }
            Cache("before-move", source); Directory.Move(source, source + "-moved"); Cache("after-move", source + "-moved"); Cache("broken", "relative");
            var rows = FighterWorkspaceSources.Read(caches);
            Assert.Equal(3, rows.Count); Assert.Single(rows.Where(s => s.ItemFolder != null));
            Assert.Contains(rows, s => s.SourceFolder == source && s.Diagnostic!.Contains("moved or deleted"));
            Assert.Contains(rows, s => s.SourceFolder == source + "-moved" && s.Diagnostic == null);
            Directory.Delete(source + "-moved"); Assert.All(FighterWorkspaceSources.Read(caches), s => Assert.Null(s.ItemFolder));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void DuplicateWorkspaceDiagnosticNamesBothPathsAndTheWorkspaceToRename()
    {
        string message = FighterWorkspaceSources.DuplicateDiagnostic("local.tumbleweed", new[] { "/creator/B", "/creator/A" });
        Assert.Contains("/creator/A and /creator/B", message);
        Assert.Contains("Rename package.json.packageId in /creator/B", message);
    }
    [Fact]
    public void PracticeLogsRemainReadableWithoutInstalledFighterAssets()
    {
        using var bytes = new MemoryStream();
        var header = new SessionHeader { Fighters = new() { new() { EntityId = 1, PackageId = "workshop.3839108801", PackageVersion = "old", CookedContentHash = "old" } } };
        using (var writer = new SessionLogWriter(bytes, header)) writer.Complete(Array.Empty<SwingRecord>(), new SessionEnd { Reason = "left" });
        var read = SessionLogReader.Read(new MemoryStream(bytes.ToArray()));
        Assert.Equal("workshop.3839108801", read.Header.Fighters.Single().PackageId); Assert.False(read.Truncated);
    }
}
