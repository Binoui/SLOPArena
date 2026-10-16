# Blender MCP technical recipes

Observed during Manki character polish with Blender 5.2. Verify API compatibility and scene assumptions before reuse. No persistent helper implementation is provided here.

## MCP state and evidence
- Initialize required Python imports in each execution call; globals did not persist reliably between calls. Explicitly managed `bpy.app.driver_namespace` state is possible, but remove scratch state afterward.
- Render to an explicit output path, then read that image. When parallel image reads appeared associated with the wrong paths, sequential reads resolved the ambiguity. This tool anomaly was reported; do not assume it is universal or permanent.
- When loading library objects, Blender can replace entries in the supplied `target.objects` list with object references. Pass a fresh list and retain names independently.
- Imported objects may be unlinked and retain parents/offsets. Remove only positively identified scratch imports, never arbitrary `.001` objects.

## Stable comparison placement
Animated rig locations can be reset by rendering/frame evaluation. Put each comparison rig under an unanimated display-offset empty rather than changing its keyed location. Keep material and lighting treatment identical across the comparison.

## Restore-safe pose checks
Save each pose bone's `matrix_basis` and the active action. Temporarily detach the action when necessary to prevent render evaluation resetting the test pose. Restore in `finally`, re-evaluate the current frame and update the view layer. Inspect other animation drivers/NLA before assuming action detachment is sufficient.

For a world-space test axis, transform it by the inverse rig-world linear matrix. Rotate the pose bone around its head using translation → rotation → inverse translation applied to its saved pose matrix. Do not assume the sign means raised/lowered: compare the hand's evaluated world position before and after. Manki checks selected signs by observed Z movement for raised/lowered arms and Y movement for forward reach.

## Editing evaluated geometry without corrupting rest coordinates
For mesh object O, rig R and influencing pose bone P, Manki's linear armature deformation used:

`K = inverse(O.world) * R.world * P.matrix * inverse(P.bone.matrix_local) * inverse(R.world) * O.world`

Normalize weighted sums over valid influences. With blended linear matrix A, convert a desired world delta to rest-space delta:

`delta_rest = inverse(A) * inverse(O.world).linear * delta_world`

For a new vertex, use the inverse normalized blended affine K on its target mesh-local evaluated position.

This was used with preserve-volume disabled. It is not a general inverse for dual-quaternion skinning, envelopes, shape keys or arbitrary modifier stacks. Reject singular transforms and validate resulting evaluated positions. Preserve original rest coordinates for untouched vertices.

## Custom normals without welding the actual asset
1. Copy the mesh into a disposable proxy; retain destination corner normals.
2. Remove proxy custom-normal/sharp-edge attributes if present.
3. Store original corner indices in a BMesh loop integer layer.
4. Weld only the proxy at an asset-scale-appropriate tolerance; update normals.
5. Mark smooth and apply the intended sharp angle (Manki used 60 degrees).
6. Map proxy corner normals back using stored corner indices. Replace only the intended destination region.
7. Apply `normals_split_custom_set`, validate, then remove the proxy.

Manki used a weld tolerance of 0.00001 in its mesh coordinates. Do not copy that tolerance without checking scale. Preserve UV seams, topology and skin weights on the real mesh.

## Shoulder repair lesson
Radially reshaping existing patch vertices plus improved normals looked better in clay, but textured lighting still exposed poor triangles and painted patches. The accepted repair replaced local shoulder topology and used local texture strips matched to the retained upper-arm boundary. It required seam/winding/degeneracy and weight checks plus posed renders. A subsequent approved pass reduced radial bulk while preserving the boundary.

The ring positions, radii, bone-weight formulas and texture-strip dimensions were specific to Manki. Do not promote them to a generic shoulder reconstruction algorithm. Cleaner topology can still produce the wrong art direction: Manki's first reconstructed shoulders were too muscular.

## Dark speckled fur at gameplay distance (Manki 2, 2026-10-16)
Generated remeshes often model fur as small flat-shaded spikes. Under a near-vertical key light every facet that faces down or sideways goes dark, so the fur reads as dark speckled tips once the character is small on screen. Blender renders with a bright ambient did not show it; check in the target engine first. In Training, forcing sharper mips (texture `mipMapBias` −3) and turning off shadow receiving changed nothing, while diffused body-like normals on fur vertices removed it. `tools/blender/manki_rig/fur_normals.py` diffuses area-weighted vertex normals 40 times over the mesh edges and blends fur corners (orange texels) towards them; bones, weights and UVs stay identical.

The A/B was done without importing anything: in a `sloparena.stage.views` session, `eval` swapped the live renderer's mesh copy or runtime material texture, which Play Mode discards.

## Repainting a generated face (Manki 2, 2026-10-16)
Rebaking from the generator's high-poly source does not help when its texture carries the defects (dirt, smeared mask, a lid painted like a wound). `tools/blender/manki_rig/face_paint.py` instead paints in an orthographic front view and projects back:
- Render the head with a z-buffer and per-pixel triangle ids; take features that have geometry (brows) from triangle ids, not from colour, or the painted fringe survives and the ridge goes half skin-coloured.
- Project cleaned regions only onto camera-facing texels, but thin features (lash line, mouth, nostrils) with a permissive facing/depth test, or the groove walls keep the old paint and the line breaks up.
- Dilate edits into texels no triangle owns, using the same texel-centre rule as the projection. A polygon-fill coverage mask is wider and leaves hairline seams of old paint.
- Preview with backface culling: without it, Blender showed the inside of the head through the wink's hairline slit.

## Generated model rejected by Mixamo (Manki v3, 2026-10-16)
Mixamo rejected a generator FBX with four embedded PBR maps and quad-dominant topology. It accepted a re-export with one triangulated mesh, one material and a single colour map beside the model, with transforms applied and feet at the origin (`tools/blender/manki_rig/mixamo_package.py`). The exact cause was not isolated. UVs survive Mixamo unchanged, so keep the generator's full-resolution colour map for the game and use the upload's map only as Mixamo's preview.

Feature-placement tools tied to one mesh (face paint coordinates, tail detection thresholds) need re-checking for each regenerated model, even of the same design. v3's face sat differently in the same camera, its brows were painted correctly (so they are kept rather than repainted), and its club-shaped tail end confused a tip and root search tuned on Manki 2. Put per-model numbers in data (`face_profiles/*.json`) and regress the previous model's output byte for byte after each change.
