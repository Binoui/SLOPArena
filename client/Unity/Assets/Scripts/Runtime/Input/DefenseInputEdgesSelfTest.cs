using System;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace SlopArena.Client.Input;

public static class DefenseInputEdgesSelfTest
{
#if UNITY_EDITOR
    [MenuItem("Tools/SLOPArena/Tests/Defense Input Edges")]
#endif
    public static void Run()
    {
        DefaultBindingsAreDefenseFirst();
        KeyboardDefenseAndDirectGrabLatchOnce();
        ControllerChordBothOrdersAndSameFrame();
        ControllerChordRearmsWhenEitherComponentReleases();
        MixedDeviceAttackRemainsNormal();
        SuppressionDropsPendingEdgesUntilRelease();
        Debug.Log("[DefenseInputEdgesSelfTest] Passed keyboard defense/grab, both chord orders, same-frame chord, rearm, mixed-device slot selection, and suppression checks.");
    }
    private static void DefaultBindingsAreDefenseFirst()
    {
        var shieldPaths = HumanInputActions.Get("Shield").bindings.Select(binding => binding.path).ToArray();
        var grabPaths = HumanInputActions.Get("Grab").bindings.Select(binding => binding.path).ToArray();
        Require(shieldPaths.Contains("<Keyboard>/leftShift") && shieldPaths.Contains("<Gamepad>/rightTrigger"),
            "Defense defaults are not Left Shift and RT.");
        Require(grabPaths.Length == 1 && grabPaths[0] == "<Keyboard>/c" &&
            HumanInputActions.RebindableActions.Contains("Grab"), "C grab is not independently rebindable.");
        Require(!HumanInputActions.RebindableActions.Contains("Burst"),
            "The retired Burst action remains rebindable.");
        var dashPaths = HumanInputActions.Get("Dash").bindings.Select(binding => binding.path).ToArray();
        Require(HumanInputActions.RebindableActions.Contains("Dash") && dashPaths.Contains("<Keyboard>/4")
            && dashPaths.Contains("<Gamepad>/rightShoulder"), "The homing dash is not rebindable on 4 and RB (ADR-0042).");
    }

    private static void KeyboardDefenseAndDirectGrabLatchOnce()
    {
        var edges = new DefenseInputEdges(true);
        edges.Update(true, false, false, false, false, true, false);
        Require(edges.TakeShieldPressed(), "A fresh keyboard defense press was lost.");
        edges.Update(true, false, false, false, false, false, false);
        Require(!edges.TakeShieldPressed(), "Held keyboard defense repeated its edge.");

        edges.Update(false, false, false, false, false, false, true);
        Require(edges.TakeGrabPressed(), "Direct C grab did not emit an edge.");
        edges.Update(false, false, false, false, false, false, false);
        Require(!edges.TakeGrabPressed(), "Held direct grab repeated its edge.");
    }

    private static void ControllerChordBothOrdersAndSameFrame()
    {
        var modifierFirst = new DefenseInputEdges(true);
        modifierFirst.Update(false, false, false, true, true, false, false);
        modifierFirst.Update(true, true, true, true, false, false, false);
        AssertGrabOnly(ref modifierFirst, "LB then RT");
        modifierFirst.Update(true, true, false, true, false, false, false);
        Require(!modifierFirst.TakeGrabPressed(), "Held LB+RT repeated grab.");

        var triggerFirst = new DefenseInputEdges(true);
        triggerFirst.Update(true, true, true, false, false, false, false);
        Require(triggerFirst.TakeShieldPressed(), "Unmodified RT defense press was lost.");
        triggerFirst.Update(true, true, false, false, false, false, false);
        Require(!triggerFirst.TakeShieldPressed(), "Held RT repeated its defense edge before the chord completed.");
        triggerFirst.Update(true, true, false, true, true, false, false);
        AssertGrabOnly(ref triggerFirst, "RT then LB");

        var simultaneous = new DefenseInputEdges(true);
        simultaneous.Update(true, true, true, true, true, false, false);
        AssertGrabOnly(ref simultaneous, "same-frame LB+RT");

        var directGrabAndRt = new DefenseInputEdges(true);
        directGrabAndRt.Update(true, true, true, false, false, false, true);
        AssertGrabOnly(ref directGrabAndRt, "C+RT");
    }

    private static void ControllerChordRearmsWhenEitherComponentReleases()
    {
        var edges = new DefenseInputEdges(true);
        edges.Update(true, true, true, true, true, false, false);
        Require(edges.TakeGrabPressed(), "Initial chord did not fire.");
        edges.Update(true, true, false, false, false, false, false);
        edges.Update(true, true, false, true, true, false, false);
        Require(edges.TakeGrabPressed(), "Releasing LB did not rearm the chord.");

        edges.Update(false, false, false, false, false, false, false);
        edges.Update(true, true, true, true, false, false, false);
        Require(edges.TakeGrabPressed(), "Releasing RT did not rearm the chord.");
    }

    private static void MixedDeviceAttackRemainsNormal()
    {
        const byte normalSlot = 3;
        const byte specialSlot = 10;
        Require(DefenseInputEdges.ResolveFaceSlot(false, true, normalSlot, specialSlot) == normalSlot,
            "Controller LB reinterpreted a keyboard attack as a special.");
        Require(DefenseInputEdges.ResolveFaceSlot(true, true, normalSlot, specialSlot) == specialSlot,
            "Controller LB no longer modifies a controller face-button attack.");
    }

    private static void SuppressionDropsPendingEdgesUntilRelease()
    {
        var edges = new DefenseInputEdges(true);
        edges.Update(true, false, false, false, false, true, true);
        edges.ClearPending(true);
        Require(!edges.TakeShieldPressed() && !edges.TakeGrabPressed(), "Suppressed edges leaked from the pending latch.");
        Require(!edges.ShieldHeld(true), "Held defense resumed without a release after suppression.");

        edges.Update(false, false, false, false, false, false, false);
        edges.Update(true, false, false, false, false, true, false);
        Require(edges.TakeShieldPressed(), "A fresh defense press did not work after release.");
    }

    private static void AssertGrabOnly(ref DefenseInputEdges edges, string chord)
    {
        Require(edges.TakeGrabPressed(), $"{chord} failed to emit grab.");
        Require(!edges.TakeShieldPressed(), $"{chord} emitted a competing defense edge.");
        Require(!edges.ShieldHeld(true), $"{chord} allowed a shield tick.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
