---
name: sloparena-stage-design
description: "Design SLOPArena PvP stages and solo Arcade traversal courses with focused interviews, route/composition sketches, graybox review and a Stage Design Brief. Use for new layouts, jump/shortcut design or substantial composition revisions. Implementation and baking belong to sloparena-stage-authoring."
metadata: {"category": "game-dev"}
---

For live Editor commands, use your own caller identity and bounded gateway lease from the [Unity CLI guide](../../docs/contributing/unity-cli.md) and [coordination protocol](../../docs/contributing/unity-editor-coordination.md). Run project commands from the selected checkout. Busy ownership waits in the gateway; blocked or unknown status requires recovery.

# SLOPArena Stage Design

## 1. Purpose and boundary

This skill is the interactive workflow for one stage's layout and visual composition. It produces:

- a locked Stage Design Brief at `docs/design/stages/<key>.design.md`, and
- gray-box blockout screenshots in `.stage-authoring-cache/<key>/design/`, identifying which the user has reviewed and which remain implementation evidence.

It must NOT run `sloparena.stage.bake`, `sloparena.stage.inspect`, asset-selection or provenance work, material authoring, or lighting polish. Those belong to [`sloparena-stage-authoring`](../sloparena-stage-authoring/SKILL.md), which begins only after LOCK (section 5). Unity blockout content is limited to gray-box primitives or simple original massing meshes (coasts, ridges, banks, building volumes), with no Colliders and at most one temporary light. Persist generated meshes and the neutral gray material as valid assets/sub-assets. No detailed surfacing or imported prop dressing in this phase.

## 2. Inputs

- [`docs/design/stage-concepts.md`](../../docs/design/stage-concepts.md) is required reading and must be read first. It exists and defines the design contract.
- **Choose the stage purpose before applying topology rules.** Normal PvP uses Stage Concepts' combat-floor and 2–4-player criteria. Solo Arcade traversal courses also use [`docs/design/traversal-courses.md`](../../docs/design/traversal-courses.md): interview about journey, challenge rhythm, ordinary/ability branches, jump variety, objectives/checkpoints and scale before building. Preserve existing answers; do not turn Sanctuary's three forks or specific abilities into universal requirements. Its solo acceptance path replaces PvP acceptance, not the Shared collision/presentation boundary.
- The typed production commands `sloparena.stage.bake` and `sloparena.stage.inspect` exist at the paths documented by [`sloparena-stage-authoring`](../sloparena-stage-authoring/SKILL.md); this design workflow must not run them.
- For an existing stage's cosmetic-only pass: the gameplay-shell section of `docs/design/stages/<key>.md` and the baked arena are fixed constraints. Re-mass cosmetics within those constraints; collision, spawn or boundary changes require an explicitly scoped layout revision and the production skill's gameplay path.
- **Blast-plane clearance (the ONE background rule).** The bake derives kill planes from the collision mesh: side planes at mesh bounds ± `ArenaCollision.SideBlastMargin` (10), kill height at minY − 10, top blast at highest surface + 20. The corridor between the planes is player-death space — an ejected fighter flies through it until crossing a plane. Background geometry must not intersect that corridor: nothing an ejected player would visibly pass through, no surface at landable height inside it. The `sloparena.stage.design-capture` command draws translucent red kill-plane walls and reports the planes as numbers, so this is checkable per iteration. It is fine that the stage limit itself reads softly; it is never fine for players to fly through terrain when ejected.
- **Platform reachability rule of thumb (shell design).** Calibrate against the roster reference character, not intuition: read `jumpForce`/`gravity`/`airJumpVMultiplier` from `content-cooked/<character>/character.runtime.json` (FightGuy reference: single-jump apex ≈ 2.0, double-jump apex ≈ 3.3; `src/Shared/AI/MovementProbe.cs` computes exact per-character metrics). Level-1 platform: top ≈ single apex (one well-timed jump, trivial with double). Level-2 platform: top above double apex — unattainable from the floor even with a double jump, close enough that a character's body grazes it; reachable with a double jump from a level-1 platform. Reference implementation (industrial_rooftop HVAC decks): L1 top 2.01, L2 top 4.43.
- **Gray-box captures judge massing only; prop placement is arithmetic + numeric verification.** Flat captures cannot reveal whether a window sits on a building's face or floats 2 units off it. Two concrete traps, both observed: (1) parenting a prop to a scaled cube multiplies the child's local position/scale by the parent's scale — windows landed at x=−180; either compute WORLD positions from the parent's renderer bounds (`face ± halfProp + embed`) or divide local values by parent scale; (2) destroying objects while iterating the transform skips entries — collect targets into a list first. Standard recipe: place props in world space computed from the building's bounds (window center = face ± (propHalfDepth − embed)), then verify with a bounds dump (every prop overlaps its host on the two tangent axes and protrudes on the face axis) — never by trusting the yaw captures.
- **If the shell itself is the problem** (layout, routes, boundary shape feel boring or unjustified), that is a gameplay change, not a cosmetic re-mass. Design the shell concept conversationally first — premise, named route purposes, spawn plan, top-down sketch — then implement it through the production skill's gameplay path (collision scene → bake → affected arena/course tests → the purpose-appropriate human review). Establish the route before extending its art; approved art may be retained through later targeted geometry tuning.

Early layout talk should prefer terminal-ASCII wireframes. They are throwaway thinking tools, not deliverables.

For courses, show each fork's approach, ordinary detour, ability crossing and common landing together. Check that required target placement preserves the intended choice. Calibrate short/full/running/double jumps with the actual kit through the production path; a visually large gap or an apex estimate alone does not establish difficulty. For an approved spacing revision, tune gaps individually and record protected landmarks rather than uniformly scaling the scene.

### Geometry before dressing

For each background region, decide its silhouette, depth, support, and surrounding negative space before choosing props. Block out the place, not a collection of asset-pack scenes:

- Start with land/building volumes and their connecting slopes, foundations, shorelines, or gaps. A coast needs thickness and a water-facing profile, not a flat sand sheet hiding seams.
- Compose near, middle, and far masses around the fixed shell. Vary height and spacing; leave intentional open water/sky and the entire fight/recovery corridor clear. Do not distribute equal landmarks around a ring just to fill every yaw.
- Check all sides and low views before adding detail. If the gray landforms still read as disconnected pads, floating scenery, or cardboard façades, more props will not fix them.
- Name the later dressing roles in the brief: houses on a village pad, palms on a low island, a pier at a harbor edge. Existing assets may constrain a role's dimensions, but do not let the available pack dictate the geography.


## 3. Wireframe loop (gray-box + design-capture)

Iterate visual massing with throwaway scripts, then capture with the typed design-capture command:

1. Write a throwaway C# script to `/tmp/<name>.cs` with NO `using` directives and NO class/method declarations — invoke `eval_file` through the gateway; its Roslyn wrapper compiles the file as the body of a generated `Execute()` method (so a trailing `return` supplies its result), and all `UnityEditor`/`UnityEngine` types must be fully qualified. Pattern:

   ```csharp
   string prefabPath = "Assets/Resources/Stages/<key>.prefab";
   // The gray material MUST be persisted. Two traps, both observed rendering magenta:
   // - an in-memory Material is not saved by SaveAsPrefabAsset (reference lost);
   // - a .mat under a dot-folder (Assets/.stage-capture-cache/…) is not imported by
   //   Unity, so its GUID never resolves. Store it as a sub-asset of the prefab.
   UnityEngine.Material gray = System.Linq.Enumerable.FirstOrDefault(
       System.Linq.Enumerable.OfType<UnityEngine.Material>(
           UnityEditor.AssetDatabase.LoadAllAssetsAtPath(prefabPath)),
       m => m.name == "DesignGray");
   if (gray == null) {
       gray = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
       gray.SetColor("_BaseColor", new UnityEngine.Color(0.6f, 0.6f, 0.6f, 1f));
       UnityEditor.AssetDatabase.AddObjectToAsset(gray, prefabPath);
       UnityEditor.AssetDatabase.SaveAssets();
   }
   var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
   string result;
   try {
       // edit only the agreed blockout roots; use primitives or original massing meshes
       // with no Colliders; assign `gray` to each MeshRenderer
       UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
       result = "ok";
   } finally {
       UnityEditor.PrefabUtility.UnloadPrefabContents(root);
   }
   return result;
   ```

2. Render five supporting deterministic edit-mode views:

   ```bash
   bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.design-capture --stage <key>
   ```
3. **Run an agent full-orbit check before dressing.** Five design-capture views are not a 360° review. In a clean, stopped Editor, use a temporary additive scene with a stage instance and explicitly named cameras; capture through the gateway's `sloparena.capture.game-view --source camera`. Keep lighting consistent, isolate the temporary render, and restore the original scene/selection and close only your temporary scene in the same bounded batch. Do not retain a lease while writing scripts.

   Cover intermediate yaw angles as well as N/E/S/W, plus low views around the recovery rim. The tested `splash_deck` framing below is a starting recipe, not a universal camera or stage-size rule; adjust distance/target to the shell and record the actual poses.

   | Sweep | Yaw degrees | Radius / camera Y | Look-at XYZ | Vertical FOV |
   | --- | --- | --- | --- | --- |
   | Elevated | 0 through 345, step 15 | 53 / 25 | (0, −1, 0) | 42° |
   | Low recovery | 0 through 315, step 45 | 35 / −1 | (0, −3, 0) | 52° |

   Camera X/Z relative to the shell center are `sin(yaw) * radius` / `cos(yaw) * radius`. Use radians for the trig functions. Inspect every frame, not just a contact sheet or the best angle. Look for rear-face voids, flat joins, hovering props, repeated silhouettes, occluded edges, and false recovery routes. Intentional empty water/sky is not a defect.

   For each already-created camera, use a fresh output path; the camera capture command does not create the camera or perform the sweep:

   ```bash
   bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.capture.game-view --source camera --camera <camera-name> --output .ability-lab-cache/<key>/<pass>/orbit-000.png --width 1600 --height 900
   ```

   Keep supporting captures local, record scene restoration, and remove throwaway setup scripts after proof. These are Edit Mode rendering checks, not match-camera, gameplay, or human acceptance.

4. **After every meaningful composition change, the human reviewer enters Play Mode and reviews the stage through the real in-game camera.** Move, jump, and rotate freely from the main floor, every accessible platform, and the recovery/death-space views. Check that the world reads as a 3D place; that nearby façades, windows, and ramps have believable depth; and that background geometry neither masquerades as a route nor visibly intercepts an ejected fighter. The agent records the observations and iterates the gray box. Optional in-game screenshots may be kept in `.stage-authoring-cache/<key>/design/` as discussion evidence.
   Ask these questions in that review; they turn vague impressions into decisions:

   1. Does the playable shell feel good enough to fight on from the real camera?
   2. Do bridges, joins, and route transitions read as intended without suggesting a false route?
   3. Does the background establish a three-dimensional world rather than a set of panels?
   4. Does each small accessible platform have a clear purpose and feel worth using?
   5. Could a player mistake background for a safe route? If they try it, do they cleanly blast rather than collide with visible terrain?
   6. Which gray-box forms must survive into production, and which are placeholders for real assets?

   This is a visual-design review, not the production workflow's external 2–4-player PVP acceptance. For a solo course, ask whether the takeoff, landing, branches and next destination read clearly and whether the intended jump differences feel distinct. A scripted route completion does not answer those human questions. The solo implementation-authorization exception in section 5 does not claim this review passed.

5. Asset edits may cause a reload/connection gap. Keep dependent operations in the same bounded gateway batch and let it reconcile known work before release. Observe ownership through gateway `status`; blocked or uncertain completion requires reconciliation, not replaying `eval_file`, capture, or another mutation to obtain an acknowledgment.

6. For a new stage, saving the agreed blockout into `client/Unity/Assets/Resources/Stages/<key>.prefab` is expected. For an existing stage, preserve accepted content and the gameplay shell; use isolated temporary blockout or targeted edits to explicitly approved roots, never wholesale replacement. Production retains approved massing and replaces only agreed placeholders; captures and numbered decisions record what must survive.

Design captures are supporting evidence for silhouette, massing, camera-region composition, busy/empty zones, landmark placement, and numeric clearance. They are not a substitute for the human in-game review, material-quality review, prop-provenance work, or numeric placement verification.

## 4. Decision vocabulary

Composition intent MUST be written in gameplay-camera region language — "from the default camera: left third / center / right edge / foreground bottom" — with the yaw named, because the player reads the stage through the camera. Record world coordinates and bounds separately for placement and numeric verification.

The match camera (`CameraMount` + `CinemachineOrbitalFollow`) orbits with FREE 360° yaw (pitch clamped 0–45°, mouse-driven zoom). Composition MUST therefore hold from every yaw angle, not one. Name the quarter-turn views and any stage-pinned vantage, then inspect the intermediate angles using section 3's full-orbit check. Different directions may deliberately have different density, including open water or sky; an uncomposed rear side or one-direction cardboard backdrop is a defect.

Include a "busy here / empty here" zoning statement.

## 5. LOCK protocol

For normal PvP visual design, use the full sign-off/review sequence below. For an
approved solo Arcade course, an explicit instruction to implement a concrete
route or bounded revision may lock that implementation intent before the user
plays the exact new geometry. Record the approved scope and keep playable visual,
controller and difficulty acceptance explicitly pending. Do not label unseen
captures approved. Reuse that authorization for implementation, baking and
verification; do not ask for a second ceremonial sign-off or a fresh interview
for every gap adjustment. A new unresolved design choice still needs the user's
input. This exception does not change normal PvP acceptance.

The normal PvP design session ends by:

1. Reading the numbered decisions back to the user for explicit sign-off.
2. Confirming that the user completed the in-game visual-design review in section 3 and that its observations are resolved or explicitly accepted.
3. Writing the Stage Design Brief per section 6, front matter `status: locked` and `locked:` dated, only after that sign-off.
4. Stating that production (`sloparena-stage-authoring`) may now run.

For the normal PvP sequence, "locked" means the brief plus reviewed screenshots.
For the approved solo path, distinguish the locked implementation scope from
pending human review in the brief. Neither replaces purpose-appropriate human
playability acceptance.

## 6. Stage Design Brief format

Path: `docs/design/stages/<key>.design.md` — sibling of the production brief; `<key>` is the immutable stage key.

Front matter:

```yaml
---
key: <stage key>
status: draft | locked
locked: <YYYY-MM-DD, set only on sign-off>
approved_by: <user>
blockout:
  - .stage-authoring-cache/<key>/design/lock-default.png
---
```

Body sections, each mandatory:

- `## Personality` — exactly one sentence: the stage's joke.
- `## Composition decisions` — numbered, in camera-region language, covering every major mass, the landmark, foreground clusters, surface treatment, and the SCENE WORLD: what exists below the stage, above it, and at far distance so the fight sits inside a believable place rather than on a platter.
- `## Negative decisions` — explicit exclusions that bind production: no second hero landmark, no clutter on gameplay routes, no background geometry through the blast corridor, and any stage-specific exclusions.
- `## Palette & lighting` — time of day, ambient temperature, accent colors: names and vibe, not hex or material specs.
- `## Camera vantages` — list.

Screenshot paths are cache references; never commit binaries.

## 7. Who decides what

The split between this skill and production (`sloparena-stage-authoring`): design decides WHAT should exist and why; production decides HOW it is made real.

| Decision | Design | Production |
|---|---|---|
| Fight premise, routes, floor shape, spawns | Decides (shell concept) | Implements via collision scene → bake → tests |
| What masses exist, where, in camera language | Decides (numbered decisions) | Places to match; reports PASS/DEVIATION |
| What must NOT exist | Decides (negative decisions) | Bound hardest by these |
| Palette & lighting DIRECTION (time of day, accent vibe) | Decides | — |
| Lighting/material EXECUTION (lights, skybox rig, materials) | — | Decides, within the locked direction |
| Surface treatment INTENT ("one plane, two seams") | Decides | Chooses actual materials/textures |
| Asset provenance, LODs, performance | — | Owns entirely (asset-selection skill) |
| Background/skybox STORY (below, above, far distance) | Decides (scene world) | Builds within the story |
| Gray-box blockout screenshots | Produces (lock evidence) | Replaces with real content |
| Any collision/spawn/boundary change | Proposes only (→ gameplay path) | Executes + bakes + tests |

The test for "whose call is this?": would a different implementation still be faithful to the brief? If yes, production has latitude. If changing it would break the stage's joke or a locked decision, design owns it. Anything unresolved between the two phases goes to the user as an explicit DEVIATION, never silently resolved by either side.
