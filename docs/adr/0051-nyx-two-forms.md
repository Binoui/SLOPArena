# ADR-0051: Nyx, one fighter with two forms

**Status:** Accepted, 2026-10-29. The owner accepted Nyx's kit on 2026-10-29 ([her page](../characters/nyx.md)),
answered this record's six questions the same day and asked for the build to go as far as it can (see
[Owner's answers](#owners-answers)). Apart from the swap cooldown the owner set, it sets no tuning numbers.
**Extends:** [ADR-0039](0039-full-world-rollback.md) (World Snapshots),
[ADR-0042](0042-controller-layout-three-normals-homing-dash.md) (slot grid),
[ADR-0045](0045-bashie-shell-state.md) (a fighter state that moves read, `slop.ability.thrust.v1`) and
[ADR-0048](0048-bashie-rescue-ring-tether.md) (tether, `Reeled`)

## Context

Nyx swaps between two forms, like Zelda and Sheik. The real form, a shadow witch, is slow on the ground and floaty in
the air, and her shadows do the hitting. The fake form, an influencer brawler, runs fast, falls fast and hits hard up
close. F swaps in both directions and hits: Going Live pulls nearby enemies in, Logging Off pushes them away. A move
that connects can be cancelled into F during its recovery. A Hex Crow perched on a target survives the swap, and the
fake form's next hit on that target pops it for a bonus hit. One skeleton carries two skinned meshes, and the swap
changes which mesh draws inside the flash or the shard burst.

The engine assumes one kit per fighter:

- A fighter has one `CharacterDefinition` for the whole match. `ServerSimulation._defs[id]` is read in about 40
  places for movement, capsule, presentation roles and slots. In Unity, `PlayerRenderer`, `HUDManager`,
  `CombatFeedback` and `ProjectileVFXManager` hold or fetch the same definition.
- A move is found by wire slot and airborne flag through `GetCookedSlotAbility`, an index into 14 `CookedSlots`.
  Hitboxes keep `Def`, `Slot` and `Airborne` and look their move up again for bone tracking and hit visuals. Hit
  records in World Frames carry `AttackSlot` and `Airborne` and nothing else about the move.
- Movement (`def.Movement`) and the locomotion, hit and defense clips (`IdleAnim`, `RunAnim`, `ShieldAnim` and the
  rest) belong to the definition. Server hurtboxes sample the clip that `ResolveBoneAnimFrame` picks from those
  fields.
- Cooldowns are 11 counters on `CharacterState`, one per wire slot.
- A running `CookedTimelineAbility` holds its own `CookedSlotDefinition`, so a move keeps its content whatever the
  fighter does. A World Snapshot restores it by slot ordinal against the fighter's one definition.
- IASA is the only early cancel. Nothing cancels on a confirmed hit.
- CONTEXT.md: a Character Package contains exactly one playable Character.
- The resolver already has a homing projectile (Manki's rocket: `HomingTargetEntityId` and a cooked turn rate) and a
  sticky one that attaches to a fighter on first body contact and follows it (Mayhem bombs: `StickyFuseTicks`,
  `AttachedEntityId`, `ExplodeAttachedTo`). Packages cannot author either. No attack can hit a projectile.
- Package hitboxes can launch `awayFromOwner` or `towardOwner`. FightGuy and Sir Bonk author pulls.
- `PlayerRenderer` finds the fighter's mesh with `GetComponentInChildren<SkinnedMeshRenderer>()` for the death blink
  and the respawn re-enable, and `WeaponAttach` does the same. Both see one mesh.

## Decision

### 1. The form is stored on the fighter

- **A. A stored `FormState` on `CharacterState` (recommended).** It holds the active form and the inactive form's
  cooldown bank (section 4), as `FlyingState` holds Manki's flight. World Snapshots write it in declaration order
  like every other field, so rollback restores it with the rest of the fighter. World Frames embed the World
  Snapshot and need no change for it.
- **B. Derived, as ADR-0045's shell is.** Rejected. The shell can be read from `ActionState` and the running move.
  The form has to survive every state: hitstun, shield, grabbed, KO flight. Nothing she is doing now tells which form
  she is in, so deriving it would need a history of completed swaps.
- **C. A per-fighter value in `ServerSimulation`, written in the fighter's snapshot record beside KOs and hit
  credits.** Rejected. Unity, the CPU and the HUD read fighters through `CharacterState`, and this would give them a
  second channel.

Every fighter without forms reads form 0 forever. Nyx's form is set at spawn from her player's pick (section 1b) and
changes only at the swap operation (section 4). A respawn keeps the form she was KO'd in.

### 1b. Her player picks the starting form on Fighter Select

The owner asked for the starting form to be "choosable on char select" (2026-10-29).

- **A. A form byte on the seat's pick, set with a toggle on her card (recommended).** The seat's choice carries a
  form next to its `CharacterClass`. Her card on Fighter Select shows the picked form's portrait and flips it with one
  button, as Melee lets you start as Sheik. Local matches pass the byte to the simulation at spawn. `MatchPlayer`
  gains an optional `form` key, absent meaning form 0, so the game server reads it from the master's match-start
  command. Fighters without forms ignore it.
- **B. Two cards, Nyx and Nyxxie, for one package.** Rejected. The grid would show one fighter twice, and both cards
  still need the same form byte behind them.
- **C. A match setting outside Fighter Select.** Rejected: the owner asked for the pick on Fighter Select.

The master server's lobby has to carry the byte for online matches. That is a change in the master repository. Until
it lands, an online Nyx starts in the real form.

### 2. One package with a second grid, and a runtime definition per form

- **A. An `alternateForm` block in `character.json`, cooked into the same package, with one runtime definition per
  form (recommended).**
- **B. A form dimension on the slot address.** `SlotAddress` gains a form, `CanonicalSlotProjection` grows to 28
  entries, and `GetCookedSlotAbility(wire, airborne, form)` replaces the lookup. The data is the same as A, but every
  caller has to learn about forms: about 30 runtime call sites, about 60 test files, and every `def.Movement` and
  presentation-role read.
- **C. Two linked packages, `nyx-real` and `nyx-fake`, with the fighter switching its registered package.** Rejected.
  It breaks "one package, one character", doubles roster pins, hashes, poses and Fighter Select entries, and admits
  a package that nobody can pick.
- **D. Branches by form inside each slot's timeline.** Rejected: authoring has no branches by design.

**Source.** The top-level `movement`, `presentation` and `slots` stay form 0, the real form. The new optional
`alternateForm` block is form 1, the fake form: `id`, `displayName`, a full `movement` block, a `presentation` block
limited to locomotion, hit and defense roles, and its own 14 canonical `slots` with aliases as today. It cannot
override the capsule, hurtbox bones, hip height, shield radius, capture geometry, weight, visual scale or model
offsets, because she has one body. A role it leaves empty plays form 0's clip, which is the same rig. The package
requires a new public character-level capability, `slop.ability.forms.v1`, and the block exists exactly when the
capability is required, as ADR-0045's `shell` block does. There are exactly two forms: the block is not a list.

**Cook.** `CookedCharacterDefinition` gains an optional `AlternateForm` (movement, presentation, 14 slots), and
`CookedSlotDefinition` gains `Form` (0 or 1). Ordinals stay 0 to 13 inside each form. The block is written only when
present, so every other package cooks to identical bytes and keeps its hashes. Cooked schema stays 4. The runtime
API floor is the next free value at implementation (ADR-0050's implementation is claiming one too). The compiler and
the cooked loader share one bounds check, and the manifest lists the capability like any other.

**Runtime.** `CookedCharacterRuntimeAdapter` builds two `CharacterDefinition` values for a forms package. They share
identity and body fields and differ in `Movement`, presentation roles and `CookedSlots`. `MatchContentEntry` keeps
both. `ServerSimulation` resolves a fighter's definition through one accessor that reads her form, and every current
`_defs[id]` read goes through it. Code that already takes a definition then gets the form's values without knowing
that forms exist: movement, `ResolveBoneAnimFrame`, `MoveReach` and the CPU's `EnsureProfile`.

Three things outlive a swap, so they name their form explicitly:

- **The running activation** keeps the definition it started with in `CharacterDef`, and its snapshot record writes
  its form beside the slot ordinal. Without that, a restore during Going Live's recovery would rebuild Logging Off.
- **Each hitbox** gets `Hitbox.Form`, written in snapshots. Restore re-links `Def` to the right form, and bone
  tracking and hit visuals look up the right move.
- **Hit records** get a form byte beside `AttackSlot` and `Airborne`, so `CombatFeedback` and `ProjectileVFXManager`
  resolve a perched crow or a late projectile against the form that threw it. This changes the World Frame hit
  encoding.

### 3. Per-form movement, presentation and hurtboxes on one rig

- **A. A full `movement` block and presentation roles per form, with hurtboxes following the pose (recommended).**
  Server hurtboxes already follow the clip of the current state. With the form's definition, her idle, run, jump,
  fall, shield and hit poses are the form's, and the bone hurtbox radii stay shared. There is no per-form hurtbox
  list.
- **B. Multipliers on form 0's movement** (run speed × 1.3, gravity × 1.2). Rejected. It hides the real numbers, and
  the kit asks for two different feels, not one feel scaled.
- **C. Per-form hurtbox bone lists.** Rejected for the prototype. She has one body, and the poses already move the
  hurtboxes. Reopen it only if the long dress and the short skirt need different radii in play.

A swap in the air keeps her velocity. The new form's gravity, fall cap and air control apply from the next tick.
Jumps left are clamped to the new form's `maxJumps`. AirTime and the FloatWindow do not reset. A latched fast fall
continues at the new form's fast-fall speed.

On the Unity side, `CharacterAssetCatalog` keeps one rig prefab that carries both skinned meshes, and binds both
forms' clips as ordinary semantic IDs. `poses.bin` holds them all. A new `FormPresentation` component on the fighter
is the only writer of which mesh draws, and it follows `state.Form`. `PlayerRenderer` reads idle, run and defense
roles from the definition of the current form. A mispredicted swap flips back at the next Anchor like any other state,
and the flash plays once because presentation events are deduplicated by identity.

Many presentation paths enumerate the model's renderers today: the death blink, the respawn re-enable,
`WeaponAttach`, air-dodge afterimages, the Beeline outline, seat colours (`MatchVisualStyle`) and the target-lock
highlight. Each must skip the hidden form. Deactivating the hidden mesh's GameObject keeps most of them right; the
ones that pass `includeInactive: true` need a filter. The backpack pole and its hand-held copy are props toggled by
presentation events, with no simulation.

### 4. The swap move, the pull and push, the swap-cancel and cooldowns

**How the swap is expressed.**

- **A. A typed timeline operation, `swapForm`, on each form's F slots (recommended).** At its tick, her form toggles.
  A move interrupted before that tick does not swap. Going Live and Logging Off are ordinary timelines: the operation,
  a wide `spawnHitbox` on the same tick with `towardOwner` (Going Live) or `awayFromOwner` (Logging Off), and a
  presentation event for the flash or the shards. The mesh toggles on the same tick, inside the burst.
- **B. A capability, `slop.ability.form-swap.v1`, that owns the swap, the pull and the push.** Rejected. The pull and
  the push already exist as hitbox directions, and the swap is a one-tick effect. A capability adds lifecycle code
  with nothing variable to own.
- **C. A universal swap input outside the grid.** Rejected: F is the accepted swap, and it must hit.

F keeps the form it started in for its remaining recovery, because the activation holds its own slot, so its clip
and hitboxes stay consistent. Her next action uses the new form.

A `towardOwner` launch is ballistic and bent by DI, and ADR-0019 records it over-pulling Nilus's victims. The first
build uses a small fixed-hitstun pull and measures where victims land. If it cannot deliver them into Phone Slaps
range, the fallback is to drive the tether's mover (`ReelTether`) for every fighter caught. That is a separate
change.

**Swap-cancel.**

- **A. A narrow rule on the form block (recommended).** After a move's own hitbox hits a fighter, F is admitted for
  the rest of that move, past IASA. The activation records a `Connected` flag in its hit hook and writes it in its
  snapshot. Hitstun, hitstop, block stun and landing lag still block, as they block IASA, so an F pressed during
  hitstop is buffered and starts when hitstop ends. A shielded hit does not count. A projectile that lands after its
  move has ended does not count either, because hit hooks reach only the running activation.
- **B. A generic hit-cancel: any landed move cancels into any special.** Rejected for now, because it changes what
  every move is for. FightGuy's pending "Combo Champion" pitch asks for this rule. If the owner picks it, A becomes
  its F-only case.
- **C. Early IASA authored on every Nyx move.** Rejected: a whiff could then cancel into F too, which removes the
  reward for connecting.

**Cooldowns.**

- **A. One cooldown bank per form, with F on one shared clock (recommended).** `FormState` stores the inactive form's
  counters for slots 1, 3, 4, A, E and R. The swap exchanges them with the live counters, and the stored ones count
  down every tick like the live ones. Everything that reads cooldowns keeps reading the live counters: admission, the
  pending-cooldown check, the HUD ring and the CPU. A move's cooldown goes to the bank of the form it started in. F's
  cooldown goes to both.
- **B. One clock per button, shared by both forms.** Rejected. Combo 1 is Grasp into Selfie Grab, and both are R.
- **C. Fourteen counters on `CharacterState`, per form.** The same behaviour as A with more fields on every fighter.

### 5. The crow mark across forms

- **A. The resolver owns it, as a homing projectile that perches (recommended).** Hex Crow is a `spawnProjectile` on
  real A with two new optional blocks. `homing` captures her current target at spawn, turns at most
  `turnDegreesPerTick`, and gives up for good once the target is behind it, as Manki's rocket does. `perch` holds
  `perchTicks` and a `pop` hit. The first body contact attaches the crow to that fighter with no damage, through the
  existing sticky path. A shield contact spends it. Perched, it follows the carrier and expires silently after
  `perchTicks`. It is not tied to her activation, so the swap does not touch it.
- **B. A status mark on the victim's `CharacterState`** (`MarkOwnerId`, `MarkTicks`). Rejected for now. Every
  fighter's state and snapshot would grow for one character's move, and the crow in flight still needs a projectile.
  Reconsider it if a second kit needs marks; FightGuy's proposed Call-Out F would.
- **C. The crow inside an activation.** Rejected: activations end with their move.

**The pop.** When a damaging hit from Nyx in the fake form lands on a fighter carrying her crow, the engine queues the
crow's `pop` hit on that fighter, credited to her, and removes the crow. The pop resolves after the triggering hit's
hitstop, so it reads as a second hit. Real-form hits do not pop. The pop is an engine rule in the hit pass, not an
activation hook, so any fake-form move can pop it, including one that started after the swap.

**New resolver pieces.**

- A `PopOnly` flag. Today the sticky path explodes when its fuse runs out and when its carrier is KO'd. The crow must
  do neither.
- A fragile rule for "hit away": a crow in flight that overlaps an opposing fighter's active hitbox is destroyed. It
  is not reflected.

She has at most one perched crow per target, and a new perch replaces the old one. Her KO removes her crows. The
carrier's KO removes the crow without a pop.

**How it is shown.** The projectile visual follows the hitbox in flight. While `AttachedEntityId` names a carrier,
`ProjectileVFXManager` draws the crow on that fighter's head bone, so every player sees the mark. The HUD needs
nothing.

The `homing` block is also the public behaviour that Manki's internal bazooka lists as its migration path.

### 6. Reuse map

Every normal in both forms uses existing operations. Specials reuse capabilities where one fits.

| Form | Slot | Move | Uses | New piece |
|---|---|---|---|---|
| Real | 1 | Shadow Flick | Root-relative `spawnHitbox` in front, `startupAimCorrection` | None |
| Real | 3 | Grasping Hands | Two sphere hitboxes beside and above her, upward launch | None |
| Real | 4 | Shadow Lash | Long floor capsule hitbox, slow startup | None |
| Real | air 1 | Shadow Ring | Sphere hitbox around her body | None. The resolver's `Ring` shape is Mayhem-only and not needed. |
| Real | air 3 | Up Claw | Hitbox above her | None |
| Real | air 4 | Shadow Fist | Hitbox below her, downward angle | None |
| Real | A | Hex Crow | `spawnProjectile`, sticky attach, homing steering | `homing` and `perch` blocks, `PopOnly`, fragile rule, pop in the hit pass |
| Real | E | Bat Scatter | `slop.ability.thrust.v1` with `aim: stick` on a recovery slot. Presentation hides her body inside the bats. | None in Shared |
| Real | R | Grasp | `slop.ability.tether.v1`: pair fields, `Reeling`, `Reeled`, `ReelTether`, `TetherHolds`, arrival window | An optional `atTarget` block: no thrown ring. After `warningTicks`, a stationary catch volume rises at the target's feet, captured at the press through `TryGetCurrentTarget`. A miss is a fixed recovery with no retract. |
| Real | F | Going Live | `towardOwner` hitbox, presentation event | `swapForm` |
| Fake | 1 | Phone Slaps | Three hitboxes in one timeline linked by `fixedHitstunTicks` | None |
| Fake | 3 | Ring Light Uppercut | Launcher hitbox along the pole. The pole leaves the backpack by presentation event. | None in Shared |
| Fake | 4 | Platform Stomp-Kick | Hitbox | None |
| Fake | air 1 | Spinning Slap | Sphere hitbox | None |
| Fake | air 3 | Ring Light Overhead | Arc of hitboxes above her | None |
| Fake | air 4 | Double Dropkick | Downward hitbox | None |
| Fake | A | Ring Light | `startupAimCorrection` for the snap turn, `forwardLunge` with `stopInAttackRange`, a short cone of spheres with `fixedHitstunTicks` and zero launch for the dazzle | A hitbox facing filter, so only fighters facing her are dazzled |
| Fake | E | Ring Light Rise | `slop.ability.aimed-recovery.v1` with `riseSteerAcceleration` | None. If the rotor must hit, `thrust.v1` straight up with its `trailHitbox` does that instead. |
| Fake | R | Selfie Grab | Grab rules (shields do not stop it, armour does not resist it, grab clash), pair fields, `Grabbed`, `CanPlaceVictim`, the throw release into `Thrown` | A capability, `slop.ability.command-grab.v1`: lunge, authored capture volume, a short hold with Nyx `Attacking` and the victim `Grabbed` under a new `DefenseInteractionPhase`, a both-sides validator like `TetherHolds`, then a release with an authored launch |
| Fake | F | Logging Off | `awayFromOwner` hitbox, presentation event | `swapForm` |

Grasp reuses the tether instead of getting its own capability because the drag, the pair, the arrival window and
every cleanup path are the same. Only the start differs: a placed catch instead of a thrown ring. Selfie Grab cannot
reuse the universal grab, because that grab is a defense state outside the slot grid, with no clip, cooldown or
authored launch of its own.

### 7. HUD deck and CPU per form

**HUD.**

- **A. One deck that relabels on the swap (recommended).** `HUDManager` builds the deck from the local fighter's
  current form and rebuilds names, icons and cooldown maxima when `state.Form` changes. Cooldown rings read the live
  counters, which hold the active form's clocks after the bank exchange. The F card shows the form she will become.
  `tools/hud_icons` gets one deck per form.
- **B. Both decks at once.** Rejected: it doubles the HUD for one fighter and hides which moves are live.

**CPU.**

- **A. The current policy per form, plus one swap rule (recommended).** `EnsureProfile` already rebuilds when the
  definition changes. It caches both forms' profiles so a swap costs nothing, and slot scoring then uses the form's
  real reach, travel and cooldowns. One new rule picks F: swap to the fake form when the target is inside its close
  range, and to the real form when the target is outside it. On Normal and Hard the CPU also swap-cancels after a
  confirmed hit; Easy swaps on range only. Opposing CPUs read Nyx's threat ranges through her current definition, with
  no change.
- **B. A scripted policy per form.** Rejected: it duplicates the policy.

The CPU gets no crow rule in the first pass. Any fake-form hit pops the crow.

### 8. Build order

Each phase ends playable in Training, with Shared tests for its rules.

0. **Subtract first, with no behaviour change.** Every `_defs[id]` read goes through one accessor. `PlayerRenderer`,
   `HUDManager`, `CombatFeedback` and `ProjectileVFXManager` get the fighter's definition from one place. The
   renderer's mesh lookups go through one helper.
1. **Two grids and the swap.** The forms capability, `alternateForm`, the two definitions, `FormState` with cooldown
   banks, `swapForm`, `Hitbox.Form`, the activation's form in snapshots, the form byte in hit records, the swap's
   pull and push, and the starting form from section 1b with its Fighter Select toggle. The package `nyx`
   (`CharacterClass` 8) uses her own rig and carries borrowed placeholder moves in both forms. Play: swap and feel
   the two movement profiles. Tests: a swap in the air, a restore during F, a form-0 hitbox resolving after the swap,
   the cooldown banks, a respawn keeping the form, and a match started in each form.
2. **Normals for both forms and the swap-cancel rule.** Play: hit, then swap-cancel.
3. **Specials on existing capabilities:** Bat Scatter, Ring Light Rise, and Ring Light without the facing filter. The
   two meshes and `FormPresentation` land here if the rigs are ready.
4. **Grasp,** the tether's `atTarget` block. Play: combo 1 up to Phone Slaps.
5. **Hex Crow:** homing, perch, pop and the fragile rule. Play: combos 2 and 3.
6. **Selfie Grab,** the command-grab capability. Play: combo 1 in full.
7. **Finish:** the Ring Light facing filter, the CPU swap rule, both decks' icons, an Ability Lab form selector, and
   Fighter's License if she joins it.

Until phase 7, Ability Lab shows and edits form 0 only. The fake form is edited in `character.json` and tried in
Training.

## Prototype defaults

- She starts in the form her player picked and respawns in the form she was KO'd in.
- The swap keeps her percent, velocity, position and lock target. Jumps left are clamped to the new form.
- F's cooldown is one shared clock of 300 ticks (5 seconds), much shorter than other fighters' F, because the kit's
  combos swap.
- Each form keeps its own E clock, so a swap can give a second recovery.
- Only a hit on a fighter opens the swap-cancel. Shields, the Slopball ball and course targets do not.
- Grasp's spot is fixed at the press. A target that leaves it during the warning is missed.
- Bat Scatter has no invincibility: the bats are her hurtbox.
- A crow hit in flight is destroyed.
- The dazzle is a fixed stun with no launch. The facing filter waits for phase 7.
- Selfie Grab beats shields and clashes with a grab on the same tick, as grabs do.
- Both forms share weight, capsule and shield size.
- Other numbers are set by the implementer as starting points, then by play.

## Owner's answers

The owner answered the six questions on 2026-10-29.

1. **Spawn form.** Her player picks the starting form on Fighter Select (section 1b). She respawns in the form she
   was KO'd in.
2. **F cooldown.** Five seconds for now.
3. **Recovery.** Each form has its own E clock.
4. **Hit away.** A crow hit in flight is destroyed.
5. **Bat Scatter.** She can be hit while she is a swarm of bats.
6. **Before the rigs.** "Go for all you can." The owner sent both Mixamo rigs with the answers, so the build uses her
   own rig, with clips borrowed from another fighter until her own are approved.

## Consequences

- New state: `FormState` on `CharacterState`, `Hitbox.Form`, the activation's form and `Connected` flag in its
  snapshot record, and the next World Snapshot format. Phase 1 took format 17 (ADR-0052 took 16), World Frame format 8
  for the hit record's form byte and runtime API `1.16.0`. The command hold takes the next `DefenseInteractionPhase` value (6). There is no new `ActionState`: the
  swap is an ordinary move. The change goes under "Unreleased" in `SimulationProtocol.cs`; release prepare does the one
  protocol bump.
- A seat's pick and `MatchPlayer` carry a starting form. Online starts in the real form until the master lobby passes
  the form through.
- Every other package cooks to identical bytes. Every fighter's snapshot grows by the `FormState` bytes.
- Nyx is about twice a normal kit's animation work: [her page](../characters/nyx.md) lists 24 pose sheets.
- Risk: a `_defs` read or a Unity path that still holds one definition shows the wrong form's moves. Phase 0's single
  accessor and a test that swaps and checks each consumer are the guard.
- Risk: a presentation path that enumerates renderers shows the hidden mesh. `FormPresentation` as the only writer
  and a Training check of every effect on both forms are the guard.
- Risk: the `towardOwner` pull may over-pull or under-pull. Measure before tuning, and fall back to the tether mover
  if needed.
- Risk: Unity keys projectile visual rows by canonical slot ID. If both forms ever throw projectiles from the same
  slot, the rows need a form qualifier. Nyx's kit does not do that.
