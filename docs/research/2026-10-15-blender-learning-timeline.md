# Blender learning and edited-model archive: timeline follow-up

Inspected **15 October 2026**, as a follow-up to the development timeline (`2026-10-14-development-timeline.md`, local research) and tools/models timeline (`2026-10-14-tools-and-models-timeline.md`, local research). All dates below are **Europe/Paris**. Paths to personal archives identify local evidence; those files are not distributed with this repository. A [visual summary](2026-10-15-blender-learning-timeline.html) and [full file-date CSV](2026-10-15-blender-files.csv) accompany this note.

The main folder is ~/blender/ (`~/blender`): **57 `.blend` files and 46 numbered backups**, plus exports, textures and scripts. Two smaller locations also survive: ~/Documents/Blender/ (`~/Documents/Blender`) has a saved pose asset, and ~/Documents/tumble-authoring-v01/ (`~/Documents/tumble-authoring-v01`) has three September animation projects and many previews. The CSV indexes **291 files across these three locations**, including those images and exports; that is not 291 edited models.

**The strongest conclusion is that the initial Blender-learning period was already underway on 4–5 June, and the largest retained rigging-experiment cluster is 20–25 June.** Native saves, exports and original user messages support this together. The learning continues into July; later dated files should be described as production/experiments rather than automatically attributed to manual beginner practice.

## Dates and exact places to look

| Period | Surviving files / date anchors | What this adds to the vlog |
| --- | --- | --- |
| **4 June, 18:33** | Untitled.blend (`~/blender/Untitled.blend`) | Earliest native Blender save in the inspected folders. Its generic name alone does not identify the model or prove this was your first-ever Blender use. |
| **5–6 June** | narodin.blend (`~/blender/narodin.blend`) (5 June 00:23), medium_run.blend (`~/blender/medium_run.blend`) (00:55), scaled_monkey.blend (`~/blender/scaled_monkey.blend`) (13:52); `anim_monkey_rename`, `anim_monkey_renamed`, `anim_monkey_ccopy` | First scale/import and animation-management experiments. anim_monkey.glb (`~/blender/anim_monkey.glb`) is dated 5 June 16:29 and embeds Blender-export metadata, one skin and 13 clips, including idle/run/jump and attacks. Clip presence does not prove you animated each from scratch. |
| **7–8 June** | manki_rmb.blend (`~/blender/manki_rmb.blend`) (7 June 23:40), manki/manki_rmb_loop.blend (`~/blender/manki/manki_rmb_loop.blend`) (23:49), manki/manki_rmb_attack.blend (`~/blender/manki/manki_rmb_attack.blend`) (8 June 00:06) | The charged-attack loop and release become separate work products. FBX exports occur on the same night. This supports the original 7 June conversation about making the attack in Cascadeur, then handling/exporting it through Blender. |
| **14–17 June** | manki/bunny.glb (`~/blender/manki/bunny.glb`) (14 June 19:39); `bunny2` / `bunny3` (16 June); `new_bunny` / `new_new_bunny` / `bunny_new` / `bunny_save` (17 June) | The rabbit file progression makes the repeated cleanup visible. The 14 June GLB contains Blender-export metadata, a skin and 13 clips. Do not wait until the 15–17 June `.blend` saves to date all rabbit Blender work. |
| **20–21 June** | manki/manki2_before_ik.blend (`~/blender/manki/manki2_before_ik.blend`), manki/manki2_rigging_test.blend (`~/blender/manki/manki2_rigging_test.blend`), `test_rig`, `manki_test_rig`, `manki_try_1`, `manki_try_2`, manki_test_autorig_skinned.blend (`~/blender/manki_test_autorig_skinned.blend`) | The most explicit rig/IK/auto-rig/skinning test filenames. An auto-rig export script survives dated 21 June. Filenames identify experiments; they do not prove which rig was accepted. |
| **23–25 June** | `new_manki`, `new_manki_cont`, new_bunny/new_bunny_arma.blend (`~/blender/new_bunny/new_bunny_arma.blend`), `new_bunny_skin`, `bunny_gamerig`, `bunny_gamerig_after_run`; manki_clean/mesh_scaled_bones.blend (`~/blender/manki_clean/mesh_scaled_bones.blend`) and manki_clean/manki_rig_skin.blend (`~/blender/manki_clean/manki_rig_skin.blend`) | Continued attempts to make model, bones, skin and animations work together. This overlaps OMP adoption and the HY-Motion/retargeting experiment on 24–25 June, before the full Unity migration. |
| **10–11 July** | `manki_clean/aerosol`, `bomb`, `bazooka.blend1`; fight_guy/fightguy_norig.blend (`~/blender/fight_guy/fightguy_norig.blend`), fight_guy/fightguy_mixamo_fix.blend (`~/blender/fight_guy/fightguy_mixamo_fix.blend`), fight_guy/fightguy_rig.blend (`~/blender/fight_guy/fightguy_rig.blend`) | A second clear character-work burst, with FightGuy rig/retargeting variants and Manki props. The original 11 July discussion explicitly describes still learning animation and Auto-Rig Pro Remap. |
| **30 July / 6 August / 19 August** | nilus.blend (`~/blender/nilus.blend`) (30 July), manki_clean/test_imp_new_new_manki.blend (`~/blender/manki_clean/test_imp_new_new_manki.blend`) (6 August), manki_clean/bazooka.blend (`~/blender/manki_clean/bazooka.blend`) (19 August) | Later character/import/prop work. The bazooka backup from 10 July demonstrates why an August last-save date should not be narrated as its first appearance. |
| **4–6 September** | manki_clean/try_mcp.blend (`~/blender/manki_clean/try_mcp.blend`) (4 September), manki_clean/bonk.blend (`~/blender/manki_clean/bonk.blend`) / manki_clean/bonk_sword.blend (`~/blender/manki_clean/bonk_sword.blend`) (6 September) | Later MCP and Bonk work products. The name `try_mcp` suggests a test; use a conversation to attribute the exact automation. |
| **18 September** | tumble_blocking_v01.blend (`~/Documents/tumble-authoring-v01/tumble_blocking_v01.blend`) (15:11), `tumble_blocking_v02` (15:16), `tumble_motion_v03` (15:20) | An additional animation-authoring archive outside the main folder: blocking → revision → motion, with contact sheets, GIFs, FBX export and Unity preview videos. This is useful later-process footage, with authorship not established from timestamps alone. |

## Original learning messages to pair with the files

Source: Hermes state database (`~/.hermes/state.db`). These direct user turns are stronger evidence of your experience than a filename:

| Date | Pointer | What you were trying to learn |
| --- | --- | --- |
| **4 June, 22:49** | `messages.id = 6318`, session `20260604_194101_a19379` | You have opened Blender, can see the armature, and cannot select a single bone. A concrete beginner moment on the same date as the earliest saved project. |
| **5 June, 13:42** | `messages.id = 6750`, same session | “je comprends pas le flow”: how to put several Mixamo FBX animations on one Blender rig. This aligns with the `anim_monkey*` exports and saves. |
| **13 June, 20:07** | `messages.id = 19921`, session `20260613_200738_9dafd8` | You want to animate Manki/Bunny yourself, struggle with Blender and ask about gizmos. This places the personal learning story just before the rabbit-save burst. |
| **14 June, 00:45** | `messages.id = 20209`, session `20260613_123412_e0528d` | Action Editor, hips and keyframes need explaining. This gives the abstract “learning animation” chapter a specific obstacle. |
| **15 June, 23:34** | `messages.id = 23779`, session `20260615_233357_23d07a` | You describe moving Bunny's armature in Pose Mode and setting it as Rest Pose, breaking the animations. This is a specific manual rig-editing mistake alongside the rabbit revisions. |
| **17 June, 19:33** | `messages.id = 24605`, session `20260617_172712_7bd098` | You say “im not a blender pro.” This sits in the `new_bunny` / `new_new_bunny` revision period. |

The original **14 June** Action Editor/scale messages **20209 / 20217** were copied into a resumed session at 12:45; copied messages **20607** onward should not move the original discussion into the afternoon. User-pasted tracebacks also connect the conversations directly to this folder: message **7029**, 5 June 21:31, names `~/blender/anim_monkey.blend/Text`; message **20204**, 14 June 00:43, names `~/blender/manki/bunny.blend/strip_root_motion.py`.

Two OMP conversations continue the same learning story:

- **24 June, 15:12:** OMP setup/animation session (`~/.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl`), line **358**. You want ComfyUI to animate the GLB because you struggle with Blender. This overlaps the busiest rigging-save period, rather than following an established, effortless animation pipeline.
- **11 July, 17:02–17:32:** original animation/retargeting session (`~/.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-11T14-46-31-407Z_019f51a4-eeef-7000-b5f2-f1517e23aaa1.jsonl`), lines **106, 108, 110**. You say “don't know too much about animating, just learning,” decide learning retargeting is best, then report using **Auto-Rig Pro Remap** and a shoulder-rotation problem. This aligns closely with the FightGuy saves that day.

## What the timestamps mean

The CSV records last modification time (`mtime`), filesystem birth time when available, metadata-change time (`ctime`), size and file kind. **Use modification time as the date of the surviving file state, not the date you started learning or first created that model.** Blender can replace a file during saving, so filesystem birth time often also follows the latest saved incarnation. Copies and timestamp-preserving transfers can change the relationship further. `ctime` is metadata-change time, not creation time.

Backups preserve useful earlier states. Two concrete examples:

- manki/manki.blend1 (`~/blender/manki/manki.blend1`) is dated **20 June**, while manki/manki.blend (`~/blender/manki/manki.blend`) is last saved **24 June**. The later main-file date does not erase the earlier work.
- manki_clean/bazooka.blend1 (`~/blender/manki_clean/bazooka.blend1`) is dated **10 July**, while manki_clean/bazooka.blend (`~/blender/manki_clean/bazooka.blend`) is last saved **19 August**. The prop existed in this archive by July.

Of the main folder's 103 native/backup files, **78 have retained modification dates in June**, and **42 fall on 20–25 June**. This shows the shape of the retained archive; it is not a save-frequency log or a measure of hours practiced. One file can have many lost earlier saves, and a backup is not another distinct model. Later files may reflect manual work, scripts or agent-assisted work, which timestamps cannot separate.

## A good personal beat for the video

Start with the attractive generated character, then the 4 June “how do I select a bone?” message, then the 20–25 June stack of rigging tests. The filenames `new_bunny` → `new_new_bunny` and `manki_try_1` → `manki_try_2` make the iteration understandable without explaining rig mathematics. Your 11 July “just learning” message prevents compressing the story into a single weekend.

For later before/after footage, look at the tumble blocking contact sheet (`~/Documents/tumble-authoring-v01/blocking-contact-sheet.png`), the motion preview (`~/Documents/tumble-authoring-v01/v03-three-cycles.mp4`) and the Unity preview (`~/Documents/tumble-authoring-v01/unity-hit-drift-fall.mp4`). Their dates and filenames were inspected; their visual quality was not evaluated in this follow-up.

Only filesystem metadata, existing exports and original conversations were read. Blender was not opened and original projects were not edited.
