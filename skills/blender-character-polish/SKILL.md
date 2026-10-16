---
name: blender-character-polish
description: "Safely polish existing textured, rigged characters through Blender MCP. Use for mesh, normals, texture, material, accessory, skin-weight and deformation cleanup; candidate comparisons and game-asset handoff. Not animation authoring or Unity package integration."
---

# Blender character polish

## Ownership
Preserve the character's approved identity, rig compatibility and gameplay contracts. Read the character handoff and current ticket first. Animation creation/import belongs to `sloparena-animation-authoring`; package integration belongs to `sloparena-character-workflow`. This skill owns Blender asset editing and visual evidence, not simulation or publishing.

Use the [character model polish guide](../../docs/characters/character-model-polish.md) for the art brief, scope choice, acceptance and current handoff. Select up to three high-value defects; do not repeat every Manki intervention on another character.

## Inspect before editing
Read the available MCP tool schema before first use. Inventory the active file, candidate objects, parents, transforms, modifiers, rig/actions, UVs, weights, material slots and packed/external images. Record source paths and identify the approved baseline. Never infer that an object named Original contains the original source: comparisons may use the preceding iteration.

Distinguish geometry, split normals, painted texture, lighting and skin-weight defects. Inspect matching textured and clay views; clay alone cannot establish that a textured defect is fixed. Reuse existing materials and rig infrastructure before adding replacements. Diagnose weight ownership and local triangle density before smoothing a deformation defect.

Preserve skeleton hierarchy, rest transforms, bind pose and animation bindings by default. Joint relocation, additional bones or changed rig mapping requires an explicitly approved rig-change scope and the character workflow's retargeting/cooked-pose checks; it is not incidental surface cleanup.

## Bounded iteration
1. State the intended change, preserved features and non-goals; obtain approval.
2. Save a separate candidate. Never overwrite approved studies or Unity source during exploration.
3. Change the smallest surface that fixes the demonstrated problem. Do not globally weld UV seams, smooth intentional angular shapes, redesign the skeleton or add detail to hide defects.
4. Compare identical cameras, lighting, materials and poses against the previous pass and periodically against the untouched original. Label both baselines accurately; check cumulative silhouette drift.
5. Validate changed regions in motion. Use actual bound clips where available; otherwise document controlled poses and explicitly leave full animation verification open.
6. Deliver candidate path, textured comparisons, relevant motion evidence, checks performed and remaining defects. User approval promotes the next baseline.

## Evidence gates
- Geometry: inspect topology, winding, degenerate faces, normals and joins in the changed region. `mesh_health` in `tools/blender/stagekit/blender.py` counts opposite-wound neighbours, zero-area faces and loose vertices for any objects; compare original and candidate.
- Skinning: inspect weights and extreme deformations; unchanged weights do not by themselves prove good motion.
- Textures: inspect seams, stretching and transitions under matching lighting.
- Appearance: include front, side/three-quarter and gameplay-size views as appropriate. A Blender gameplay-size approximation is not Unity verification.
- Structural repairs: arrange an early target-engine check through `sloparena-character-workflow`, with ordinary backface culling and raised-arm, underside and rear views as relevant. Do not hide missing coverage with double-sided shading. Record this gate as open if Unity is unavailable.
- Export: compare original/candidate triangles, imported vertices, renderer/material counts, texture formats/dimensions and memory estimates. One material is not one draw call; estimates are not performance benchmarks. Preserve bind pose/scale and bake required procedural shading for the target shader. Keep the comparison scene separate from the shipping asset.
- Shipping: real Unity animation, attachment, lighting and gameplay-camera acceptance remains required through the existing project workflow.
- Stopping rule: prioritize gameplay-distance value. Numerical safety, controlled poses and static contact targets do not establish visual acceptance, animated grips or intersection-free motion.

## Handoff and durability
Record exact input/output paths, object roles, asset dependencies, approval and verification status. A local path in an issue is not an upload. Check tracking/ignore state and availability; report untracked assets explicitly. Never commit, push or upload without permission.

Identify one current handoff with approved editable source, final asset paths, remaining defects, cost and pose implications, and exact integration state. Explicitly supersede earlier records after adoption or asset moves; retain their evidence without treating stale status as current.

See [technical recipes](references/technical-recipes.md) for observed Blender/MCP pitfalls and tested algorithm patterns. These are recipes to adapt and verify, not universal repair scripts.
