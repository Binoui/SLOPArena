# Model swaps

A model swap dresses a built-in fighter in a creator's rigged humanoid GLB
([ADR-0065](../adr/0065-workshop-item-format.md), path B). The fighter keeps its
cooked package, moves, clips, hitboxes and hurtboxes. Only the drawn model changes,
so the swap never reaches Shared, the GameServer or the match content hash. The
[Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md) Phase 1 builds it.

```text
MatchConfig.ModelSwaps[player 1-4]       (launch option or folder today; Fighter Select,
  -> FighterModelSwap                      Workshop screen and Rooms later)
  -> ModelSwapImporter (glTFast, over several frames, built-in model still shown)
  -> PlayerRenderer.WearModel            (the base fighter's clips through Animancer)
```

## Choosing a swap

`MatchConfig.ModelSwaps` maps a player number to a `ModelSwapChoice`, a GLB path.
Two players on the same fighter can wear different models. Until the screens exist,
`ModelSwapLaunchOptions` fills it at launch from `-modelSwap 2=C:\models\dusty.glb`
(repeatable, or pairs joined by `;`), or else from `p1.glb` to `p4.glb` in the
`ModelSwaps` folder under the player's data folder. A bone map file sits beside the
GLB as `<name>.humanoid.json`, or as `humanoid.json` in a Workshop item folder.

`FighterModelSwap` reads the GLB on a worker thread after checking its length against
`WorkshopGlbLimits.MaxFileBytes`, and `humanoid.json` against 64 KB, so an oversized
file is refused before a byte is read. Dropping the swap or destroying the fighter
cancels the read.

`MatchBase.SetupRenderer` calls `FighterModelSwap.Track` for every fighter. On the
next frame the component reads its player's choice, so the player number is final.
A later `SetupRenderer` for the same renderer (Training's fighter change) drops the
swap and asks again.

## What the importer does

`ModelSwapImporter.ImportAsync` takes GLB bytes and what it must match on the base
fighter, and returns a model or a refusal with a `ModelSwapRefusalReason`. Each step
runs in its own frame, so a match never waits on it.

1. The file-level precheck runs first (`FilePrecheck`). It calls
   `WorkshopGlbPrecheck.Check` in Shared with `WorkshopGlbLimits.Default`, which
   refuses a file outside the limits for file size, triangles, bones and texture
   size before glTFast reads it. It also refuses what glTFast would trust without
   bounds: a node graph that is not a forest (a missing child, a node with two
   parents, a cycle, a scene root that is also a child), an accessor that reads past
   its buffer view, attributes that count different vertices, and the features a
   swap never needs (sparse accessors, morph targets, GPU instancing). The refusal
   reason is `FileLimits`.
2. glTFast loads the GLB with no clips and no external downloads. A swap file must
   embed its buffers and images.
3. Textures shrink to `TextureMaxSide` (1024 px) with mipmaps and DXT1 where the GPU
   takes it. Fighters draw opaque, so alpha is dropped. Unity decodes PNG on the main
   thread, so `ModelSwapTextures.PngLoader` decodes and box-shrinks PNGs on a worker
   thread with `PngShrink` in Shared. It refuses a header that is not first or
   appears twice, and dimensions outside the precheck's limits, so a PNG cannot
   show the precheck one size and the decoder another. A malformed PNG refuses the
   file (`ImageUnreadable`). JPEG and valid PNG kinds it does not read (interlaced,
   packed samples) use Unity's decoder and a GPU shrink. The loader owns every
   texture it makes, so a failed or dropped import frees them.
4. The bone map comes from Mixamo node names, with any `prefix:` ignored
   (`HumanoidBoneMap` in Shared), or from `humanoid.json`, which wins. Missing
   required human bones refuse the file.
5. Upright and ground: up runs from the hips to the head and right from the left
   thigh to the right thigh in the rest pose. An `Upright` node turns that frame
   onto the game's axes, puts the hips over the origin and the lowest skinned
   vertex on the floor. The vertex is skinned in code, because `BakeMesh` returns
   glTFast's unskinned mesh before the first render.
6. A Humanoid avatar is built from the rest pose with `AvatarBuilder`. The palm rule
   rolls its reference pose so the thumbs point forward, half on the forearm and
   half on the hand.
7. Size: a `Fit` node scales the model so its human scale matches the base
   fighter's. Foot IK stays off, as on built-in fighters and in the pose bake: on
   Sir Bonk's clips it lifted Dusty's model about 0.4 m.
8. Materials use `MatchVisualStyle.CreateFighterMaterial` with the player's seat
   rim, and the ink line is applied when the stage look has one.

`ImportedSwapModel` owns the glTFast import, avatar, materials, textures and inked
mesh copies, and `Dispose` frees them. The renderer destroys the model object it
takes off. If wearing fails after the built-in model was discarded, the renderer
builds the built-in model again and the import is disposed.

## Wearing it

`PlayerRenderer.WearModel` poses both models on the idle clip, builds weapon
sockets, swaps the model under the hit-feel pivots, carries the current clip over
and rebinds `WeaponAttach`. `TakeOffModel` builds the built-in model again and
gives it the look every fighter got at the match start
(`MatchVisualStyle.StyleModel`: fighter shader, seat rim, ink line). Everything
that reads the renderer's model follows: hit flash, x-ray silhouette and prop queue,
death blink, seat rim, air-dodge and homing-dash ghosts, attack pitch, presentation
bones by human name, Manki's flight layer and the baked sword trail.

The weapon rule (ADR-0065) places a prop at its bone with offsets in that bone's
space. A creator's bones roll and scale differently, so `ModelSwapSockets` gives each
weapon bone a socket under the matching human bone, turned and scaled as the
built-in bone is in the same pose. A helper bone that is not a human bone, such as
Sir Bonk's pauldron, keeps its offset from the nearest human bone above it. A prop
whose bone the rig lacks stays hidden. No built-in weapon config has such a prop:
the player builds log a warning for an unresolved weapon bone, and no run logged
one for any built-in fighter.

## Which fighters accept a swap

`ModelSwapBases` lists them. FightGuy, Sir Bonk, Wibou, Manki and Dusty
(`salamander`) accept one. Bashie (`ShellTuckRig`, `WhistlePuffRig`) and Nyx
(`FormPresentation`) refuse with a reason, because their presentation reshapes
their own body. A fighter missing from the table refuses until someone checks it.

## Not carried over

- The built-in rig's own scripts: Sir Bonk's cape and pauldron springs, Manki's tail
  spring and Wibou's coat. A creator's extra bones move with the bone they are
  weighted to.
- Presentation anchored to the built-in rig's own non-human bones (`rig:` ids) falls
  back to the event's world position.
- Hitstun clip extrapolation, which writes the built-in rig's bone positions.
- The death blink blinks the first skinned mesh only, as on built-in fighters.

## Verifying

Shared coverage is `HumanoidBoneMapTests`, `PngShrinkTests` and
`WorkshopGlbPrecheckTests`. Swaps only run in a Unity player or the Editor, so the
step 1.3 check is a player build that wears a model on each accepted fighter in a
real match beside an unswapped one.

## Workshop screen and local library

Home's **Workshop** button opens a controller-focusable overlay. **Import from disk**
lists GLBs from `Workshop/Import` under `Application.persistentDataPath`, opens that
folder, or accepts a typed path. Each text field has a controller keyboard button
that calls Steam's gamepad keyboard when Steam is available.

Import chooses an admitted base fighter from `ModelSwapBases`, checks the GLB through
the runtime importer, and saves `Workshop/Items/<local-id>/item.json`, `model.glb`,
and an optional `humanoid.json`. The manifest hashes both payloads. Title defaults
to the source filename. Creator defaults to the Steam persona. License, attribution,
and the AI-assisted flag are set during import. The screen renders a 512-pixel
portrait from the imported model. The portrait and publish settings sit beside the
item folder, outside its hashed payload.

**Preview in Training** starts the base fighter with player 1 wearing the item.
**Wear on player 1** persists the choice. **Take off player 1 model** removes it.
`ModelSwapLibrary.Wear(player, item)` and `WorkshopLibrary.Worn(player)` are the
per-player API for later Fighter Select and Room integration. A saved item is checked
again at launch and before the fighter imports it. A choice only dresses its declared
base fighter. Launch arguments still override saved choices for testing.

`WorkshopLibrary.Verify` reads bounded files and checks the manifest, hashes and GLB
precheck in that order. The importer then checks the avatar before an item is accepted
or offered to wear. A refusal displays the named diagnostic and offers no wear action.
Item folders contain only the manifest and its declared payloads.

`SteamWorkshopClient` contains no Unity types. Its host initializes Steam and pumps
callbacks. In the player, `ChatSession` owns both. Workshop uses app 5324910 only.
A Playtest or offline build keeps local import, preview and wear, and explains that
Steam Workshop is unavailable. It does not attempt cross-app publishing.

Publishing creates an item once, remembers its ID as soon as Steam returns it, then
updates that ID. Uploads include title, description, visibility, type and base tags,
the compact hash map, the AI-assisted tag when selected, and the rendered portrait.
The screen shows Steam upload status and byte progress. If Steam requests acceptance
of its Workshop legal agreement, the item's Steam page opens. The content guidelines
appear before publishing. The screen defaults to private visibility.

**My items** and **Subscribed** query Steam with pagination and show install state.
**Download and verify** downloads the selected item, verifies its folder and imports
its avatar before offering wear and Training. Items that fail show the refusal.
The item's page and the game's Workshop page open in Steam's overlay.
