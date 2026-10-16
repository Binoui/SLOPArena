using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ShieldGrabPunishTests
{
    public static IEnumerable<object[]> CloseUnsafeNormals()
    {
        foreach (var defender in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        {
            yield return new object[] { defender, CharacterClass.FightGuy, AbilitySlots.Slot2 };
            yield return new object[] { defender, CharacterClass.Manki, AbilitySlots.Slot3 };
            yield return new object[] { defender, CharacterClass.Wibou, AbilitySlots.Slot2 };
            yield return new object[] { defender, CharacterClass.Bonk, AbilitySlots.Slot1 };
        }
    }

    [Theory]
    [MemberData(nameof(CloseUnsafeNormals))]
    public void CloseUnsafeNormalCanBeShieldGrabbedBeforeAttackerCanEscape(
        CharacterClass defender, CharacterClass attacker, byte slot)
    {
        var result = RunPunish(defender, attacker, slot, 1.2f);

        Assert.True(result.Blocked, "The authored move must actually contact shield.");
        Assert.True(result.GrabStarted, "Grab must start after block stun, directly from shield.");
        Assert.True(result.Captured, "The close attacker must remain inside grab reach after block response.");
        Assert.False(result.AttackerCouldActBeforeCapture, "Capture must precede the attacker's escape opportunity.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void SpacedBlockedNormalRemainsOutsideShieldGrab(CharacterClass defender)
    {
        var result = RunPunish(defender, CharacterClass.FightGuy, AbilitySlots.Slot4, 2.4f);

        Assert.True(result.Blocked);
        Assert.True(result.GrabStarted);
        Assert.False(result.Captured, "Blocking a distant hit must not grant an automatic capture.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void TimingSafeNormalCanRetreatBeforeShieldGrabBecomesActive(CharacterClass defender)
    {
        var result = RunPunish(defender, CharacterClass.FightGuy, AbilitySlots.Slot1, 1.2f);

        Assert.True(result.Blocked);
        Assert.True(result.GrabStarted);
        Assert.True(result.AttackerCouldActBeforeCapture);
        Assert.False(result.Captured, "Reach tuning must not erase recovery-based escape.");
    }

    private static PunishResult RunPunish(
        CharacterClass defender, CharacterClass attacker, byte slot, float distance)
    {
        var attackerContent = BuiltInContentResolver.Resolve(attacker);
        var defenderContent = BuiltInContentResolver.Resolve(defender);
        var attackerDef = CompileSource(attacker);
        var defenderDef = CompileSource(defender);
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var attackerState = TestHelpers.PlayerState(50f, 50f);
        attackerState.PY = attackerDef.CapsuleHeight * 0.5f;
        var defenderState = TestHelpers.PlayerState(50f, 50f + distance);
        defenderState.EntityId = 2;
        defenderState.PY = defenderDef.CapsuleHeight * 0.5f;
        defenderState.FacingYaw = MathF.PI;
        sim.RegisterEntity(1, attackerDef, attackerState, attackerContent.Baked);
        sim.RegisterEntity(2, defenderDef, defenderState, defenderContent.Baked);

        var result = new PunishResult();
        bool grabPressed = false;
        for (int tick = 0; tick < 180; tick++)
        {
            defenderState = sim.GetState(2);
            bool grab = result.Blocked && !grabPressed
                && defenderState.BlockStunTicks == 0 && defenderState.HitstopTicks == 0;
            grabPressed |= grab;
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new()
                {
                    ActiveSlot = tick == 0 ? slot : AbilitySlots.None,
                    // Attempt retreat as soon as recovery/IASA permits; an idle dummy
                    // would incorrectly make timing-safe moves look punishable.
                    MoveY = result.Blocked ? -1f : 0f,
                },
                [2] = new() { ShieldHeld = true, GrabPressed = grab },
            });

            foreach (var hit in sim.LastTickHits)
                if (hit.Blocked && hit.TargetEntityId == 2)
                    result.Blocked = true;
            if (result.Blocked && sim.LastTickOrdinaryActionOpportunities.Contains(1ul))
                result.AttackerCouldActBeforeCapture = true;
            defenderState = sim.GetState(2);
            result.GrabStarted |= defenderState.State == ActionState.GrabAttempt;
            if (defenderState.State == ActionState.Grabbing && defenderState.InteractionId != 0)
            {
                var captured = sim.GetState(1);
                Assert.Equal(ActionState.Grabbed, captured.State);
                Assert.Equal(defenderState.InteractionId, captured.InteractionId);
                Assert.Equal(2ul, captured.InteractionPartnerId);
                result.Captured = true;
                break;
            }
        }
        return result;
    }

    private static CharacterDefinition CompileSource(CharacterClass character)
    {
        string id = character.ToString().ToLowerInvariant();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string package = Path.Combine(directory.FullName, "client/Unity/Assets/CharacterPackages", id);
            if (File.Exists(Path.Combine(package, "character.json")))
            {
                var compiled = CharacterPackageCompiler.Compile(
                    File.ReadAllText(Path.Combine(package, "package.json")),
                    File.ReadAllText(Path.Combine(package, "character.json")),
                    CharacterCookProfile.TrustedBuiltIn);
                Assert.NotNull(compiled.CookedPackage);
                return CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!, character);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"Character package source not found: {id}");
    }

    private sealed class PunishResult
    {
        public bool Blocked;
        public bool GrabStarted;
        public bool Captured;
        public bool AttackerCouldActBeforeCapture;
    }
}
