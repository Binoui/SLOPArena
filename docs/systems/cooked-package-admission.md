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
The compiler uses this same function to write budgets. Schema-defined capability costs
remain the authority for budget accounting.

`CookedPackageLimits` owns admission constants. It bounds input bytes before cloning,
directory reads before and during reading, JSON depth, pose counts and decoded float values.
The assembler checks pose byte availability before the animation loader allocates frames.
Runtime parsing rejects invalid enums, incomplete slot grids, empty timelines, operation
ordering, operation counts, and projectile lifetime and speed violations.

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

The lifetime envelope assumes every public emitter runs once every owner simulation tick
across all slots, including alternate forms. This bounds overlap between activations.
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
| Hitbox lifetime envelope | 322 | 2048 | Four times, rounded |
| Projectile lifetime envelope | 1140 | 4560 | Four times |
| Projectile speed, metres per second | 30 | 120 | Four times |
| Projectile flight lifetime, ticks | 240 | 960 | Four times |
| Flight plus attachment, ticks | 570 | 2280 | Four times |
| Manifest bytes | 2653 | 16384 | Four times, rounded |
| Runtime bytes | 62693 | 2097152 | 32 times, rounded; retains long display-string contract |
| Binding bytes | 15748 | 65536 | Four times, rounded |
| Pose bytes | 851998 | 4194304 | Four times, rounded |
| Pose bones | 28 | 128 | Four times, rounded |
| Pose animations | 34 | 256 | Four times, rounded |
| Frames per animation | 720 | 4096 | Four times, rounded |
| Total decoded pose floats | 212688 | 1048576 | Four times, rounded |

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
live hitboxes and finite state, without a wall-clock performance threshold.
