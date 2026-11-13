# Cooked package admission

The client and GameServer use `CookedCharacterPackageLoader` in Shared.
The application roster authorizes exact package revisions, including both hashes.
The compatibility file entry points select `TrustedBuiltIn` only for those pins.
Other revisions select `Workshop`. The internal overload takes an explicit host profile.
`AdmittedFighter.TestFixture` always selects Workshop. It grants no production admission.
The load result retains its host profile internally. The admission constructor refuses a trusted
load without the shipped roster or the existing development adapter.
`LoadAssembly` is the existing trusted authoring preview adapter. Unit 4d must use the
Workshop file path for player-created cooks, rather than the trusted preview adapter.

Workshop refuses internal capability declarations and operations with `capability.untrusted`.
Undeclared operations still receive `package.capability.undeclared`.
The loader compares stored budgets against `CharacterPackageCompiler.ComputeBudget`.
The compiler uses this same function to write budgets and runs the same runtime/resource limit checks before returning a cook. Projectile flight and speed schema ranges name the code constants; a reflection test prevents drift. Schema-defined capability costs
remain the authority for budget accounting.

`CookedPackageLimits` owns admission and deterministic runtime pool constants. It bounds input bytes before cloning,
directory reads before and during reading, JSON depth, pose counts and decoded float values.
The assembler checks pose byte availability before the animation loader allocates frames.
Runtime parsing rejects invalid enums, incomplete slot grids, empty timelines, operation
ordering, operation counts, body hurtbox and declaration counts, and projectile lifetime and speed violations.

## Limits and measurements

The reference set is the seven packages pinned by origin/main at
`d7a8b13fe99e9fb0d88293f28cf5a8960b70e95d`.
File sizes are bytes. Pose values count decoded floats, rather than object overhead.
Headroom is four times the maximum rounded up to a power of two, except where stated.

| Fighter | Ops per stage / timeline | Hitbox lifetime envelope | Projectile lifetime envelope | Pose bones / animations / max frames / floats |
| --- | --- | --- | --- | --- |
| Bashie | 34 / 34 | 116 | 40 | 24 / 25 / 720 / 120528 |
| Bonk | 13 / 13 | 278 | 480 | 21 / 33 / 240 / 116046 |
| FightGuy | 18 / 19 | 282 | 224 | 19 / 23 / 179 / 78489 |
| Manki | 5 / 5 | 80 | 0 | 28 / 24 / 119 / 113736 |
| Nyx | 8 / 8 | 322 | 1140 | 21 / 34 / 720 / 212688 |
| Salamander | 3 / 3 | 91 | 300 | 26 / 24 / 360 / 113256 |
| Wibou | 4 / 4 | 123 | 540 | 21 / 22 / 120 / 66969 |

The requested lifetime envelope charges every public emitter once per owner simulation tick
across all slots, including alternate forms. It limits what a package can request; it is not
an upper bound on actual live objects when hitstop pauses melee aging. Runtime pools bound
actual live objects independently of lifetime, cooldown, cancellation, form, or capability.
Typed capability accounting uses actual engine lifetimes, including tether catches, the flash
cut's `legTicks + 1`, thrust trails, and flight plus perch time. The steered charge's two
volumes and the shell ride's single window have bounded ownership and are removed before
replacement. Internal capability effects remain trusted engine code and are excluded from
this untrusted-content envelope. An unaccounted public capability fails admission closed.
These are conservative admission counts, not observed peak live objects.

The accounting covers direct hitbox and projectile operations, targeted leap, thrust, dash
strike, flash cut, command grab, steered charge, charged projectile, charged stream, finger
guns, tether's thrown and placed branches, and shell effects. Aimed recovery and cannonball
create no resolver volumes. Charged streams emit at most once per owner tick. Lifecycle
validators still enforce the capability-specific conditions that this count assumes.

| Limit | Measured maximum | Ceiling | Selection |
| --- | ---: | ---: | --- |
| Operations per stage | 34 | 64 | Existing compiler ceiling, 1.88 times |
| Operations per timeline | 34 | 256 | Existing compiler ceiling, 7.53 times |
| Public emitters across fighter | 45 | 128 | Twice, rounded |
| Direct hitboxes per timeline | 10 | 64 | Four times, rounded |
| Direct projectiles per timeline | 3 | 16 | Four times, rounded |
| Requested hitbox lifetime envelope | 322 | 2048 | Four times, rounded |
| Requested projectile lifetime envelope | 1140 | 4560 | Four times |
| Projectile speed, metres per second | 30 | 120 | Four times |
| Projectile flight lifetime, ticks | 240 | 960 | Four times |
| Flight plus attachment, ticks | 570 | 2280 | Four times |
| Manifest bytes | 2653 | 16384 | Four times, rounded |
| Runtime bytes | 62693 | 2097152 | 32 times, rounded; retains long display-string contract |
| Binding bytes | 15748 | 65536 | Four times, rounded |
| Pose bytes | 851998 | 4194304 | Four times, rounded |
| Runtime lookup identifier characters | 45 | 256 | Four times, rounded |
| Pose identifier UTF-8 bytes | 29 | 128 | Four times, rounded |
| Body hurtboxes, capsules plus bone definitions | 24 | 32 | 1.33 times |
| Attachment points | 2 | 8 | Four times |
| Presentation IDs | 41 | 256 | Four times, rounded |
| Stage animation references | 6 | 32 | Four times, rounded |
| Capability requirements | 5 | 32 | Existing compiler ceiling, 6.4 times |
| Pose bones | 28 | 128 | Four times, rounded |
| Pose animations | 34 | 256 | Four times, rounded |
| Frames per animation | 720 | 4096 | Four times, rounded |
| Total decoded pose floats | 212688 | 1048576 | Four times, rounded |

### Collision work and runtime pools

`SpellResolver.Spawn` refuses an effect before assigning its emission ID or allocating hit
history if any live pool would overflow. Counts come from the current active resolver list,
including after snapshot restoration; no unsnapshotted counter decides gameplay. The exact engine-owned `StageHazardOwner.EntityId` and `MayhemHostEntityId` keep their existing arena budgets and do not consume fighter pools. Package operations cannot choose their owner identity. Mines
account for their existing replacement before this decision. Refusal drops that emission:
a player may see a projectile fail to appear or a melee window fail to hit. The ability and
its cooldown continue. This applies identically in local play, the GameServer, prediction,
and rollback resimulation. No wire or snapshot fields were added.

The projectile pool conservatively includes **all non-freezing volumes**, including a few
melee/capability windows. Every projectile belongs to this pool; no file-controlled label can
bypass it. All volumes also count against owner/world totals. Tracked volumes cost 25 pose
units (the engine's maximum 24 sweep steps plus one conservative unit); other volumes cost
one. Both owner and world pose pools are enforced at spawn. A four-seat fighter contact
budget is therefore `800 × 3 opponents × (32 body hurtboxes + 1 shield) = 79,200` tests per tick.
This bounds volume updates and fighter collision work; arena geometry and non-fighter game
modes have separate engine costs. It does not promise a hardware-independent frame time.

Admission weights the requested envelope as `(hitbox lifetime × 25 + projectile lifetime)
× 3 × 33`, capped at 990,000 requested contact units per fighter (10,000 pose units).
The maximum built-in requested pose envelope is Nyx's 9,190. Runtime clipping is the tighter
bound on actual collision work, including repeated activations and paused melee aging.

Four-seat stress matches exercise all fourteen moves (both forms for Nyx), repeated for
720 ticks per slot with charge/release cycles. A normal hitstun cancellation separates slot
exercises while lingering projectiles remain. Spawn-time peaks include effects removed
before the end of a tick, which is why they exceed post-tick peaks for FightGuy.

| Fighter | Live owner / world | Non-freezing owner / world | Pose owner / world | Peak contact tests | Refused |
| --- | ---: | ---: | ---: | ---: | ---: |
| Manki | 4 / 16 | 3 / 12 | 25 / 100 | 864 | 0 |
| FightGuy | 10 / 40 | 8 / 32 | 75 / 300 | 1680 | 0 |
| Wibou | 3 / 12 | 3 / 12 | 25 / 100 | 2520 | 0 |
| Bonk | 2 / 8 | 1 / 4 | 50 / 200 | 1680 | 0 |
| Bashie | 1 / 4 | 1 / 4 | 25 / 100 | 540 | 0 |
| Salamander | 2 / 8 | 2 / 6 | 50 / 200 | 864 | 0 |
| Nyx | 5 / 20 | 2 / 8 | 25 / 100 | 336 | 0 |

| Runtime ceiling | Built-in maximum | Chosen | Headroom |
| --- | ---: | ---: | ---: |
| Live volumes, owner / world | 10 / 40 | 16 / 64 | 1.6 times |
| Non-freezing volumes, owner / world | 8 / 32 | 12 / 48 | 1.5 times |
| Collision pose units, owner / world | 75 / 300 | 200 / 800 | 2.67 times |
| Requested collision pose units | 9190 | 10000 | 1.09 times |

The contract suite saturates the weighted pool with an admitted four-seat package: eight
42-tick tracked volumes per owner, maximum bodies, alternating facing and long offsets to
force maximum sweep sampling, on a repeating 43-tick stage. Over 1,200 ticks it reaches
73,728 contact tests at peak and averages 70,420.48, with 32 live volumes at peak and 30.56
on average. Fixed peak and sustained thresholds fail if limit changes reopen this work bound.
Separate tests fill both count pools, exercise charged streams at intervals 1 and 3, finger
guns and tether, and restore a form-swap activation with a full pool.

The web demo embeds its bundled roster in the assembly and uses only its exact four-field
pins as trust authority. JavaScript's roster and stale-pin fallback select Workshop for any
other revision. Editor cook trust additionally requires roster membership, the application
repository's exact `client/Unity` project and `Assets/CharacterPackages/<id>` folder, and
physical, non-symlink source paths (including both JSON files). An external package named
`nyx` does not gain trust. Source creator metadata is not authority.

These runtime refusals change Shared simulation behavior for excess content. The next release
requires a protocol bump coordinated with the Master; this unit deliberately does not bump it.

Steam Deck memory measurement and owner confirmation of these constants remain outstanding.
The float ceiling covers sample storage; it does not measure total managed allocation.

## Next integration

Unit 4b needs an internal external-admission adapter that selects Workshop explicitly,
enforces host provenance namespaces, and resolves portable bindings before admission.
Do not expose external files in Fighter Select yet. The external admission policy still requires
implementation before player-created fighters ship.

The contract suite has no skips. It exercises the original eight gaps, mutations across
multiple fighters, file bounds, and trusted runtime and pose fingerprints obtained with
origin/main's loader. The timeout encloses the load itself. Simulation assertions count
live volumes, volume/body pairs, sweep contact tests, and finite state, without a wall-clock performance threshold. Runtime fingerprints use canonical concrete payload serialization, including budgets, with golden values captured from origin/main in a scratch build.
