# SLOPArena — PvP Demo Roadmap (v2)
> **Historical execution plan.** Preserved as a dated decision/implementation record, not a current checklist. Current product work follows the [playable friends demo reset](2026-09-05-playable-demo-reset.md). Revalidate historical status claims against current code before reuse.


> **Status:** Rewritten 2026-08-01. Replaces `2026-07-03-pvp-roadmap.md` (archived below as Appendix A).
> **Goal:** Online PvP demo — 2-4 players, server browser, lobby room, stock mode, over UDP.
> **ADRs:** [0003](../adr/0003-server-browser-over-matchmaking.md)–[0008](../adr/0008-lobby-room-match-flow.md) define the architecture.
> **Repos:** SLOPArena (this repo: client + game server + shared) · [SlopArena-MasterServer](https://github.com/Binoui/SlopArena-MasterServer) (matchmaking/meta API).
 
**Post-demo architecture direction:** ADR-0022–ADR-0028 establish the Workshop-first
content boundary, deterministic creator primitives, package compatibility, online
content synchronization, built-in capability versioning, and the open-source ModKit.
These decisions constrain new systems now but do not add Workshop shipping
requirements to this PvP demo roadmap.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────┐
│                    Master Server (SignalR + REST)         │
│  - POST /auth/guest → JWT + temp SteamId                 │
│  - GET /servers → server browser list                    │
│  - LobbyHub (SignalR): lobby state, char select, chat-ready│
│  - POST /match/result → ELO/MMR (already exists)         │
└──────────┬───────────────────────────────┬──────────────┘
           │ SignalR (lobby/meta)           │ HTTP (match lifecycle)
           │                               │
    ┌──────┴──────┐                ┌───────┴───────┐
    │  Client A   │                │  Game Server   │
    │  (Unity)    │◄───UDP 60Hz───►│  (ServerApp)   │
    │             │  match loop     │  MatchInstance │
    └─────────────┘                └───────────────┘
           │                               ▲
           │ SignalR                       │ subprocess
    ┌──────┴──────┐                ┌───────┴───────┐
    │  Client B   │◄───UDP 60Hz───►│  (same process │
    │  (Unity)    │  match loop     │  as Client A   │
    └─────────────┘                │  if hosting)   │
                                   └───────────────┘
```

**Two protocols, clear boundary:**
- **SignalR (master server)** — auth, server browser, lobby room, character select, results, chat.
- **UDP (game server)** — match simulation only. Countdown → fight → match-end signal.

---

## What Already Exists (audited 2026-08-01)

| Area | Status | Detail |
|---|---|---|
| Game server simulation | ✅ | `MatchInstance`: 60Hz, input buffering, 3-stock, match lifecycle, timeout |
| Game server orchestration | ✅ | `MultiMatchOrchestrator`: port allocation, 15 concurrent matches |
| Game server registration | ✅ | `GameServerRegistration`: registers + heartbeats to master server |
| Master server API | ✅ Partial | Server register, heartbeat, match result + ELO. **Missing:** guest auth, server browser list, SignalR lobby hub |
| Combat pipeline | ✅ | ServerAbility lifecycle, HitboxEvent, SpellResolver, 24 ability subclasses across 4 chars |
| Character roster | ✅ Partial | Manki ~70%, FightGuy ~70%, Kistu ~30%, Nilus ~1%. All have full data-driven kits; art/anim varies |
| Client network (UDP) | ✅ | `NetworkClient`: UDP, receive thread, ConcurrentQueue, `SendInput`, `ReceiveStates` |
| Client sim bridge | ✅ | `ISimulationBridge`, `NetworkSimulationBridge` (raw state display), `LocalSimulationBridge` (training) |
| Client PvP scene | ✅ | `Arena_PvP.unity` with `PvPMatch`, NetworkClient, HUD, spawn points |
| Client UI flow | ✅ | MainMenu → Lobby → CharSelect → StageSelect → Match |
| Client HUD | ✅ Partial | Single-player damage % + cooldowns. **Missing:** opponent %, stocks, 2-4 player layout |
| Client input | ✅ | `InputController`: single Keyboard/Mouse per client (correct for online — each instance is one player) |
| Prediction/rollback | ❌ Absent | `NetworkSimulationBridge` explicitly says "Phase 1: no prediction/rollback." Not blocking the demo — raw state display is playable on localhost/LAN |
| Server browser | ❌ Missing | No `GET /servers` endpoint, no client browser UI |
| Lobby room (SignalR) | ❌ Missing | No lobby hub, no lobby UI, no char-select sync |
| Guest auth | ❌ Missing | No `/auth/guest` endpoint, no client auth flow |
| 2-4 player support | ❌ Missing | `MatchInstance` hardcoded for 2 players (entity IDs 1-2) |
| Character-to-server | ❌ Missing | Server hardcodes both players to `CharacterClass.Manki` |
| Stock mode | ❌ Missing | Server tracks deaths but has no stock count or last-standing win check |
| Client MatchState handling | ❌ Missing | `PvPMatch` never reads `MatchState` field from packets |
| Chat | ❌ Deferred | SignalR hub will be ready for it; not built now |

---

## Phase Decomposition

### Phase 0 — Foundation Fixes (prerequisite, no new features)

Fix blockers that prevent any PvP work. Each is small and independently verifiable.

| Task | What | Files |
|---|---|---|
| **0.1** | Fix port mismatch: `MatchConfig.ServerPort=9876` vs orchestrator base port. Align to one value. | `MatchConfig.cs`, `ServerConfig.cs` |
| **0.2** | Fix `_aimHandler` unwired in `Arena_PvP.unity` (assigned `{fileID: 0}`). | `Arena_PvP.unity` |
| **0.3** | Client reads `MatchState` from packets and surfaces it (countdown/fight/results). | `PvPMatch.cs`, `NetworkClient.cs` |
| **0.4** | Fix `CharacterStatePacket` serialization — verify all PvP-relevant fields round-trip (the old roadmap's P0.1/P0.2 — may already be done). | `CharacterStatePacket.cs` |

**Verify:** Two clients connect to `ServerApp`, both see each other move, `MatchState` transitions visible in client console.

---

### Phase 1 — Guest Auth + Server Browser

**Deliverable:** Player authenticates as guest, sees a list of game servers, can join one.

| Task | What | Repo | Files |
|---|---|---|---|
| **1.1** | `POST /auth/guest` endpoint on master server: generate temp SteamId (Guid), create User row, issue JWT. | MasterServer | `Program.cs`, `DTOs/`, `Data/Models/User.cs` |
| **1.2** | `GET /servers` endpoint: list heartbeat-fresh (last beat < 15s), not-full game servers. | MasterServer | `Program.cs` |
| **1.3** | Client auth flow: on launch, request guest JWT, store it, include as Bearer in all master server calls. | SLOPArena | `Runtime/Network/MasterServerClient.cs` (new) |
| **1.4** | Client server browser UI: query `GET /servers`, display list (name, region, players x/max, ping), join button. | SLOPArena | `Runtime/UI/ServerBrowserUI.cs` (new), `UI/server-browser.uxml` (new) |
| **1.5** | Wire server browser into menu flow: MainMenu → Server Browser → (join selected server's lobby). | SLOPArena | `MainMenuUI.cs`, scene flow |

**Verify:** Launch client → guest auth → server browser shows registered game servers → click join.

---

### Phase 2 — SignalR Lobby Hub

**Deliverable:** Players join a lobby, see each other, host can start.

| Task | What | Repo | Files |
|---|---|---|---|
| **2.1** | `LobbyHub` SignalR hub on master server: `JoinLobby(serverId)`, `LeaveLobby()`, player list push, `HostStart()` → signals game server. | MasterServer | `Hubs/LobbyHub.cs` (new), `Program.cs` |
| **2.2** | Match lifecycle bridge: master server → game server "start match" (HTTP or SignalR backchannel). Game server → master server "match ended" with results. | Both | `MultiMatchOrchestrator.cs`, `GameServerRegistration.cs` |
| **2.3** | Client SignalR client: connect to `LobbyHub`, handle join/leave/start events. | SLOPArena | `Runtime/Network/LobbyClient.cs` (new) |
| **2.4** | Client lobby room UI: player list (2-4 slots), host "Start" button, leave button. Chat-ready placeholder. | SLOPArena | `Runtime/UI/LobbyRoomUI.cs` (new), `UI/lobby-room.uxml` (new) |
| **2.5** | Wire lobby into scene flow: Server Browser → join → Lobby Room → (host starts) → Char Select. | SLOPArena | Scene management |

**Verify:** Two clients → both in lobby → both see each other in player list → host clicks Start → both transition to char select.

---

### Phase 3 — Character Select + Match Start

**Deliverable:** Players pick characters in-lobby, match starts with correct characters.

| Task | What | Repo | Files |
|---|---|---|---|
| **3.1** | In-lobby character select via SignalR: each player picks, selection broadcasts to all. Lock-in. | MasterServer | `LobbyHub.cs` |
| **3.2** | Client char select UI (multiplayer): grid of 4 chars, shows other players' picks, lock-in button. | SLOPArena | `Runtime/UI/LobbyCharSelectUI.cs` (new/extend `CharSelectController.cs`) |
| **3.3** | Master server sends player list + character classes to game server at match start. | Both | `LobbyHub.cs`, `MultiMatchOrchestrator.cs`, `MatchInstance.cs` |
| **3.4** | Game server reads character classes (not hardcoded Manki). Spawns entities with correct `CharacterClass`. | SLOPArena | `MatchInstance.cs` |
| **3.5** | Client connects to game server UDP on match start (using server IP:port from lobby). | SLOPArena | `PvPMatch.cs`, `NetworkClient.cs` |

**Verify:** Two clients → lobby → char select → both pick different characters → match starts → each client renders the correct character for each player.

---

### Phase 4 — 2-4 Player Match Support

**Deliverable:** Game server and client handle 2-4 players in a match.

| Task | What | Repo | Files |
|---|---|---|---|
| **4.1** | Generalize `MatchInstance` from 2 to N players: dynamic entity IDs (1-4), per-player input queues, broadcast all states to all clients. | SLOPArena | `MatchInstance.cs` |
| **4.2** | Stock mode: `StockCount` per entity, decrement on KO, last-standing win check, eliminated players go spectating. | SLOPArena | `MatchInstance.cs`, `Simulation.cs`, `CharacterState.cs` |
| **4.3** | Client HUD for 2-4 players: damage % and stocks for all players, player-name labels. | SLOPArena | `HUDManager.cs`, `HUD.uxml` |
| **4.4** | Client renders 2-4 entities: all opponent renderers, not just one. | SLOPArena | `PvPMatch.cs`, `EntityRegistry` |
| **4.5** | Respawn logic for stock mode: respawn at available spawn point, brief invincibility. | SLOPArena | `MatchInstance.cs`, `Simulation.cs` |

**Verify:** 3 clients → lobby → char select → match → 3 characters visible → KOs decrement stocks → last player standing wins → results → back to lobby.

---

### Phase 5 — Embedded Host-and-Play

**Deliverable:** Player hosts a game server from the Unity client, others join via server browser.

| Task | What | Repo | Files |
|---|---|---|---|
| **5.1** | Unity spawns `ServerApp` as subprocess: pass config (port, master server URL), monitor process, graceful shutdown on quit. | SLOPArena | `Runtime/Network/ServerHost.cs` (new) |
| **5.2** | Host flow UI: "Host" button in server browser → starts server → registers with master → auto-join own lobby. | SLOPArena | `ServerBrowserUI.cs`, `ServerHost.cs` |
| **5.3** | Host connects to localhost for the match (same as any client, just 127.0.0.1). | SLOPArena | `PvPMatch.cs` |

**Verify:** Client A clicks Host → appears in server browser → Client B joins → both in lobby → match plays.

---

### Phase 6 — Results + Return to Lobby

**Deliverable:** Match ends → results screen → all players return to lobby.

| Task | What | Repo | Files |
|---|---|---|---|
| **6.1** | Game server detects winner → signals master server with results. | SLOPArena | `MatchInstance.cs`, `MultiMatchOrchestrator.cs` |
| **6.2** | Master server pushes results to all clients via SignalR. | MasterServer | `LobbyHub.cs` |
| **6.3** | Client results UI: winner display, stock counts, "Return to Lobby" (auto after delay). | SLOPArena | `Runtime/UI/ResultsUI.cs` (new) |
| **6.4** | Game server resets to waiting-for-start state. Players return to lobby room. | Both | `MatchInstance.cs`, `LobbyHub.cs` |

**Verify:** Match ends → results screen → all clients return to lobby → host can start another match.

---

### Phase 7 — Prediction + Rollback (post-demo, smoothness)

**Deliverable:** Smooth local movement with server reconciliation. No rubber-banding.

Not blocking the demo — raw state display is playable on localhost/LAN. This is the old roadmap's Phase 2, unchanged. See `2026-07-03-pvp-roadmap.md` Appendix A for the detailed task breakdown.

---

## Dependency Graph

```
Phase 0 (Fixes)
    │
    ▼
Phase 1 (Auth + Browser) ──── Phase 5 (Host-and-Play)
    │                              │
    ▼                              │
Phase 2 (Lobby Hub) ◄─────────────┘
    │
    ▼
Phase 3 (Char Select + Start)
    │
    ▼
Phase 4 (2-4 Player Match)
    │
    ▼
Phase 6 (Results + Return)
    │
    ▼ (post-demo)
Phase 7 (Prediction/Rollback)
```

- Phase 0 is prerequisite to all.
- Phase 1 and 5 can start in parallel after Phase 0 (auth/browser vs host subprocess — different codebases).
- Phases 2→3→4→6 are sequential (each builds on the last).
- Phase 7 is post-demo polish.

---

## Cross-Repo Contracts

Two repos must agree on data shapes:

| Contract | Master Server | SLOPArena |
|---|---|---|
| Guest auth response | `POST /auth/guest` → `{ token: string, steamId: string }` | `MasterServerClient.AuthGuest()` |
| Server list item | `GET /servers` → `[{ id, name, ip, port, region, currentPlayers, maxPlayers, isOfficial }]` | `ServerBrowserUI` renders |
| Lobby hub messages | `JoinLobby`, `LeaveLobby`, `PlayerJoined`, `PlayerLeft`, `HostStart`, `CharSelected`, `MatchStarted`, `MatchEnded` | `LobbyClient` handles |
| Match start payload | `{ matchId, players: [{ steamId, characterClass, entityId }] }` | `MatchInstance` receives |
| Match result payload | `{ matchId, winnerSteamId }` (already exists as `MatchResultRequest`) | `MatchInstance` sends |

---

## Testing Strategy

- **Phase 0:** Two Unity instances + `ServerApp`. Console log verification.
- **Phase 1-2:** Master server running locally (PostgreSQL). Client auth + browser + lobby.
- **Phase 3-4:** 2-4 Unity instances + master server + game server. Full match flow.
- **Phase 5:** One Unity instance hosts, another joins.
- **Phase 6:** Play to completion, verify results + lobby return.
- **Continuous:** `dotnet test tests/Shared.Tests/` after every shared code change.
- **Master server:** xUnit tests for endpoints (new test project or in-repo).

---

## Appendix A: Archived Roadmap (2026-07-03)

The previous roadmap is preserved at `docs/plans/2026-07-03-pvp-roadmap.md` (original content, marked superseded at top). Its Phase 1 (PvP Bridge) and Phase 4 (UI Flow) shipped. Its Phase 2 (Prediction/Rollback) is now our Phase 7. Its Phase 3 (Polish) and Phase 5 (FightGuy) are character/art work, tracked separately from this PvP milestone. Its Phase 6 (Hardening: handshake, packet loss, reconnect) remains future work after the demo.
