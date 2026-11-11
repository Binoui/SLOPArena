# ADR-0066: Match look: a look per stage, lights outside URP's limit and an ink line on fighters

**Status:** Accepted 2026-11-10. The maintainer asked how the game could look better without new
models or maps, picked the layers from paint-overs on real captures ("4 3 B looks nice"), saw them
rendered in the Editor on Slop Pit, After Hours and Splash Deck, and said "go" to the build order.
In-game review is filed on the Slop Factory board.
**Extends:** the stage presentation pipeline (`sloparena.stage.art-apply`,
[stage authoring](../../skills/sloparena-stage-authoring/SKILL.md)). Presentation only: no Shared,
server or gameplay change.

## Context

- **Matches drew no post-processing.** Every scene camera had it off, so the grade volume
  `MatchVisualStyle` built (ACES, bloom) never rendered. Stage notes that tuned glow "for the
  match's ACES grade" described a picture nobody saw.
- **Player builds had no fighter shader.** Nothing referenced `SLOPArena/GraphicCharacter`, so
  Unity left it out of builds. `Shader.Find` returned null there: the seat rim, the fighter fill
  and `FighterXRay` did nothing on Steam.
- **The picks:** glow, a quieter background, a light on hits, a lamp over the Slop Pit ring, and
  stage light with an ink line on fighters.
- **URP gives a renderer at most four extra lights** on Ultra and one on Medium. After Hours'
  roof already carries five. A hit light added as a real point light did not show on it, and a
  real ring lamp competes with the lights a stage already has.
- **Fighter meshes are flat shaded or split along seams.** A hull pushed out along vertex normals
  tears open at every hard edge.

## Decisions

1. **The fighter shader ships.** It moved to `Resources/Shaders` and is loaded with
   `Resources.Load`, like `FighterXRay`.
2. **Matches render post-processing.** `MatchVisualStyle` switches it on for the match's base
   cameras, including one that joins later, and restores them when the match ends. The grade comes
   from the stage's look. ACES is gone: it shifted hues and bleached emission.
3. **A stage owns its look.** `StageLook` on the stage prefab root carries `StageLookSettings`:
   grade, far blur, scenery tints, pools of light, shafts, dust and the fighter look.
   `sloparena.stage.art-apply` writes it from an optional `look` in the stage's presentation. The
   defaults in `StageLookSettings` are the look of a stage without one, so every stage, an older
   one or a Workshop one, gets the fighter treatment and a light grade.
4. **The default grade keeps a stage's brightness.** It has no tonemapping, a little contrast and
   saturation, a soft bloom and a light vignette: measured on three bright stages it stays within
   2% of the picture before. A stage built on glow asks for Neutral tonemapping, which rolls
   highlights off instead of clipping them and darkens a bright stage by 6 to 9%. The three tuned
   stages use it, as in the captures the maintainer saw.
5. **Quieter scenery is a tint per renderer**, named in the look and applied through property
   blocks when the stage spawns. A name is a renderer or a group above it. `art-apply` refuses a
   name that matches nothing. A fade by
   distance in the stage shaders was rejected: it cannot tell a far platform from scenery, and a
   sky needs less than a skyline. Vertex colours from the generators would need a Blender rebuild
   of every stage.
6. **Look lights are not URP lights.** A stage's pools and the flash of a hit are four slots in
   global shader arrays (`SlopArenaStageLights.hlsl`). The stage shaders and the fighter shader
   work them out themselves, so they show on any floor and on every quality level. A pool falls
   off like a URP spot light of the same values: the Slop Pit canvas measures the same as under
   the real lamp of the preview. Forward+ would lift the limit too, but it changes the renderer of
   every quality level and was not measured.
7. **Fighters take the stage's light.** Two kick lights on their edges, a lift and a cap on lamp
   light are global shader values set once a match. A stage that names no kicks gets them from its
   key light. The cap exists because the fighter shader adds every lamp: without it a strong pool
   bleaches whoever stands in it.
8. **The ink line is an inverted hull along smoothed normals.** `FighterInk` averages the normals
   of each corner into the tangents of a mesh copy when a fighter is styled; the shader pushes
   back faces out by a constant pixel width, kept between two widths in metres. This needs
   readable meshes, so Read/Write is on for the body models. Baking at cook was rejected: it adds
   a payload and a hash to every package. A model postprocessor reimports every model in the
   project when it changes. A screen-space outline needs a renderer feature on every quality
   level and draws a different line from the one that was picked.
9. **A hit flashes the colour of its impact graphic**, stronger and wider by tier, holds through
   Hitstop and fades. It lifts what is dark and leaves a bright floor alone, so a lit canvas does
   not burn out. Reduced Flashing switches it off; the hit-feel review shows it regardless.
10. **Low quality keeps the grade and a plain bloom** and drops the far blur.
11. **`art-apply` applies presentation data on a stale stamp.** Since the hazards changed their
    scenes and arenas, After Hours and Splash Deck art carries an old stamp, and the command
    refused every change to them. It still refuses to install a model on a stale stamp; lights,
    atmosphere and look go through and the result says `staleStamp`.

12. **Every rostered stage gets a look of its own.** After the first three the maintainer asked
    for the same on every stage (2026-11-11). A stage without a generator keeps its look in
    `<stage>.look.json` beside its source scene, applied by `sloparena.stage.look-apply`. Bright
    stages keep their brightness and only quiet their scenery; dusk and night stages take Neutral
    and more bloom. The far blur starts behind anything a player must read: a course's route
    ahead, Mayhem's screens and host booth.

## Consequences

- Every stage's picture changes. The three tuned stages match the captures the maintainer saw;
  the others keep their brightness and gain the ink line, kicks from their key light, a contact
  shadow and a soft bloom.
- Read/Write on six body models changed their import settings, which recooked six packages:
  source, content and package hashes and the roster pins changed, gameplay payloads did not.
- A new fighter whose body model is not readable has no ink line and logs one warning.
  Wibou's coat model is still local and ignored, so its Read/Write setting is too.
- The look costs about 0.3 to 0.5 ms a frame at 1920×1080 on Ultra in the Editor (RTX 4070),
  most of it post-processing. Run to run the Editor varies by as much.
- Per-stage values live in `tools/blender/<stage>/presentation.py`. A look change needs no Blender
  run and no C#.
- QA on Medium found a false shadow across the far half of the Slop Pit ring. It predates the
  look: the stage shaders read the main light's shadow without its distance fade, so a quality
  level with one cascade read past its shadow map beyond 20 m. The stage, water, kraken and
  fighter shaders now fade shadows at the level's shadow distance; Ultra is unchanged.
- QA also saw Sir Bonk's seat-colour silhouette through the plank he holds. `FighterXRay` draws
  between the stage and the fighters, and a held prop was drawn with the stage. The Editor always
  showed it; builds never did, because they had no fighter shader. A fighter's own opaque props
  are now drawn in the fighters' queue.

## Not decided here

- Fighters that join after the match starts are not styled; that was already the case.
- A rebuild gives the same geometry for After Hours and Splash Deck, so their stale stamps are a
  record problem, not a visible one. Refreshing them means committing two rebuilt models.
- Cost on a Steam Deck and the look in a player build are not measured.
