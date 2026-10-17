# Unity tools and self-tests

The primary package UI remains **Tools → SLOPArena → Ability Lab**. The other
project menus now share **Tools → SLOPArena** and make their purpose explicit:

| Menu | Purpose |
| --- | --- |
| Authoring → Character Asset Catalog | Edit semantic rig/clip/presentation bindings; validate and cook through the package service |
| Diagnostics → Sword Motion Audit / Validate Projectile VFX | Inspect existing presentation inputs |
| Tests | Focused Editor regressions, including package fixture restoration |
| Presentation → Rebuild Wibou / FightGuy Presentation | Regenerate a package's effects, trail style and projectile visual (`sloparena.presentation.<package>.build`); see the [character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md) |
| Prototypes | Scene, lighting, VFX and arena generators; inspect their output paths before replacing content |
| Legacy | Standalone skeleton/arena exports and old animation-config population; these do not publish today's character packages |

Use `sloparena.character.*`, `sloparena.stage.*` and `sloparena.lab.*` for the
maintained command paths. See [Unity CLI](unity-cli.md),
[character authoring](../characters/adding-a-new-character.md), and
[Ability Lab](../systems/ability-lab.md). Menu organization does not change the
Shared simulation, roster identities, package schema or script GUIDs.

## Package self-tests

Run in Edit Mode with the affected package assets saved. Tests refuse unsaved
catalogs in their package/output scope. Package creation, binding and generated
catalog persistence save only their own asset, preserving unrelated drafts.
Uncommitted saved files are preserved; Git cleanliness is not a prerequisite.
Imported font/shader dirty flags are not treated as editable asset drafts.

The following tests restore their exact starting source/catalog bytes, Unity
metadata, cooked roster/packages, generated catalogs, and intermediate cook files:

- **Character Package Authoring**;
- **Character Asset Cooker**;
- **Editor Development Content**;
- **Ability Lab Package Editing**.

They preserve the existing recook queue and prevent test imports or later Editor
frames from publishing test state. Restoration reimports the original catalogs
without cooking them again. The authoring binding probe restores the original
clip chosen by the contributor, rather than a hardcoded historical clip.

**Package Fixture Restoration** uses a temporary package and tests a successful
return, a deliberately thrown exception, original loaded clip restoration, extra
output deletion, queue preservation, unrelated draft preservation during package
operations, and refusal of an unsaved asset. It does not
need the four fighters' licensed rigs or clips; the project still needs its normal
Unity dependencies to compile.

The package-specific tests retain their source and binding requirements. Checks
that need a current persisted FightGuy preview prepare a cook inside the scope;
the starting cooked outputs and roster are restored afterwards, including a
previously stale cook. Missing or invalid authoring inputs remain real failures.
The cooker check verifies the current authored bindings rather than a historical
move list. Managed exception restoration does not establish recovery from an
Editor process crash.

For automated checks, use the existing gateway and your own bounded lease under
the [coordination protocol](unity-editor-coordination.md). Read semantic results
and the current Console; an exception or a failed restoration must fail the check.
Avoid executing a prototype menu merely to verify that the menu exists.

## Adding or updating a test

Use disposable package assets where the contract permits them. Tests requiring
trusted built-in identities use `CharacterPackageSelfTestScope` before any write
or cook. Include an injected failure when changing preservation behavior. Keep
checks tied to the changed seam rather than running every Editor self-test.
