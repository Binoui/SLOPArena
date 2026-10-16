---
key: table_manners
status: locked
locked: 2026-10-16
approved_by: maintainer
approval_scope: selected kids' craft table, centre cake layout, and floor-length cloth implementation
runtime_camera_review: pending
blockout:
  - .stage-authoring-cache/table_manners/craft-20261016/inspect-shell/
  - tmp/table-manners-craft/pass-01/
---

# Table Manners — Kids' Craft Table

On 2026-10-16 the maintainer requested a rework using the procedural stage process,
with the name and geometry open to change. From the presented options they selected
**Kids' craft table**, **Centre cake**, and **Floor-length cloth**. Those choices
authorize this bounded implementation. The earlier Cozy Interior living room is
superseded. The new captures have not been approved by the maintainer; in-game
visual review and two/four-player PvP acceptance remain pending. The stage keeps
the immutable key `table_manners` and its existing display name.

## Personality

Tiny fighters ruin craft time by fighting over a child's play-dough birthday cake.

## Fight premise

The round table is the main combat floor. Circle the centre cake to flank an
opponent, or jump onto its low, flat top to contest height and return to the
surrounding floor. Four inward-facing diagonal spawns leave the cake unoccupied
at the start. The cloth forms a solid recovery face down to the lower death plane;
there is no under-table route, wall jump, climbing, or automatic rescue.

## Composition decisions

Camera yaw below names the direction the gameplay camera looks: yaw 0° looks
north (+Z), 90° east (+X), 180° south, and 270° west. Coordinates are recorded
separately by `tools/blender/table_manners/layout.py`.

1. **Round craft table.** From every yaw, the broad round top fills the foreground
   and centre, edged by a raspberry band. Keep its roughly 30-unit diameter and
   four diagonal spawns. The 64-sided outline is the authoritative functional
   silhouette; the central cake and surrounding floor remain clearly distinct.
2. **One centre cake.** From the default spawn view the play-dough cake is the
   central landmark, approximately eight units across and 1.5 high. Its flat top
   is reachable with one full jump by each roster fighter. Coloured layers,
   flush lettering, small frosting marks and googly eyes explain the craft joke
   without adding blocking or landable shapes beyond the baked drum.
3. **Floor-length cloth.** From low recovery views, the gingham drape follows
   the table wall down to the lower death plane. Its folds stay within the
   authoritative outline. No legs, underside gap, chair or toy creates a false
   route through the death/recovery space.
4. **Flush craft surface.** From higher camera pitches, pink gingham, paper
   drawings, stickers, tape, paint marks and tiny glitter describe a used craft
   table. Drawings include the roster, a rocket and a rainbow. Keep the details
   flat and the table edge readable; raised crayons, scissors and craft pots
   belong in the distant room or below the lower death plane.
5. **North window and garden.** At yaw 0°, the sunny window and curtains occupy
   the middle background, with garden trees, fence, clouds and sky beyond it.
   The window has depth and a complete surrounding wall. Keep the exterior
   quieter than the table and leave the camera/fight corridor clear.
6. **Room around the whole orbit.** At yaw 90°, the bunk bed and toy box occupy
   the background; yaw 180° reveals a block castle, toy train and chair; yaw 270°
   reveals the art desk, lamp, shelving and drawings. Furniture is complete 3D
   massing, supported on the room floor beyond the combat/camera envelope.
   These are subordinate scale cues, not additional playable platforms.
7. **Below, above and far away.** Low views reveal wood flooring, a rug and
   scattered craft supplies below the lower death plane. Above, a high ceiling,
   hanging lantern, bunting and balloons complete the room. The garden and sky
   extend the north view beyond its window rather than leaving a backdrop void.
8. **Quiet movement.** Curtains, bunting and balloons sway gently; glitter and
   glow accents twinkle, and a toy train moves in the distant room. All movement
   is cosmetic shader animation. The table, cake and all authoritative geometry
   remain static.

Busy zones are the distant room corners and small drawing patches. The main
fighting floor between papers, its perimeter band and the recovery wall stay
comparatively quiet. No single view is allowed to hide the functional edge.

## Negative decisions

- No second gameplay prop, extra platform, hazard or moving collision.
- No tall decoration, candle, crayon or craft tool on a walkable surface.
- No background triangle inside the derived blast corridor or room furniture
  within the checked camera envelope above the lower death plane.
- No background floor that appears survivable before the knockout boundary.
- No retained Cozy Interior pack dependency in the rebuilt presentation.
- No gameplay-owning scripts or cosmetic Colliders in the runtime prefab.
- No claim that generated views establish human visual or PvP acceptance.

## Palette & lighting

Bright afternoon indoors: warm wood and daylight, cool sky fill, raspberry-pink
gingham, buttery yellow cake, and primary-colour craft supplies. The table carries
the strongest readable edge contrast. Softer room colours and faint garden haze
keep fighters clear. Stage-owned daylight must work in PvP without relying on
Training's extra key/fill/rim lights; review both lighting setups.

## Camera vantages

- Actual gameplay camera from Spawn_01: eight directions and two higher views,
  under both stage-only PvP lighting and the Training scene's additional lights.
- Full elevated orbit at 15° intervals and low recovery views at 45° intervals.
- Native inspection: top, front, back, left, right and isometric shell overlays.
- Human free orbit from the floor, cake top, edge and recovery positions remains
  pending, followed by external two/four-player PvP acceptance.

## History

The 2026-10-10 green coffee-table living room and its detail/polish passes used the
installed Cozy Interior pack. Its source and local presentation were backed up in
`.stage-authoring-cache/table_manners/craft-20261016/backup/` before the approved
rework. The new authoritative table/cloth/cake shell replaces its slab and legs;
original generated art replaces the old room roots. Vendor source assets are not
part of the rework.
