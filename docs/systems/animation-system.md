# Unity Animation System

Animancer plays clips selected from Shared simulation state. Online, that state comes
from the predicted world corrected by authoritative frames. The runtime path is:

```text
MatchContentCatalog
  -> admitted cooked character package + generated client catalog/rig
  -> PlayerRenderer
  -> Animancer
```

## Runtime ownership

MatchBase and Ability Lab call `PlayerRenderer.Configure` with the definition,
baked poses, animation catalog and rig. It validates the sources before replacing
the working model and refreshes animation bindings when the rig is reused.
Entity registration and weapon setup remain with their existing owners.

Every roster fighter uses an admitted cooked package under `content-cooked/<package>/`.
The package contains the runtime definition, pose tracks, client bindings, and manifest
hashes. `MatchContentCatalog` requires valid cooked packages and keeps each immutable
definition with its baked pose projection.

The generated Unity `CharacterAnimationCatalog` is a regenerable client binding. It
contains the package ID, source hash, rig reference, semantic animation IDs, frame
counts, and extrapolation metadata. Client setup rejects missing, duplicate, or
mismatched package bindings. Generated assets live under
`Resources/Generated/CharacterPackages/<package>/`.

`MatchBase.SetupRenderer` resolves each package's generated client assets before model
setup and rejects unresolved bindings. `PlayerRenderer` retains legacy
`CharacterAnimationConfig` compatibility helpers and Resources fallbacks for callers without a generated
catalog. Those helpers are compatibility code, not current roster authoring paths.
Edit the package and its `CharacterAssetCatalog.asset`, then cook and validate it through
the [character workflow](../characters/adding-a-new-character.md).

## Playback

`PlayerRenderer.ApplyServerState()` drives `UpdateAnimationState()`:

- idle/run use cooked presentation semantic IDs;
- jump, fall, air dodge, and hitstun use cooked presentation IDs;
- attacks use `(AttackSlot, ComboStage)` and cooked `AnimationNames`;
- attack clip speed maps baked frames to the authored stage duration;
- generated extrapolation metadata controls behavior past clip length.

Animancer plays clips directly. AnimatorController transitions and animation callbacks
do not own gameplay timing or hit results.

## Cooked poses

Server hitboxes use cooked pose tracks and semantic bone IDs. The client receives the
same `entry.BakedAnimation` payload used by the catalog entry. This keeps rendered
poses, bone-attached collision, and server timing on the same package hash.

## Weapon attachment

Package client bindings can supply a `WeaponAttachConfig`. `WeaponAttach` places the
prop on its configured bone with the authored position and rotation offsets.
`DeterministicPoseTrackBaker` uses that configuration to bake weapon points such as
`_weapon_hilt` and `_weapon_tip` alongside the character poses. The package cook owns
those tracks; the legacy skeleton-bake menu is not the roster authoring path.

## Source references

- [`MatchContentCatalog`](../../src/Shared/MatchContentCatalog.cs) admits cooked roster content.
- [`MatchBase.SetupRenderer`](../../client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs) resolves package bindings.
- [`PlayerRenderer`](../../client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs) selects and plays clips from Shared state.
