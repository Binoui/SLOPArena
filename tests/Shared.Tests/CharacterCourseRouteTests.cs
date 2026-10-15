using System.Numerics;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Cooked FightGuy and the actual baked course; only ordinary inputs drive the complete run.</summary>
public sealed class CharacterCourseRouteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainAndShortcutRoutesCompleteWithTheNormalKitWithoutFalling(bool shortcut)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "data", "arenas"))) root = root.Parent;
        Assert.NotNull(root);
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root!.FullName, "data", "arenas", "fightguy_sanctuary.arena"))!.Value;
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        var spawn = arena.SpawnPoints[0];
        sim.RegisterEntity(1, entry.Definition, new()
        {
            PX = spawn.X,
            PY = spawn.Y,
            PZ = spawn.Z,
            FacingYaw = spawn.Yaw,
            AimYaw = spawn.Yaw,
            JumpsLeft = entry.Definition.Movement.MaxJumps,
            AirDodgesLeft = 1
        }, entry.BakedAnimation);
        sim.SetRespawnPosition(1, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
        sim.BeginCourseAttempt(1); sim.Tick(new());
        var events = new List<CharacterCourseEvent>();
        bool usedE = false, usedR = false;
        void Tick(InputState input)
        {
            sim.Tick(new() { [1] = input });
            if (sim.GetState(1).AttackSlot == AbilitySlots.E) usedE = true;
            if (sim.GetState(1).AttackSlot == AbilitySlots.R) usedR = true;
            foreach (var e in sim.LastTickCourseEvents) events.Add(e);
        }
        void Wait(int ticks) { for (int i = 0; i < ticks; i++) Tick(default); }
        void Stop() { Tick(new() { ShieldHeld = true, ShieldPressed = true }); Wait(12); }
        string Where() { var s = sim.GetState(1); return $"{s.PX:F2},{s.PY:F2},{s.PZ:F2} grounded={s.IsGrounded} deaths={s.Deaths}"; }
        InputState Toward(float x, float z)
        {
            var s = sim.GetState(1); var v = new Vector2(x - s.PX, z - s.PZ); float l = v.Length(); if (l > .01f) v /= l;
            float strength = Math.Clamp(l * 1.3f, 0, 1);
            short yaw = (short)(MathF.Atan2(v.X, v.Y) * 18000 / MathF.PI);
            return new() { MoveX = v.X * strength, MoveY = v.Y * strength, FacingYaw = yaw, AimYaw = yaw };
        }
        void Walk(float x, float z)
        {
            var before = sim.GetState(1).Deaths;
            for (int i = 0; i < 400; i++)
            {
                var s = sim.GetState(1); float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (sim.Course!.Value.Ended || d < .35f) { Stop(); return; }
                Tick(Toward(x, z));
                if (sim.GetState(1).Deaths != before) throw new Exception("Fall walking " + x + "," + z + " -> " + Where());
            }
            throw new Exception("walk stalled " + x + "," + z + " -> " + Where());
        }
        void JumpTo(float x, float z, int secondJumpTick = 25, bool shortHop = false)
        {
            Assert.True(sim.GetState(1).IsGrounded, "Grounded takeoff required: " + Where());
            var before = sim.GetState(1).Deaths;
            for (int i = 0; i < 160; i++)
            {
                var s = sim.GetState(1); float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > 15 && s.IsGrounded && d < 1f) { Stop(); return; }
                var input = Toward(x, z); input.Jump = (i == 0 || i == secondJumpTick); input.JumpHeld = i == 0 || (!shortHop && i < 55);
                Tick(input);
                if (sim.GetState(1).Deaths != before) throw new Exception("Fall jumping " + x + "," + z + " -> " + Where());
            }
            throw new Exception("jump stalled " + x + "," + z + " -> " + Where());
        }
        void RunJumpTo(float x, float z, int secondJumpTick = -1)
        {
            for (int i = 0; i < 6; i++) Tick(Toward(x, z));
            JumpTo(x, z, secondJumpTick);
        }
        void AbilityJumpTo(float x, float z, int eTick = -1, int rTick = -1)
        {
            var before = sim.GetState(1).Deaths;
            for (int i = 0; i < 200; i++)
            {
                var state = sim.GetState(1);
                float distance = Vector2.Distance(new(state.PX, state.PZ), new(x, z));
                if (i > Math.Max(eTick, rTick) + 65 && state.IsGrounded && distance < 1.5f) { Stop(); return; }
                var input = Toward(x, z); input.Jump = i == 0; input.JumpHeld = i < 24;
                if (i == eTick) input.ActiveSlot = AbilitySlots.E;
                if (i == rTick) input.ActiveSlot = AbilitySlots.R;
                Tick(input);
                if (sim.GetState(1).Deaths != before) throw new Exception("Fall ability jump " + x + "," + z + " -> " + Where());
            }
            throw new Exception("ability jump did not land " + x + "," + z + " -> " + Where());
        }
        void Shoot(int index)
        {
            if (sim.IsCourseTargetBroken(index)) return;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var s = sim.GetState(1); var target = arena.Arcade!.Course!.Targets[index].Position;
                while (s.GetCooldown(AbilitySlots.A) > 0 || s.AttackSlot != 0) { Tick(default); s = sim.GetState(1); }
                short yaw = (short)(MathF.Atan2(target.X - s.PX, target.Z - s.PZ) * 18000 / MathF.PI);
                for (int i = 0; i < 23; i++) Tick(new() { ActiveSlot = i == 0 ? AbilitySlots.A : (byte)0, IsAiming = true, FacingYaw = yaw, AimYaw = yaw, TargetEntityId = sim.CourseTargetInputAlias(index) });
                for (int i = 0; i < 100; i++) Tick(new() { FacingYaw = yaw, AimYaw = yaw, TargetEntityId = sim.CourseTargetInputAlias(index) });
                if (sim.IsCourseTargetBroken(index)) { return; }
            }
            throw new Exception("target did not break " + index + " -> " + Where());
        }
        Wait(2);
        Walk(-11, -32); Shoot(0); Shoot(1);
        Walk(-23.5f, -24); Walk(-28, -20); Shoot(2);
        if (shortcut) { Walk(-28, -16); AbilityJumpTo(-28, 1, rTick: 20); }
        else
        {
            Walk(-32.65f, -15.45f); JumpTo(-35.5f, -13, -1, true);
            Walk(-36.8f, -11.75f); JumpTo(-41.5f, -4.5f);
            Walk(-41.7f, -5); RunJumpTo(-37, 2.5f);
            Walk(-35.9f, 2.5f); JumpTo(-32.5f, 2.2f, -1, true);
        }
        Shoot(3); Shoot(4); Walk(-27, 5);
        Walk(-25, 6.4f); JumpTo(-24, 11.2f, -1, true);
        Walk(-21, 16.4f); JumpTo(-20, 19.5f, -1, true);
        Walk(-20, 21.5f); Walk(-18, 26); Shoot(5); Shoot(6);
        if (shortcut) { Walk(-17, 26); AbilityJumpTo(-6, 26, eTick: 24); }
        else
        {
            Walk(-20, 29.2f); JumpTo(-20, 34, -1);
            Walk(-18.5f, 35.1f); JumpTo(-13, 36.75f);
            Walk(-11.8f, 35.7f); JumpTo(-7, 32.5f, -1);
            Walk(-6.8f, 31); JumpTo(-6, 28.5f, -1);
        }
        Shoot(7); Shoot(8); Shoot(9); Walk(-5, 26);
        Walk(-1.6f, 26); JumpTo(2, 26, -1, true);
        Walk(6.3f, 26); JumpTo(12, 26, -1); Walk(16, 18); Shoot(10); Shoot(11);
        if (shortcut) { Walk(17, 14); AbilityJumpTo(26, -4, eTick: 24, rTick: 60); }
        else
        {
            Walk(19.3f, 16); JumpTo(26, 16);
            Walk(27.3f, 14.6f); JumpTo(33, 10);
            Walk(32.2f, 9.1f); JumpTo(32, 1.5f);
            Walk(30.9f, .4f); JumpTo(27.8f, -3, -1);
        }
        Shoot(12); Shoot(13); Shoot(14); Walk(26, -5);
        Walk(26.5f, -4); RunJumpTo(28, -14); Shoot(15);
        Walk(25.7f, -15.7f); JumpTo(19, -22); Shoot(16);
        Walk(21, -23.6f); JumpTo(27, -28, -1); Shoot(17);
        Walk(25.6f, -29.4f); JumpTo(19, -36.5f); Shoot(18);
        Walk(17.4f, -35.5f); JumpTo(10, -32, -1); Shoot(19); Walk(7, -32);
        Assert.Equal(CharacterCoursePhase.Completed, sim.Course!.Value.Phase);
        Assert.Equal(20, sim.Course.Value.BrokenTargets);
        Assert.Equal((byte)0, sim.GetState(1).Deaths);
        Assert.InRange(sim.Course.Value.ElapsedTicks, 1ul, 120ul * 60);
        Assert.Equal(20, events.Count(e => e.Kind == CharacterCourseEventKind.TargetBroken));
        for (int i = 0; i < 3; i++)
            Assert.Contains(events, e => e.Kind == CharacterCourseEventKind.CheckpointSaved && e.Index == i);
        Assert.Equal(shortcut, usedE);
        Assert.Equal(shortcut, usedR);
    }

    [Theory]
    [InlineData(false, 0f)]
    [InlineData(false, 12f)]
    [InlineData(true, 0f)]
    [InlineData(true, 12f)]
    public void LongBridgeTransferNeedsTheSecondJump(bool doubleJump, float startingSpeed)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "data", "arenas"))) root = root.Parent;
        Assert.NotNull(root);
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root!.FullName, "data", "arenas", "fightguy_sanctuary.arena"))!.Value;
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        // Near the first pole's forward corner: give the single jump its best ordinary takeoff.
        var start = new Vector2(-37.18f, -11.32f); var target = new Vector2(-41.5f, -4.5f);
        var direction = Vector2.Normalize(target - start); float hip = entry.Definition.CapsuleHeight * .5f;
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        sim.RegisterEntity(1, entry.Definition, new()
        {
            PX = start.X, PY = .25f + hip, PZ = start.Y,
            VX = direction.X * startingSpeed, VZ = direction.Y * startingSpeed,
            IsGrounded = true, JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1
        }, entry.BakedAnimation);
        sim.BeginCourseAttempt(1); sim.Tick(new()); sim.Tick(new() { [1] = default });
        bool landed = false;
        for (int i = 0; i < 150; i++)
        {
            var state = sim.GetState(1); var delta = target - new Vector2(state.PX, state.PZ);
            float distance = delta.Length(); if (distance > .01f) delta /= distance;
            float strength = Math.Clamp(distance * 1.3f, 0, 1);
            short yaw = (short)(MathF.Atan2(delta.X, delta.Y) * 18000 / MathF.PI);
            sim.Tick(new() { [1] = new()
            {
                MoveX = delta.X * strength, MoveY = delta.Y * strength,
                Jump = i == 0 || (doubleJump && i == 25), JumpHeld = i < 55,
                FacingYaw = yaw, AimYaw = yaw
            } });
            state = sim.GetState(1);
            if (i > 10 && state.IsGrounded && MathF.Abs(state.PY - (1.3f + hip)) < .1f
                && Vector2.Distance(new(state.PX, state.PZ), target) < 2.5f) { landed = true; break; }
        }
        Assert.Equal(doubleJump, landed);
    }
}
