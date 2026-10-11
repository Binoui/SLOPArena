---
name: sloparena-asset-selection
description: "Prepare verified Unity environment assets from a stage concept: deterministic catalog retrieval, diverse shortlist generation, safe Unitypackage materialization, prefab inspection, thumbnails, and contact-sheet evidence."
category: game-dev
---

Before gateway examples, set `ORCA_TERMINAL_HANDLE` to your own runtime-issued terminal handle; the gateway verifies its incarnation and fails closed if it is missing or mismatched. Use the canonical gateway in [`docs/contributing/unity-cli.md`](../../../docs/contributing/unity-cli.md) and the [shared Editor coordination protocol](file:///home/binoui/Documents/projects/sloparena-workspace/docs/unity-editor-coordination.md). Wait for an independent lease; never inject work into an owner's batch. Runtime ownership status replaces historical Markdown ownership; `blocked` or unknown is not free.

# SlopArena Asset Selection

Use this skill when an agreed stage composition needs imported environment assets for specific roles. Prepare an inspected workset before introducing new dependencies; gray-box geography may already exist. This skill does not author a stage scene or decide its layout.

## Authority and boundaries

The tracked catalog is the source for retrieval:

```text
docs/assets/catalog/index.json
  └── prefabs.jsonl
```

The catalog records source-pack identity as `(sourcePack, id)`. The generated workset and inspection artifacts are local cache data under `.asset-catalog-cache/` and are not runtime content.

This workflow may:

- rank catalog entries against a concept profile;
- enforce role quotas, family diversity, and deterministic ordering;
- materialize selected Unitypackage contents without overwriting files;
- inspect actual Unity prefab metadata;
- render normalized thumbnails and a contact sheet;
- hand the verified workset to stage composition.

This workflow must not:

- place assets in a scene;
- mutate an Arena or stage scene;
- decide gameplay collision geometry;
- add runtime gameplay ownership to decorative assets;
- author camera composition or lighting as final stage design;
- hand-edit generated catalog or inspection output;
- use a natural-language model call as the ranking authority.

Unity remains the authority for imported prefab identity and measurements. The server/shared simulation remains the authority for gameplay.

## Targeted procurement, not asset-led composition

Read the stage's numbered composition decisions and gray massing first. The [stage-design workflow](../sloparena-stage-design/SKILL.md) owns geography, silhouettes, depth layers, and negative space; procurement supplies missing pieces for that place.

- State each needed role and its host: houses on the village pad, palms on low islands, pier modules at the harbor edge. Record available footprint, height, scale, and silhouette constraints in the design/handoff notes, not invented profile-schema fields.
- Reuse suitable assets already resident in the accepted stage before retrieving another pack. Reuse does not waive reference, shader, collider-removal, or placement checks. Original coast/ridge/foundation meshes belong to stage authoring, not catalog retrieval.
- Query only the missing roles. Quotas describe the requested workset, not a target amount of clutter to place; a broad shortlist is not permission to fill every gap.
- Judge the inspected candidate against its geometric role, not just its thumbnail. If it needs awkward filler terrain or breaks the silhouette, revise procurement or report a design deviation; do not silently redesign the place around the pack.
- Keep the existing identity, provenance, materialization, and visual-evidence checks for every newly referenced external asset, even if its pack is already imported. A pack's sample scene is not an approved stage composition.


## Pipeline

```text
agreed composition → missing asset roles → concept/profile
        │
        ▼
AssetCatalog probe
        │
        ▼
probe workset → materialize → inspect → report/contact sheet
        │
        ▼
AssetCatalog query
        │
        ▼
workset.json: candidates, shortlist, roles, required packs
        │
        ▼
AssetCatalog materialize
        │
        ▼
Unity AssetDatabase import
        │
        ▼
sloparena.assets.inspect --render-thumbnails --compact
        │
        ├── inspection.json
        ├── report.html
        ├── prefab thumbnails
        └── contact-sheet.png
                │
                ▼
        stage-authoring workflow
```

## 1. Create a concept profile

Profiles use schema version `1`:

```json
{
  "schemaVersion": 1,
  "concept": "industrial rooftop fight club",
  "preferredTags": ["industrial", "urban"],
  "terms": ["roof", "warehouse", "vent", "railing", "barrel"],
  "excludedTags": ["fantasy", "tropical"],
  "candidateLimit": 100,
  "perFamilyLimit": 2,
  "roles": [
    {
      "name": "hero-structure",
      "quota": 4,
      "categories": ["building", "structure"],
      "stageTags": ["large-structure"],
      "terms": ["roof", "warehouse"]
    }
  ]
}
```

Required rules:

- `schemaVersion` must be `1`.
- `concept` must be nonblank.
- `candidateLimit` must `1..500`.
- `perFamilyLimit` must `1..20`.
- Role names must be unique and nonblank.
- Role quotas must be `1..40`.
- Total shortlist size is the sum of role quotas. There is no mandatory global minimum or maximum.
- Empty tag and term arrays are valid.

Use roles to describe stage responsibilities, not physical input or gameplay move identity. Typical roles are `hero-structure`, `skyline`, `perimeter`, `utilities`, `lighting-signage`, and `clutter`.

Comparisons are invariant-lowercase. Tags, categories, and stage tags use exact matching. Terms use case-insensitive substring matching against prefab names and then paths.

## 2. Query the catalog

From the repository root:

```bash
dotnet run --project tools/AssetCatalog -- \
  query \
  --profile .asset-catalog-cache/<concept>/profile.json \
  --out .asset-catalog-cache/<concept>/workset.json
```

The query streams `prefabs.jsonl`, reports malformed records with line numbers, and rejects duplicate `(sourcePack,id)` identities.

Retrieval scores are deterministic:

- preferred tag: `+12` each;
- distinct term in prefab name: `+10` each;
- distinct term only in path: `+4` each;
- category used by any role: `+6`;
- requested stage tag: `+5` each;
- excluded tag: `-12` each.

Shortlist role scoring adds:

- matching role category: `+7`;
- matching role stage tag: `+5` each;
- matching role term: `+6` each.

Tie-breakers are ordinal and stable. The workset records scores and literal reasons; do not treat an opaque ranking as sufficient evidence.

Acceptance checks:

```bash
jq '{schemaVersion, candidates: (.candidates|length), shortlist: (.shortlist|length), roles: ([.shortlist[].role] | sort | group_by(.) | map({role: .[0], count: length})), diagnostics}' \
  .asset-catalog-cache/<concept>/workset.json
```

Confirm that:

- shortlist count is the sum of declared role quotas, except when a role is explicitly underfilled;
- each role reaches its quota, or has an explicit `ROLE_UNDERFILLED` diagnostic;
- `(sourcePack,id)` pairs are unique;
- no family exceeds `perFamilyLimit`;
- `requiredPacks` contains only packs represented in the shortlist.

Do not invent fallback assets to fill a role. Role underfill is explicit.

## 2a. Preview the deterministic shortlist

Run this immediately after profile creation:

```bash
dotnet run --project tools/AssetCatalog -- \
  probe \
  --profile .asset-catalog-cache/<concept>/profile.json \
  --out .asset-catalog-cache/<concept>/probe-workset.json \
  --per-role 1
```

The probe reuses the same scoring, tie-breakers, family limits, roles, and `selectionStatus: "selected"` records as the full query, but selects at most `--per-role` ranked candidates for each role (`1..3`, default `1`). Materialize and inspect this probe, then review its `report.html` and `contact-sheet.png` before running the full query. Revise the profile or preview renderer when evidence is unsuitable; never substitute an asset from another category.

## 3. Materialize selected source packs

```bash
dotnet run --project tools/AssetCatalog -- \
  materialize \
  --workset .asset-catalog-cache/<concept>/workset.json \
  --unity-project client/Unity
```

Materialization extracts only the Unitypackages in `requiredPacks`. It preserves asset and folder `.meta` files and copies only missing files.

The operation is globally preflighted:

- missing archives fail with `SOURCE_ARCHIVE_MISSING`;
- invalid Unitypackage pathnames fail closed;
- traversal, absolute paths, duplicate destinations, and missing payloads are rejected;
- differing existing files fail with `IMPORT_CONFLICT`;
- any failure copies nothing;
- identical reruns report files as `unchanged`.

Never add an overwrite flag or manually copy vendor files around a conflict. Resolve the conflict deliberately, then rerun the complete workset.

## 4. Inspect imported prefabs in Unity

Observe runtime ownership status: `held`/`releasing` means invoke a gateway command and let it wait for an independent lease; `blocked`/unknown means stop. A `free`, settled status with zero active operations is only an availability observation; then recompile via its own gateway command.

```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts status --project-path /home/binoui/Documents/projects/SlopArena/client/Unity
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- recompile
```

The inspection command is Editor-only and must run against the main checkout:

```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.assets.inspect --workset .asset-catalog-cache/<concept>/workset.json --output .asset-catalog-cache/<concept>/inspection.json --render-thumbnails --compact
```

Inspection exposes three independent contracts:

- `selectionStatus`: semantic shortlist membership. Generated workset items are `"selected"`, and preview failures never change it.
- `technicalValidation`: `"pass"` or `"fail"` with technical diagnostics such as `ASSET_NOT_FOUND`, `IDENTITY_MISMATCH`, `NO_RENDERERS`, `INVALID_BOUNDS`, `MISSING_REFERENCE`, `UNSUPPORTED_SHADER`, and `INSPECTION_EXCEPTION`.
- `visualEvidence`: `"pass"` only after a nonblank, framed thumbnail is written; otherwise `"unavailable"` with diagnostics such as `THUMBNAIL_NOT_REQUESTED`, `THUMBNAIL_GPU_UNAVAILABLE`, `THUMBNAIL_BLANK`, clipping, or preview-render exceptions.

For every shortlisted prefab, inspection verifies the catalog ID against `AssetDatabase.AssetPathToGUID`, loads isolated prefab contents, and always unloads them. It measures inactive children too:

- root-local combined renderer bounds;
- renderer count;
- total and enabled collider counts;
- material slots and unique non-null materials;
- supported, unsupported, and missing shaders;
- highest-detail triangle count;
- every LOD group's levels, transition heights, renderers, and triangles;
- missing mesh or material references.

`success` reflects technical validation only. Visual unavailability never invalidates a technically valid prefab. A null graphics device can still produce metadata and report state but causes `THUMBNAIL_GPU_UNAVAILABLE` for visual evidence.

## 5. Review visual evidence

Each successful prefab receives a deterministic 384×384 PNG. The contact sheet is five columns, row-major, and includes `contactSheetCells` mapping each cell back to `(sourcePack,id)`, role, name, and a short identity label.

Open `report.html` first. It is self-contained: the labeled five-column grid, contact sheet, and thumbnails are embedded as data URIs and work without network access or relative paths. The report then shows role counts, source pack, short identity, `selectionStatus`, technical status, visual status, and diagnostic codes only when present. Full identities remain in expandable detail text.

Read the report and contact sheet and spot-check at least one asset from each important role:

- hero structure;
- perimeter;
- utilities;
- lighting/signage;
- clutter.

Reject or revise the profile when assets are clipped, invisible, badly framed, visually duplicated, magenta, or conceptually wrong. A visual warning can trigger human review, but must not change the semantic shortlist or substitute a different category. Metadata-only technical success is not visual suitability.

Record verification in the repository's gitignored `TESTING-UNITY.md` when this workflow changes or when a new concept workset is accepted. This evidence does not validate stage placement, gameplay, or camera composition.

## Handoff to stage authoring

Pass the following files to the later stage-authoring workflow:

```text
.asset-catalog-cache/<concept>/profile.json
.asset-catalog-cache/<concept>/probe-workset.json
.asset-catalog-cache/<concept>/workset.json
.asset-catalog-cache/<concept>/inspection.json
.asset-catalog-cache/<concept>/report.html
.asset-catalog-cache/<concept>/contact-sheet.png
.asset-catalog-cache/<concept>/*--*.png
```

Stage authoring must use the inspection results as asset evidence, then make explicit decisions about placement, composition, camera, lighting, and gameplay geometry. It must not reinterpret an asset's decorative collider as authoritative gameplay collision.

## Verification checklist

- [ ] Profile schema and role quotas validate; total shortlist size is the sum of quotas.
- [ ] Requested roles have hosts and geometric constraints from the agreed composition; existing suitable stage assets were considered first.
- [ ] Probe is deterministic, obeys per-role and family limits, and records underfill explicitly.
- [ ] Query output is deterministic and contains score reasons.
- [ ] Shortlist identities are unique and family limits hold.
- [ ] `selectionStatus`, `technicalValidation`, and `visualEvidence` remain independent.
- [ ] Role quotas are satisfied or underfill is explicit.
- [ ] Required packs equal the shortlist's distinct source packs.
- [ ] Materialization succeeds without conflicts or overwrites.
- [ ] Identical rerun reports zero copied files.
- [ ] Unity recompiles with zero current console errors.
- [ ] Every prefab identity matches its catalog ID.
- [ ] Metadata inspection has finite bounds and no dependency failures.
- [ ] Report is self-contained, labeled, and reviewed before stage handoff.
- [ ] Thumbnails are nonempty and visually usable when GPU rendering is available.
- [ ] Contact-sheet cells map back to every rendered shortlist item.
- [ ] No scene, gameplay, or final camera claims are made from this workflow alone.
