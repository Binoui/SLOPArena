# Ability Lab

## Purpose

Ability Lab is SLOPArena's internal fighter/move authoring and debugging environment. It is a package-first UI Toolkit editor shell.

## Usage

Open `Tools → SlopArena → Ability Lab` from any scene. The window automatically creates one private preview scene with its own rig, camera and lighting; the saved Lab scene and **Create Lab Rig** button are no longer needed. Preview objects stay outside your open scene and are cleaned up on close, script reload and entry into Play Mode. Select a source package from `Assets/CharacterPackages`; the selector shows its display name and package ID while persisting only the package ID. FightGuy opens as `fightguy` with Ground `1` selected.

The preview is rooted. It loads the last verified cooked package from staged `Application.streamingAssetsPath/content-cooked` or the repository `content-cooked` directory, in that order. It never depends on the process working directory and never cooks when the window opens. In Edit Mode, stop playback at tick `0`, use the timeline slider or bounded `−1`/`+1` controls, and inspect the embedded viewport, baked bones, hurtboxes, and hitboxes. Right-drag orbits, middle-drag pans, and scrolling zooms the private preview camera.

`SAVE + COOK` is the only path that persists source and cooks artifacts. `Cooked`, `Stale`, `Cook failed`, and structured diagnostics expose package authority and binding problems. An unavailable package never falls back to a legacy character.
In the Moves tab, drag a marker or hitbox body to a snapped source tick. Drag the narrow hitbox right edge to resize its active endpoint. `Add forward lunge` creates a facing-relative movement operation; edit its start tick, speed in metres per second, and duration. The direction is captured when it starts, and the move preserves its resulting momentum. Changes commit once on release through the package workspace; Escape cancels a drag, and Undo restores one release. Use the `0.5×..4×` Zoom control with horizontal scrolling for dense timelines. While stopped in package Edit Mode, click an active resolved hitbox in the embedded viewport and use its radius handle; radius edits use the same source-owned Undo path. Unavailable, playing, or non-Moves states have no package handles.

Preview and editing use character packages only. The former Nilus Compatibility tab and legacy preview path have been removed. Save + Cook is disabled until a package is open.

## Ownership

Ability Lab owns editor and preview tooling. Core gameplay data and simulation remain owned by SLOPArena/shared runtime code. Canonical package move identity is the Shared `CanonicalSlotProjection` (`ground.*` then `air.*`); human labels are UI adapters.

## Direction

The tool may eventually become the basis of a standalone creator tool, so Ability Lab-specific code should stay isolated from unrelated game code.

## Dependency rule

Prefer:

```text
AbilityLab → SLOPArena gameplay/shared code
```

Avoid:

```text
SLOPArena gameplay → AbilityLab
```
