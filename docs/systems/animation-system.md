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

## GLB motion for portable fighters

A made fighter plays its own GLB clips ([ADR-0065](../adr/0065-workshop-item-format.md), path A).
Admission returns a `FighterItemSnapshot` whose `PortableFighterMotion` holds the parsed skeleton and
every bound clip. `MadeFighterPresentation.PrepareAsync` turns that into what the generated assets
give a built-in fighter, once per revision for the session:

```text
FighterItemSnapshot (verified bytes + PortableFighterMotion)
  -> ModelSwapImporter.ImportFighterAsync   (glTFast; upright node from the Shared root correction;
                                             generic Animator, no avatar)
  -> GlbMotionClip per clip                 (GlbMotionRig.SampleLocal at every tick)
  -> GlbMotionBinder + placeholder clips    (one legacy key clip per semantic ID: length and loop only)
  -> runtime CharacterAnimationCatalog      (CookedCharacterClientAssetResolver finds it by revision)
  -> PlayerRenderer                         (AdoptModel binds one GlbMotionState per key)
```

`GlbMotionState` is an Animancer `AnimationJobState`. Its job writes the sampled local transforms of
every node any of the fighter's clips animates, through `TransformStreamHandle`s. Ticks are the
sampled keys exactly, and between ticks the display blends the two neighbours. Animancer runs its
pre-updates before playable time advances, so each GLB state hands its job the time its playable
reaches in this evaluation, the time a clip state samples, rather than the previous frame's. The state's key is the
semantic ID's placeholder clip, so the renderer's existing `Play(clip, fade)` calls work unchanged.
Fades between GLB clips, from a GLB clip into idle, landing and hitstun all happen in Animancer's
mixer, not in a pass written after it. The bake reads the same sampler, so the drawn bones sit on
`poses.bin` at every tick. Built-in fighters keep their generated catalogs and clip states.
A GLB-driven model leaves the shared humanoid grab and throw clips unloaded, because shared
clips stay closed to made fighters.

A revision's model, clips, catalog and art live while anyone uses them. `PrepareAsync` returns a
`MadeFighterPresentation.Lease` for the caller (the menu that offers the fighter), and
`MatchBase.SetupRenderer` takes another for each Match that draws it and disposes it when the Match
is destroyed. The last lease destroys the assets, so a menu can let go mid-Match. A cached
presentation whose Unity objects no longer exist is dropped, never served, and every Play session
starts with an empty cache.

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
- [`GlbMotion`](../../src/Shared/GlbMotion.cs) is the rule-1 sampler; [`PortablePoseBake`](../../src/Shared/PortablePoseBake.cs) bakes and verifies with it.
- [`GlbMotionState`](../../client/Unity/Assets/Scripts/Runtime/Workshop/GlbMotionState.cs) and [`MadeFighterPresentation`](../../client/Unity/Assets/Scripts/Runtime/Workshop/MadeFighterPresentation.cs) draw a made fighter.
