using System.Collections.Generic;
using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public sealed class TelemetryProvenanceTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

    [Fact]
    public void Recorder_UsesActivationIdentityForOverlappingSameSlotHits()
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(Def);
        var target = TestHelpers.NpcState(z: 4f);
        target.PY = TestHelpers.GroundPY(Def);
        TestHelpers.RegisterPlayer(sim, Def, attacker);
        TestHelpers.RegisterNpc(sim, Def, target);

        var recorder = new SlopArena.Shared.AI.MatchRecorder();
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 },
            [100] = default,
        };
        var tickInputs = new Dictionary<ulong, InputState>
        {
            [1] = default,
            [100] = default,
        };

        recorder.RecordPresses(sim, 0, inputs, Def);
        sim.ActivateAbility(1, new ImmediateAbility(), slot: 2, def: Def);
        sim.Tick(tickInputs);
        recorder.RecordTick(sim, 0, tickInputs, Def);
        ulong firstActivation = sim.GetLastActivationId(1);

        recorder.RecordPresses(sim, 1, inputs, Def);
        sim.ActivateAbility(1, new ImmediateAbility(), slot: 2, def: Def);
        sim.Tick(tickInputs);
        recorder.RecordTick(sim, 1, tickInputs, Def);
        ulong secondActivation = sim.GetLastActivationId(1);

        Assert.NotEqual(firstActivation, secondActivation);

        var grounded = sim.GetState(1);
        grounded.IsGrounded = true;
        grounded.Deaths = 1;
        sim.SetState(1, grounded);
        sim.LastTickHits.Add(new SpellResolver.HitResult
        {
            OwnerEntityId = 1,
            TargetEntityId = 100,
            AttackSlot = AbilitySlots.Normal1,
            ActivationId = firstActivation,
            Airborne = true,
            Damage = 2f,
        });
        recorder.RecordTick(sim, 2, tickInputs, Def);

        var first = Assert.Single(recorder.Record.Swings, swing => swing.ActivationId == firstActivation);
        var second = Assert.Single(recorder.Record.Swings, swing => swing.ActivationId == secondActivation);
        Assert.True(first.Connected);
        Assert.False(second.Connected);
        Assert.True(Assert.Single(recorder.Record.Hits).Air);
    }

    [Fact]
    public void Recorder_AcceptsActivationThatDiesBeforePostTickState()
    {
        var arena = TestHelpers.TestArena();
        arena.KillMaxX = 10f;
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(Def);
        TestHelpers.RegisterPlayer(sim, Def, state);
        var recorder = new SlopArena.Shared.AI.MatchRecorder();
        var input = new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 },
        };

        recorder.RecordPresses(sim, 0, input, Def);
        sim.ActivateAbility(1, new CancelMutatorAbility(), slot: 2, def: Def);
        var falling = sim.GetState(1);
        falling.PX = 20f;
        sim.SetState(1, falling);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        recorder.RecordTick(sim, 0, input, Def);

        Assert.Single(sim.LastTickDeaths);
        Assert.True(Assert.Single(recorder.Record.Swings).Accepted);
    }

    [Fact]
    public void Recorder_CookedSpecialHitCarriesActivationToSwing()
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(Def);
        attacker.FacingYaw = 0f;
        var target = TestHelpers.NpcState(z: 3f);
        target.PY = TestHelpers.GroundPY(Def);
        sim.RegisterEntity(1, Def, attacker, TestHelpers.LoadBakedData(Def));
        sim.RegisterEntity(100, Def, target, TestHelpers.LoadBakedData(Def));

        var recorder = new SlopArena.Shared.AI.MatchRecorder();
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = AbilitySlots.R },
            [100] = default,
        };
        for (int tick = 0; tick < 100 && recorder.Record.Hits.Count == 0; tick++)
        {
            recorder.RecordPresses(sim, tick, inputs, Def);
            sim.Tick(inputs);
            recorder.RecordTick(sim, tick, inputs, Def);
            inputs[1] = default;
        }

        var hit = Assert.Single(recorder.Record.Hits);
        Assert.NotEqual(0ul, hit.ActivationId);
        var swing = Assert.Single(recorder.Record.Swings, x => x.ActivationId == hit.ActivationId);
        Assert.True(swing.Accepted);
        Assert.True(swing.Connected);
    }
    [Fact]
    public void Resolver_PreservesActivationAndAirThroughProjectileExplosion()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox
        {
            X = 0f, Y = -1f, Z = 0f,
            Radius = 0.2f,
            DurationTicks = 10,
            Gravity = 1f,
            OwnerId = 1,
            AttackSlot = AbilitySlots.Normal1,
            ActivationId = 42,
            ActivationAirborne = true,
            Explosion = new ProjectileExplosion
            {
                Radius = 1f,
                Damage = 3f,
                DurationTicks = 2,
                Knockback = new KnockbackData { Profile = KnockbackProfile.Light },
            },
        });

        resolver.CheckGroundCollision(TestHelpers.TestArena());
        var pending = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal((ulong)42, pending.activationId);
        Assert.True(pending.airborne);

        resolver.Spawn(new Hitbox
        {
            X = 0f, Y = 0f, Z = 0f,
            Radius = 1f,
            DurationTicks = 2,
            OwnerId = 1,
            AttackSlot = AbilitySlots.Normal1,
            ActivationId = pending.activationId,
            ActivationAirborne = pending.airborne,
        });
        var hits = resolver.Tick(new List<SpellResolver.EntityData>
        {
            new() { Id = 100, PosX = 0f, PosY = 0f, PosZ = 0f, Radius = 0.5f, Active = true },
        });

        var hit = Assert.Single(hits);
        Assert.Equal((ulong)42, hit.ActivationId);
        Assert.True(hit.Airborne);
        Assert.Equal(AbilitySlots.Normal1, hit.AttackSlot);
    }

    [Fact]
    public void DeathEvent_CapturesPreCancelStateAndRecentKiller()
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(Def);
        var victim = TestHelpers.NpcState(z: 1f);
        victim.PY = TestHelpers.GroundPY(Def);
        TestHelpers.RegisterPlayer(sim, Def, attacker);
        sim.RegisterEntity(100, Def, victim, TestHelpers.LoadBakedData(Def));

        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = attacker.PY, Z = 1f,
            Radius = 1f,
            DurationTicks = 2,
            Damage = 1f,
            OwnerId = 1,
            AttackSlot = AbilitySlots.R,
            ActivationId = 9,
        });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });

        sim.ActivateAbility(100, new CancelMutatorAbility(), slot: 2, def: Def);
        var falling = sim.GetState(100);
        falling.PY = -30f;
        falling.IsGrounded = false;
        falling.PZ = -1f; // Outside the heightmap so below-floor death isn't snapped onto it.
        falling.PX = 4.5f;
        sim.SetState(100, falling);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });

        var death = Assert.Single(sim.LastTickDeaths);
        Assert.Equal("bottom", death.Boundary);
        Assert.Equal((ulong)100, death.EntityId);
        Assert.Equal((ulong)1, death.KillerEntityId);
        Assert.Equal((ulong)1, death.LastHitEntityId);
        Assert.Equal(AbilitySlots.R, death.LastHitSlot);
        Assert.Equal(ActionState.Attacking, death.State.State);
        Assert.Equal(4.5f, death.State.PX);
        Assert.Equal(-30f, death.State.PY);
        Assert.Equal(1, sim.GetState(100).Deaths);
    }

    [Fact]
    public void DeathEvent_RetainsStaleLastHitButClearsCredit()
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(Def);
        var victim = TestHelpers.NpcState(z: 1f);
        victim.PY = TestHelpers.GroundPY(Def);
        TestHelpers.RegisterPlayer(sim, Def, attacker);
        sim.RegisterEntity(100, Def, victim, TestHelpers.LoadBakedData(Def));

        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = attacker.PY, Z = 1f,
            Radius = 1f,
            DurationTicks = 2,
            Damage = 1f,
            OwnerId = 1,
            AttackSlot = AbilitySlots.Normal1,
            ActivationId = 10,
        });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });
        sim.SetTick(200);

        var falling = sim.GetState(100);
        falling.PY = -30f;
        falling.IsGrounded = false;
        falling.PZ = -1f; // Outside the heightmap so below-floor death isn't snapped onto it.
        sim.SetState(100, falling);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });

        var death = Assert.Single(sim.LastTickDeaths);
        Assert.Equal((ulong)1, death.LastHitEntityId);
        Assert.Equal(0ul, death.KillerEntityId);
    }

    [Fact]
    public void DeathEvent_SelfHitContextDoesNotReplaceRecentKillerCredit()
    {
        var arena = TestHelpers.TestArena();
        arena.KillMaxX = 10f;
        var sim = TestHelpers.MakeSim(arena);
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(Def);
        var victim = TestHelpers.NpcState(z: 1f);
        victim.PY = TestHelpers.GroundPY(Def);
        TestHelpers.RegisterPlayer(sim, Def, attacker);
        sim.RegisterEntity(100, Def, victim, TestHelpers.LoadBakedData(Def));

        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = attacker.PY, Z = 1f,
            Radius = 1f,
            DurationTicks = 2,
            Damage = 1f,
            OwnerId = 1,
            AttackSlot = AbilitySlots.Normal1,
            ActivationId = 12,
        });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });

        var reset = sim.GetState(100);
        reset.PX = 0f;
        reset.PY = TestHelpers.GroundPY(Def);
        reset.PZ = 5f;
        reset.IsGrounded = true;
        reset.State = ActionState.Idle;
        reset.HitstopTicks = 0;
        reset.HitstunTicks = 0;
        sim.SetState(100, reset);
        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = reset.PY, Z = 5f,
            Radius = 1f,
            DurationTicks = 2,
            Damage = 1f,
            OwnerId = 100,
            AttackSlot = AbilitySlots.R,
            CanHitOwner = true,
            ActivationId = 13,
        });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });

        var falling = sim.GetState(100);
        falling.PX = 500f;
        falling.IsGrounded = false;
        sim.SetState(100, falling);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [100] = default });

        var death = Assert.Single(sim.LastTickDeaths);
        Assert.Equal((ulong)1, death.KillerEntityId);
        Assert.Equal((ulong)100, death.LastHitEntityId);
        Assert.Equal(AbilitySlots.R, death.LastHitSlot);
    }

    [Fact]
    public void DeathEvent_RecordsSelfHitWithoutCallingItAKill()
    {
        var sim = TestHelpers.MakeSim();
        var victim = TestHelpers.PlayerState();
        victim.PY = TestHelpers.GroundPY(Def);
        sim.RegisterEntity(1, Def, victim, TestHelpers.LoadBakedData(Def));

        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = victim.PY, Z = 0f,
            Radius = 1f,
            DurationTicks = 2,
            Damage = 1f,
            OwnerId = 1,
            AttackSlot = AbilitySlots.R,
            CanHitOwner = true,
            ActivationId = 11,
        });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });

        var falling = sim.GetState(1);
        falling.PY = -30f;
        falling.IsGrounded = false;
        falling.PZ = -1f; // Outside the heightmap so below-floor death isn't snapped onto it.
        sim.SetState(1, falling);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });

        var death = Assert.Single(sim.LastTickDeaths);
        Assert.Equal((ulong)1, death.LastHitEntityId);
        Assert.Equal(0ul, death.KillerEntityId);
    }

    private sealed class ImmediateAbility : ServerAbility
    {
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
            => EndAbility(ref s);
    }

    private sealed class CancelMutatorAbility : ServerAbility
    {
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def) { }

        public override void OnCancel(ref CharacterState s)
        {
            s.State = ActionState.Idle;
            s.PX = 999f;
        }
    }
}
