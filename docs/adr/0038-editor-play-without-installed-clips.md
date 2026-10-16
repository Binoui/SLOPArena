# ADR-0038: Editor play without installed animation clips

**Status:** Accepted — 2026-10-16
**Amends:** [ADR-0029](0029-character-authoring-and-cooking.md) (Character Authoring and Cooking)

## Context

Most animation clips, and the effect prefabs adapted from vendor packs, come from
licensed packs that the repository cannot redistribute. The project is remaking them,
which will take a while. Meanwhile a fresh clone has the fighters' gameplay but not
their clips.

Under ADR-0029, Editor Training and Solo Play compile the current source and semantic
assets in memory and fail closed. A clip that is not installed is reported as an
asset-catalog error, so a clone without the clips cannot start a match. Ability Lab
preview also needs a generated animation catalog, which is gitignored, so fresh clones
never have one.

Gameplay does not need the clips. The Shared simulation, hitboxes and hurtboxes use
the cooked pose data in `content-cooked/`, which is committed and hash-verified.

## Decision

In the Unity Editor only:

- When the only errors preparing a package are bound animation clips, presentation
  prefabs or weapon prefabs that are not installed, Editor play compiles the current
  gameplay source and pairs it with the committed `poses.bin` and `client.bindings`.
  The committed package is hash-verified first. The assembler still checks that its
  bindings and poses cover every animation and presentation the current source needs.
- An edit that changes a pose-bake input (visual or hurtbox bone scale, model Y offset,
  crouch or slide animation, capsule or hip height) or needs an attachment bone that
  the committed poses lack fails with `asset-catalog.pose.rebake-required` or
  `asset-catalog.attachment.missing`. The committed poses would no longer be exact.
- That package's animation catalog is built from the committed bindings, with
  uninstalled clips and prefabs left empty. Those moves keep their hitboxes and
  hurtboxes but do not animate, and their effects are skipped. The console warns once
  per package, move or effect.
- Ability Lab builds its preview catalog from the committed bindings in the same way
  whenever no generated catalog resolves.
- Cooking, publishing, roster admission, player builds, PvP and the GameServer do not
  change. They still need the exact clips or the persisted package, and fail closed.

## Consequences

- A clone with the fighter models but without the licensed clips can play Training,
  open Ability Lab, scrub moves and edit gameplay values. Edits are saved as source and
  cooked later by someone who has the clips.
- In Editor play, missing art no longer counts as invalid content. Invalid source
  still blocks match start, as ADR-0029 requires.
- A contributor's view can differ from the shipped game in looks (unanimated moves),
  never in gameplay: the same cooked poses drive the simulation.
- Moves start animating as soon as their remade clips are committed and cooked.

## Rejected alternatives

- **A stand-in mannequin renderer:** more code to maintain, and the fighter models
  themselves are being committed.
- **A gameplay-only publishing cook:** it would split the source hash into gameplay and
  presentation parts and change admission. Revisit it when contributors need to publish
  cooks without the clips.
