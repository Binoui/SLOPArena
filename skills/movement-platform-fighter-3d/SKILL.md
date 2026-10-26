---
name: movement-platform-fighter-3d
description: "SLOPArena Unity 6 C# movement and combat fundamentals: camera-relative 8-way movement, JumpArc, ShortHop, FastFall, grounded Shield, directional air dodge, server authority, and Smash-style percent."
metadata: {"category": "game-dev", "tags": ["unity", "csharp", "platform-fighter", "movement", "combat"]}
---

# SLOPArena Movement and Platform-Fighter Fundamentals

Use this skill for movement, input, and universal combat changes. The authoritative implementation is `src/Shared/ServerSimulation.cs`; Unity gathers input and renders state. Read [`docs/architecture-overview.md`](../../docs/architecture-overview.md), [`docs/systems/combat-systems.md`](../../docs/systems/combat-systems.md), and [`CONTEXT.md`](../../CONTEXT.md) before changing settled mechanics.

## Movement contract

- Input is camera-relative and snaps to eight directions. The camera is a world sibling with absolute yaw.
- Ground movement has one Run tier. Rush is its initial burst; grounded reversals remain immediate after Rush. Turnaround is removed.
- Ground and double jumps use per-character movement data. ShortHop is release-timed during JumpSquat; double jumps are full jumps.
- JumpArc is the complete jump animation. Fall starts when the JumpArc finishes while the entity remains airborne, not at the physics apex.
- FastFall requires a fresh Down press while already descending and latches through release. Ascent/apex presses are discarded; locks and authored vertical motion cannot be overridden. Ordinary aerial attacks may coexist.
- Grounded Down selects Crouch or momentum-spending Slide; actionable held-Down landings may slide. SlideJump retains ordinary squat and jump resources. See the canonical combat guide for thresholds, interruption/reset rules, normal carry and settled CrouchBrace rather than duplicating tuning here.
- Defense (Left Shift / RT) holds Shield while grounded. A fresh airborne press starts a directional air dodge, aimed by the stick or forward when neutral. Shared owns its invulnerability, recovery, and one-use-per-flight resource.
- The old Universal Dash is retired. The universal homing dash (ADR-0042) replaces it: float, then a dash that turns toward the target, ending in a cancel window on arrival or a punishable miss. Kit-owned dash, lunge, and recovery abilities retain their authored behavior.
- Automatic ledge grabs and LedgeHang are disabled for the demo. Their old state and packet fields remain reserved; recover onto a stage surface with movement, jumps, and kit abilities.
- FloatWindow is restored by landing, taking damage, or RecoveryMove. Normal air attacks do not reset it.

All durations are 60 Hz simulation ticks. Do not implement movement from render-frame delta time.

## Input boundary

`InputController.Poll()` and `BuildInputState()` produce `SlopArena.Shared.InputState`. Down hold and the one-shot DownPressed edge are independent of movement axes. `ShieldHeld` and `ShieldPressed` supply defense intent; `GrabPressed` supplies grab intent. The state also carries movement, jump, canonical active slot, camera aim, target intent, JumpHeld, facing-camera edge, and target-lock toggle fields. `DashPressed` supplies homing dash intent; the legacy Burst bit remains reserved and inert. The client supplies intent; Shared simulation validates and applies gameplay.

The canonical move grid is grounded and aerial variants of `1`, `3`, `4`, `A`, `E`, `R`, and `F` (three normals, ADR-0042). Physical controls are remappable adapters. They are not package identity.

NPCs use generated/training inputs rather than keyboard state. Keep AI input on the same Shared simulation path as player input.

## Simulation order

When changing movement, follow [`ServerSimulation.Tick`](../../src/Shared/ServerSimulation.cs) and the helpers it calls. `PreTickAbilities`, `SimulateMovement`, and `Simulation.SimulateTick` own different admission and movement phases. Use their current call order rather than a copied tick sequence.

Preserve server authority. Client prediction and rollback replay the Shared rules; they must not add client-only corrections for gameplay mechanics.

## Combat invariants

Damage uses percent, not an HP depletion rule. Knockback is resolved from the hit profile and current percent. Shared owns Hitstun, Hitstop, rotational DI, SDI, ASDI, Shield, air dodge, grabs, throws, IASA, landing lag, and air-use counters. Simultaneous attacks trade; reciprocal active grabs clash. Attack Clash remains planned, not implemented, under ADR-0015. Burst is reserved and inert. See [`docs/systems/combat-systems.md`](../../docs/systems/combat-systems.md).

Use pure Shared math for collision and displacement. Unity physics, animations, VFX, and camera effects are presentation or input aids only.

## Change checklist

1. Read the domain term in `CONTEXT.md` and the relevant ADR before changing a settled mechanic.
2. Trace input → Shared state → movement/combat transition → rendered state.
3. Reuse existing `MovementStats`, `CharacterState`, `InputState`, and resolver fields.
4. Add a behavioral Shared test for a new boundary or transition.
5. Run:

```bash
dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true
```

For Unity-facing changes, use the Unity CLI recompile and console gate described in [`docs/contributing/unity-cli.md`](../../docs/contributing/unity-cli.md).
