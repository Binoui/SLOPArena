# ADR-0067: A fighter is identified by its package

**Status:** Accepted 2026-11-12. Units 1 to 5 are merged. Unit 6 removes the remaining identity enum uses; isolated verification is in progress. The owner approved the design ("agree with design") with three
choices: a Workshop badge in the same Fighter Select grid, Arcade mode support later, and a
portrait rendered from the model when the creator gives none.
**Deciders:** @Binoui
**Related:** [ADR-0025](0025-workshop-package-architecture.md),
[ADR-0026](0026-workshop-multiplayer-synchronization.md),
[ADR-0029](0029-character-authoring-and-cooking.md),
[ADR-0064](0064-workshop-creator-route.md), [ADR-0065](0065-workshop-item-format.md),
[Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md) (decisions D8 to D13)

## Context

The owner wants players to make fighters as files and play them. A proof on 2026-11-11 took one
files-only fighter into a real Match in a player build. It worked only by taking a built-in
fighter's slot. The match path names a fighter with the `CharacterClass` enum in admission,
selection, and the HUD, so the HUD showed the borrowed fighter's name, an empty portrait, and its
move icons.

A census on 2026-11-12 found 373 non-test source lines in 59 files that depend on the fixed
list. Admission has several separate gates: a roster manifest that requires a selector, a
catalog builder that requires four named fighters, a handle map record that requires the enum,
selector strings in the Master's Rooms, and enum parsing on the GameServer and the client.

Much already works by package. The loader pins package ID, version, and hashes. The simulation
registers a definition and poses per entity and never branches on the enum. Catalog entries have
a nullable legacy selector. Replay rosters, Arcade records, and feedback carry package identity.
ADR-0029 already says that package identity is Character identity and calls the selector
temporary.

## Decision

1. **A fighter is its package.** Outside a Match, a seat holds a fighter choice: one exact
   package revision and a starting form. No new `CharacterClass` value is ever added.
2. **Provenance comes from the host.** Whether a package is bundled, local, or a Steam item is
   decided by the adapter that read the files, never by a field inside the package. A bare ID
   such as `fightguy` is valid only from the shipped roster. A local package must be `local.*`.
   A downloaded package must be `workshop.<id>` for the ID Steam delivered.
3. **Admission is a type.** Only Shared's admission can create an admitted fighter. It applies
   the namespace rule, the cook profile, capability checks, and limits. A Match Content Catalog
   is built only from admitted fighters, under a match-content policy (local play, standard
   Room, Workshop Room) that is checked against the complete set.
4. **A prepared Match owns its content.** Starting a local Match resolves every seat at once
   into the roster plus the chosen revisions, the handle map, and each seat's presentation. It
   returns that whole, or a rejection with diagnostics before anything spawns. A recook during
   a Match does not change the Match. Two revisions of one package in one Match are refused.
5. **One library, one presentation path.** An immutable snapshot lists every installed fighter,
   built-in or made. Fighter Select, the HUD, results, and feedback read a resolved fighter's
   name and art from it. No caller builds a resource path from a fighter's name. Each fighter
   carries a `card.json` with its description, work-in-progress flag, and Tutorial hint.
6. **Local Matches get a selector-free handle map.** Map schema 3 holds a handle, an exact
   identity, and a display name. New local replays store the admitted content map. The online
   schema 2 map and its hash do not change in Phase 2.
7. **The enum survives in one adapter.** The Room and launch formats still carry selector
   strings. One protocol adapter reads and writes them until Phase 3 replaces those formats in
   a single release. The enum is private to `LegacyFighterProtocol`; callers pass package IDs.
   Stored hurtbox files accept old names and write package IDs, and reject numeric identities.
   `tools/check_fighter_identity.py` guards the source boundary.

### The owner's choices

- Made fighters appear in the same Fighter Select grid as built-ins, with a Workshop badge.
- Made fighters play in Brawl, Training, and couch Matches first. Each Arcade mode opens later,
  one at a time, and keeps its own admission list until then.
- A made fighter's portrait is rendered from its model when the creator gives none. Move icons
  are optional and fall back to a neutral glyph, never to another fighter's art.

## Considered options

Two designs were sketched independently from one grounding map and scored by a third pass
against the code.

- **Exact-revision choices with match-owned content (chosen as the base).** It pins what a seat
  means, keeps the online map untouched, and treats drawing a files-only fighter and hardening
  the loader as part of the roll-out.
- **The package ID as the fighter, resolved at Match start.** It had the simpler public surface
  and deleted the enum inside Phase 2. It lost on its roll-out: it wrote lowercase selectors
  into the online schema 2 map, which changes the content hash between builds
  (`src/Shared/MatchContentHandleMap.cs` writes the enum's own spelling), and it moved built-in
  art, which can be a transitive cook input. Its match-content policy, its census and rewrite
  tool, and its `card.json` are kept.
- **Runtime values added to the enum.** The numbers mean nothing across machines, and every
  caller keeps the enum.
- **A union of enum or package.** It keeps two ways to name a fighter forever.
- **One mutable registry for menus and Matches.** It exposes recook timing and registration
  order to every caller.

## Roll-out

Each unit lands alone and leaves every mode playing. Units 1 to 3 change nothing a player sees.

1. The catalog accepts a fighter with no selector. Local handle map schema 3 and the replay
   content map. Training's player 1 resolves by package.
2. The HUD, results, and feedback read a resolved fighter's name and art. `card.json` for the
   seven built-ins.
3. Every local seat stores a fighter choice. Fighter Select lists from the library, and
   `MenuRoster` is deleted.
4. Four pieces that make a made fighter real: (a) the loader takes a trust profile, recomputes
   budgets, and applies limits; (b) the `fighter` item type and portable bindings; (c) GLB clips
   and shared clips as motion sources in the renderer, with one sampling rule for the bake and
   the display; (d) the in-game cook with diagnostics on screen.
5. Installed fighters appear at Fighter Select and play in every local seat.
6. The remaining enum uses go, outside the protocol adapter.

Phase 3 then changes the Room choice, the launch request, the online handle map, and the
Master's roster list in one release with a protocol bump.

## Consequences

- Admission, selection, and presentation each lose their fixed list. A contributor adds a
  built-in fighter by adding a package, its art, and its card.
- No unit intends a recook of the seven fighters. Each unit proves that with the release
  builder's cook check, because art and scripts can be transitive cook inputs.
- New local replays carry more metadata. Old replays stay readable.
- A subscribed fighter is other people's data loaded into local play. Unit 4a therefore lands
  before any installed fighter is shown.
- Unit 4c needs a shared clip library with clear rights to publish baked joint positions, and
  one rule for where a clip puts the hips. Both wait on the owner's animation inventory.
- Limits on memory need a measurement on a Steam Deck.
- ADR-0065's `fighter` item gains `card.json` and a portrait among its payload files.

## Non-goals

This ADR does not design Phase 3's wire formats, the shared clip library, the portable binding
fields, or the in-game editor.
