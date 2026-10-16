# SLOPArena — Menu & UI Flow Design
**Date:** 2026-07-09
**Status:** Approved

---

## Goal

Add a complete front-end UI flow to SLOPArena: main menu, lobby, character select, and stage select. Both training and multiplayer go through the same character/stage select screens. No more hardcoded Inspector values for character or arena.

---

## Screen Flow

```
MainMenu
├── Training Mode ──────────────────────────────────────────→ CharSelect → StageSelect → Arena_Offline
└── Multiplayer
    ├── Host Game → [instructions: run server manually] → Lobby → CharSelect → StageSelect → Arena_PvP
    └── Join Game → [IP entry field] ─────────────────→ Lobby → CharSelect → StageSelect → Arena_PvP
```

Six scenes total (four new, two existing):

| Scene | Status |
|---|---|
| `MainMenu` | New |
| `Lobby` | New |
| `CharSelect` | New |
| `StageSelect` | New |
| `Arena_Offline` | Exists — remove Inspector char/stage fields from MatchBase |
| `Arena_PvP` | Exists — same |

Scene transitions use `SceneManager.LoadScene` (not Addressables — no need at this scale).

---

## MatchConfig — Data Handoff Between Scenes

A static class (not MonoBehaviour, not ScriptableObject) that persists across scene loads:

```csharp
public static class MatchConfig
{
    public static GameMode Mode;           // Training | PvP
    public static CharacterClass PlayerClass;
    public static CharacterClass OpponentClass;  // PvP only; 0 until known
    public static string ArenaName;
    public static bool IsHost;
    public static string ServerIP;         // Join path: IP entered by player
    public static int ServerPort = 9876;
}

public enum GameMode { Training, PvP }
```

`MatchBase.OnMatchStart()` reads `MatchConfig` instead of `[SerializeField]` fields. The `[SerializeField]` fields `_playerClass` and `_arenaName` are removed from `MatchBase` entirely — no fallback, no Inspector override.

---

## Screen 1 — MainMenu

**Scene:** `MainMenu.unity`
**Script:** `MainMenuController.cs`

Layout: game title at top, two options below as a nested list (C style from mockup):

```
TRAINING MODE
MULTIPLAYER ›
  HOST GAME
  JOIN GAME     [IP input field]  [Connect button]
```

Behaviour:
- **Training Mode** → sets `MatchConfig.Mode = Training`, `MatchConfig.IsHost = true`, loads `CharSelect`
- **Multiplayer** → expands inline to show Host / Join sub-items
- **Host Game** → sets `MatchConfig.Mode = PvP`, `MatchConfig.IsHost = true`, `MatchConfig.ServerIP = "127.0.0.1"`, loads `Lobby`
- **Join Game** → reveals an IP text input field + Connect button. On connect: sets `MatchConfig.Mode = PvP`, `MatchConfig.IsHost = false`, `MatchConfig.ServerIP = enteredIP`, loads `Lobby`

UI implementation: Unity UI Toolkit (`UIDocument`) — consistent with existing `HUDManager`.

---

## Screen 2 — Lobby

**Scene:** `Lobby.unity`
**Script:** `LobbyController.cs`

Purpose: wait for opponent to connect before proceeding to character select.

Layout:
- Player list: two slots (P1, P2), each showing "Connected" / "Waiting…" status
- Host view: **START** button — enabled once at least one opponent is connected
- Client view: "Waiting for host to start…" text, no Start button

Technical flow:
- `LobbyController.Start()` creates `NetworkClient`, connects to `MatchConfig.ServerIP:ServerPort`
- Polls `NetworkClient.IsServerConnected` each second to update P2 slot
- Host presses Start → loads `CharSelect` immediately
- Client: polls `NetworkClient.IsServerConnected` each second; has no Start button. Client manually navigates to `CharSelect` once they know the host has started (Phase 1 limitation — no server-push signal yet). In practice for the demo: host and client coordinate out-of-band (voice/text) and both press their respective buttons at roughly the same time.

**Training mode** skips `Lobby` entirely — `MainMenuController` loads `CharSelect` directly.

---

## Screen 3 — CharSelect

**Scene:** `CharSelect.unity`
**Script:** `CharSelectController.cs`

Layout — two panels, stacked vertically:

**Panel 1 — Character Grid**
- 5 portrait slots in a row, each `CharacterClass` value
- Selected slot highlighted (P1 badge, red border, glow)
- Unselected slots dimmed
- Locked/unreleased slots show `?` with a dashed border

**Panel 2 — Character Preview**
Three-column layout inside the panel:
- Left column: Q and W ability cards (key label + name + one-line description)
- Center: character name above, 3D model viewport below (RenderTexture camera — see note), tagline below model
- Right column: E and R ability cards

**3D model viewport:** A secondary `Camera` renders the selected character's model into a `RenderTexture` displayed via a `RawImage` UI element. The model is a separate off-screen GameObject that `CharSelectController` swaps when the selection changes. If a character has no model yet (`?` slots), show a silhouette placeholder.

**Ability data source:** `CharacterDefinition.GetSlotAbility()` from `CharacterRegistry.Get(selectedClass)`. Display name and one-line description come from `AbilitySpec.DisplayName` and `AbilitySpec.Description` — these fields will be added to `AbilitySpec` if not already present.

**Confirmation:**
- Player clicks a character → preview updates
- Player presses **SELECT** (or clicks again on the highlighted character) → `MatchConfig.PlayerClass = selected`, loads `StageSelect`
- In PvP: opponent character (`MatchConfig.OpponentClass`) is unknown at this point — resolved later when the server sends opponent state. The `CharSelect` screen only picks the local player's character.

---

## Screen 4 — StageSelect

**Scene:** `StageSelect.unity`
**Script:** `StageSelectController.cs`

Layout:
- Row of stage cards (one per arena registered in `ArenaRegistry.All`)
- Each card: stage thumbnail (a static screenshot or a coloured placeholder), stage name, size tag
- Selected card highlighted with red border + "SELECTED" badge

**Host flow:**
- All cards are clickable
- **CONFIRM STAGE** button appears when a card is selected
- On confirm → `MatchConfig.ArenaName = selected.Name` → loads the match scene

**Client flow:**
- Cards are display-only (not clickable)
- "Waiting for host to select stage…" message shown
- For Phase 1: client also shows the stage cards but with no confirm button. When the host confirms, the host's client loads the match — the client side loads `Arena_PvP` simultaneously (both sides load independently; the server already has the arena baked in from server startup)
- Phase 1 limitation: both clients must pick the same arena manually. True server-side stage broadcast is Phase 2 work

**Match scene loading:**
- `Mode == Training` → `SceneManager.LoadScene("Arena_Offline")`
- `Mode == PvP` → `SceneManager.LoadScene("Arena_PvP")`

---

## MatchBase Changes

Remove `[SerializeField]` for `_playerClass` and `_arenaName` from `MatchBase`. In `OnMatchStart()`:

```csharp
// Before (Inspector):
var playerDef = CharacterRegistry.Get(_playerClass);
string arenaPath = ... _arenaName ...

// After (MatchConfig):
var playerDef = CharacterRegistry.Get(MatchConfig.PlayerClass);
string arenaPath = ... MatchConfig.ArenaName ...
```

`_cameraMount`, `_playerRenderer`, `_inputController`, `_aimHandler`, `_hudManager` remain as `[SerializeField]` — these reference scene GameObjects, not data, so Inspector wiring is correct for them.

In `PvPMatch`:
- `_networkClient.ServerIP` is set from `MatchConfig.ServerIP` in `OnMatchStart()` instead of a hardcoded value
- `NetworkSimulationBridge` is initialised with `MatchConfig.ServerIP` and `MatchConfig.ServerPort`

---

## ArenaRegistry — DisplayName & Thumbnail

`ArenaDefinition` already has `DisplayName`. Add a `PreviewColor` field (a hex string or `Color32`) as a placeholder thumbnail tint until real screenshot assets exist:

```csharp
public string PreviewColor; // e.g. "#2a4a2a" for pit (green), "#1a1a2e" for training (dark)
```

Real stage thumbnails (512×288 PNG, stored in `Resources/StagePreviews/`) can replace the colour placeholder later without touching the screen code.

---

## AbilitySpec — Display Fields

Add two string fields to `AbilitySpec` in `src/Shared/AbilitySpec.cs`:

```csharp
public string DisplayName;   // e.g. "Grapple", "Bazooka"
public string Description;   // e.g. "Grab & throw enemy", one line
```

Fill these in `MankiData.cs` and `FightGuyData.cs`. `CharSelectController` reads them for the ability cards in Panel 2.

---

## Build Settings

Add four new scenes to `ProjectSettings/EditorBuildSettings.asset` in order:

```
0: Scenes/MainMenu
1: Scenes/Lobby
2: Scenes/CharSelect
3: Scenes/StageSelect
4: Scenes/Arena_Offline
5: Scenes/Arena_PvP
```

Scene 0 (`MainMenu`) is the entry point for standalone builds.

---

## Out of Scope (This Spec)

- Room codes / matchmaking server — Phase 5 per netcode roadmap
- In-lobby chat — future community feature
- Party system — future community feature
- Server-side stage broadcast to clients — Phase 2
- Opponent character shown in CharSelect — Phase 2 (requires lobby sync protocol)
- Sound / music on menu screens
- Character unlock system
- Settings / options screen

---

## Acceptance Criteria

1. Launching the game opens `MainMenu`, not a match scene
2. Training path: Main Menu → CharSelect → StageSelect → Arena_Offline loads with selected char and arena, no Inspector overrides needed
3. PvP Host path: Main Menu → Host → Lobby shows "P2: Waiting…", Start button loads CharSelect → StageSelect → Arena_PvP with selected char and arena
4. PvP Join path: Main Menu → Join → enter IP → Lobby shows "P1: Connected" → client can proceed to CharSelect → StageSelect → Arena_PvP
5. Selecting a character in CharSelect updates the 3D preview and ability cards
6. Stage cards in StageSelect reflect all arenas in `ArenaRegistry`
7. `MatchConfig.PlayerClass` and `MatchConfig.ArenaName` are set correctly before match scenes load — verified by a log in `MatchBase.OnMatchStart()`
