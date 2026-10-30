using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FacingHitboxTests
{
    private static readonly List<SpellResolver.EntityData> Targets = new()
    {
        new() { Id = 100, PosZ = 1, EndZ = 1, Radius = 0.5f, Active = true },
    };

    private static SpellResolver Resolver(bool filter)
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox
        {
            OwnerId = 1, Radius = 3, Damage = 1, DurationTicks = 10, HitsMultipleOpponents = true,
            SourceEvent = new HitboxEvent { RequiresFacingOwner = filter },
        });
        return resolver;
    }

    [Theory]
    [InlineData(true, 180, true)]
    [InlineData(true, 120, true)]
    [InlineData(true, 60, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 0, true)]
    public void OnlyOptedInHitboxesRequireTheVictimToFaceTheOwner(bool filter, float yaw, bool hits)
    {
        var fighters = new Dictionary<ulong, CharacterState>
        {
            [1] = new(),
            [100] = new() { PZ = 1, FacingYaw = yaw * MathF.PI / 180f },
        };
        Assert.Equal(hits ? 1 : 0, Resolver(filter).Tick(Targets, fighters: fighters).Count);
    }

    [Fact]
    public void TurningTowardTheOwnerDuringTheWindowStillHitsOnce()
    {
        var resolver = Resolver(true);
        var fighters = new Dictionary<ulong, CharacterState> { [1] = new(), [100] = new() { PZ = 1 } };
        Assert.Empty(resolver.Tick(Targets, fighters: fighters));
        fighters[100] = new() { PZ = 1, FacingYaw = MathF.PI };
        Assert.Single(resolver.Tick(Targets, fighters: fighters));
        Assert.Empty(resolver.Tick(Targets, fighters: fighters));
    }

    [Fact]
    public void TheFilterDoesNotChangeNonFighterContacts()
    {
        Assert.Single(Resolver(true).Tick(Targets, fighters: new Dictionary<ulong, CharacterState>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotRoundTripKeepsTheFilterAndItsContactBehavior(bool facing)
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var sim = FormsFixture.Sim(def, 1);
        var dummy = sim.GetState(100);
        dummy.PZ = FormsFixture.Z + 2.8f;
        dummy.FacingYaw = facing ? MathF.PI : 0;
        sim.SetState(100, dummy);
        FormsFixture.Step(sim, new InputState { ActiveSlot = AbilitySlots.A, TargetEntityId = 100 });
        for (int i = 0; i < 20 && !sim.Resolver.GetActiveHitboxes().Any(h => h.SourceEvent.RequiresFacingOwner); i++) FormsFixture.Step(sim);
        Assert.Contains(sim.Resolver.GetActiveHitboxes(), h => h.SourceEvent.RequiresFacingOwner);
        var copy = FormsFixture.Sim(def, 1);
        copy.RestoreSnapshot(sim.CaptureSnapshot());
        bool connected = sim.GetState(100).HitstunTicks > 0;
        Assert.True(sim.CaptureSnapshot().ContentEquals(copy.CaptureSnapshot()));
        for (int i = 0; i < 35; i++)
        {
            FormsFixture.Step(sim);
            FormsFixture.Step(copy);
            Assert.True(sim.CaptureSnapshot().ContentEquals(copy.CaptureSnapshot()));
            connected |= sim.GetState(100).HitstunTicks > 0;
        }
        Assert.Equal(facing, connected);
    }

    [Fact]
    public void FacingFilterRoundTripsSourceCookAdmissionAndRejectsAnOlderApi()
    {
        var source = CharacterPackageSourceCodec.CreateAuthoringReady("facing-fixture", "Facing fixture", "test", "MIT", "test");
        var hit = new HitboxSource(AuthoringHitboxShape.Sphere, 1, 0, 0, 1, 0, 0, 0, null, null,
            1, 30, 1, 0, 12, 1, true, 0, RequiresFacingOwner: true);
        var edit = CharacterPackageSourceCodec.AddOperation(source, 0, 0, new SpawnHitboxOperationSource(0, AuthoringUnit.Meters, hit));
        Assert.True(edit.IsValid);
        var parsed = CharacterPackageSourceCodec.Load(CharacterPackageSourceCodec.SerializeManifest(edit.Source!.Manifest),
            CharacterPackageSourceCodec.SerializeCharacter(edit.Source.Character));
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Diagnostics));
        var compiled = CharacterPackageCompiler.Compile(parsed.Source!);
        Assert.NotNull(compiled.CookedPackage);
        Assert.Equal("1.19.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var assembly = CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(compiled.CookedPackage));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics));
        var admitted = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(admitted.IsValid, string.Join("; ", admitted.Diagnostics));
        var evt = admitted.ToCharacterDefinition().GetCookedSlotAbility(AbilitySlots.Slot1, false)!.Timeline.Stages[0].ReadHitboxes().Single();
        Assert.True(evt.RequiresFacingOwner);
        var older = new CookedCharacterPackage(compiled.CookedPackage.Metadata with { RuntimeApiMin = "1.18.0" },
            compiled.CookedPackage.Definition, compiled.CookedPackage.Budget, compiled.CookedPackage.Diagnostics,
            System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(compiled.CookedPackage.CanonicalBytes).Replace("1.19.0", "1.18.0")));
        var rejected = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(older)));
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, d => d.Code == "package.compatibility.unsupported");
    }
}

public sealed class NyxCpuTests
{
    private static BotMemory Memory(CpuDifficulty difficulty, CharacterState target)
    {
        var memory = new BotMemory { Difficulty = difficulty };
        for (int i = 0; i < 30; i++) memory.ObserveOpponent(target);
        return memory;
    }

    [Theory]
    [InlineData(CpuDifficulty.Easy, 0, 1f, true)]
    [InlineData(CpuDifficulty.Normal, 0, 1f, true)]
    [InlineData(CpuDifficulty.Hard, 1, 8f, true)]
    [InlineData(CpuDifficulty.Normal, 0, 8f, false)]
    [InlineData(CpuDifficulty.Normal, 1, 1f, false)]
    public void RangeSelectsTheFormWhoseReachFits(CpuDifficulty difficulty, byte form, float distance, bool swap)
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var self = TestHelpers.PlayerState(0, 0); self.Form.Active = form;
        var target = TestHelpers.NpcState(0, distance);
        var input = new HeuristicBotPolicy().Decide(self, def, new Random(7), Memory(difficulty, target));
        Assert.Equal(swap, input.ActiveSlot == AbilitySlots.F);
    }

    [Theory]
    [InlineData(CpuDifficulty.Easy, false)]
    [InlineData(CpuDifficulty.Normal, true)]
    [InlineData(CpuDifficulty.Hard, true)]
    public void OnlyNormalAndHardCancelAConfirmedHitBeforeIasa(CpuDifficulty difficulty, bool cancel)
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var self = TestHelpers.PlayerState(0, 0);
        self.State = ActionState.Attacking; self.AttackSlot = AbilitySlots.Slot1; self.AnimLockTicks = 30;
        var target = TestHelpers.NpcState(0, 1);
        var memory = Memory(difficulty, target);
        memory.RecordOpponentContact(AbilitySlots.Slot1, CpuContactKind.Hit, target with { HitstunTicks = 30 }, 0);
        for (int i = 0; i < 30; i++) memory.ObserveOpponent(target);
        var input = new HeuristicBotPolicy().Decide(self, def, new Random(7), memory, default(ArenaDefinition),
            activeMove: def.GetCookedSlotAbility(AbilitySlots.Slot1, false), canSwapCancel: true);
        Assert.Equal(cancel, input.ActiveSlot == AbilitySlots.F);
    }

    [Theory]
    [InlineData("whiff")]
    [InlineData("cooldown")]
    [InlineData("hitstop")]
    [InlineData("hitstun")]
    [InlineData("blockstun")]
    [InlineData("landing")]
    public void CancelKeepsTheAdmissionLocks(string gate)
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var self = TestHelpers.PlayerState(0, 0);
        self.State = ActionState.Attacking; self.AttackSlot = AbilitySlots.Slot1; self.AnimLockTicks = 30;
        if (gate == "cooldown") self.SetCooldown(AbilitySlots.F, 60);
        if (gate == "hitstop") self.HitstopTicks = 5;
        if (gate == "hitstun") self.HitstunTicks = 5;
        if (gate == "blockstun") self.BlockStunTicks = 5;
        if (gate == "landing") self.LandingLagTicks = 5;
        var target = TestHelpers.NpcState(0, 1);
        var memory = Memory(CpuDifficulty.Hard, target);
        memory.RecordOpponentContact(AbilitySlots.Slot1, CpuContactKind.Hit, target with { HitstunTicks = 30 }, 0);
        for (int i = 0; i < 30; i++) memory.ObserveOpponent(target);
        var input = new HeuristicBotPolicy().Decide(self, def, new Random(7), memory, default(ArenaDefinition),
            activeMove: def.GetCookedSlotAbility(AbilitySlots.Slot1, false), canSwapCancel: gate != "whiff");
        Assert.NotEqual(AbilitySlots.F, input.ActiveSlot);
    }

    [Fact]
    public void FakeFormUsesRingLightRiseToRecoverWithoutJumps()
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var floor = new float[121];
        var arena = TestHelpers.TestArena();
        arena.MinX = arena.MinZ = -5; arena.MaxX = arena.MaxZ = 5;
        arena.Heightmap = new ArenaHeightmap { Data = floor, Width = 11, Height = 11,
            CellSize = 1, OriginX = -5, OriginZ = -5 };
        var self = TestHelpers.PlayerState(6, 0);
        self.Form.Active = 1; self.PY = 3; self.VY = -2; self.IsGrounded = false; self.JumpsLeft = 0;
        var target = TestHelpers.NpcState(0, 0);
        var input = new HeuristicBotPolicy().Decide(self, def, new Random(7), Memory(CpuDifficulty.Hard, target), arena);
        Assert.Equal(AbilitySlots.E, input.ActiveSlot);
        Assert.True(input.MoveX < 0);
    }

    [Theory]
    [InlineData((byte)0, true)]
    [InlineData((byte)1, false)]
    public void OpposingCpuReadsTheCurrentFormsThreatReach(byte form, bool shields)
    {
        var nyx = TestHelpers.ResolveSourceDef("nyx");
        var self = TestHelpers.PlayerState(0, 0); self.State = ActionState.Shielding;
        var target = TestHelpers.NpcState(0, 3.5f);
        target.State = ActionState.Attacking; target.AttackSlot = AbilitySlots.Slot4; target.Form.Active = form;
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        for (int i = 0; i < 30; i++) policy.ObserveOpponent(memory, target, nyx);
        var input = policy.Decide(self, TestHelpers.EngineDef, new Random(7), memory);
        Assert.Equal(shields, input.ShieldHeld);
    }

    [Fact]
    public void SelfieGrabIsAnAvailableShieldPunishAndBothRangedSpecialsAreUsed()
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var self = TestHelpers.PlayerState(0, 0); self.Form.Active = 1; self.SetCooldown(AbilitySlots.F, 300);
        var target = TestHelpers.NpcState(0, 1.5f); target.State = ActionState.Shielding;
        var policy = new HeuristicBotPolicy();
        bool grabbed = false;
        for (int seed = 0; seed < 100; seed++)
            grabbed |= policy.Decide(self, def, new Random(seed), Memory(CpuDifficulty.Hard, target)).ActiveSlot == AbilitySlots.R;
        Assert.True(grabbed);
        self.Form.Active = 0; target.PZ = 6; target.State = ActionState.Idle;
        var used = new HashSet<byte>();
        for (int seed = 0; seed < 100; seed++)
        {
            var memory = Memory(CpuDifficulty.Hard, target); var rng = new Random(seed);
            for (int tick = 0; tick < 30; tick++)
            {
                policy.ObserveOpponent(memory, target, def);
                var input = policy.Decide(self, def, rng, memory);
                if (input.ActiveSlot == 0) continue;
                used.Add(input.ActiveSlot); break;
            }
        }
        Assert.Contains(AbilitySlots.A, used);
        Assert.Contains(AbilitySlots.R, used);
    }
}
