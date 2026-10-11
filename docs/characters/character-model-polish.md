# Character model polish

Use a generated or existing model as a base worth preserving, not as an instruction to rebuild everything. A directed Blender/MCP pass repairs its highest-value defects while keeping the character recognizable. Not every character needs every repair below.

Blender execution belongs to `blender-character-polish`; animation authoring belongs to `sloparena-animation-authoring`; Unity integration and cooking belong to `sloparena-character-workflow`. Follow the [asset conventions](../contributing/conventions.md), [Unity CLI workflow](../contributing/unity-cli.md), and [verification modes](../testing.md).

## Start with a small art brief

Before editing, record:

- **Baseline:** exact source file, approved candidate, rig, textures, and dependencies. Keep an untouched original and save candidates separately.
- **Identity:** features to preserve, such as silhouette, proportions, facial asymmetry, palette, and intentional angular shapes.
- **Priorities:** the three highest-value visible defects, supported by close-up and gameplay-distance views. Fewer is fine.
- **Non-goals:** areas not being redesigned, animation work excluded, and details too small to justify further effort.
- **Scope:** surface polish or an explicitly approved rig repair.

Suggested starting request:

> Use the character-polish workflow to inspect this generated model. Preserve its identity and propose the three highest-value changes before editing. Separate surface repairs from rig changes and show the baseline at gameplay distance.

## Choose the repair, not a blanket treatment

| Symptom source | First investigation | Avoid |
| --- | --- | --- |
| Shading | Split normals, sharp edges, matched clay/textured lighting | Welding real UV seams to fix normals |
| Painted artifact | Albedo, UV seams, texture stretching | Resculpting a painted shadow |
| Missing or malformed surface | Coverage, winding, joins, local triangle quality | Double-sided rendering to hide a hole |
| Bad deformation | Joint placement, weight ownership, local triangle density | Global smoothing or subdivision before diagnosis |
| Material mismatch | Target shader, roughness/metallic treatment, portable baking | Detail that aliases or disappears at gameplay distance |

Prefer the least invasive repair that addresses the demonstrated cause. Normals cannot repair a hole; better topology cannot by itself recover the intended art direction.

**Surface polish preserves skeleton hierarchy, rest transforms, bind pose and animation bindings by default.** Joint relocation, added bones, or changed rig/import mapping is a rig-change branch, not an incidental cleanup. Approve that scope and verify retargeting and authoritative pose consequences separately. Weight edits still require deformation checks even when the skeleton is unchanged.

## Iterate with comparable evidence

1. Save a separate candidate and change a bounded region.
2. Compare identical cameras, lighting, materials and poses against the previous pass. Also compare periodically against the untouched original to catch cumulative silhouette drift.
3. Inspect both clay and textured views. Include front, side/three-quarter and the hidden surfaces relevant to the edit.
4. Check the changed region in motion. Controlled poses are useful diagnostics, not proof of actual clip playback, animated grips, or intersection-free movement.
5. After structural repair, verify early in Unity using ordinary target-material backface culling. Inspect raised arms, undersides and rear views before spending another pass on detail. Use the installed CLI/Pipeline on the main checkout; a Blender approximation is not Unity verification.
6. Promote only the approved candidate. Keep remaining defects explicit; stop when additional detail has little gameplay-distance value.

For mesh-only exploration, do not force a publishing cook after each iteration. Use the existing local-preview boundary. For Unity-facing work, maintain the short root `TESTING-UNITY.md` checklist required by the project.

## Acceptance and integration

Separate these claims:

- **Numerical safety:** finite vertices, no new degenerate faces, valid weights/bounds and preserved protected regions.
- **Visual acceptance:** intended silhouette/materials under target lighting, hidden-surface coverage and acceptable deformation at relevant viewing distances.
- **Runtime compatibility:** actual bound clips, Avatar/import settings, attachments, renderer bounds and retained skin influences.
- **Authoritative content:** exact rig/clip/import bindings and cooked poses. Valid Avatar, unchanged bone names, or unchanged gameplay JSON do not establish pose equivalence.

When replacing a rig or its bindings, compare old/new retargeted output at matching clip times, including relevant attachment and authoritative pose tracks. If a presentation-only change is required, investigate discrepancies before promotion; otherwise obtain explicit acceptance of the changed authoritative content and verify its affected behavior. Never silently classify a changed `poses.bin` as cosmetic.

Record original-versus-candidate triangles, imported vertices, renderers, materials, texture dimensions/formats and estimated memory. One material does not mean one draw call. Asset counts and compression estimates are not a frame-rate benchmark. Set budgets for the actual target rather than copying Manki's numbers.

Accepted integration follows [Adding a Character](adding-a-new-character.md) and the existing inspect → cook → verify → roster refresh workflow. Do not hand-edit cooked output or bypass server authority.

## One current handoff

End with one clearly identified current record containing:

- approved editable source and final Unity asset paths, dependencies, and approval scope;
- comparisons and motion evidence, with controlled poses versus real runtime checks labelled;
- checks performed, unresolved defects, cost changes, and rig/cooked-pose implications;
- integration state: exploratory, approved candidate, locally adopted/cooked, or published;
- earlier handoffs superseded by this record, including later asset moves;
- tracking, backup/upload availability and missing dependencies. Local paths are not uploads; do not commit, push or upload without permission.

Retain historical evidence, but do not leave the next session to infer the current candidate from directory names or stale issue comments.

## Manki case study — 2026-09-15

These are observations from saved comparisons and review/handoff records, not a universal repair recipe or a statement of current package status. Evidence was local under `art/blender/exports/`; binary availability on another machine is not guaranteed.

| Observation | Transferable lesson |
| --- | --- |
| Normals polish improved surface shading while retaining the generated identity. Jaw finishing improved the profile transition. | Preserve the strong base; target visible defects instead of redesigning everything. |
| Rebuilt shoulders looked cleaner but too bulky; a later pass slimmed them. | Geometric cleanliness and artistic correctness are separate acceptance criteria. |
| Earlier Blender shoulder passes did not establish Unity coverage. A later local repair filled exposed inner surfaces under backface culling. | Check hidden surfaces in the target renderer early; do not mask missing geometry with shader settings. |
| Finger repair records identify cross-digit weights, abrupt thumb transitions and insufficient local triangle density. Controlled curls improved, but static holds were not animated grip proof. | Diagnose weights/topology locally and label the evidence boundary. |
| Hair subdivision trials introduced boundary/normal artifacts and were rejected. | More geometry is not automatically better; retain the simpler successful candidate. |
| Material export baked shader treatment into portable maps; close-range shading differences remained. | Validate the target shader, not only the Blender source appearance. |

Recorded measurements clarify the tradeoffs:

- Shoulder-hole repair added 88 triangles; two matched background-pixel regions changed from 1,893 and 575 to zero. This proves those sampled views, not every possible pose.
- Original-to-repaired cost rose from 6,029 to 19,261 triangles and 1 to 10 renderers. Compressed texture-memory estimates rose from about 0.67 to 16 MiB; no controlled GPU-performance conclusion follows.
- Unity comparison recorded up to 0.048397 m head/right-hand retarget difference. A later adoption/cook record explicitly reported changed `poses.bin` with byte-identical `character.runtime.json`; it did not claim that difference resolved or presentation-only.

Evidence locators: `manki-polish-d4dicc_i/02-textured-comparison.png`, `manki-jaw-finish-w4pemvsd/comparison-right.png`, `manki-slim-shoulders-1tqy44jw/01-volume-comparison.png`, `manki-finger-repair-u1dwghf3/repair-review.json`, `manki-hair-1trer59e/hair-review.json`, `manki-materials-q9ilcjeh/material-review.json`, `manki-unity-199/issue-handoff.txt`, and `manki-shoulder-repair-199/{issue-handoff,cook-handoff}.txt`, all beneath that local export directory.

Reusable Blender implementation details remain in the [polish technical recipes](../../skills/blender-character-polish/references/technical-recipes.md). Manki-specific radii, weight formulas and tolerances are not defaults for another character.
