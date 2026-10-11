using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class StartupTargetProjectileTests
{
    [Theory]
    [InlineData(CharacterClass.Wibou, false)]
    [InlineData(CharacterClass.Wibou, true)]
    [InlineData(CharacterClass.FightGuy, false)]
    [InlineData(CharacterClass.FightGuy, true)]
    public void ProjectileFiresAfterEightStartupTicksAndTracksLaunchTarget(CharacterClass characterClass, bool airborne)
    {
        var def = CompileDefinition(characterClass);
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = airborne ? TestHelpers.GroundPY(def) + 2f : TestHelpers.GroundPY(def);
        player.IsGrounded = !airborne;
        sim.RegisterEntity(1, def, player);
        var targetDef = TestHelpers.EngineDef;
        var target = TestHelpers.NpcState(0f, 8f);
        target.PY = TestHelpers.GroundPY(targetDef);
        sim.RegisterEntity(100, targetDef, target);
        var input = new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = -9000, AimPitch = -5000 };
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [100] = default });
        Assert.Equal(characterClass == CharacterClass.FightGuy ? ActionState.Aiming : ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal(characterClass == CharacterClass.FightGuy, sim.GetState(1).IsAiming);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        TestHelpers.AssertNear(player.FacingYaw, sim.GetState(1).FacingYaw);
        input.ActiveSlot = 0;
        input.IsAiming = false;
        if (characterClass == CharacterClass.FightGuy)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [100] = default });
        for (var i = 0; i < (characterClass == CharacterClass.FightGuy ? 7 : 6); i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [100] = default });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            TestHelpers.AssertNear(player.FacingYaw, sim.GetState(1).FacingYaw);
        }
        target = sim.GetState(100);
        target.PX = 8f;
        target.PZ = 0f;
        target.PY = sim.GetState(1).PY + 4f;
        sim.SetState(100, target);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [100] = default });
        var shots = sim.Resolver.GetActiveHitboxes();
        Assert.Equal(characterClass == CharacterClass.Wibou ? 3 : 1, shots.Count);
        var shot = shots.OrderBy(x => MathF.Abs(x.VZ)).First();
        Assert.True(shot.VX > 0f && MathF.Abs(shot.VZ) < .01f, $"expected launch-time target, got {shot.VX},{shot.VY},{shot.VZ}");
        Assert.True(shot.VY > 0f, "target's elevated launch-time position should produce upward pitch");
        TestHelpers.AssertNear(MathF.PI / 2f, sim.GetState(1).FacingYaw);
        TestHelpers.AssertNear(sim.GetState(1).FacingYaw, MathF.Atan2(shot.VX, shot.VZ));
        float vx = shot.VX;
        float vz = shot.VZ;
        sim.SetState(100, target with { PX = -8f });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [100] = default });
        var continued = sim.Resolver.GetActiveHitboxes().OrderBy(x => MathF.Abs(x.VZ)).First();
        TestHelpers.AssertNear(vx, continued.VX);
        TestHelpers.AssertNear(vz, continued.VZ);
        TestHelpers.AssertNear(MathF.PI / 2f, sim.GetState(1).FacingYaw);
    }

    [Theory]
    [InlineData(CharacterClass.Wibou, false)]
    [InlineData(CharacterClass.Wibou, true)]
    [InlineData(CharacterClass.FightGuy, false)]
    [InlineData(CharacterClass.FightGuy, true)]
    public void ProjectileWithoutEligibleTargetUsesFacingAndCancelBeforeLaunchFiresNothing(CharacterClass characterClass, bool airborne)
    {
        var def = CompileDefinition(characterClass);
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = airborne ? TestHelpers.GroundPY(def) + 2f : TestHelpers.GroundPY(def);
        player.IsGrounded = !airborne;
        player.FacingYaw = MathF.PI / 2f;
        sim.RegisterEntity(1, def, player);
        var input = new InputState { ActiveSlot = 11 };
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        for (var i = 0; i < (characterClass == CharacterClass.FightGuy ? 8 : 7); i++) sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        var shots = sim.Resolver.GetActiveHitboxes();
        Assert.Equal(characterClass == CharacterClass.Wibou ? 3 : 1, shots.Count);
        var shot = shots.OrderBy(x => MathF.Abs(x.VZ)).First();
        Assert.True(shot.VX > 0f && MathF.Abs(shot.VZ) < 0.01f);
        Assert.InRange(shot.VY, -.02f, .02f);
        TestHelpers.AssertNear(player.FacingYaw, sim.GetState(1).FacingYaw);

        var cancelled = TestHelpers.MakeSim();
        cancelled.RegisterEntity(1, def, player);
        cancelled.Tick(new Dictionary<ulong, InputState> { [1] = input });
        cancelled.SetState(1, cancelled.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 20 });
        for (var i = 0; i < 12; i++) cancelled.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Empty(cancelled.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData(CharacterClass.Wibou, false)]
    [InlineData(CharacterClass.Wibou, true)]
    [InlineData(CharacterClass.FightGuy, false)]
    [InlineData(CharacterClass.FightGuy, true)]
    public void SelectedTargetOverridesNearestAndRemovedTargetFallsBackForward(CharacterClass characterClass, bool airborne)
    {
        var def = CompileDefinition(characterClass);
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def) + (airborne ? 2f : 0f),
            IsGrounded = !airborne,
        };
        sim.RegisterEntity(1, def, player);
        var targetDef = TestHelpers.EngineDef;
        sim.RegisterEntity(100, targetDef, TestHelpers.NpcState(8f, 0f) with { PY = TestHelpers.GroundPY(targetDef) });
        sim.RegisterEntity(101, targetDef, TestHelpers.NpcState(0f, 3f) with { PY = TestHelpers.GroundPY(targetDef) });
        var input = new InputState { ActiveSlot = 11, TargetEntityId = 100 };
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        input.ActiveSlot = 0;
        for (int i = 0; i < (characterClass == CharacterClass.FightGuy ? 8 : 7); i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        var shot = sim.Resolver.GetActiveHitboxes().OrderBy(x => MathF.Abs(x.VZ)).First();
        Assert.True(shot.VX > 0f && MathF.Abs(shot.VZ) < .01f);

        var removed = TestHelpers.MakeSim();
        removed.RegisterEntity(1, def, player);
        removed.RegisterEntity(100, targetDef, TestHelpers.NpcState(8f, 0f) with { PY = TestHelpers.GroundPY(targetDef) });
        input.ActiveSlot = 11;
        removed.Tick(new Dictionary<ulong, InputState> { [1] = input });
        removed.RemoveEntity(100);
        input.ActiveSlot = 0;
        for (int i = 0; i < (characterClass == CharacterClass.FightGuy ? 8 : 7); i++)
            removed.Tick(new Dictionary<ulong, InputState> { [1] = input });
        shot = removed.Resolver.GetActiveHitboxes().OrderBy(x => MathF.Abs(x.VX)).First();
        Assert.True(shot.VZ > 0f && MathF.Abs(shot.VX) < .01f);
    }

    [Fact]
    public void KiShotRecoveryContinuesAfterProjectileLaunch()
    {
        var def = CompileDefinition(CharacterClass.FightGuy);
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = 11 } };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int tick = 2; tick <= 45; tick++)
        {
            sim.Tick(inputs);
            Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        }
        sim.Tick(inputs);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    private static CharacterDefinition CompileDefinition(CharacterClass characterClass)
    {
        string id = characterClass == CharacterClass.Wibou ? "wibou" : "fightguy";
        string root = Path.Combine(FindRepoRoot(), "client", "Unity", "Assets", "CharacterPackages", id);
        var result = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")), File.ReadAllText(Path.Combine(root, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage!);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "client", "Unity", "Assets", "CharacterPackages", "wibou", "package.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Character package sources not found");
    }
}
