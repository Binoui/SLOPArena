# ADR-0064: The installed game is the Workshop creator tool

**Status:** Proposed — 2026-11-10
**Deciders:** @Binoui
**Amends:** [ADR-0027](0027-open-source-modkit-preview.md). It replaces "Unity is the preferred
authoring environment for the official ModKit" and the Unity box at the top of its workflow.
**Related:** [ADR-0025](0025-workshop-package-architecture.md),
[ADR-0029](0029-character-authoring-and-cooking.md),
[ADR-0065](0065-workshop-item-format.md),
[Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md) (decision D2). It replaces the Editor-first creator route of the
[2026-10-13 plan](../plans/2026-10-13-public-reference-kit.md).

## Context

The owner wants players to make fighters and share them through Steam Workshop. ADR-0027 made the
Unity Editor the creator ModKit. Today that route needs Unity, the paid Animancer 8.4.0, and the
gateway. A cook from a clean clone is unproven, and there is no preview in the installed game
(roadmap Appendix A, `docs/contributing/unity-setup.md:157`).

Two games show what the entry cost does to supply (roadmap Appendix B, from the Steam Workshop
browse pages on 2026-11-10). Rivals of Aether asked for sprites and script files. It got 2,299
public items in its first ten months, 941 of them characters, from 120 average players. Rivals of
Aether II asks creators to install an Unreal 5.8 modkit. It has 148 public items after ten months,
from 339 average players. That count leaves out mods hidden after its engine upgrade, so the real
gap is smaller than 15 to 1, but still large.

The cook needed the Editor for two jobs. It bakes `poses.bin` in
`DeterministicPoseTrackBaker.cs`, and it fits capsules in `tools/character_sizes/hurtbox_fit.eval.cs`.
Phase 0 tested whether the game can do without it.

- **Step 0.1.** A Win64 player with no Editor and no generated catalog loaded `fightguy.glb` through
  glTFast 6.18.0 in 111 to 123 ms. Playing the GLB's own clips stayed within 0.085 mm of the
  built-in fighter on every tick of five clips, except 0.454 mm on the idle loop's last tick
  (`tmp/workshop-spikes/glb-runtime/b3-main/report.txt`).
- **Step 0.2.** A throwaway bake from the GLB matched Unity's `poses.bin` within 0.40 mm on
  FightGuy, Wibou, and Bonk. A port of the capsule fit landed within 0.6 mm. A five-minute bot Match
  gave the same hit events with both pose files: 61 in the mirror Match and 510 in the four-fighter
  Match (`tmp/workshop-spikes/pose-bake/results.txt`).

The gameplay half needs no Editor already. `CharacterPackageCompiler` is plain Shared code with a
`Workshop` profile, and the web demo loads the same four cooked files without Unity.

## Decision

1. **The installed game is the creator tool.** It imports a creator's files, cooks them under
   `CharacterCookProfile.Workshop` with the pose bake and capsule fit, shows diagnostics, previews
   the result in Training, and publishes to Steam Workshop. A creator needs the game, a rigged model, and a
   text editor. A creator does not need Unity.
2. **Workshop content uses open formats.** The model and its clips travel as one GLB. Gameplay
   source is the JSON Character Authoring Document. The item manifest is JSON. The cooked files are
   the formats the game already writes. [ADR-0065](0065-workshop-item-format.md) defines what an
   item holds.
3. **The Unity project stays as a second exporter for contributors.** Built-in fighters keep the
   Unity authoring and cooking path of ADR-0029, and their packages keep their current schema.
   Unity already exports a GLB for all seven fighters through `client/Web/tools/fighter_glb.py`,
   which needs frame counts and a bone map added to write the full item. The game validates an
   item the same way whichever tool made it.
4. **ADR-0027's preview rule stays.** The installed game is the authoritative preview. Because the
   game is now also the cook tool, the preview needs no separate ModKit step.
5. **A GLB in an item is a published format, not a raw source asset.** ADR-0027 rejected loading
   raw source assets in the installed game, and ADR-0025 keeps raw FBX out of the runtime contract.
   This ADR keeps both rules. The cooked gameplay files stay the simulation contract. The GLB is the
   presentation payload, held to the constraints in ADR-0065, listed in the manifest, and covered by
   its hash.

ADR-0027's other rules stand: the ModKit code is open source, creators use any DCC tool, and
Humanoid is the default and not a universal requirement.

## Considered options

- **Unity Editor ModKit (ADR-0027 and the 2026-10-13 plan).** It reuses the cook and Ability Lab as
  they are. It lost because it puts a game-engine install, a paid plugin, and the gateway in front
  of every creator. Rivals 2 is the closest example of that cost, and its supply is low. A cook from
  a clean clone is also unproven, and art would still need a payload format.
- **Server sync first, tools later.** It builds the catalog and handle-map work first. It lost
  because players see nothing for the longest time, and it fixes validation before the item format
  is known (roadmap Appendix D).
- **AssetBundles as the payload.** They lost because they need the Unity Editor to build and tie
  every item to one Unity version. Rivals 2 hid every older mod after its Unreal 5.8
  upgrade.
- **A visual kit editor in the game now.** It lost on timing. Files plus a reload in the game is the
  Rivals 1 workflow, and agents can write the files. It waits for Phase 5 and demand.

## Consequences

- The game gains two new runtime parts: a GLB importer and an in-game cook. Both read untrusted
  files, so both need the limits in ADR-0065.
- glTFast becomes a client dependency. The owner approved a glTF importer for the spike (decision
  D7), and step 0.1 used glTFast 6.18.0. Approving this ADR approves shipping it. The cook source
  hash tracks `Packages/manifest.json`, so adding it marks all seven cooks stale, and Phase 1
  recooks them.
- Player builds do not contain the `SLOPArena/GraphicCharacter` shader, so `MatchVisualStyle`
  leaves fighters on URP Lit (found by step 0.1). A GLB fighter only gets the game's look once this
  is fixed.
- Workshop fighters look plainer than built-in fighters at first. Six of the seven built-in
  fighters use bespoke presentation scripts.
- Two exporters can drift. A check should export each built-in fighter through the GLB route and
  compare its bake with the Unity cook, as step 0.2 did by hand.
- With no editor UI to explain fields, the authoring reference must be generated from code and
  readable by a person or an LLM agent.

### What stays unproven

- Every GLB in Phase 0 came from Unity. Clips that never went through Unity's humanoid solve, and
  GLBs from Blender or other tools, are untested.
- Tails, shells, forms, game weapon attachment, effects, sound, `PlayerRenderer` states, Steam Deck
  frame time, and memory are untested (step 0.1 and 0.2 reports).
- The bake that passed is a throwaway. A C# port inside the game is not written.
- In step 0.2, full world snapshots differed on 2,598 of 18,000 ticks in the mirror Match and 1,801
  of 18,000 in the four-fighter Match, with equal hits and fighter fields. ADR-0065 lists the cause
  as an open question.
- No outside creator has made a fighter this way. The Phase 2 check measures that time.

## Non-goals

This ADR does not define the item format (ADR-0065), the Workshop screen, the in-game cook's code,
the importer's limits, or a visual editor. It does not change how built-in fighters are authored.
