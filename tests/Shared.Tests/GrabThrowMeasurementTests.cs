using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using static SlopArena.Shared.Tests.GrabHoldTests;

namespace SlopArena.Shared.Tests;

public sealed class GrabThrowMeasurementTests
{
    private readonly ITestOutputHelper _output;
    public GrabThrowMeasurementTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 50)]
    [InlineData(false, 100)]
    [InlineData(true, 0)]
    [InlineData(true, 50)]
    [InlineData(true, 100)]
    [InlineData(true, 150)]
    public void RealLaunchMilestones(bool fling, ushort percent)
    {
        foreach (float di in new[] { 0f, 1f })
        {
            var arena = TriangleArena();
            arena.MaxX = arena.MaxZ = 200f;
            arena.KillMaxX = arena.KillMaxZ = 300f;
            for (int i = 0; i < arena.CollisionTriangles.Length; i++)
            {
                var triangle = arena.CollisionTriangles[i];
                triangle.AX *= 5f; triangle.AZ *= 5f; triangle.BX *= 5f; triangle.BZ *= 5f;
                triangle.CX *= 5f; triangle.CZ *= 5f;
                arena.CollisionTriangles[i] = triangle;
            }
            arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
            var (sim, _) = Capture(arena: arena);
            var victim = sim.GetState(2);
            sim.SetState(2, victim with { DamagePercent = percent });
            Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveY = fling ? 1 : 0 });
            var start = sim.CaptureSnapshot();
            int ready = -1, stunEnd = -1, release = -1, apexTick = -1, landing = -1, death = -1;
            float releaseY = 0, releaseZ = 0, releaseX = 0, heightReady = 0, heightEnd = 0, aboveReady = 0, aboveEnd = 0;
            float distanceReady = 0, distanceEnd = 0, distanceLanding = 0, apex = float.MinValue;
            bool launched = false, airborne = false;
            var victimInput = new InputState { MoveX = di };
            for (int tick = 1; tick <= 600; tick++)
            {
                Tick(sim, victim: victimInput);
                var owner = sim.GetState(1); victim = sim.GetState(2);
                if (release < 0 && sim.LastTickHits.Any(h => h.OwnerEntityId == 1 && h.TargetEntityId == 2))
                { release = tick; releaseY = victim.PY; releaseX = victim.PX; releaseZ = victim.PZ; }
                if (release < 0) continue;
                if (victim.PY - releaseY > apex) { apex = victim.PY - releaseY; apexTick = tick; }
                launched |= victim.State == ActionState.Hitstun;
                airborne |= launched && !victim.IsGrounded && victim.PY > releaseY + 0.01f;
                float distance = MathF.Sqrt(MathF.Pow(victim.PX - releaseX, 2) + MathF.Pow(victim.PZ - releaseZ, 2));
                if (ready < 0 && Ready(owner))
                { ready = tick; heightReady = victim.PY - releaseY; aboveReady = victim.PY - owner.PY; distanceReady = distance; }
                if (launched && stunEnd < 0 && Ready(victim))
                { stunEnd = tick; heightEnd = victim.PY - releaseY; aboveEnd = victim.PY - owner.PY; distanceEnd = distance; }
                if (airborne && landing < 0 && victim.IsGrounded && victim.VY <= 0f)
                { landing = tick; distanceLanding = distance; }
                if (sim.LastTickDeaths.Count > 0) { death = tick; break; }
                if (ready >= 0 && stunEnd >= 0 && landing >= 0) break;
            }
            int ownerJump = Accepted(sim, start, 1, new() { Jump = true, JumpHeld = true }, victimInput);
            int ownerSlot = Accepted(sim, start, 1, new() { ActiveSlot = AbilitySlots.Slot1 }, victimInput);
            int victimJump = Accepted(sim, start, 2, new() { Jump = true, JumpHeld = true, MoveX = di }, default);
            _output.WriteLine("kind | % | DI X | press | release | owner ready | jump | slot | victim ready | air action | advantage | slot margin | height ready/end | above owner ready/end | apex@tick | distance ready/end/land | landing | death");
            _output.WriteLine($"{(fling ? "fling" : "toss")} | {percent} | {di} | 0 | {release} | {ready} | {ownerJump} | {ownerSlot} | {stunEnd} | {victimJump} | {stunEnd - ready} | {stunEnd - ownerSlot} | {heightReady:F3}/{heightEnd:F3} | {aboveReady:F3}/{aboveEnd:F3} | {apex:F3}@{apexTick} | {distanceReady:F3}/{distanceEnd:F3}/{distanceLanding:F3} | {landing} | {death}");
            Assert.Equal(22, release);
            Assert.True(ready > release && stunEnd > release && ownerJump > release && ownerSlot > release && victimJump > release);
            Assert.True(landing > 0, "The wide flat stage must include the first landing.");
            if (!fling) Assert.True(stunEnd - ready >= 10, "Neutral toss leaves at least ten ticks to chase.");
        }
    }

    [Fact]
    public void EveryAdmittedFighterCanJumpAndConnectAnInstalledAerialBeforeTossStunEnds()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), "content-cooked/roster/manifest.json"));
        foreach (var entry in manifest.Entries)
        {
            var content = BuiltInContentResolver.Resolve(entry.Selector);
            var def = content.Definition;
            var sim = new ServerSimulation(TriangleArena());
            var owner = TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) };
            var targetDef = TestHelpers.EngineDef;
            var target = TestHelpers.PlayerState(20f, 20.8f) with { EntityId = 2, PY = TestHelpers.GroundPY(targetDef) };
            sim.RegisterEntity(1, def, owner, content.BakedAnimation);
            sim.RegisterEntity(2, targetDef, target);
            Tick(sim, new() { GrabPressed = true });
            for (int i = 0; i < 7; i++) Tick(sim);
            Assert.Equal(ActionState.Grabbing, sim.GetState(1).State);
            Tick(sim, new() { ActiveSlot = AbilitySlots.F });
            var start = sim.CaptureSnapshot();
            bool connected = false;
            foreach (byte slot in new[] { AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4 })
            {
                if (def.GetCookedSlotAbility(slot, true) == null) continue;
                sim.RestoreSnapshot(start);
                int jump = -1, aerial = -1, contact = -1, end = -1;
                bool sawStun = false, pressed = false;
                for (int t = 1; t <= 160; t++)
                {
                    owner = sim.GetState(1);
                    var input = new InputState { Jump = jump < 0, JumpHeld = true };
                    if (jump > 0)
                    {
                        float dx = sim.GetState(2).PX - owner.PX, dz = sim.GetState(2).PZ - owner.PZ;
                        float distance = MathF.Sqrt(dx * dx + dz * dz);
                        if (distance > 0.1f) { input.MoveX = dx / distance; input.MoveY = dz / distance; }
                    }
                    if (jump > 0 && !owner.IsGrounded && owner.State != ActionState.JumpSquat && !pressed)
                    { input.ActiveSlot = slot; pressed = true; }
                    Tick(sim, input);
                    owner = sim.GetState(1); target = sim.GetState(2);
                    if (jump < 0 && sim.LastTickAcceptedActions.Contains(1ul) && owner.State == ActionState.JumpSquat) jump = t;
                    if (aerial < 0 && owner.AttackSlot == slot && !owner.IsGrounded) aerial = t;
                    sawStun |= target.State == ActionState.Hitstun;
                    var hits = sim.LastTickHits.Where(h => h.OwnerEntityId == 1 && h.TargetEntityId == 2 && h.AttackSlot == slot).ToArray();
                    if (hits.Length > 0) { contact = t; break; }
                    if (sawStun && target.HitstunTicks == 0 && target.HitstopTicks == 0) { end = t; break; }
                }
                if (contact > 0 && sim.GetState(2).HitstunTicks > 0)
                {
                    _output.WriteLine($"chase | {entry.PackageId} | weight {def.Weight} | jumpForce {def.Movement.JumpForce} | squat {def.Movement.JumpSquatTicks} | slot {slot} | jump {jump} | aerial {aerial} | contact {contact} | victim remaining stun {sim.GetState(2).HitstunTicks}");
                    Assert.True(sim.GetState(2).HitstunTicks > 0, "The aerial contacts before toss stun ends.");
                    connected = true; break;
                }
                _output.WriteLine($"attempt | {entry.PackageId} | slot {slot} | jump {jump} | aerial {aerial} | contact {contact} | stun end {end}");
            }
            Assert.True(connected, $"{entry.PackageId} needs an actual installed aerial contact before toss stun ends.");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void EdgeFlingMeasuresAgencyAndKill(ushort percent)
    {
        var arena = TriangleArena();
        arena.KillMinX = arena.KillMinZ = -10f;
        arena.KillMaxX = arena.KillMaxZ = 50f;
        var (sim, _) = Capture(MathF.PI * 0.5f, arena);
        var owner = sim.GetState(1); var victim = sim.GetState(2);
        sim.SetState(1, owner with { PX = 37f });
        sim.SetState(2, victim with { PX = 37.65f, DamagePercent = percent });
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = 1 });
        int ready = -1, action = -1, death = -1;
        float xReady = 0f, xAction = 0f;
        bool launched = false;
        for (int t = 1; t <= 600; t++)
        {
            victim = sim.GetState(2);
            var input = ready >= 0 && action < 0 && !victim.IsGrounded ? new InputState { Jump = true, JumpHeld = true, MoveX = -1 } : default;
            Tick(sim, victim: input);
            victim = sim.GetState(2);
            launched |= victim.State == ActionState.Hitstun;
            if (sim.LastTickDeaths.Any(d => d.EntityId == 2)) { death = t; break; }
            if (ready < 0 && launched && Ready(victim)) { ready = t; xReady = victim.PX; }
            if (ready >= 0 && action < 0 && sim.LastTickAcceptedActions.Contains(2ul) && !victim.IsGrounded) { action = t; xAction = victim.PX; }
            if (action > 0) break;
        }
        _output.WriteLine($"edge | {percent}% | platform x<=40 | blast x=50 bottom=-20 | weight 100 | default tuning | neutral DI until escape | ready {ready}@x{xReady:F3} | accepted jump {action}@x{xAction:F3} | death {death}");
        if (percent == 0) Assert.True(action > ready && xAction < 50f && death < 0, "Low-percent edge fling permits an accepted air jump before the blast plane.");
        if (percent == 200) Assert.True(death > 0, "High-percent fling retains launch KO potential near the edge.");
    }

    private static bool Ready(CharacterState s) => s.HitstopTicks == 0 && s.HitstunTicks == 0
        && s.AnimLockTicks == 0 && s.LandingLagTicks == 0 && ActionStates.AdmitsActions(s.State);

    private static int Accepted(ServerSimulation sim, WorldSnapshot start, ulong id, InputState press, InputState victim)
    {
        sim.RestoreSnapshot(start);
        for (int t = 1; t <= 600; t++)
        {
            Tick(sim, id == 1 ? press : default, id == 2 ? press : victim);
            if (sim.LastTickAcceptedActions.Contains(id)) return t;
            if (sim.LastTickDeaths.Any(d => d.EntityId == id)) return -1;
        }
        return -1;
    }
}
