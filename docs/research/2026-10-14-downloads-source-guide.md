# Downloads sources for the SlopArena development vlog

Research cut-off: **14 October 2026**. This guide indexes the SlopArena-related design exports, PDF and video-preparation decks recovered from Downloads. It adds original decisions, revisions and presentation material that the Git timeline alone does not preserve. The later sections cover character-art iterations, model-container evidence, recordings, old build archives and exact OMP conversations.

Dates require care. A date written inside a document is its stated revision/decision date. A reviewed Git hash establishes the baseline the draft describes. A filesystem modification time is only stored metadata: downloading, copying and restoring can preserve or change it. None of the undated exports below proves its authoring date from mtime alone. Times are Europe/Paris.

## The sources to open first

| Period supported by the evidence | Local source | Best script use |
| --- | --- | --- |
| Introduced in OMP 15 August; PDF creation date absent | [SlopArena_UI_Specification.pdf](../../../../../Downloads/SlopArena_UI_Specification.pdf) | Show a formal visual/technical handoff to an agent, then compare it with actual combat footage. |
| Explicit proposal date 21 August | [ADR-0001_ Workshop-First Community Content Architecture.md](../../../../../Downloads/sloparena/art/ADR-0001_%20Workshop-First%20Community%20Content%20Architecture.md) | Explain the original community-creation ambition before it became seven ADRs and cooked packages. |
| Undated decks; stored mtimes 7–8 September | [ai_use_youtube_presentation.html](../../../../../Downloads/ai_use_youtube_presentation.html), [ai_development_workflow.html](../../../../../Downloads/ai_development_workflow.html), [ai_harness_omp.html](../../../../../Downloads/ai_harness_omp.html) | Existing slide/script material about the experience of developing with AI. |
| Reviewed 21 September baseline; stored mtime 21 September | [SlopArena_Frontend_Shell_Implementation_Plan.md](../../../../../Downloads/sloparena/SlopArena_Frontend_Shell_Implementation_Plan.md) | A concrete UI revision: the social dock changes position and behavior, while the chat session survives navigation. |
| Explicit revision date 23 September | [sloparena-down-action-implementation-plan-v3.md](../../../../../Downloads/sloparena/sloparena-down-action-implementation-plan-v3.md) | A movement-design reversal: frequent sliding becomes a success case; attacks keep their commitment. |
| Undated specification; stored mtime 25 September 00:38 | [sloparena-phase-1-offsite-deployment-spec.md](../../../../../Downloads/sloparena/art/sloparena-phase-1-offsite-deployment-spec.md) | Why hosting moved away from Alfred/home infrastructure and what needed to become reproducible. |
| Reviewed 25 September baseline; stored mtime 25 September 19:38 | [sloparena-phase-2-steam-integration-plan.md](../../../../../Downloads/sloparena-phase-2-steam-integration-plan.md) | The original plan for Steam identity and relay transport, including the tests and initially deferred features. |
| Committed 1 October; Downloads copy mtime 2 October | [sloparena-visual-identity.html](../../../../../Downloads/sloparena-visual-identity.html) | A ready-to-show offline visual board, interface prototype and identity manifesto. |

## Combat UI: an external agent handoff, still in the Burst era

[SlopArena_UI_Specification.pdf](../../../../../Downloads/SlopArena_UI_Specification.pdf) is a six-page **Combat UI Specification & Technical Implementation Guide**, version 1.0.0. Its first page explicitly addresses an automated LLM/MCP agent or developer. It proposes two layers: tracked overhead player badges/damage/stocks, and a bottom action bar for `1, 2, 3, 4, A, E, R, F`, Dash and a prominent Burst meter.

Useful pointers:

- **Page 1, §§1–2:** the system overview and hierarchy tree; good material for explaining how a visual idea was made concrete enough for an agent.
- **Page 2, §§3.1–3.2:** the damage-color ranges, player badges, cooldown masks, readiness flashes and Burst glow.
- **Pages 3–6, §4 and §5:** example C# and USS implementation. These are proposed reference scripts, not evidence that the code was imported unchanged into the project.

The stored mtime is **15 August 12:14:05**. `pdfinfo` reports WeasyPrint 62.3 and six A4 pages, but no creation/modification metadata. It says Unity 2022+ despite the project's Unity 6 client; use this as an exported design handoff rather than the authoritative engine/version contract. Its Burst controls place it in a historical combat design that was later changed. The 15 August OMP conversation below explicitly introduces this as a document you had just generated, giving stronger timing evidence than the filesystem timestamp alone. The [15 August HUD implementation `ec0981e`](https://github.com/Binoui/SlopArena/commit/ec0981e) is a useful comparison; proximity of the timestamp is not proof of direct authorship or import.

## The original Workshop manifesto before the ADR split

[ADR-0001_ Workshop-First Community Content Architecture.md](../../../../../Downloads/sloparena/art/ADR-0001_%20Workshop-First%20Community%20Content%20Architecture.md) explicitly says **Proposed — 21 August 2026**. This is an earlier exported proposal titled ADR-0001; it is not the current repository's ADR-0001 about fall ramps.

It provides more accessible examples than the later split architecture documents: a creator's `fighter.json` references a built-in animation/VFX ID, while the installed game privately resolves the actual licensed asset. It also shows a beginner workflow from a model through rigging/Humanoid import to default locomotion/reaction animations and custom attacks. The original reference roster is Manki/FightGuy/Kistu/Nilus.

Use **§§2–5** for the reason packages should reference capabilities instead of copying assets; **§6** for Unity as the creator environment and Ability Lab's intended role; **§7** for the installed-game preview idea; **§8** for the Humanoid path. The **Follow-up Decisions** at the end foreshadow package identity, runtime character registries, gameplay primitives, ModKit, multiplayer synchronization and compatibility policy.

This supports a narrative of “a game I could build characters for” expanding into “a game other people could build characters for.” Compare it with [the accepted ADR set in `360e7e2`, 22 August](https://github.com/Binoui/SlopArena/commit/360e7e2) and [character cooking in `7bec5fd`, 27 August](https://github.com/Binoui/SlopArena/commit/7bec5fd). The export proposes a creator ecosystem; it does not prove Steam Workshop uploads or the final public ModKit had shipped. Stored mtime is **21 August 14:02:17**.

## Existing AI-development video material

These HTML files are particularly useful because some already contain **speaker guidance**, first-person explanations and editable slides. They describe a development workflow rather than dating specific game commits. None contains an explicit authoring date; stored mtimes cluster on 7–8 September. Correlate them with OMP sessions and recordings before making a dated claim about when the video was planned or published.

### The larger outline with speaker notes

[ai_use_youtube_presentation.html](../../../../../Downloads/ai_use_youtube_presentation.html) has **17 slides**, three embedded images, keyboard navigation and no external scripts. Stored mtime: **7 September 14:46:54**.

The most useful slides are:

- **Slide 2, “The failure mode”:** autonomous work turning into a suspicious large diff, overengineering and repeated self-fixes. The speaker note asks to make this relatable. Pair this with a real SlopArena incident rather than presenting it as a universal rule.
- **Slides 3–6:** thinking versus worker models and routing a bounded task rather than choosing one model for the entire project.
- **Slides 7–8:** relevant context, session noise and shake/compact. The note explicitly says a 300k-token observation should be presented as personal experience rather than a universal law.
- **Slides 9–11:** OMP as the harness, Orca as the IDE, inspectable traces. The Orca screenshot is a placeholder; this file is not footage of the actual IDE.
- **Slide 12:** the human → thinking → defined task → worker → review loop diagram.
- **Slides 14–16:** avoiding ritualized process, writing project skills after repeated mistakes, and evaluating where the workflow failed. Slide 15 explicitly suggests **SlopArena stage authoring, animation authoring and character cooking** as examples.

These notes can supply the vlog's reflective thread alongside the chronological game story: what made the collaboration productive, where autonomy went wrong, and what the developer still had to decide.

### The shorter presentations and alternate revisions

[ai_development_workflow.html](../../../../../Downloads/ai_development_workflow.html) is a **nine-slide** deck titled “My experience with agentic coding.” Stored mtime: **8 September 00:26:06**. Its sections are model cost/roles, context management, **frame → plan → do → check**, docs versus skills, custom skills, avoiding ritualization, evaluation and close human–AI collaboration. It is a convenient presentation version for a talking-head explanation or screen recording.

[ai_use_youtube_presentation_v3.html](../../../../../Downloads/ai_use_youtube_presentation_v3.html) contains the same nine core subjects with a different arrangement/presentation, stored mtime **8 September 00:11:08**. It is not byte-identical to the deck above. Use the visible slide order: some internal `data-index` values repeat or skip, so those attributes are poor script scene numbers.

[preview.html](../../../../../Downloads/preview.html) is a **14-slide earlier/alternate version**, not an empty preview. Stored mtime **7 September 15:19:54**. It retains more first-person detail: “I personally use /shake a lot,” dedicated OMP/Orca slides, a distinction between compacting the same task and starting a new subject, and specific versus fuzzy task workflows. This is useful when looking for a more personal voice than the polished nine-slide export. The revision order beyond the stored times still needs session evidence.

[preview (1).html](../../../../../Downloads/preview%20%281%29.html) is a standalone **Docs vs Skills** comparison card, stored mtime **7 September 21:59:35**. It is suitable as a cutaway if the script explains what changed when project knowledge became reusable skill instructions.

[ai_harness_omp.html](../../../../../Downloads/ai_harness_omp.html) has two slides, stored mtime **8 September 21:33:27**. **“AI HARNESS ≠ MODEL”** separates models from the tools/context/loop around them; **“OMP IS MY HARNESS”** gives three stated reasons: low configuration, the needed models/sessions/skills/project context, and ongoing updates. Its footer explicitly mentions repeatable SlopArena work. This is a strong original artifact for the “why OMP?” part of the vlog, though it does not establish when OMP was first adopted.

## Frontend: a specific visual revision after the single-scene migration

[SlopArena_Frontend_Shell_Implementation_Plan.md](../../../../../Downloads/sloparena/SlopArena_Frontend_Shell_Implementation_Plan.md) is an implementation proposal reviewed against **`0ef65129527501157d1c6ba3ad9e440c342829a3`**, the single-frontend migration committed **21 September 01:17**. Stored mtime is **21 September 20:46:06**; no authored date appears in the export.

The opening and **§1** document a visible decision change: the old right-hand dock/automatic compact replacement is superseded by a **compact top bar and bottom-left conversation panel**. The panel looks floating but gets reserved layout space, while the current page uses the upper area and lower-right summary/actions. Chat should remain visibly usable at 1280×720 and 1920×1080. Home loses its roster showcase and keeps the three actual play modes.

**§3** explains preserving drafts, conversation/history and social identity across UI host changes; **§4** describes identity/focus traps; **§7** stages the work through Home/Fighter Select, shared social behavior and remaining pages; **§8** gives the acceptance matrix. These are useful details for a story about why “make chat persistent” involved more than placing a widget on screen.

Compare this export with the [earlier right-dock plan at the reviewed baseline](https://github.com/Binoui/SlopArena/blob/0ef6512/docs/plans/2026-09-20-frontend-social-flow.md), [shell implementation `d8abb46`, 21 September](https://github.com/Binoui/SlopArena/commit/d8abb46), and [shared identity/social focus `679902e`, 23 September](https://github.com/Binoui/SlopArena/commit/679902e). The export explicitly says no runtime changes or Unity verification had been performed **for the plan**; screenshots or implementation receipts are needed for the resulting behavior.

## Down v3: the strongest recovered gameplay-design revision

[sloparena-down-action-implementation-plan-v3.md](../../../../../Downloads/sloparena/sloparena-down-action-implementation-plan-v3.md) explicitly states **Revision 3 — 23 September 2026**, superseding v2 as the complete handoff. Stored mtime: **23 September 10:21:57**. Its reviewed code observations are retained from the 21 September `0ef6512` baseline; the v3 document says it is not a fresh repository audit.

The opening is a meaningful design correction: **frequent sliding is intended**, rather than something to discourage through cooldowns, minimum residence time or artificial punish windows. Basic run slides, landing slides, slide-jumps and ordinary attacks out of slides belong to core locomotion. Depth comes from placement, momentum, aerial choice and timing. The balancing concern is one universally dominant route, not merely a high slide count.

Use the opening **Design intent** and **§1** for the narrative; **Landing slide** and **Aerial → fast-fall → landing slide** for the mechanics. Fast-fall requires a new press while descending, but holding Down at actionable contact selects a low slide landing. Releasing before landing retains the fast descent and declines the slide. Landing lag and unfinished attacks remain committed; the proposal does not grant every aerial a free cancel. Basic slide-jumps need no extra precision window; late-dash conversion is a separate optional experiment.

**§2** preserves a real prototype comparison: slide friction at 2× versus 3× RunSpeed, lower landing-entry thresholds than running-entry thresholds, and separate fall-speed trials. The listed travel durations/distances are calculated experiment seeds, explicitly not measured results. **§§5–7** supply useful filming/test routes and questions: run→slide→jump, short/full jump→aerial→fast-fall→landing slide, platform approaches, first-action latency and pose/hurtbox agreement.

Compare with [Down implementation `63a727e`](https://github.com/Binoui/SlopArena/commit/63a727e) and [crouch/slide `8101ec5`](https://github.com/Binoui/SlopArena/commit/8101ec5), both committed **24 September**, then the [current Contextual Down documentation](../systems/combat-systems.md). This source supports a larger September movement chapter than a single “added crouch” bullet.

## The original off-site and Steam plans

### Phase 1: remove home-server dependencies

[sloparena-phase-1-offsite-deployment-spec.md](../../../../../Downloads/sloparena/art/sloparena-phase-1-offsite-deployment-spec.md) has no explicit authored date or pinned baseline; stored mtime is **25 September 00:38:38**. Its source list references moving `main` branches, so it is less precisely anchored than Phase 2.

**§1, Outcome** says no production runtime dependency may point to Alfred or another home service. **§2** settles one external VPS with Docker Compose, Caddy, PostgreSQL and a dedicated GameHost. **§7** distinguishes reproducible application releases from database restore; **§9** insists a downloaded off-host backup must actually be restored to a clean target. **§10** separates local/CI evidence from live clients, network restrictions and recovery. Steam integration, player-created rooms and seamless scaling are deliberately out of this phase.

This is useful for explaining the change from “it runs on my machine/home server” to “friends can depend on a reproducible service.” Match it to [restricted VPS deployment `5ba1be4`](https://github.com/Binoui/SlopArena/commit/5ba1be4), [live UDP proof `e3a6dbe`](https://github.com/Binoui/SlopArena/commit/e3a6dbe), and [off-host recovery `d7983c7`](https://github.com/Binoui/SlopArena/commit/d7983c7), all on 25 September. Its acceptance criteria are requirements, not deployment receipts.

### Phase 2: authenticate with Steam and connect to the dedicated host

[sloparena-phase-2-steam-integration-plan.md](../../../../../Downloads/sloparena-phase-2-steam-integration-plan.md) pins game **`1db8836`**, authored **25 September 18:06**, and Master **`263a19d`**. Its stored mtime is **25 September 19:38:40**. The status is explicitly a proposed plan ready to split into local specifications.

**Objective / Architecture decisions** explains the product story: players launch the Playtest, Steam verifies identity/entitlement, the existing Master owns chat and match authorization, and the VPS remains the authoritative simulator. Steam's P2P API is a connection path to the dedicated GameHost, not a switch to player-hosted simulation.

Use **2A** for the uncertainty before implementation: native libraries, Linux/Windows compatibility and the actual OVH relay route had to be proved. **2B** covers stable identity without a separate account system; **2C** binds an authenticated Steam connection to the Master-assigned match slot; **2D** requires an actual two-account installed-client match and rematch.

A useful before/after is preserved: this original plan defers **Rooms and invitations**, while [Rooms `124fe4b`](https://github.com/Binoui/SlopArena/commit/124fe4b) and [Steam room joining `3086dce`](https://github.com/Binoui/SlopArena/commit/3086dce) arrive on 27 September. The original 2A requires a packaged Windows↔VPS probe; [the subsequent repository plan at `d3eb6ee`](https://github.com/Binoui/SlopArena/blob/d3eb6ee/docs/plans/2026-09-25-steam-playtest-integration.md) records the explicit decision to proceed on the Linux OVH proof while retaining Windows install/gameplay acceptance for 2D. That is an evidenced change in sequencing, not proof the deferred test passed.

## The portable visual identity board

[sloparena-visual-identity.html](../../../../../Downloads/sloparena-visual-identity.html) is a self-contained local board: embedded art-direction image, palette/type samples, clickable selection/Ready Up, and a replayable winner-stamp animation. Its lower **Visual direction** section contains the seven-part identity text, from character silhouettes and improvised arenas to fight-flyer graphics and intentional imperfection. The central idea is an authored collision of scavenged low-poly characters with DIY tournament graphics, with readable controls underneath.

The strongest script pointers are **§3 Arenas and world** (a memorable place to fight), **§4 Graphic identity** (the graphic layer unifies disparate assets), and **§7 Intentional imperfection** (curated roughness versus unreadable or unattended UI). The image is explicitly a generated direction study; it is not a screenshot of a finished in-game arena.

This Downloads file is **byte-for-byte identical** to [the repository visual-reference-board.html](../design/references/sloparena-visual-identity/visual-reference-board.html). SHA-256: `7d66b217a2daf5b971c524a29777193074c203f407f68070cf75ce634be76c3e`. Git first records that board in [the 1 October checkpoint `31929e2`](https://github.com/Binoui/SlopArena/commit/31929e2); the Downloads copy's stored mtime is **2 October 00:57:57**. It is convenient offline footage and a design reference, rather than independent evidence of a new 2 October decision.

The uppercase `Downloads/SlopArena/` directory contains a playable distribution's `README.txt` / `HOSTING.txt`, while lowercase `Downloads/sloparena/` contains the design exports above. Keep those distinct when searching. For exact motivations or chronology behind an undated export, pair its title/baseline with OMP conversation evidence before adding a new milestone to the vlog.



## Character art and the practical asset pipeline

These files give the vlog a visual history that implementation commits cannot show alone. I opened the character sheets and previews listed below. Art dates are filesystem timestamps or names unless supported by a conversation; they are leads for the script, not acceptance dates.

| Era / topic | Source to open | What it contributes |
| --- | --- | --- |
| Early June: Manki | [character_sheet.jpeg](../../../../../Downloads/sloparena/monkey/character_sheet.jpeg) | Orange, mischievous monkey in black vest/shorts; a four-view model sheet. File mtime 5 June. The folder also contains image-edit prompts, generated 3D versions, rigged/scaled FBX files and animation saves. |
| 13 June: Bunny | [character_front.jpeg](../../../../../Downloads/sloparena/rabbit/character_front.jpeg) | Rabbit martial artist in a blue gi and red headband. File mtime 13 June, consistent with the original Hermes concept discussion. Side/back images, generated GLB and exported animations are beside it. |
| Early July: FightGuy | [character_sheet.jpeg](../../../../../Downloads/sloparena/fightguy/character_sheet.jpeg) | Human fighter/ninja in red and black. File mtime 3 July. The same folder contains front/back/side views and a generated model, giving visual context for the later Bunny-to-FightGuy transition. |
| July: Kistu | [front.png](../../../../../Downloads/sloparena/kistu/front.png) | The earlier human swordfighter model, with four views and its FBX. File mtime 30 July; these are useful for comparing the character before the Wibou model change. |
| July: Manki kit concept | [ChatGPT Image Jul 18, 2026, 11_07_31 PM.png](../../../../../Downloads/sloparena/art/ChatGPT%20Image%20Jul%2018%2C%202026%2C%2011_07_31%20PM.png) | A detailed concept/ability poster with bomb, grapple, bazooka and aerial attacks. The filename carries 18 July. Treat move text as historical concept art; it is not authoritative current frame data. |
| 24 September: Steam presentation | [library_assets_overview.jpg](../../../../../Downloads/sloparena/art/sloparena_steam_library_assets/previews/library_assets_overview.jpg) | A composite preview of the four-character Steam capsule, header, wordmark and hero. The accompanying README explicitly records checks on 24 September 2026. |

The model containers give a concrete production chain: [the generated Manki GLB](../../../../../Downloads/sloparena/monkey/1_Cartoonish_orange_monkey_with_mischievous_expressi_Tripo_P1_Game_Ready_ee3a1f03.glb) records `https://tripo3d.ai` as generator and has no skin or animations; [the animated Manki save](../../../../../Downloads/sloparena/monkey/save/anim_monkey.glb) records Blender export, one skin and 13 clips (`idle`, `run`, `jump`, `flying_kick`, hit reactions and others). These are preserved work products that show generation and animation integration, not proof which exact model was shipped. `new_manki/anims/` also contains `hymotion_1782314338491.glb`, which aligns with the 24–25 June OMP experiments with generated animation.

The original [store-image export notes](../../../../../Downloads/sloparena/art/final/sloparena_steam_assets/README.txt) and [library-image export notes](../../../../../Downloads/sloparena/art/sloparena_steam_library_assets/README.txt) explain how approved artwork was adapted into Steam-sized outputs. Use `promo_with_tagline/`, `steam_upload/`, the source `poster.png`/`bigposter.png`, and the library `previews/` folder to recover the iterations and branding work. These files establish prepared art, not publication by themselves.

## A date refinement: Kistu → Wibou began before the October commit

The Wibou asset folder has files dated 24 August. More decisively, [an OMP conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-24T22-30-51-921Z_01a035e5-dd11-7170-a3d9-44c4e4e97e42.jsonl) records your actual request at **00:32 on 25 August** (Europe/Paris), line 5: “i want to change kistu model by wibou.” This is a model-replacement decision while Kistu is still the identity in code. [The formal name-change conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-29T11-07-44-009Z_01a0ecd9-6009-7590-9b08-a7635702bff0.jsonl) follows on **29 September**, line 7. The main-repository rename checkpoint remains **1 October**. For narration, those are three different events: a new model, a renamed fighter, and the saved integration. The early Wibou folder name alone would not have established that sequence.

## Early gameplay and downloadable demo artifacts

- [sloparena_dev_clip.mp4](../../../../../Downloads/sloparena_dev_clip.mp4): **30.88 seconds**, 1920×1080. Sampled frames at 10s and ~21.6s show Godot-era Manki on the yellow/grey blockout, damage percentages, spell/action labels and debug body volumes/effects. File mtime is 11 June; the exact capture date is not established by metadata alone. This is a strong early before/after clip.
- [SlopArena-0.2.0-demo.1.zip](../../../../../Downloads/SlopArena-0.2.0-demo.1.zip): archive entry timestamps cluster on **2 August**. [The extracted play guide](../../../../../Downloads/SlopArena-0.2.0-demo.1/README.txt) describes the early download→extract→Windows EXE→Training/Join flow and mouse-attack controls. Source for the first shareable-build chapter, not proof a tester completed a match.
- [SlopArena-0.2.0-demo.2.zip](../../../../../Downloads/SlopArena-0.2.0-demo.2.zip): archive entry timestamps cluster on **4 August**; the contained README repeats the earlier player guide. The directory [Downloads/SlopArena](../../../../../Downloads/SlopArena) is another preserved Windows build, with matching August-era files. These builds were inspected as files and archives, not executed.
- [22 August video in Downloads](../../../../../Downloads/2026-08-22%2019-19-57.mp4): another copy/variant of the recording name already found in OBS. Its hash differs from the OBS file; it has not been treated as an additional dated milestone or reviewed visually here.

## Conversations that connect Downloads back to the work

| Topic | Conversation | Jump to |
| --- | --- | --- |
| 15 August UI specification review | [Review SlopArena UI specification](../../../../../.omp/agent/sessions/-orca-workspaces-SlopArena-GUI/2026-08-15T10-14-33-672Z_01a004ea-85c8-7000-9495-cab140af8a10.jsonl) | Line 4: asks to build the GUI from the freshly generated PDF and challenge the spec |
| 21 September frontend-shell export | [O28 (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-21T18-46-26-240Z_01a0c54a-74c0-764c-b713-51213443099d.jsonl) | Line 6: passes the original Downloads plan into the implementation/spec workflow |
| 25 August Wibou model replacement | [O29 (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-24T22-30-51-921Z_01a035e5-dd11-7170-a3d9-44c4e4e97e42.jsonl) | Line 5: replace the Kistu model; session filename begins 24 August UTC |
| 29 September Wibou name change | [O30 (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-29T11-07-44-009Z_01a0ecd9-6009-7590-9b08-a7635702bff0.jsonl) | Line 7: explicit rename request |
| 26 September promotional-video work | [Create a Sloparena Promotional Clip](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-26T13-59-12-848Z_01a0de03-4ad0-7551-b3da-139891ed6430.jsonl) | Line 344: points to Steam library assets and asks to decide the story/slides before making a clip |
| Late September splash-art changes | [Update Character Splash Art and Crops](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-28T22-11-33-040Z_01a0ea12-c230-7754-91c4-f55c6cd67331.jsonl) | First user request and following turns: imported portraits/splashes and crops; session starts 28 September UTC |

The strongest immediate script additions are the original character-art sequence, the old demo ZIP/play guide, your existing AI-workflow presentation notes, and the model/name/integration distinction in the Kistu→Wibou story.

A [visual shortlist of Downloads sources](2026-10-14-downloads-source-guide.html) accompanies this guide. The [main development timeline](2026-10-14-development-timeline.md) incorporates the date refinements and extra source pointers.
