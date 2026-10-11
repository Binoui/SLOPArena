# SlopArena

3D platform fighter (Smash/DKO-style) with server-authoritative 60Hz simulation.

## Language

**AirTime**:
An airborne tick counter that determines fall gravity phase. Incremented each tick while airborne. Reset behavior is conditional:
- **0** on: the RecoveryMove, taking damage, landing (re-grants FloatWindow; aerial attacks no longer reset — ADR-0015)
- **FloatWindowTicks** on: any jump (ground or double). JumpArc handles the ascent visually, so the float window is skipped — full gravity applies immediately after jump.
_Avoid_: air timer, hang time, air duration

**FloatWindow**:
The initial period of AirTime during which reduced (`AirFloatGravity`) gravity applies. Post-ADR-0015: entered only via the RecoveryMove, taking damage, or landing — normal air attacks no longer reset into it (momentum-preserve attacks). Per-character value.
_Avoid_: hover time, stall window, float duration


**GravityWindow**:
A move-local timeline interval that multiplies the currently active airborne gravity by a
normalized scale from 0 to 1. It does not reset the FloatWindow or change MaxFallSpeed.
FastFall overrides it; Hitstop pauses its duration, and interruption ends it.
_Avoid_: FloatWindow reset, fall-speed cap change

**FallRamp**:
~~Deprecated — removed by ADR-0020 (gravity ramp → float-window-only).~~ The old progressive fall-speed acceleration from FloatWindow gravity to full gravity.
_Avoid_: gravity ramp, fall acceleration curve

**FallGravity**:
The full gravity constant applied once the FloatWindow ends. Equivalent to `MovementStats.Gravity`.
_Avoid_: terminal velocity, max gravity

**FallRampDuration**:
~~Deprecated — removed by ADR-0020.~~ The old per-character ramp tick count.
_Avoid_: ramp time, acceleration window

**JumpArc**:
The complete animation clip played during a single jump (ground or double). Plays through both ascent and baked descent. The character transitions to the Fall animation only when the JumpArc clip finishes while still airborne — not at the physics peak. Enables a clean, full jump animation without an abrupt mid-air switch.
_Avoid_: jump animation, jump loop, air state

**JumpArcActive**:
A per-entity bool (client-side) tracking whether the JumpArc clip is currently playing. Cleared on landing, aerial attack, or hitstun.
_Avoid_: isAscending, wasAscending, jump state

**Warp**:
~~Deprecated — removed by ADR-0015 (timing-model pivot).~~ The auto-dash during attack initiation that moved the entity toward the target at SprintSpeed. Its dormant runtime machinery was deleted in the 2026-10-11 simulation cleanup. The compiler and cooked loader reject attempts to enable it.
_Avoid_: (no longer used)

**WarpRange**:
~~Deprecated — ADR-0015.~~ The per-attack trigger distance for Warp. The source/cooked field remains reserved at zero for content compatibility; it has no runtime behavior.
_Avoid_: (no longer used)

**WarpCone**:
~~Deprecated — ADR-0015.~~ The 120° forward-facing cone within which Warp could target. Legacy stage tracking was also deleted; current aim correction belongs to its explicit cooked operation.
_Avoid_: (no longer used)

**ShortHop**:
A reduced jump triggered by releasing the jump key within a short window (3–5 ticks) of pressing it. Tap = short hop, hold = full jump. The spacing/approach tool that makes neutral readable (Melee-style). Release-timing is digital-optimal on keyboard (ADR-0016).
_Avoid_: mini jump, light jump, tap jump

**FastFall**:
A fresh press of the dedicated Down action while already descending commits to the character's fast-fall speed until landing or an interrupting action. Release does not cancel it; ascent/apex presses are discarded. Backward movement is independent. Ordinary aerial attacks may coexist; Hitstun, Hitstop, ledges and authored vertical motion cannot be overridden.
_Avoid_: dive, plummet, down air

**Crouch**:
A held-Down grounded low posture. It brakes residual movement without steering or crouch-walking. Release restores ordinary control immediately; legal actions retain their existing admission rules.

**Slide**:
A grounded low posture that spends existing horizontal momentum without an entry impulse or steering. A fresh Down press while running, or held Down on an actionable moving landing, can enter it. It ends on release, insufficient speed, walk-off or an accepted action.

**SlideJump**:
An ordinary grounded jump accepted from Slide. It preserves remaining momentum through the normal jump squat without refilling speed or changing jump cost, vertical force or ShortHop timing.

**CrouchBrace**:
Reduced ordinary launch strength for a fighter already settled motionless in Crouch when a hit connects. The contact snapshot survives Hitstop and release; damage, freeze and Hitstun remain unchanged. Sliding, entry/braking crouch and fixed/custom forces do not qualify.

**Run**:
The single ground locomotion tier (ADR-0020 §1 — replaces the old walk/sprint split; Melee's "dash" tier is NOT adopted). Reached instantly from the Rush. Releasing brakes to a stop fast (`GroundStopFriction`, 36 m/s² — no semi-truck drift). Changing axis while at run speed is an instant redirect — the perpendicular velocity is cleared, never carried between axes (no diagonal drag), and reversing direction snaps immediately to `RunSpeed`. No selectable walk speed on 8-way input.
_Avoid_: walk, sprint (the deleted two-tier model)

**Rush**:
The reversal-free burst that starts a Run from a standstill — a fixed window (`RushTicks`, ~10 ticks) during which velocity is at `RunSpeed` immediately. Reversing within the window is an instant full-speed flip that restarts it; a perpendicular (90°) redirect also restarts the window. Releasing inside the window stops dead (no drift — a tap is a fixed burst, not a slide). Holding one direction steady past the window enters Run proper; reversals remain immediate.

**Turnaround**:
~~Removed — grounded reversals are now immediate after the Rush window.~~

**Shield**:
A permanent, full-body defensive state entered by holding Defense (Left Shift / RT) while actionable and grounded. It blocks ordinary attacks from every direction and freezes combat facing. Releasing enters a vulnerable 7-tick ShieldDrop; contact adds block stun and hitstop, and releasing during stun starts the full drop only after stun expires.
_Avoid_: directional block

**LedgeHang**:
Disabled for the friends demo: the Shared simulation does not enter this state on an edge approach. Fighters fall past ledges and must recover onto a stage surface. The old state value and packet fields remain reserved for compatibility; ADR-0020 §4 records the earlier design, not the current demo rule.
_Avoid_: ledge grab (the action), edge hang, tether

**Forward Air Dodge**:
A fresh airborne Defense press commits to ten ticks of facing-locked horizontal
movement, with invulnerability for the first five ticks, then twenty ticks of
vulnerable recovery. It spends one air dodge until a genuine landing or respawn.
It retains ordinary gravity and cannot turn, attack, jump or shield-cancel
before recovery ends. Landing ends movement but not remaining commitment;
ledge grabs cannot interrupt recovery while disabled.
_Avoid_: air dash, directional dodge, wavedash

**RecoveryMove**:
The per-character dedicated upward/diagonal burst used to return to the stage after being knocked out — one Slot per kit, long cooldown. The only move that resets the FloatWindow; normal air attacks no longer do (ADR-0015).
_Avoid_: up-B, getup move, escape move

**Clash**:
The symmetric commit resolution (phase 2, ADR-0015): two simultaneous Interruptible hitboxes connecting within a few ticks resolve as a mutual bounce — no damage, short mutual stun + pushback, reset to neutral — instead of a random trade. The timing-model substitute for whiff-punish in free-camera 3D.
_Avoid_: parry (Wibou-specific), counter, trade

**Slot**:
One canonical move entry in the 16-entry grid: ground/air × `1`-`4`, `A`, `E`, `R`, `F`. The kit has twelve button concepts—eight normals and four specials—each with grounded and aerial entries. `LMB` and `RMB` are camera controls, not attack slots. Normals are single moves; there are no automatic LMB chains or charged RMB attacks. `A` is the signature special, `E` is recovery-capable mobility, `R` is the playmaking special, and `F` is the long-cooldown power special.
_Avoid_: button, ability slot, move slot

**Move (UI label)**:
The human-facing name for a **Slot** in Ability Lab. It never creates a second persisted domain object; selection and editing resolve directly to the canonical Slot identity.
_Avoid_: move ID, move slot

**Knockback**:
The launch velocity applied when hitting a target. Combination of base push and damage-scaling growth (higher % = further launch). Uses frontloaded exponential decay (λ = 1.8/s): the launch is fastest right after the hit and smoothly slows — most travel happens early, the victim drifts in the tail. Decaying all axes also flattens launch arcs. Profile table maps archetypes to angle/base/growth:
- **Light**: 15°, base=2, growth=1.5 — combo glue, slight pop
- **Medium**: 15°, base=8, growth=5 — knockdown, reset
- **Launcher**: 25°, base=8, growth=4 — pop-up, stays on screen
- **Kill**: 20°, base=18, growth=10 — blast zone send
- **Spike**: -45°, base=12, growth=4 — downward, grounded bounce
Any attack can also use a per-hit **Custom** profile with its own angle/base/growth overrides.
_Avoid_: push force, hit reaction, knockback velocity

**Hitstun**:
The victim-side no-action lock after a damaging hit. Inputs buffer according to simulation rules; residual launch continues after the lock expires.

**Hitstop**:
The brief per-pair freeze when an attack connects or is blocked — not global; the match clock keeps running. Damaging knockback launches only when its freeze ends. Block hitstop does not queue damaging launch or DI effects.
_Avoid_: hitlag (Melee connotation), freeze, hit pause

**Duration Lock**:
A fixed-tick state during which a character cannot act. Two kinds: the attacker's attack commitment (`AnimLockTicks`, from startup through recovery) and the victim's Hitstun or block stun.
_Avoid_: endlag, animation lock, commitment lock

**Combo Influence**:
The defender's launch-drift input — additive velocity applied to remaining horizontal knockback in the held direction, scaled to the launch magnitude. Captured during Hitstop + Hitstun, applied when the lock expires. Additive (Smash-4 vectoring model), not rotational (Smash DI) — 3D-native: push where you want to drift.
_Avoid_: DI (Smash rotation connotation), vectoring (Smash 4), smash DI

**BlockStun**:
The defender-side action lock after a blocked contact. Its duration is `clamp(ceil(0.6 × incoming damage) + 2, 4, 15)` ticks; overlapping contacts retain the greater remaining duration rather than stacking.
_Avoid_: damaging Hitstun

**FloatWindowReset**:
The restoration of FloatWindow gravity by setting AirTime to 0 mid-air. Triggered by: the RecoveryMove, taking damage, or landing (ADR-0015: aerial attacks no longer reset it — that was the hover crutch). Without a reset, the character progresses into full gravity (the old FallRamp is removed by ADR-0020).
_Avoid_: air reset, float restore, hover refresh

**CPU Opponent**:
A computer-controlled fighter intended to challenge a human player. It uses its kit, pursues combos, and recovers, with mistakes appropriate to its difficulty.
_Avoid_: AI player

**CPU Reaction Delay**:
The nonzero time between an opponent action becoming observable and a CPU Opponent responding to that observation. It delays new opponent information, not execution of actions already selected from earlier observations.
_Avoid_: input lag, decision interval

**CPU Observation**:
Information about an opponent's combat state that a CPU Opponent can use after its CPU Reaction Delay. It can include exact remaining Hitstun and attack-recovery times, but never the opponent's input commands.
_Avoid_: input reading

**True Combo**:
A sequence of hits against one defender with no opportunity for an ordinary action between hits. Shield and Grab provide defensive/action options according to their normal state gates.
_Avoid_: pressure string, hit streak

**Pressure String**:
A sequence of attacks that maintains pressure while leaving the defender opportunities to act between attacks. It is not a True Combo.
_Avoid_: true combo, guaranteed follow-up

**Stage Recovery**:
The return from offstage to a stage surface. It can combine normal movement, jumps, and a character's RecoveryMove or other kit mobility; ledge grabs are disabled for the demo.
_Avoid_: attack recovery, RecoveryMove (when referring to the whole return)

**Stage-side Edgeguarding**:
Attacks against an offstage opponent from the stage or a ledge, without pursuing that opponent beyond the stage edge.
_Avoid_: offstage pursuit

**CPU Difficulty**:
One of three named CPU skill tiers: Easy, Normal, and Hard. All tiers retain the full functional character move repertoire; reaction speed, move selection, spacing, and follow-up reliability distinguish them.
_Avoid_: CPU level, 1–9 scale

**Character Kit**:
A character's grounded and aerial normals and specials, identified by the canonical Slot grid. Universal mechanics such as Shield, Grab, air dodge, and Combo Influence are not part of the Character Kit.
_Avoid_: control scheme

## PvP / Multiplayer

**ServerBrowser**:
The normal Unity directory of named public Rooms from Master. It shows every Room phase, member count and whether joining is allowed; only Lobby Rooms with free slots accept new members. Physical GameHosts are allocated at Match start, not chosen in this directory. Explicit development host/address tools are separate.
_Avoid_: matchmaking queue, GameHost list

**Room** (LobbyRoom in the UI):
The Master-managed, named group of up to four players. Membership and Server Chat survive Character Select, Arena selection, a Match, Results and rematch. The leader advances preparation; the Room admits new members only in Lobby. Master returns the matching Room to Lobby and clears preparation on a completed or canceled Match, without erasing membership or chat.
_Avoid_: physical GameHost lobby, Match, waiting roster

**GuestAuth**:
Access to SlopArena under a temporary player identity without Steam authentication. A guest identity is not a Steam identity.
_Avoid_: dev login, anonymous auth, temp account

**SlopArena Chat**:
In-game text communication between SlopArena players, separate from Steam's chat system.
_Avoid_: Steam chat (when referring to this feature)

**Global Chat**:
The SlopArena-wide chat channel, not restricted to one GameServer, LobbyRoom, or Match.
_Avoid_: server chat

**Server Chat**:
The SlopArena Chat channel shared by members attached to the same Master Room. A Room's Server Chat is isolated from other Rooms and from physical GameServer lobby membership.
_Avoid_: lobby chat, match chat

**Direct Message**:
A SlopArena Chat message visible only to its sender and recipient, both connected to chat. Direct Messages are not queued for offline delivery.
_Avoid_: inbox, offline message

**Social Dock**:
The menu's reserved area for SlopArena Chat, separate from the current match-preparation page. On compact layouts, a persistent social strip provides access to a chat view that temporarily replaces the page.
_Avoid_: chat overlay (for the menu dock)

**Unread Message**:
An incoming, unmuted SlopArena Chat message not yet acknowledged by viewing its conversation at the newest messages while the game has focus and the conversation is unobscured. A brief combat preview alone does not acknowledge the message.
_Avoid_: notification (when referring to unread conversation state)

**Guest Session**:
A player's visit to SlopArena during one game launch, with one temporary identity across chat reconnects. A later launch starts a new Guest Session.
_Avoid_: persistent account

**Display Name**:
A player's chosen name shown in SlopArena Chat, entered on first launch and remembered for later launches. Display Names need not be unique; remembering a name does not preserve a player identity across Guest Sessions.
_Avoid_: player ID

**Session Tag**:
A short identifier that distinguishes a player alongside their Display Name for one Guest Session.
_Avoid_: Steam ID

**Channel Backlog**:
Recent Global Chat or Server Chat messages available to players who join after those messages were sent. Direct Messages are not part of a Channel Backlog.
_Avoid_: offline inbox

**Personal Mute**:
A player's choice to hide a sender's messages and alerts across SlopArena Chat for the current game launch. It applies to the sender's identity, not their Display Name.
_Avoid_: ban, account block

**StockMode**:
The win condition for PvP matches. Each player starts with N stocks (default 3). Getting KO'd (void death or blast zone) costs one stock. A player with 0 stocks is eliminated. Last player with stocks remaining wins. Scales naturally from 2 to 4+ players.
_Avoid_: lives mode, stock battle, elimination mode

**HostAndPlay**:
The embedded host model where a player starts the game server from the Unity client (subprocess), registers it with the master server, and plays on the same machine (connecting to localhost). The demo targets this at technical players only: the host machine must be reachable from outside (port forwarding or LAN), which non-technical players won't do. The server binary and registration flow are identical to a dedicated server.
_Avoid_: listen server, client-hosted, peer-to-peer host

**OfficialServer**:
An operator-run, always-on GameHost available to Master for Match allocation. Players do not choose it from the normal ServerBrowser; `isOfficial` remains a registration property.
_Avoid_: public Room, dedicated Room, hosted lobby

**MatchFlow**:
The lifecycle of a PvP group: ServerBrowser → Room (Lobby) → Character Select → Stage Select → Match Starting → Fight → Results → the same Room (Lobby), if membership remains valid. Master owns Room preparation and its terminal transition; the GameHost runs the authoritative Match and sends its result or cancellation. Results remains viewable until the player returns; leaving or Room expiry routes to the browser with an explanation. A rematch requires new selections and Lock-in.
_Avoid_: physical-server lobby flow, automatic rematch

## Prediction & Rollback

**ConfirmedTick**:
The highest match tick for which the client holds a full server-authoritative snapshot for every non-self entity. The base state PredictedTrack rebuilds from; it advances as state packets arrive, one tick at a time. Does not apply to LocalTrack — the self entity is never rebuilt from a snapshot.
_Avoid_: ack tick, sync tick, last confirmed

**RollbackWindow**:
The span of ticks between ConfirmedTick and the currently rendered tick, replayed on PredictedTrack entities whenever the confirmed base advances or a mismatch is corrected. Applies only to opponents currently in a Predictable ActionState — RawTrack entities have no window (they render the latest packet directly), and LocalTrack has no window (it never rebuilds).
_Avoid_: rewind window, prediction buffer, lag window

**InputRelay**:
The server broadcasting each entity's consumed InputState — or an explicit no-input marker when the server had nothing to consume for it that tick (empty queue → drop → `default(InputState)`) — alongside its state packet, so every client can replay opponents' exact inputs *and omissions* during re-simulation. Feeds PredictedTrack's replay; LocalTrack uses the player's own true input buffer instead.
_Avoid_: input forwarding, input piggyback, input echo

**LocalTrack**:
The self entity's `ServerSimulation`, run continuously on the client from match start — never rebuilt from a received snapshot, fed the player's own true InputState every tick. Corrected only by snapping the wire-serialized fields when a received packet disagrees; fields absent from the wire (attack timers, knockback, ability-instance state) never diverge because this track is never reconstructed from a lossy snapshot.
_Avoid_: self-prediction, client-side prediction (ambiguous with PredictedTrack), own-entity sim

**PredictedTrack**:
An opponent entity currently in a Predictable ActionState. Rebuilt from ConfirmedTick's snapshot and replayed forward tick-by-tick using InputRelay data (or the no-input marker) up to the current local tick. Diverges only at the frontier (ticks past the last received relay), corrected by snap on the next packet.
_Avoid_: opponent prediction, ghost sim, replayed entity

**RawTrack**:
An opponent rendered from received state rather than local re-simulation while its action or Hitstop requires private simulation state. Returns to prediction only when its confirmed state is reconstructible.
_Avoid_: unpredicted entity, fallback display, snap-only entity

**Predictable ActionState**:
An action whose movement can be reproduced from confirmed state and exact input history. Idle, JumpSquat, Run, Sliding, Crouching, Shielding, ShieldDrop, GrabAttempt, detached Throwing recovery, Thrown flight/recovery and the forward air-dodge movement/recovery phases qualify only without active Hitstop, block-hitstop or a coupled interaction.
_Avoid_: safe state, simple state, movement state

**Complex ActionState**:
An action requiring private ability/resolver state or paired server authority. Attacking, Hitstun, LedgeHang and linked Grabbing/Grabbed/Throwing render from authoritative state; the self track uses a targeted barrier for confirmed capture or block contact instead of replaying through a coupled interval. The retired `Warping` numeric value remains reserved for wire compatibility.
_Avoid_: unsafe state, hard state, ability state


## Game Server (src/Server)

**GameHost** (GameServer process):
The dedicated .NET console process in `src/Server/` that registers and heartbeats with Master, accepts Match start commands, and runs 2–4-player server-authoritative Matches (multiple concurrently). Master assigns a compatible GameHost only when a Room launches; clients receive its route for gameplay, not Room membership or Server Chat.
_Avoid_: Room, server (ambiguous), match server

**MatchControlServer**:
The HTTP control plane on the GameServer. Listens on TCP at the registered base port and exposes `POST /match/start` for the master server. Parses the roster, asks the orchestrator for a port, and replies with it. UDP matches bind base+offset, so TCP control and UDP simulation coexist on the same port number. This is the seam that keeps the GameServer stateless between matches (ADR-0008).
_Avoid_: control endpoint, match API, HTTP server

**MultiMatchOrchestrator**:
The component inside the GameServer that manages port allocation and match lifecycle across concurrent matches. Assigns each new match to the next free UDP port, tracks active MatchInstances, and reclaims ports on match end. The single owner of the match collection — nothing else spins up or tears down a match directly.
_Avoid_: match manager (collides with client-side MatchManager), server manager, match pool

**MatchInstance**:
One running match — 2-4 rostered players, one dedicated UDP port, one thread, full 60Hz ServerSimulation. Spawned by the orchestrator on match start with the roster's character classes + entity IDs; disposed on match end (winner detected or all opponents gone). Countdown starts once every rostered player has connected.
_Avoid_: match (too generic), game session, server instance

**Roster**:
The ordered player list the master server sends at match start. Each entry pairs a player's SteamId with their locked-in CharacterClass and an assigned EntityId (1..N by lobby join order). Drives entity spawning on the GameServer — replaces the old hardcoded-Manki path.
_Avoid_: player list, team, lineup

**PortAllocation**:
The scheme where each match binds a dedicated UDP port from `base_port` to `base_port + max_concurrent_matches - 1`. One match per port. The TCP MatchControlServer listens on base_port itself; UDP matches use base+offset, so the two never collide on the same number.
_Avoid_: port pool, port map, port range (too vague)

## Disambiguation: "server"

The word "server" is overloaded in SlopArena. Use these distinct terms:
- **Master server** — the separate-repo ASP.NET Core app (SignalR/REST, PostgreSQL) that owns Rooms, browser, chat and Match records. Never runs simulation.
- **GameHost** (the `GameServer` process above) — the .NET console process in `src/Server/` that hosts authoritative Matches and reports their outcomes to Master. Not a browseable Room.
- **ServerSimulation** — the pure C# tick loop in `src/Shared/` (`Simulation.cs`, `CombatMath.cs`). Runs on GameHost and client prediction tracks; not a process.

Bare "server" is ambiguous; name the owner instead.
 
## Workshop / Creator Content

**WorkshopContent**:
Creator-authored fighter, stage, or presentation content distributed through Steam Workshop. WorkshopContent references SlopArena capabilities by public identity and owns its creator-provided assets.
_Avoid_: mod, addon, user content

**BuiltInContent**:
SlopArena-provided animation, VFX, SFX, material, or gameplay capability available to WorkshopContent through a public semantic identifier. Its runtime implementation is private to SlopArena.
_Avoid_: internal asset, Unity asset, default asset

**BuiltInContentId**:
The public semantic identifier for a BuiltInContent capability. Compatible implementation replacement may retain the ID; incompatible semantics require a new versioned ID.
_Avoid_: asset path, prefab ID, resource path

**CreatorPrimitive**:
An engine-owned, versioned, deterministic gameplay capability that WorkshopContent composes for movement, combat, stage, or presentation behavior. It is not creator-executable code.
_Avoid_: mod script, plugin, custom code

**Ability (UI label)**:
The human-facing name for a typed authoring operation that invokes a character capability. It does not create a second runtime or authoring model.
_Avoid_: StartCapability, capability operation

**Source Conflict**:
The condition where loaded Working Draft source differs from package files changed externally. Saving is blocked until the user explicitly reloads or reverts.
_Avoid_: merge conflict, last-writer-wins

**WorkshopPackage**:
An immutable published unit of WorkshopContent with an identity, version, cooked runtime content hash, dependencies, compatibility declaration, and creator rights metadata.
_Avoid_: mod file, Workshop item (when exact bytes matter)

**Character Package**:
A content package containing exactly one playable Character. Package identity is Character identity; a published revision is fixed by package ID, version, and cooked-content hash.
_Avoid_: character class, fighter file, character bundle

**Character Authoring Document**:
The editable source of a Character's gameplay semantics within a Character Package. It is an input to cooking, not a runtime definition.
_Avoid_: character config, runtime JSON, CharacterDefinition

**Cooked Character Definition**:
The immutable, normalized gameplay definition produced from a Character Authoring Document and loaded for simulation.
_Avoid_: character source, authoring JSON, fallback definition

**Character Asset Catalog**:
The authoritative package mapping from local semantic asset identifiers to the source assets used during cooking.
_Avoid_: AnimConfig, resource list, asset paths

**Animation Definition**:
A package-local animation capability that pairs one visual animation with its deterministic pose track.
_Avoid_: clip name, animation file, spell animation

**Match Content Catalog**:
The immutable, match-scoped mapping from compact content handles to exact loaded package revisions.
_Avoid_: global registry, CharacterRegistry, content cache

**Content Handle**:
A compact identifier assigned during match creation to one exact entry in the Match Content Catalog. It has no stable meaning outside that match.
_Avoid_: CharacterClass, package ID, global content ID

**Stale Cook**:
The state where saved authoring inputs no longer match the source hash recorded by the last valid cooked artifact.
_Avoid_: dirty content, cache issue, stale JSON

**Working Draft**:
The current editable Character Authoring Document state in Ability Lab. It may be invalid or unsaved and is never the authoritative preview.
_Avoid_: live content, runtime definition

**Authoritative Preview**:
The Ability Lab presentation loaded from the last successfully cooked Character Package. It remains authoritative while the Working Draft is stale or cook-failed.
_Avoid_: draft preview, editor preview

**Compatibility Preview**:
A read-only Ability Lab presentation loaded through the legacy character adapter for characters not yet represented by a Character Package.
_Avoid_: legacy authoring, fallback package

**ContentRequirement**:
The exact WorkshopPackage and BuiltInContentId set a match must resolve and validate before its simulation starts.
_Avoid_: mod list, asset list, loadout

**OfficialException**:
An explicit case where official content bypasses the creator-facing content model. It records the reason, scope, owner, and migration path.
_Avoid_: special case, internal shortcut
