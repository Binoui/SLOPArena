# SLOPArena — Post-Release Fixes: Baked Data Shipping + In-Match Pause Menu
**Date:** 2026-08-02
**Status:** Approved (ready-for-agent)
**Spec source:** user report "several issues after exe release"

---

## Problem Statement

Two issues surfaced in the standalone exe release:

1. **Solo training has no floor.** In a match, the character falls through the visible stage and lands on an invisible surface far below it (`KillHeight + 1`). The Editor is unaffected. Suspected by the reporter to be "no server handling baking" — investigation shows it is a **baked-data shipping problem on the client**, not a server-side issue.
2. **Esc is not a pause.** Pressing Esc in training immediately discards the session and loads the Main Menu. There is no pause menu, no Quit option inside a match, and the mouse stays captured (you cannot click anything). PvP matches have no Esc handling at all — the only way out is Alt-F4.

## Solution

- Baked arena and skeleton data is bundled into the player build via `StreamingAssets` and resolved from `Application.streamingAssetsPath` (Editor keeps a repo-root fallback). Match scenes **require** the baked arena file — the silent fallback to hardcoded, collision-less arena definitions is removed, so the floor behaves identically in Editor and exe.
- Esc opens a small centered pause menu (Resume / Quit Game) in both `TrainingMatch` and `PvPMatch` via one shared component on `MatchBase`. While open: simulation frozen, cursor released and visible. Esc toggles the menu; Resume re-locks the cursor and continues; Quit Game exits to desktop.

## User Stories

### Baked data / floor

1. As a player launching the distributed exe, I want the training stage to have a solid floor, so that I can practice movement and combat instead of falling through the stage.
2. As a player, I want every arena's floor and platforms to match its baked collision in both the Editor and the built game, so that gameplay is identical in dev and release.
3. As a player in solo training, I want to stand on the stage at spawn instead of sinking to an invisible floor far below, so that the game feels correct.
4. As a player, I want a broken build (missing baked data) to fail loudly with a clear error, so that it is obvious something is misconfigured instead of the game silently degrading.
5. As a dev, I want all baked arena files and skeleton `.bin` files bundled into the player build automatically, so that shipping an exe no longer requires hand-copying a `data/` folder next to it.
6. As a dev, I want one client-side resolver that finds baked content via `Application.streamingAssetsPath` in builds and the repo root in the Editor, so that both environments load the same files through one code path.
7. As a dev, I want `ArenaRegistry.Get(name)` to stop silently returning an unrelated arena (`_hardcoded[0]`) for unknown names, so that misconfiguration surfaces instead of silently playing on the wrong stage.
8. As a PvP player hosted from the client, I want the bundled game server to find the same arenas and skeleton data as the client, so that online matches have floors and bone-attached hitboxes too.

### Pause menu

9. As a player in training, I want Esc to open a small centered pause menu instead of dropping me to the main menu, so that I can pause without losing my session.
10. As a player, I want a **Resume** option in the pause menu, so that I can continue the match exactly where I left off.
11. As a player, I want a **Quit Game** option in the pause menu, so that I can exit to desktop without Alt-F4.
12. As a player, I want the mouse cursor released and visible while the pause menu is open, so that I can click the menu buttons.
13. As a player, I want the simulation frozen while paused, so that I don't get hit (or the NPC doesn't run away) while reading the menu.
14. As a player, I want Esc to toggle the pause menu closed again, so that resuming is one keypress.
15. As a PvP player, I want the same pause menu in online matches, so that I can pause and quit there too.
16. As a dev, I want one shared pause component used by both match types, so that behavior stays consistent and is maintained once.

## Implementation Decisions

### Baked data shipping (floor fix)

- **Root cause.** `TrainingMatch`, `PvPMatch`, and `MatchBase.LoadBakedData` resolve baked content with `Path.Combine(Application.dataPath, "..", "..", "..", "data", ...)`. This only works when the build lives inside a repo-shaped tree (dev machine); a distributed exe has no `data/` at that path. Arena file missing → fallback to `ArenaRegistry.Get(name)` → hardcoded definitions carry **no** `Heightmap`/`CollisionTriangles` → `Simulation` grounds at `arena.KillHeight + 1` (`colosseum`: Y=-9), below the visual stage. Skeleton `.bin` files missing → silent degradation to capsule-only hurtboxes (same class of bug, less visible). See `NilusAbilityTests` fixture comment, which already documents that `ArenaRegistry` definitions have `Heightmap.Data == null` "built from the .tscn collision mesh at load time".
- **Relocate baked content under `client/Unity/Assets/StreamingAssets/`** (git-tracked, so Unity always packages it into the player build):
  - `data/arenas/*.arena` → `StreamingAssets/arenas/*.arena` (matches `ServerHost`'s existing bundled-player convention).
  - `data/*_skeleton.bin` → `StreamingAssets/data/*_skeleton.bin` (keeps the `res://data/...` suffix mapping mechanical).
  - Repo-root `data/` copies are removed; all consumers resolve through the new resolver. `.gitignore`/docs updated accordingly.
- **One client-side resolver** (static helper, e.g. `BakedContentPaths` in the client runtime): `Resolve("arenas/colosseum.arena")` → `Application.streamingAssetsPath/<rel>` in builds; in the Editor, fall back to `<repo root>/data/<rel>` reusing `ServerHost`'s repo-root resolution (extracted/shared). `ServerHost`'s own arena-dir resolution is refactored to use it (its `streamingAssetsPath/arenas` branch is prior art — line ~117).
- **Baked arena is required for matches.** `TrainingMatch`/`PvPMatch`: when the `.arena` file cannot be resolved, log a clear error naming the missing path and stop match start (no silent hardcoded fallback for simulation). The hardcoded `ArenaRegistry` list remains only as metadata/display source (`StageSelect` cards via `ArenaRegistry.All`) and server pre-`LoadFromDirectory` defaults.
- **`ArenaRegistry.Get` unknown-name trap.** `Get(name)` currently returns `_hardcoded[0]` when `name` is not found (e.g. `island` from `Island_arena.arena` silently becomes `pit`). Change to return the arena or a clear failure (`null`/throw) with callers handling it — the spec is that no match ever silently runs on a different arena than requested.
- **Server parity.** The bundled server (`StreamingAssets/Server`) already receives `ArenaDataDir` pointing at `StreamingAssets/arenas` via `HostedServerConfig` (written by `ServerHost`) — arenas covered. Skeleton bins: `MatchInstance.LoadBakedData` reads `res://data/...` relative to the server working directory (the binary dir), so the packaging step copies the skeleton `.bin` files next to the bundled server binary (`StreamingAssets/Server/data/`). Dev runs (`dotnet run`, `server.json` `arenaDataDir: "data/arenas"`) are unchanged.

### Pause menu

- **One shared component** (e.g. `MatchPauseMenu`, a `MonoBehaviour` + UI Toolkit `UIDocument` panel, consistent with `HUDManager` and the approved menu-flow spec) wired on `MatchBase`; both `TrainingMatch` and `PvPMatch` get it with no per-class pause logic.
- **Toggle behavior.** Esc (`Keyboard.current.escapeKey.wasPressedThisFrame`) toggles Running ↔ Paused. TrainingMatch's direct `SceneManager.LoadScene("MainMenu")` Esc handler is removed.
- **While paused:**
  - `Time.timeScale = 0f` — freezes `FixedUpdate`, so the local sim (and input send in PvP) stops ticking for free; UI remains interactive (Update is unscaled). Restore `1f` on resume.
  - Cursor released: `Cursor.lockState = CursorLockMode.None; Cursor.visible = true`. Camera mouse input suppressed via `CameraMount.SetMode(CameraMode.FreeCursor)` after `FreezeAtCurrentAngles()` (existing API; re-locks on `SetMode(CameraMode.Normal)` at resume).
  - Small centered panel (UI Toolkit) with **Resume** and **Quit Game** buttons.
- **Resume:** hide panel, `SetMode(CameraMode.Normal)`, `Time.timeScale = 1f`, cursor re-locked. Sim resumes from the same tick state (local sim: exact continuation; PvP: client re-syncs from the next server state — acceptable Phase-1 behavior, no prediction).
- **Quit Game:** `Application.Quit()`; in the Editor, stop play mode (`#if UNITY_EDITOR`). PvP: existing `ServerHost.OnApplicationQuit`/`NetworkClient` teardown already covers hosted-server cleanup.

## Testing Decisions

- **What makes a good test:** external behavior only. For the floor: a shipped arena must parse and provide real ground collision — `Simulation` must ground entities at the baked floor Y, never at `KillHeight + 1`. For the registry: `Get` must not silently return a different arena. Pause behavior is UI — verified by playtest, not unit test.
- **Modules tested:** `tests/Shared.Tests` gains a data-driven suite over the shipped baked arenas (prior art: `TestHelpers.TestArena`/`RiseArena` fixture idiom — the existing suite already encodes "heightmap drives grounding").
  - For each `data/arenas/*.arena`: `ArenaBinaryFormat.LoadFromFile` succeeds; `Heightmap.Data != null`; `Heightmap.Sample(spawn.X, spawn.Z)` ≈ expected floor Y at every `SpawnPoint`; `CollisionTriangles` non-empty. This is the direct regression guard for "no floor" (would have caught the exe bug at bake time).
  - `ArenaRegistry.Get("unknown-name")` → clear failure (contract for decision above).
- **Pause menu:** no automated seam exists (client UI; the repo's unit-test infrastructure covers `src/Shared` only). Verification is a Unity playtest checklist written to `TESTING-UNITY.md` (existing worktree-agent convention): start training → Esc opens panel, cursor visible, sim frozen, NPC frozen → Resume re-locks cursor and sim continues → Esc again pauses → Quit Game exits; repeat on the PvP path. Also verify Esc-pause works while aiming/attacking and that no input leaks across the toggle.

## Out of Scope

- Settings/options screen, audio options, controller navigation of menus
- Pausing the remote game server or lobby/queue states (pause is client-side; the server keeps simulating in PvP — Phase-1 acceptable)
- Addressables-based content loading
- CI automation of the Unity build / packaging step (no Unity build workflow exists in CI today; the packaging step is documented and manual)
- Standalone (non-bundled) game-server skeleton-bins deep-dive beyond the packaging note above
- Stage thumbnails and other StageSelect cosmetics

## Further Notes

- The "no floor" report is **not** a server-handling issue: solo training is `LocalSimulationBridge` (pure client). It is baked-data shipping + fragile `Application.dataPath/../../..` resolution. The Editor worked because that path resolves to the repo root.
- `NilusAbilityTests` already documents the trap the fix removes: hardcoded arena definitions carry no heightmap, so fixtures are used in tests instead — after this change, shipped arenas are testable directly.
- Release checklist gains one step: build must include `StreamingAssets/` (Unity does this automatically for player builds); verify `arenas/*.arena` and `data/*.bin` exist in the build output before distributing.
