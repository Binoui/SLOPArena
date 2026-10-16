---
name: slopbot-animation
description: Adapt an existing sourced humanoid animation clip within an approved scope, then verify its exact import, binding, and playback on the actual character.
---

# SlopBot animation helper

Use this helper only for bounded adaptation of an existing, sourced humanoid clip on a supported character. It does not create original motion, custom rigs, or gameplay behavior.

## Load shared instructions first

On every direct invocation, load the SlopBot entry skill and its progress instructions through the harness skill loader, or read `../slopbot/SKILL.md` and the referenced `../slopbot/references/progress.md` from this public skills tree. Follow their selected-checkout, scope, mode, approval, tool/ownership, and resume rules. If either shared instruction is unavailable, stop before mutation; do not use this helper to bypass the entry skill. Resolve all project documentation below from the user's explicitly selected SLOPArena checkout, never from this skill's installation path or a private workspace. Record observed decisions, evidence, and blockers in the entry skill's `.slopbot/<package-or-goal>.md` record; do not claim unperformed work.

## Scope and source safety

Resolve technical authorities from the selected game root: `docs/systems/animation-system.md`, `docs/characters/adding-a-new-character.md`, `docs/contributing/conventions.md`, and `docs/characters/character-model-polish.md`. This helper orchestrates those rules, not a second private workflow. Follow the repository's supported Unity operation route and ownership protocol; examples do not grant permission or authorize bypassing a missing tool.

Before changing anything, confirm the source clip is approved for use and its source, version, license, attribution, and redistribution rights are known. Keep the untouched source recoverable; for FBX reimport, preserve byte-identical FBX and `.meta` copies and make a separate candidate. Do not overwrite the only working source. If the actual character uses an unsupported rig or needs rig/model rework, stop and hand off that separate scope.

Inspect and record the exact package and semantic animation ID; catalog entry; source file, action/take, and revision; imported asset and clip identity; rig/Avatar and Humanoid or Generic mode; importer settings including root-motion policy; embedded Unity clip `fileID`; clip frame range, duration and authored FPS; and current playback mapping. Filenames and paths are not semantic identities. Verify the catalog binds the intended semantic ID to this exact imported clip and its corresponding pose-track identity.

## Define and approve a bounded correction

Start from the real source action and rig. Observe the bad clip on the actual character and name the visible defect, affected interval, likely bones/channels, and evidence that demonstrates it. Agree the smallest correction and what must remain unchanged before editing. Keep the known-good poses, hierarchy, rest pose, scale, bone names, take identity, entry/end transition, and root behavior unless the approved scope explicitly says otherwise. A changed rig/import mapping is a separate rig-change approval, not an incidental animation fix.

Map each authoritative gameplay event in `character.json`'s 60 Hz timeline to the source using the actual source FPS, source start frame, playback-start tick, and runtime playback multiplier. For constant forward playback:

```text
sourceFrame = startFrame + (tick - playbackStartTick) / 60 * sourceFps * playbackSpeed
tick = playbackStartTick + (sourceFrame - startFrame) / (sourceFps * playbackSpeed) * 60
```

Use actual runtime playback values, not assumed 1x; trace the real mapping when speed changes, offsets, or holds make the constant-speed formula inapplicable. Separate authoritative event poses from optional presentation beats. Animation conforms to gameplay timing; never change gameplay timing, hitboxes, damage, input, or server simulation to make a clip fit. If the motion cannot meet the approved timing contract with a bounded correction, hand off for separately approved gameplay or motion scope.

## Correct, import, and verify

Use only an available, supported animation authoring/import tool. If it is missing, do not invent commands, automate an external app, or claim completion. Hand off the exact source/action, defect and affected frames, approved correction, rig/import details, playback/event mapping, recovery copy location, and expected evidence; resume only after inspecting the returned result.

Make the bounded correction in the existing action, preserving authored FPS and intended duration. Inspect the affected interval, then compare before/after at matching event frames and runtime speed. Verify the complete action plus playback entry and exit on the actual character at gameplay-camera scale. Check readability and pose contrast, transition/handoff, root stability, foot sliding, interpolation overshoot, deformation/clipping, and any relevant attachment. A Blender viewport, static pose, export, or successful Unity import alone does not establish motion quality.

After reimport, inspect Unity's current Humanoid/Avatar validity, scale/origin/root behavior, clip duration/FPS/bindings, and actual-character playback. Resolve the embedded clip's current `fileID` and update the correct `CharacterAssetCatalog.asset` semantic binding if it changed; a correct asset GUID with a stale `fileID` is still a broken binding. Verify exact package/catalog identity and the affected runtime path after rebinding. Do not hand-edit cooked output or generated catalogs.

For a change intended to ship, follow the current character guide's inspect-before-cook and accepted-package workflow. A successful export, import, rebind, or cook is not quality acceptance; stale or previous cooked content is not evidence for the changed clip. Verify the final package against current source/catalog hashes and inspect the character using that package. Compare authoritative pose outputs before and after where they can be affected. If poses or `poses.bin` change, report that as changed authoritative content, not cosmetic presentation; investigate discrepancies and obtain explicit acceptance where required. Do not imply pose equivalence from unchanged gameplay JSON, bone names, or a valid Avatar.

Keep the record precise: source and candidate identities, approved defect/correction, exact semantic binding and current `fileID`, event-to-frame mapping, before/after and entry/exit observations, checks actually performed, pose/cook implications, unresolved issues, and current integration state. Distinguish exploratory, imported, rebound, cooked, and accepted states. Refer to the public character guide for final inspect/cook/verify/roster rules; an animation edit alone never establishes package or roster acceptance.
