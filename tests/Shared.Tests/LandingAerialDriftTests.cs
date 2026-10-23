using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Aerial landing behavior: air-started normals terminate on landing so friction can stop
/// their drift; aerial specials and explicitly landing-triggered capabilities continue.
/// </summary>
public class LandingAerialDriftTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

    private static InputState Fwd(float moveY = 1f, bool jump = false, byte slot = 0) => new()
    {
        MoveY = moveY,
        Jump = jump,
        ActiveSlot = slot,
    };

    /// <summary>
    /// Short hop, whiff an air move mid-flight, then release all input after landing.
    /// Returns (pz, vz, states, grounded) sampled each tick.
    /// </summary>
    private static (List<float> pz, List<float> vz, List<ActionState> states, List<bool> grounded)
        LandMidAerial(CharacterDefinition def, byte slot, int aerialStartTick, int releaseTick, int totalTicks)
    {
        var sim = TestHelpers.MakeSim();
        var p = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def), PZ = 0f };
        TestHelpers.RegisterPlayer(sim, def, p);

        var inputs = new Dictionary<ulong, InputState>();
        var pz = new List<float>();
        var vz = new List<float>();
        var states = new List<ActionState>();
        var grounded = new List<bool>();
        for (int t = 0; t < totalTicks; t++)
        {
            if (t < 20) inputs[1] = Fwd();
            else if (t == 20) inputs[1] = Fwd(jump: true);               // short hop
            else if (t < aerialStartTick) inputs[1] = Fwd();
            else if (t < releaseTick) inputs[1] = Fwd(slot: t == aerialStartTick ? slot : (byte)0);
            else inputs[1] = default;                                    // release all
            sim.Tick(inputs);
            var s = sim.GetState(1);
            pz.Add(s.PZ); vz.Add(s.VZ); states.Add(s.State); grounded.Add(s.IsGrounded);
        }
        return (pz, vz, states, grounded);
    }

    private static int FirstTakeoff(List<bool> grounded)
    {
        for (int t = 21; t < grounded.Count; t++) if (!grounded[t]) return t;
        return -1;
    }

    private static int FirstLanding(List<bool> grounded, int afterTick)
    {
        for (int t = afterTick; t < grounded.Count; t++) if (grounded[t]) return t;
        return -1;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void JumpStartupAttack_StartsTimeline_AndLandingStopsDrift(int attackTick, bool fastFall)
    {
        var def = TestHelpers.EngineDef;
        def = def with { Movement = def.Movement with { JumpSquatTicks = 2, FloatWindowTicks = 0 } };
        var slots = def.CookedSlots!.ToArray();
        slots[8] = new CookedSlotDefinition(8, "air.1", true, "Jump startup landing fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(60, 0, 4, 0, 0, Array.Empty<string>(),
                    Array.Empty<CookedTimelineOperation>()),
            }));
        def = def with { CookedSlots = slots };
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = -100, AZ = -100, BX = -100, BZ = 100, CX = 100, CZ = -100 },
            new CollisionTriangle { AX = 100, AZ = 100, BX = 100, BZ = -100, CX = -100, CZ = 100 },
        };
        var sim = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(sim, def, TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def),
            State = ActionState.Run,
            VZ = def.Movement.RunSpeed,
        });
        var inputs = new Dictionary<ulong, InputState>();
        bool downPressed = false, aerialStarted = false, landedDuringAttack = false;
        for (int tick = 0; tick < 120; tick++)
        {
            var before = sim.GetState(1);
            bool downEdge = fastFall && !downPressed && !before.IsGrounded && before.VY < 0f;
            downPressed |= downEdge;
            inputs[1] = new InputState
            {
                Jump = tick == 0,
                ActiveSlot = tick == attackTick ? AbilitySlots.Slot1 : (byte)0,
                DownPressed = downEdge,
            };
            sim.Tick(inputs);
            var after = sim.GetState(1);
            aerialStarted |= !after.IsGrounded && after.State == ActionState.Attacking
                && after.AttackSequence != 0 && after.AnimLockTicks > 0;
            if (!before.IsGrounded && after.IsGrounded && before.State == ActionState.Attacking)
            {
                landedDuringAttack = true;
                Assert.Equal(ActionState.Idle, after.State);
                Assert.Equal((byte)0, after.AttackSlot);
                Assert.Equal((ushort)4, after.LandingLagTicks);
            }
        }
        Assert.True(aerialStarted, "A buffered/takeoff press must execute the aerial, not just set Attacking.");
        Assert.True(landedDuringAttack, "The aerial must remain active until the landing boundary.");
        var final = sim.GetState(1);
        Assert.True(final.IsGrounded);
        Assert.Equal(ActionState.Idle, final.State);
        Assert.Equal((byte)0, final.AttackSlot);
        Assert.Equal(0f, final.VZ);
    }

    [Fact]
    public void Cyclone_LandMidMove_ContinuesUntilNaturalEnd()
    {
        var (_, _, states, grounded) = LandMidAerial(Def, AbilitySlots.R, aerialStartTick: 28, releaseTick: 48, totalTicks: 120);

        int takeoff = FirstTakeoff(grounded);
        int landing = FirstLanding(grounded, takeoff);
        Assert.True(landing > 0, "should land");
        Assert.Equal(ActionState.Attacking, states[landing]);

        int end = states.FindIndex(landing, s => s != ActionState.Attacking);
        Assert.True(end > landing, "special should end on its timeline, not touchdown");
    }

    [Fact]
    public void AirNormal_LandMidMove_StillEndsOnLanding()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        var hitbox = new CookedHitbox(
            AuthoringHitboxShape.Sphere, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f,
            null, null, 5f, 0f, 2f, 1f, 5, 3, true, 0);
        slots[8] = new CookedSlotDefinition(8, "air.1", true, "Aerial landing fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(30, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
                    }),
            }));
        def = def with { CookedSlots = slots };
        var (_, vz, states, grounded) = LandMidAerial(def, AbilitySlots.Slot1, aerialStartTick: 40, releaseTick: 60, totalTicks: 100);

        int takeoff = FirstTakeoff(grounded);
        int landing = FirstLanding(grounded, takeoff);
        Assert.True(landing > 0, "should land");
        bool attackedBeforeLanding = false;
        for (int tick = takeoff; tick < landing; tick++)
            attackedBeforeLanding |= states[tick] == ActionState.Attacking;
        Assert.True(attackedBeforeLanding, "synthetic air normal should be active before landing");
        Assert.Equal(ActionState.Idle, states[landing]);
        Assert.Equal(0f, vz[^1]); // After input release, ordinary ground braking stops drift.
    }

    /// <summary>All four Wibou air normals (keys 1-4): none may drift after landing.</summary>
    [Fact]
    public void Wibou_AllAirNormals_Land_NoDrift()
    {
        var def = TestHelpers.WibouDef;
        foreach (byte slot in new[] { AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4 })
        {
            var sim = TestHelpers.MakeSim();
            var p = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def), PZ = 0f };
            TestHelpers.RegisterPlayer(sim, def, p);

            var inputs = new Dictionary<ulong, InputState>();
            var vz = new List<float>();
            var grounded = new List<bool>();
            // Short hop, tap the air normal mid-flight, release before landing.
            for (int t = 0; t < 140; t++)
            {
                if (t < 20) inputs[1] = Fwd();
                else if (t == 20) inputs[1] = Fwd(jump: true);
                else if (t < 30) inputs[1] = Fwd();
                else if (t < 35) inputs[1] = Fwd(slot: slot);   // tap air normal
                else inputs[1] = default;
                sim.Tick(inputs);
                var s = sim.GetState(1);
                vz.Add(s.VZ); grounded.Add(s.IsGrounded);
            }

            int takeoff = -1;
            for (int t = 21; t < grounded.Count; t++) if (!grounded[t]) { takeoff = t; break; }
            int landing = -1;
            for (int t = takeoff; t < grounded.Count; t++) if (grounded[t]) { landing = t; break; }
            Assert.True(landing > 0, $"slot {slot}: should land");

            // 40 ticks after landing, drift must be fully braked to zero.
            int check = Math.Min(landing + 40, vz.Count - 1);
            Assert.Equal(0f, vz[check]);
        }
    }

    /// <summary>
    /// Wibou runs beside an enemy, full-jumps, whiffs an air normal, passes them,
    /// then releases the stick. Landing friction must stop actual carried momentum.
    /// </summary>
    [Fact]
    public void Wibou_CrossUpWhiffAirNormal_NoPersistentDrift()
    {
        var def = TestHelpers.WibouDef;
        var sim = TestHelpers.MakeSim();
        var p = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def), PZ = 0f };
        TestHelpers.RegisterPlayer(sim, def, p);
        // Offset the enemy from the run line so pushboxes cannot stop the approach.
        TestHelpers.RegisterNpc(sim, def, TestHelpers.NpcState(2f, 7f) with { PY = TestHelpers.GroundPY(def) });

        var inputs = new Dictionary<ulong, InputState>();
        var pz = new List<float>();
        var vz = new List<float>();
        var grounded = new List<bool>();
        bool attackedAirborne = false;
        for (int t = 0; t < 200; t++)
        {
            if (t < 20) inputs[1] = Fwd();                             // run toward enemy
            else if (t < 30) inputs[1] = new InputState { MoveY = 1f, Jump = t == 20, JumpHeld = true };
            else if (t == 30) inputs[1] = Fwd(slot: AbilitySlots.Slot1); // one airborne whiff
            else if (t < 60) inputs[1] = Fwd();                        // fly past, cross up
            else inputs[1] = default;                                 // release — must stop
            sim.Tick(inputs);
            var s = sim.GetState(1);
            pz.Add(s.PZ); vz.Add(s.VZ); grounded.Add(s.IsGrounded);
            attackedAirborne |= !s.IsGrounded && s.AttackSlot == AbilitySlots.Slot1;
        }

        int takeoff = -1;
        for (int t = 21; t < grounded.Count; t++) if (!grounded[t]) { takeoff = t; break; }
        Assert.True(takeoff > 0, "full jump should take off");
        int landing = -1;
        for (int t = takeoff; t < grounded.Count; t++) if (grounded[t]) { landing = t; break; }
        Assert.True(landing > 0, "should land");
        Assert.True(attackedAirborne, "cross-up must include an airborne normal");
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent); // Deliberate whiff, not hitstop drift.
        Assert.True(pz[takeoff] < sim.GetState(100).PZ, "takeoff must precede the cross-up");
        Assert.True(pz[landing] > sim.GetState(100).PZ,
            $"should land PAST the enemy (cross-up): pz={pz[landing]:F2}");
        Assert.True(vz[59] > 0f, "runout must carry momentum before stick release");

        // 40 ticks after landing, fully stopped — no persistent drift.
        int check = Math.Min(landing + 40, vz.Count - 1);
        Assert.Equal(0f, vz[check]);
    }
}
