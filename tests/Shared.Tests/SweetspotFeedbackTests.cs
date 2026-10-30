using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Rising Dragon's sweetspot feedback (ADR-0053) through FightGuy's live package: a hitbox may name its own hit
/// presentation, and a timeline presentation may wait for a sweetspot hit (a confirmed hit from such a hitbox).
/// </summary>
public sealed class SweetspotFeedbackTests
{
    private const ulong Guy = 1, Victim = 2;
    private const string SweetHit = "presentation.fightguy.rising-dragon.sweet-hit";
    private const string SourHit = "presentation.fightguy.rising-dragon.hit";
    private const string Finger = "presentation.fightguy.rising-dragon.apex";
    private static readonly CharacterDefinition Def = TestHelpers.ResolveSourceDef("fightguy");
    private static readonly BakedAnimationData Poses = BuiltInContentResolver.Resolve(CharacterClass.FightGuy).BakedAnimation!;

    [Fact]
    public void ASweetspotHitPlaysTheGoldBurstAndTheFinger()
    {
        var events = Dragon(victimZ: 0.8f, walkIn: false, out var hits);
        var hit = Assert.Single(hits);
        Assert.Equal(14f, hit.Damage);
        Assert.Contains(events, e => e.Source == PresentationEventSource.HitContact && e.PresentationId == SweetHit);
        Assert.DoesNotContain(events, e => e.PresentationId == SourHit);
        Assert.Single(events, e => e.Source == PresentationEventSource.Timeline && e.PresentationId == Finger);
    }

    [Fact]
    public void ASourspotHitPlaysTheBlueHitAndNoFinger()
    {
        var events = Dragon(victimZ: 3.0f, walkIn: true, out var hits);
        var hit = Assert.Single(hits);
        Assert.Equal(6f, hit.Damage);
        Assert.Contains(events, e => e.Source == PresentationEventSource.HitContact && e.PresentationId == SourHit);
        Assert.DoesNotContain(events, e => e.PresentationId is SweetHit or Finger);
    }

    [Fact]
    public void AWhiffPlaysNoFinger()
    {
        var events = Dragon(victimZ: 10f, walkIn: false, out var hits);
        Assert.Empty(hits);
        Assert.Contains(events, e => e.PresentationId == "presentation.fightguy.rising-dragon.spiral");
        Assert.DoesNotContain(events, e => e.PresentationId == Finger);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(15)]
    public void RestoringAWorldSnapshotKeepsTheSweetspotForTheFinger(int restoreAt)
    {
        var continuous = Make(0.8f);
        ServerSimulation? restored = null;
        var events = new List<TimelinePresentationEvent>();
        for (int tick = 0; tick < 40; tick++)
        {
            if (tick == restoreAt)
            {
                restored = Make(0.8f);
                restored.RestoreSnapshot(continuous.CaptureSnapshot());
            }
            var inputs = Inputs(tick, walkIn: false);
            continuous.Tick(inputs);
            continuous.ClearPresentationEvents();
            if (restored == null) continue;
            restored.Tick(Inputs(tick, walkIn: false));
            events.AddRange(restored.GetPresentationEvents(clear: true));
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"diverged at tick {tick}");
        }
        Assert.Single(events, e => e.PresentationId == Finger);
    }

    [Fact]
    public void PackagesWithoutTheFieldsCookWithoutThemAndKeepTheirRuntimeApi()
    {
        foreach (var id in new[] { "bashie", "wibou", "manki" })
        {
            var package = Compile(id);
            Assert.Empty(CookedObjects(package, "radius", "hitPresentationId"));
            Assert.Empty(CookedObjects(package, "operationIndex", "condition"));
            Assert.NotEqual("1.17.0", package.Metadata.RuntimeApiMin);
        }
        // The cooked grid holds the ground Dragon and its air alias.
        var fightguy = Compile("fightguy");
        Assert.All(CookedObjects(fightguy, "radius", "hitPresentationId"), x => Assert.Equal(SweetHit, x.GetString()));
        Assert.Equal(2 * 3, CookedObjects(fightguy, "radius", "hitPresentationId").Count);
        Assert.Equal(2, CookedObjects(fightguy, "operationIndex", "condition").Count);
        // Title Match's hold stage (ADR-0057) raises his minimum past the sweetspot fields' 1.17.0.
        Assert.Equal("1.20.0", fightguy.Metadata.RuntimeApiMin);
    }

    /// <summary>The <paramref name="field"/> values of every cooked object that also has <paramref name="marker"/>.</summary>
    private static List<System.Text.Json.JsonElement> CookedObjects(CookedCharacterPackage package, string marker, string field)
    {
        var found = new List<System.Text.Json.JsonElement>();
        void Walk(System.Text.Json.JsonElement e)
        {
            if (e.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (e.TryGetProperty(marker, out _) && e.TryGetProperty(field, out var value)) found.Add(value.Clone());
                foreach (var p in e.EnumerateObject()) Walk(p.Value);
            }
            else if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in e.EnumerateArray()) Walk(item);
        }
        using var document = System.Text.Json.JsonDocument.Parse(package.CanonicalBytes);
        Walk(document.RootElement);
        return found;
    }

    [Fact]
    public void TheSourceCodecKeepsBothFieldsThroughASave()
    {
        string root = Path.Combine(TestPaths.Root, "client/Unity/Assets/CharacterPackages/fightguy");
        var loaded = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")));
        Assert.True(loaded.IsValid);
        string saved = CharacterPackageSourceCodec.SerializeCharacter(loaded.Source!.Character);
        var reloaded = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(root, "package.json")), saved);
        var dragon = reloaded.Source!.Character.Slots.Single(slot => slot.Id == "ground.E").Timeline.Stages[0].Operations;
        Assert.Equal(3, dragon.OfType<SpawnHitboxOperationSource>().Count(op => op.Hitbox.HitPresentationId == SweetHit));
        Assert.Equal(AuthoringPresentationCondition.SweetspotHit,
            dragon.OfType<EmitPresentationOperationSource>().Single(op => op.PresentationId == Finger).Condition);
    }

    [Fact]
    public void AnUndeclaredHitPresentationIsRejected()
    {
        string root = Path.Combine(TestPaths.Root, "client/Unity/Assets/CharacterPackages/fightguy");
        string character = File.ReadAllText(Path.Combine(root, "character.json"))
            .Replace($"\"hitPresentationId\": \"{SweetHit}\"", "\"hitPresentationId\": \"presentation.fightguy.nope\"");
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")), character,
            CharacterCookProfile.TrustedBuiltIn);
        Assert.Contains(compiled.Diagnostics, d => d.Code == "reference.unresolved" && d.Severity == CharacterDiagnosticSeverity.Error);
    }

    private static CookedCharacterPackage Compile(string id)
    {
        string root = Path.Combine(TestPaths.Root, "client/Unity/Assets/CharacterPackages", id);
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.CookedPackage != null, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        return compiled.CookedPackage!;
    }

    private static List<TimelinePresentationEvent> Dragon(float victimZ, bool walkIn, out List<SpellResolver.HitResult> hits)
    {
        var sim = Make(victimZ);
        var events = new List<TimelinePresentationEvent>();
        hits = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(Inputs(tick, walkIn && hits.Count == 0));
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == Guy && h.TargetEntityId == Victim));
            events.AddRange(sim.GetPresentationEvents(clear: true));
        }
        return events;
    }

    private static ServerSimulation Make(float victimZ)
    {
        var sim = new ServerSimulation(BashieShellTests.Floor());
        float y = TestHelpers.GroundPY(Def);
        sim.RegisterEntity(Guy, Def, new CharacterState { EntityId = Guy, PY = y, State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2 }, Poses);
        sim.RegisterEntity(Victim, Def, new CharacterState
        {
            EntityId = Victim, PY = y, PZ = victimZ, FacingYaw = MathF.PI, State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2,
        }, Poses);
        return sim;
    }

    private static Dictionary<ulong, InputState> Inputs(int tick, bool walkIn) => new()
    {
        [Guy] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.E : (byte)0, TargetEntityId = (byte)Victim },
        [Victim] = walkIn ? new InputState { MoveY = -1f } : default,
    };
}
