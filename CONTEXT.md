# SLOPArena

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

**Air Dodge**:
A fresh airborne Defense press commits to a twelve-tick horizontal burst in the
held stick direction (forward when neutral), invulnerable for its first eight
ticks, then fourteen ticks of vulnerable recovery that glides out on the burst's
exit speed. It spends one air dodge until a genuine landing or respawn. Facing
never changes; it retains ordinary gravity and cannot steer, attack, jump or
shield-cancel before recovery ends. Landing stops all horizontal movement and
caps the remaining commitment at ten ticks; ledge grabs cannot interrupt
recovery while disabled.
_Avoid_: air dash, wavedash

**RecoveryMove**:
The per-character dedicated upward/diagonal burst used to return to the stage after being knocked out — one Slot per kit, long cooldown. The only move that resets the FloatWindow; normal air attacks no longer do (ADR-0015).
_Avoid_: up-B, getup move, escape move

**Clash**:
Reciprocal active grabs resolve as a grab clash with shared recovery. Simultaneous attacks currently trade. [ADR-0015](docs/adr/0015-timing-model-combat-pivot.md) records a planned attack-Clash design for Interruptible hitboxes with mutual bounce, no damage, short stun, and pushback. That attack behavior is not implemented.
_Avoid_: parry (Wibou-specific), counter, trade

**Slot**:
One canonical move entry in the 16-entry grid, ground/air × `1`-`4`, `A`, `E`, `R`, `F`. There are eight button concepts, four normals and four specials, each with grounded and aerial entries. The grid therefore contains eight normal moves, four ground and four air, plus eight special moves. [`CanonicalSlotProjection`](src/Shared/SlotAddress.cs) defines the entries. `LMB` and `RMB` are camera controls, not attack slots. Normals are single moves; there are no automatic LMB chains or charged RMB attacks. `A` is the signature special, `E` is recovery-capable mobility, `R` is the playmaking special, and `F` is the long-cooldown power special.
_Avoid_: button, ability slot, move slot

**Move (UI label)**:
The human-facing name for a **Slot** in Ability Lab. It never creates a second persisted domain object; selection and editing resolve directly to the canonical Slot identity.
_Avoid_: move ID, move slot

**Knockback**:
The launch velocity applied when hitting a target, derived from base push, damage-scaling growth, hit damage, victim percent, and weight. Hitstun preserves the horizontal impulse except at stage contact. Vertical launch follows the pop/gravity law; after Hitstun, neutral horizontal braking is linear. See [Hitstun and DI](docs/systems/hitstun-di.md) and [`Simulation`](src/Shared/Simulation.cs). [`KnockbackProfile`](src/Shared/KnockbackProfile.cs) retains Light, Medium, Launcher, Kill, Spike, Custom, and Adaptive profiles. Cooked package hitboxes project explicit angle/base/growth values through Custom; Adaptive resolves its angle at contact.
_Avoid_: push force, hit reaction, knockback velocity

**Hitstun**:
The victim-side no-action lock after a damaging hit. Inputs buffer according to simulation rules; residual launch continues after the lock expires.

**Hitstop**:
The brief per-pair freeze when an attack connects or is blocked — not global; the match clock keeps running. Damaging knockback launches only when its freeze ends. Block hitstop does not queue damaging launch or DI effects.
_Avoid_: hitlag (Melee connotation), freeze, hit pause

**Duration Lock**:
A fixed-tick state during which a character cannot act. Two kinds: the attacker's attack commitment (`AnimLockTicks`, from startup through recovery) and the victim's Hitstun or block stun.
_Avoid_: endlag, animation lock, commitment lock

**Directional Influence (DI)**:
The defender's rotational adjustment to launch velocity. Shared captures nonzero movement input during Hitstop and rotates the launch at freeze expiry, preserving magnitude. With no Hitstop, it uses input from the hit tick. Horizontal launches rotate in azimuth; effectively vertical launches tilt toward the held direction. SDI and ASDI shift position separately. See [Hitstun and DI](docs/systems/hitstun-di.md).
_Avoid_: additive drift, Combo Influence

**Combo Influence**:
Retired additive launch-drift design from ADR-0013, superseded by rotational DI in ADR-0019. It is not current gameplay.
_Avoid_: using this term for current DI

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
A character's grounded and aerial normals and specials, identified by the canonical Slot grid. Universal mechanics such as Shield, Grab, air dodge, and rotational DI are not part of the Character Kit.
_Avoid_: control scheme

## Arcade

**Arcade**:
Solo minigames played with a selected fighter's movement and Character Kit, offering repeatable challenges and records to beat. Online Rooms can play them too (see Match Format).

**Match Format**:
What an online Match plays: a stock fight on a PvP stage, or an Arcade format such as co-op Slopball ([ADR-0040](docs/adr/0040-online-arcade-room-pick-and-match-format.md)). The Room Pick, the Match start contract and the client's Master push carry it; each format admits its own arena, places its fighters and decides when the Match ends.
_Avoid_: game mode (when referring to the online contract)

**Co-op Slopball Run**:
An online Slopball Match in which two to four fighters share the player half against the Launcher. Returns count for the team; misses reset the combo and heat without ending the run. Fighters automatically target each new ball. Friendly fire defaults off: teammates cannot damage, block or grab one another unless the leader enables it in the Room lobby before preparation. The setting stays fixed during the Match. The leader can return everyone to the same Room from the match menu, preserving membership and chat.
_Avoid_: versus volleyball (when referring to the co-op format)

**Versus Slopball Match**:
An online Slopball Match between equal Teal and Clay camps: 1v1 or 2v2. Each camp stays on its own half. A ball touching a camp's floor awards the other camp a point; first to 21 wins. Teal serves first, the conceding camp serves next, and 2v2 servers rotate within their camp. The ball waits for its server to hit it. Points reset ball percent, fighter damage, positions and combat resources. Teammates cannot hurt, block or grab each other; rivals can fight across the net unless the Room leader selects Ball only before preparation.
_Avoid_: Co-op Slopball Run (when referring to Versus)
_Avoid_: Training (when referring to an Arcade challenge), CPU fight

**Arcade Attempt**:
One run of a selected Arcade challenge from its clean start until completion, failure, voluntary stop or retry. Pausing preserves the same attempt; a full retry begins a new one.
_Avoid_: PvP Match (when referring to an Arcade run)

**Personal Best** (Arcade):
The player's highest recorded Slopball Return count or fastest completed Character Course time for a given challenge, fighter and compatible gameplay, saved locally.
_Avoid_: match standing, stock score

**Slopball**:
A solo Arcade ball-return challenge played in an enclosed court divided by a net, using the fighter's attacks, projectiles, movement and Body Deflections.
Its sender launches a fresh ball after each successful return.
_Avoid_: solo volleyball (when referring specifically to Slopball)

**Slopball Return**:
A successful Slopball shot where a ball received in the player half is redirected by the player's attack, projectile or Body Deflection and clears the net into the sender half, including via wall or ceiling rebounds. Each ball yields one return.
_Avoid_: hit (when counting completed Slopball returns)

**Body Deflection** (Slopball):
A passive rebound from the fighter's body that redirects the ball without adding speed or damaging or launching the fighter. It can complete a Slopball Return.
_Avoid_: attack (when referring to passive ball-body contact)

**Slopball Serve**:
A fresh ball launched by the Slopball sender for the player to return.
_Avoid_: rally (when referring to an individual served ball)

**Slopball Percent**:
The ball's accumulated damage during a Slopball Serve, increasing its response to later attacks. Each fresh ball starts at zero percent.
_Avoid_: fighter damage (when referring to damage accumulated by the ball)

**Character Course**:
A character-specific Arcade challenge combining traversal and attack targets. It is completed by breaking every required target and reaching the finish.
_Avoid_: obstacle run, break-the-targets mode (when referring to the combined challenge)

**Course Target**:
A floating objective required for completing a Character Course, broken by one damaging attack or projectile hit without blocking fighter movement.
_Avoid_: CPU Opponent, bot (when referring to a Course Target)

**Course Checkpoint**:
A progress marker activated by reaching it after clearing every required target up to that point in the route. It supplies a fall-respawn location; visiting every checkpoint is not required to finish the Character Course.
_Avoid_: save point (when referring to a checkpoint within an attempt)

## Tutorial

**Tutorial**:
The offline guided introduction to the controls: a free-roam map with the player's chosen fighter and a Tutorial Dummy, where an ordered series of Tutorial Lessons ends at a completion card. It keeps no records or rewards.
_Avoid_: Training (when referring to the guided lessons), Arcade challenge

**Tutorial Lesson**:
One step of the Tutorial, such as Jump or Shield & Dodge, with its own checklist of Tutorial Objectives. Only the current lesson's objectives count; the next lesson starts after all of them are done or the lesson is skipped.
_Avoid_: level, stage, test (except as presentation copy)

**Tutorial Objective**:
A checklist entry observed from ordinary simulation results, such as landing on a block, a blocked hit or the dummy losing a life. Objectives never change gameplay.
_Avoid_: target (Course Target is a different object), quest

**Tutorial Dummy**:
An ordinary fighter fed scripted input: idle in most lessons, approaching and swinging in the shield lesson. The lesson sets its position and damage, never its rules.
_Avoid_: CPU Opponent, bot, training NPC

## PvP / Multiplayer

**ServerBrowser**:
The normal Unity directory of named public Rooms from Master. It shows every Room phase, member count and whether joining is allowed; only Lobby Rooms with free slots accept new members. Physical GameHosts are allocated at Match start, not chosen in this directory. Explicit development host/address tools are separate.
_Avoid_: matchmaking queue, GameHost list

**Room** (LobbyRoom in the UI):
The Master-managed, named group of up to four players. Membership and Server Chat survive Arena selection, Character Select, a Match, Results and rematch. The leader advances preparation; the Room admits new members only in Lobby. Master returns the matching Room to Lobby and clears preparation on a completed or canceled Match, without erasing membership or chat.
_Avoid_: physical GameHost lobby, Match, waiting roster

**GuestAuth**:
Access to SLOPArena under a temporary player identity without Steam authentication. A guest identity is not a Steam identity.
_Avoid_: dev login, anonymous auth, temp account

**SLOPArena Chat**:
In-game text communication between SLOPArena players, separate from Steam's chat system.
_Avoid_: Steam chat (when referring to this feature)

**Global Chat**:
The SLOPArena-wide chat channel, not restricted to one GameServer, LobbyRoom, or Match.
_Avoid_: server chat

**Server Chat**:
The SLOPArena Chat channel shared by members attached to the same Master Room. A Room's Server Chat is isolated from other Rooms and from physical GameServer lobby membership. The UI labels it ROOM.
_Avoid_: lobby chat, match chat

**Direct Message**:
A SLOPArena Chat message visible only to its sender and recipient, both connected to chat. Direct Messages are not queued for offline delivery.
_Avoid_: inbox, offline message

**Social Dock**:
The menu's reserved area for SLOPArena Chat, separate from the current match-preparation page. On compact layouts, a persistent social strip provides access to a chat view that temporarily replaces the page.
_Avoid_: chat overlay (for the menu dock)

**Unread Message**:
An incoming, unmuted SLOPArena Chat message not yet acknowledged by viewing its conversation at the newest messages while the game has focus and the conversation is unobscured. A brief combat preview alone does not acknowledge the message.
_Avoid_: notification (when referring to unread conversation state)

**Guest Session**:
A player's visit to SLOPArena during one game launch, with one temporary identity across chat reconnects. A later launch starts a new Guest Session.
_Avoid_: persistent account

**Display Name**:
A player's chosen name shown in SLOPArena Chat, entered on first launch and remembered for later launches. Display Names need not be unique; remembering a name does not preserve a player identity across Guest Sessions.
_Avoid_: player ID

**Session Tag**:
A short identifier that distinguishes a player alongside their Display Name for one Guest Session.
_Avoid_: Steam ID

**Channel Backlog**:
Recent Global Chat or Server Chat messages available to players who join after those messages were sent. Direct Messages are not part of a Channel Backlog.
_Avoid_: offline inbox

**Personal Mute**:
A player's choice to hide a sender's messages and alerts across SLOPArena Chat for the current game launch. It applies to the sender's identity, not their Display Name.
_Avoid_: ban, account block

**Speech Slip**:
A Server Chat message from someone playing the current Match, shown over their fighter, or beside their fight card when the fighter is off-screen or out. The local player's own messages use their card. Presentation only; the message still belongs to Server Chat.
_Avoid_: emote, taunt

**Quick Line**:
A fixed Server Chat message (`GG`, `NICE`, `ONE MORE?`) that a controller player sends from the D-pad during an online Match. It is an ordinary message with the same limits.
_Avoid_: emote, ping

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
The lifecycle of a PvP group: ServerBrowser → Room (Lobby) → Stage Select → Character Select → Match Starting → Fight → Results → the same Room (Lobby), if membership remains valid. The leader chooses what to play before anyone picks a fighter ([ADR-0040](docs/adr/0040-online-arcade-room-pick-and-match-format.md)); Solo and Training still pick fighters first. Master owns Room preparation and its terminal transition; the GameHost runs the authoritative Match and sends its result or cancellation. Results remains viewable until the player returns; leaving or Room expiry routes to the browser with an explanation. A rematch requires new selections and Lock-in.
_Avoid_: physical-server lobby flow, automatic rematch

## Replays & Spectating

**Replay**:
A saved recording of a Match's authoritative gameplay that a player can watch again for personal review or share with a bug report.
_Avoid_: video capture, rollback re-simulation

**Replay Library**:
A player's recent Matches, with access to available Replays and explanations for unavailable recordings. It remains accessible during later game launches.
_Avoid_: replay folder

**Partial Replay**:
A Replay covering only a recorded portion of a Match, such as a cancelled Match or a recording stopped at its resource limit. A Match can finish normally while its Replay remains partial.
_Avoid_: completed Replay, crash recovery

**Replay Share Link**:
A single unlisted link for a Match's Replay that any of its participants can create or revoke. Anyone holding an active link can retrieve that Replay.
_Avoid_: public replay listing, Match invite

**Live Spectator**:
A person watching an ongoing Match without having entered its fighter Roster. An eliminated fighter watching the remainder of their own Match is a different role.
_Avoid_: eliminated fighter, replay viewer

**Practice Session Log**:
An opt-in local file measuring one Training or Solo CPU match: each swing's outcome and closest approach to its target, camera and device context, and practice-control changes. It is measurement, not a Replay: it cannot be watched again and is never uploaded.
_Avoid_: replay, telemetry upload

## Prediction & Rollback

**World Snapshot**:
The complete state of a PvP Shared simulation after one tick: every fighter's full state, active ability runtimes, live hitboxes and projectiles, and simulation counters, in a canonical encoding where equal states give equal bytes. Restoring it into a simulation with the same arena, rule and registered fighters reproduces the same future on the same runtime. The basis of full-world rollback (ADR-0039); not a network character packet.
_Avoid_: save state, full packet, checkpoint (unless meaning a stored World Snapshot)

**Anchor**:
The newest World Snapshot a client has received from the GameHost and restored (or found identical to its own prediction of that tick). Full-world prediction always restarts from it; the client keeps no divergent history of its own (ADR-0039).
_Avoid_: ConfirmedTick (track model), base state, keyframe

**Prediction Window**:
The ticks a client simulates beyond its Anchor, up to its local tick, with its own recorded inputs and each opponent's last relayed input without presses. Re-simulated whenever a newer Anchor arrives; capped so a client stops predicting when frames stop arriving.
_Avoid_: RollbackWindow (track model), lag window

**ConfirmedTick**:
The highest match tick for which the client holds a full server-authoritative snapshot for every non-self entity. The base state PredictedTrack rebuilds from; it advances as state packets arrive, one tick at a time. Does not apply to LocalTrack — the self entity is never rebuilt from a snapshot. Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
_Avoid_: ack tick, sync tick, last confirmed

**RollbackWindow**:
The span of ticks between ConfirmedTick and the currently rendered tick, replayed on PredictedTrack entities whenever the confirmed base advances or a mismatch is corrected. Applies only to opponents currently in a Predictable ActionState — RawTrack entities have no window (they render the latest packet directly), and LocalTrack has no window (it never rebuilds). Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
_Avoid_: rewind window, prediction buffer, lag window

**InputRelay**:
The GameHost's broadcast of per-fighter input data alongside authoritative Match state for opponent prediction. It is separate from Replay recording.
_Avoid_: input forwarding, input piggyback, input echo

**LocalTrack**:
The self entity's `ServerSimulation`, run continuously on the client from match start — never rebuilt from a received snapshot, fed the player's own true InputState every tick. Corrected only by snapping the wire-serialized fields when a received packet disagrees; fields absent from the wire (attack timers, knockback, ability-instance state) never diverge because this track is never reconstructed from a lossy snapshot. Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
_Avoid_: self-prediction, client-side prediction (ambiguous with PredictedTrack), own-entity sim

**PredictedTrack**:
An opponent entity currently in a Predictable ActionState. Rebuilt from ConfirmedTick's snapshot and replayed forward tick-by-tick using InputRelay data (or the no-input marker) up to the current local tick. Diverges only at the frontier (ticks past the last received relay), corrected by snap on the next packet. Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
_Avoid_: opponent prediction, ghost sim, replayed entity

**RawTrack**:
An opponent rendered from received state rather than local re-simulation while its action or Hitstop requires private simulation state. Returns to prediction only when its confirmed state is reconstructible. Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
_Avoid_: unpredicted entity, fallback display, snap-only entity

**Predictable ActionState**:
An action whose movement can be reproduced from confirmed state and exact input history. Idle, JumpSquat, Run, Sliding, Crouching, Shielding, ShieldDrop, GrabAttempt, detached Throwing recovery, Thrown flight/recovery and the air-dodge movement/recovery phases qualify only without active Hitstop, block-hitstop or a coupled interaction. Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
_Avoid_: safe state, simple state, movement state

**Complex ActionState**:
An action requiring private ability/resolver state or paired server authority. Attacking, Hitstun, LedgeHang and linked Grabbing/Grabbed/Throwing render from authoritative state; the self track uses a targeted barrier for confirmed capture or block contact instead of replaying through a coupled interval. The retired `Warping` numeric value remains reserved for wire compatibility. Protocol 9 track model (ADR-0011), retired online by full-world rollback (ADR-0039); see Anchor and Prediction Window.
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

The word "server" is overloaded in SLOPArena. Use these distinct terms:
- **Master server** — the separate-repo ASP.NET Core app (SignalR/REST, PostgreSQL) that owns Rooms, browser, chat and Match records. Never runs simulation.
- **GameHost** (the `GameServer` process above) — the .NET console process in `src/Server/` that hosts authoritative Matches and reports their outcomes to Master. Not a browseable Room.
- **ServerSimulation** — the pure C# tick loop in `src/Shared/` (`Simulation.cs`, `CombatMath.cs`). Runs on GameHost and client prediction tracks; not a process.

Bare "server" is ambiguous; name the owner instead.
 
## Workshop / Creator Content

**WorkshopContent**:
Creator-authored fighter, stage, or presentation content distributed through Steam Workshop. WorkshopContent references SLOPArena capabilities by public identity and owns its creator-provided assets.
_Avoid_: mod, addon, user content

**BuiltInContent**:
SLOPArena-provided animation, VFX, SFX, material, or gameplay capability available to WorkshopContent through a public semantic identifier. Its runtime implementation is private to SLOPArena.
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
Retired Ability Lab presentation that used the legacy character adapter. Current character preview uses the package path; there is no Compatibility tab or legacy roster admission path.
_Avoid_: legacy authoring, fallback package

**ContentRequirement**:
The exact WorkshopPackage and BuiltInContentId set a match must resolve and validate before its simulation starts.
_Avoid_: mod list, asset list, loadout

**OfficialException**:
An explicit case where official content bypasses the creator-facing content model. It records the reason, scope, owner, and migration path.
_Avoid_: special case, internal shortcut
