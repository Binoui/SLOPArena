# SlopArena tools and models: a development timeline

Research cut-off: **14 October 2026**. A filterable [visual timeline](2026-10-14-tools-and-models-timeline.html) and [coding-model evidence CSV](2026-10-14-tools-and-models.csv) accompany this guide. This companion to the [development timeline](2026-10-14-development-timeline.md) separates the coding harness, selected language model, editor/automation tool, and art/animation generator. These were overlapping parts of the workflow, not necessarily a single sequence of replacements.

Dates are Europe/Paris when the source supplies a timezone. Warp stores unmarked SQL timestamp strings; its dates below retain the stored calendar rather than claiming a verified local hour. “First observed” means the earliest relevant surviving record found in the inspected evidence, not a guaranteed adoption date. A session's stored model setting is weaker evidence than a model identified on actual assistant output. An exported asset filename identifies its export label; it does not necessarily disclose the exact generation backend. Installation, configuration, attempted calls, successful responses, and shipping an asset are separate events.

## The sequence worth telling in the video

| Period | Main change in the workflow | Project context / useful story |
| --- | --- | --- |
| **22 May** | **Antigravity** for MoveBox planning/prototyping; **Warp** also active | The game and the search for a comfortable AI workflow start together. Antigravity's exact model is unrecovered; Warp records GPT and Claude labels. |
| **25 May** | **Hermes + DeepSeek V4 Flash** | You explicitly say you have just installed Hermes. This is the early Godot development period. |
| **10–19 June** | **Pro plans/reviews, Flash workers**, with a **Sonnet 4.6** debugging switch | The division between thinking and implementation already exists before OMP or Orca. The parent/child records make this visible. |
| **24 June** | **Hermes → OMP**, initially still **DeepSeek V4 Flash** | A change of harness before the Godot → Unity migration. The model does not need to change with the tool. |
| **July** | OMP stays central; **Gemini, Haiku and Opus** appear alongside DeepSeek | Character setup, animation and the Unity pipeline drive experiments. Warp continues in parallel, including ComfyUI troubleshooting. |
| **1 August** | **Orca worktrees + OMP**, with **GLM 5.2** among the workers | Separate PvP infrastructure tasks now have separate checkouts: master deployment, server foundation, lobbies/browser work. This is the first recovered Orca worktree evidence, not an installation date. |
| **13–19 August** | GPT appears in OMP: **5.6 Luna Pro**, then **Luna / Terra / Sol** | The Melee/Ability Lab period overlaps a larger change in the coding-model mix. DeepSeek continues briefly. On 19 August you explicitly discuss delegating visual-roadmap parts to Luna. |
| **September** | More GPT choices: **Astra**, then **6 Sol / 6 Luna / 6.1 Sol**; standalone **Codex CLI** workers also appear | OMP is still in use. The first scoped Codex CLI rollouts are Orca-dispatched workers for the feedback website, on 6 September. |
| **10–14 October** | **T3 Code + Codex**, mainly recorded as **GPT 6.1 Sol** | You explicitly say you just installed T3 on 10 October. Completed OMP-through-ACP runs on 11 October show another period of overlap. |

These are the main narrative beats, not nine exclusive tool eras. The exact records and smaller experiments follow below.

## Early coding and editor-automation lane

| Date / period | Harness or tool | Model recorded | What the source supports |
| --- | --- | --- | --- |
| **22 May** | **Antigravity** | **Unknown** | Conversation `3dcc278c-adb8-4260-936a-fc0b307ccebb` has MoveBox workspace, 609 steps, prototype preview, and dated GDD/implementation artifacts. The readable summary and artifact metadata expose no model. The application's name is not evidence that Gemini generated these responses. |
| **22–23 May** | **Warp** | Conversation usage labels include `GPT-5.5 (extra high reasoning)`, `Claude Opus 4.6`, and `Claude Sonnet 4.6` | Completed queries in the MoveBox directory include local-model setup questions on 22 May and a project-architecture request on 23 May. Usage metadata gives models at conversation level, not a one-model-per-prompt attribution. [Warp pointers](#warp-the-parallel-terminalassistant-lane) |
| **25 May, 23:24 onward** | **Hermes** | `deepseek-v4-flash` | Earliest recovered Godot-game session is titled “Godot Game Development Assistant.” At 23:25 the user says they have just installed the assistant and asks it to help create the game; another message names MoveBox. The export and DB session record both identify Flash. [H1](#hermes-primary-source-pointers) |
| **26 May Git snapshot** | Godot's `godot_ai` plugin | `gpt-4.1`, provider `OpenAI` **configured** | The [original `project.godot`](https://github.com/Binoui/SlopArena/blob/1f64c64/project.godot) enables the plugin and contains these model/provider values. This establishes configuration, not a completed GPT-4.1 response. It also does not identify Antigravity's model. |
| **10 June, 19:22 onward** | Hermes | `deepseek-v4-pro` | First recovered game-related Pro session is “Reprise du développement serveur,” with a direct request to resume server work. Flash remains present in later implementation sessions; this is an added model choice, not proof of a permanent switch. [H2](#hermes-primary-source-pointers) |
| **14 June, 22:45–22:47** | Hermes parent and delegated sessions | Parent `deepseek-v4-pro`; three children `deepseek-v4-flash` | A global SlopArena audit starts in Pro and spawns three Flash children. This is an early concrete example of the later thinker/worker workflow. These identifiers are recorded per session, not per response. [H3](#hermes-primary-source-pointers) |
| **15 June, 19:47** | Hermes → **Godot AI MCP** | Session `deepseek-v4-flash` | An actual `mcp_godot_ai_session_manage` result returns the live `sloparena@39b1` session, Godot 4.6.3 and the game project path. This is stronger evidence of editor connection than the earlier enabled-plugin setting. [H4](#hermes-primary-source-pointers) |
| **15 June, 23:38 onward** | Hermes → **Blender MCP** | Session `deepseek-v4-flash` | `mcp_blender_get_scene_info` returns actual Scene/Armature information; object inspection, code execution and viewport screenshot calls follow. Blender was already being used manually earlier in June. [H5](#hermes-primary-source-pointers) |
| **17 June, 17:27 onward** | Hermes, using OpenRouter | `anthropic/claude-sonnet-4.6` | Hurtbox/model-size debugging. At 17:28 the user explicitly says the prior report was written by DeepSeek V4 Flash, which became confused, so they switched. This is a well-sourced example of changing model for a difficult problem. [H6](#hermes-primary-source-pointers) |
| **19 June, 22:46–23:14** | Hermes parent and delegated sessions | Parent `deepseek-v4-pro`; three children `deepseek-v4-flash` | The ServerAbility architecture-plan review repeats the parent/worker pattern. It should be described as an observed workflow, without assuming every implementation response came from the session's final setting. [H7](#hermes-primary-source-pointers) |
| **24 June, 10:45** | **OMP** | `deepseek-v4-flash` on an actual assistant response | The [setup conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl), line 6, directly says OMP was just installed and the previous harness was Hermes; line 7 records the first Flash response. The [same day's project configuration `9132426`](https://github.com/Binoui/SlopArena/commit/9132426) adds instructions and skills. |



The early Hermes records support a useful vlog story: Flash for sustained implementation, Pro for bigger audits/design reviews, and an explicit Sonnet switch when one investigation became confused. They do not support a benchmark claim that one model was universally better or cheaper.

## OMP, Orca, Codex and T3: later workflow changes

**24 June — OMP is an explicit new installation.** The original user turn says the previous harness was Hermes; the first actual response records `deepseek-v4-flash`. The user's later instruction in the same conversation, line **86**, asks for explanations, a plan and continued involvement. This is a useful transition for the vlog: changing the working environment while retaining a familiar model. [Setup OMP For Coding](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl).

**26 June — DeepSeek Pro is also doing the Unity-transition audit in OMP.** Its first actual response appears at 13:53 Paris, during the engine migration. This is the same Flash/Pro distinction already visible in Hermes, carried into a different harness. [Audit Unity Transition Progress](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-26T11-51-18-140Z_019f03c5-1f7b-7000-8502-213e64a38289.jsonl), line **6**.

**1 August — Orca gives parallel tasks separate workspaces.** The [master-server deployment worker](../../../../../.omp/agent/sessions/-orca-workspaces-SlopArena-deploy-master-server-infrastructure/2026-08-01T15-02-15-970Z_019fbdd8-e4a2-7000-930e-e74b63ec35b4.jsonl) has an Orca worktree cwd, `/implement 28` at line **6**, and GLM 5.2 assistant responses. Other preserved worktrees cover the PvP foundation and browser/lobby work that day. Orca is the IDE/workspace layer, OMP is the assistant harness, and GLM is a model; all three names can describe the same task. The current Orca dispatch DB retains later work, so its oldest row should not be mistaken for adoption.

**13 August — GPT 5.6 Luna Pro appears during the Melee plan.** The first recorded successful response is in [Proceed with ADR implementation plan](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-13T11-25-00-042Z_019ffade-4b0a-7000-aa03-117345b1ec12.jsonl), line **14**, at 13:26 Paris. This is through OpenRouter in OMP, before the larger switch on 19 August.

**19 August — the OMP model mix changes substantially.** Actual responses begin for `gpt-5.6-luna`, `gpt-5.6-terra` and `gpt-5.6-sol`. This is not evidence of a move to the standalone Codex app: `openai-codex` is the provider identifier inside these OMP logs. In [Consolidate visual roadmap next steps](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-19T15-03-57-669Z_01a01a8c-e9e5-7000-b4f4-e2ef45177be7.jsonl), line **1530**, you ask whether delegating each part to Luna is appropriate. This gives the role split a direct human source, beyond model counts.

**September — GPT families expand while OMP remains active.** Astra has a successful response on **5 September**; 6 Sol and 6 Luna on **23 September**; 6.1 Sol on **30 September**. Selection events can be earlier: Astra was selected on 4 September and 6 Sol on 21 September. The inventory below deliberately dates actual output. The September workflow decks explain your rationale for separating planning, execution, harness, IDE and reusable project instructions.

**6 September — standalone Codex CLI exists as an Orca worker.** The [feedback-site rollout](../../../../../.codex/sessions/2026/09/06/rollout-2026-09-06T16-08-49-01a0770c-e633-72c0-93c4-64abde9255af.jsonl) identifies `codex-tui` as originator, has an Orca dispatch brief, and records the turn model as **`luna`**, line **6**, before assistant output at line **23**. The local thread DB associates that rollout with `gpt-5.6-luna`; the rollout's literal identifier is an alias. This is a website worker in the SlopArena ecosystem, not proof the main game workflow switched harness that day.

**10 October, 19:33 — T3 installation is explicit.** In the [first T3 rollout](../../../../../.codex/sessions/2026/10/10/rollout-2026-10-10T19-33-07-01a126e0-286f-7511-a26f-f69c3df2339f.jsonl), line **11**, you say: “just installed t3 code, are skills loaded ? i usually use omp tbh”. Line **1** identifies the originator as T3 Code; line **8** selects `gpt-6.1-sol`; line **16** contains assistant output. This separates the T3 adoption date from an older imported thread's date. The [T3 application DB](../../../../../.t3/userdata/statev2.sqlite) has project records created on 10 October and completed native Codex runs from 11 October onward. It also records three completed `acpRegistry_omp` runs on **11 October**, with `openai-codex/gpt-6.1-sol`: OMP and T3 overlap.

### Exact model inventory from actual OMP responses

The inspected **1,716 transcript files** include parent sessions, nested agents and SlopArena-related workspace/web/master work. After deduplication, they contain **136,362 non-error assistant response records** across **23 provider/model pairs**. Tool-call responses count as responses. This is a description of the retained logs, not a count of human work hours, productivity, cost or independent tasks.

The period below is **first and last observed output**, not a continuous exclusive run. Dates are local to Paris. “Source” links to the first observed response; the CSV also preserves last-response paths and line numbers. Model names and aliases are kept exactly as recorded.

| Model ID | Provider in OMP | First → last output | Response records | First source / line |
| --- | --- | --- | ---: | --- |
| `deepseek-v4-flash` | `deepseek` | 24 Jun → 31 Aug | 37,639 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl), **7** |
| `deepseek-v4-pro` | `deepseek` | 26 Jun → 14 Aug | 6,630 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-26T11-51-18-140Z_019f03c5-1f7b-7000-8502-213e64a38289.jsonl), **6** |
| `google/gemini-3.5-flash` | `openrouter` | 2 Jul → 3 Jul | 325 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-02T09-58-21-736Z_019f2243-e128-7000-8fe1-b5c178191c6f.jsonl), **9** |
| `~anthropic/claude-haiku-latest` | `openrouter` | 11 Jul → 19 Aug | 50 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-11T14-46-31-407Z_019f51a4-eeef-7000-b5f2-f1517e23aaa1/MapAnimationPipeline.jsonl), **7** |
| `gemini-3.1-flash-lite` | `google-antigravity` | 30 Jul → 30 Jul | 59 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-30T14-47-15-688Z_019fb37e-6fe8-7000-a1ac-71779e756952.jsonl), **10** |
| `claude-opus-4-6` | `google-antigravity` | 31 Jul → 31 Jul | 44 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-31T08-41-56-707Z_019fb756-56e3-7000-8386-7f0a38b995af.jsonl), **8** |
| `nvidia/nemotron-3-ultra-550b-a55b:free` | `openrouter` | 1 Aug → 1 Aug | 3 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-01T12-55-04-947Z_019fbd64-73f3-7000-a0cb-9e8aa516d670.jsonl), **592** |
| `z-ai/glm-5.2` | `openrouter` | 1 Aug → 2 Aug | 1,625 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-01T14-08-30-248Z_019fbda7-ac28-7000-b0d8-9b116ef3b1f4.jsonl), **21** |
| `openai/gpt-5.6-luna-pro` | `openrouter` | 13 Aug → 13 Aug | 763 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-13T11-25-00-042Z_019ffade-4b0a-7000-aa03-117345b1ec12.jsonl), **14** |
| `gpt-5.6-luna` | `openai-codex` | 19 Aug → 25 Sep | 35,351 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-19T08-48-38-335Z_01a01935-4bbf-7000-911e-ae778f8e6138.jsonl), **287** |
| `gpt-5.6-terra` | `openai-codex` | 19 Aug → 11 Sep | 938 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-19T12-26-45-256Z_01a019fc-fc88-7000-b6e3-53d20931f9c3.jsonl), **12** |
| `gpt-5.6-sol` | `openai-codex` | 19 Aug → 10 Sep | 2,763 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-19T14-37-22-387Z_01a01a74-9253-7000-ae97-e81379f3c4d2.jsonl), **12** |
| `gemini-3.7-flash` | `google-antigravity` | 21 Aug → 30 Aug | 367 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-21T16-48-17-256Z_01a02539-2568-7000-b8ff-0d1a30944e1e.jsonl), **10** |
| `z-ai/glm-5.3-flash` | `openrouter` | 2 Sep → 21 Sep | 3,204 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-02T12-52-38-437Z_01a0622d-b7a5-71f5-8762-b1a7e94f424d.jsonl), **826** |
| `z-ai/glm-5.2:free` | `openrouter` | 4 Sep → 4 Sep | 7 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-04T20-48-28-540Z_01a06e2e-137c-73d0-b944-9f5e76302aa0.jsonl), **20** |
| `gpt-6-astra` | `openai-codex` | 5 Sep → 2 Oct | 6,008 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-05T12-16-07-685Z_01a0717f-5e05-734b-bea0-21a06c1cf8d5.jsonl), **7** |
| `gemini-3.8-flash` | `google-antigravity` | 7 Sep → 2 Oct | 393 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-06T22-07-16-170Z_01a078c2-eeca-758e-bea7-4bb199445dfa.jsonl), **8** |
| `deepseek/deepseek-v4-flash-0731` | `openrouter` | 10 Sep → 10 Sep | 183 | [Transcript](../../../../../.omp/agent/sessions/-orca-workspaces-SlopArena-complete-fightguys-character-presentation/2026-09-10T15-54-52-154Z_01a08c07-6d7a-763a-967f-b3c3e393e469.jsonl), **8** |
| `gpt-6-sol` | `openai-codex` | 23 Sep → 11 Oct | 14,747 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-21T21-13-57-107Z_01a0c5d1-8273-77b7-8dac-dc9e3d512b16.jsonl), **931** |
| `gpt-6-luna` | `openai-codex` | 23 Sep → 9 Oct | 13,293 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-23T18-26-38-097Z_01a0cf85-0b91-767d-baad-40bb7de7d505/PostureAudit.jsonl), **7** |
| `xiaomi/mimo-v2.6-pro` | `openrouter` | 26 Sep → 2 Oct | 99 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena-web/2026-09-26T12-25-00-111Z_01a0ddad-09cf-7742-8168-463f5f145ec0.jsonl), **8** |
| `xiaomi/mimo-v2.6-flash` | `openrouter` | 28 Sep → 1 Oct | 125 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-27T16-05-29-515Z_01a0e39d-432b-7670-8360-ded7baaa5233.jsonl), **2064** |
| `gpt-6.1-sol` | `openai-codex` | 30 Sep → 11 Oct | 11,746 | [Transcript](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-30T07-52-07-926Z_01a0f14c-a7f6-742d-8294-7e77ffa3c12a.jsonl), **16** |

In June and July, DeepSeek dominates the recorded OMP response volume. In August and September, 5.6 Luna has the largest single-model response count; in the inspected October OMP records, 6.1 Sol does. These comparisons concern OMP only: they do not measure Hermes, Warp, art generation or standalone Codex/T3 work on an equal basis.

`google-antigravity` here is an **OMP provider identifier**. Those July–October Gemini/Opus responses do not prove the Antigravity desktop editor was reopened. Likewise, `~anthropic/claude-haiku-latest` is a moving alias; the exact underlying version is unknown. Configuration-only experiments and selections that produced no non-error response are excluded from this table, including a few Qwen/stealth/Gemini selections.

## Warp: the parallel terminal/assistant lane

[Warp local database](../../../../../.local/state/warp-terminal/warp.sqlite) preserves **361 query records** in MoveBox/SlopArena context. This includes machine/tool setup as well as direct game work. Its `ai_queries.model_id` often says `auto-genius` or `auto-efficient`, which are routing choices rather than named underlying models. The matching conversation usage metadata exposes more specific labels.

For the video, the strongest early pointer is `ai_queries.id IN (3467,3468,3469)`, cwd **MoveBox**, stored **22 May**. You ask about local Ollama/Qwen/Claude Code while making the Godot game. The associated conversation **`bc7e8409-e252-42b9-a0d7-8cc0030d462b`** contains usage labels **Claude Opus 4.6** and **GPT-5.5 (extra high reasoning)**. That supports actual Warp activity and those models somewhere in the conversation; it does not prove Ollama, Qwen or Claude Code were successfully used for the game.

`ai_queries.id = 3471`, conversation **`25c51754-7dd5-464c-ac9a-5fdd962f9410`**, is a **23 May** request for the project architecture tree; its usage metadata names **Claude Sonnet 4.6**. The same conversation continues with machine-management requests, so its whole duration should not be counted as development. On **18 July**, queries **3928** and **3929** ask Warp to start and debug ComfyUI. Their separate conversation usage metadata includes Opus 4.8 and GPT-5.5, with Haiku also present in the first.

The inventory below reports the **earliest query window of a matching conversation**, and the end of the last matching window. Warp's timestamps here do not state a timezone. The labels are conversation-level usage, not exact response model IDs or a reliable per-model adoption timestamp. A multi-day first window must remain a range.

| Usage label in Warp | Earliest matching conversation window | Last matching window ends | First conversation ID |
| --- | --- | --- | --- |
| `Claude Haiku 4.5` | 22 May–25 May | 5 Sep | `c6ff38de-0143-4fda-afc0-39305751d94e` |
| `Claude Sonnet 4.6` | 22 May–25 May | 2 Jul | `c6ff38de-0143-4fda-afc0-39305751d94e` |
| `Claude Opus 4.6` | 22 May | 1 Jun | `bc7e8409-e252-42b9-a0d7-8cc0030d462b` |
| `GPT-5.5 (extra high reasoning)` | 22 May | 20 Jul | `bc7e8409-e252-42b9-a0d7-8cc0030d462b` |
| `GPT-5 Mini` | 31 May | 31 May | `428d43ab-b41c-4016-a574-2495bd8d60b1` |
| `Claude Opus 4.6 (max)` | 31 May | 2 Jun | `6735dafa-adff-4115-9458-22d8974ac754` |
| `GPT-4.1 Mini` | 1 Jun | 1 Jun | `08526a3a-7522-41cd-b158-0dc716e03489` |
| `Claude Opus 4.8` | 10 Jun | 30 Jul | `7e083bc0-d02a-4865-b064-d0aa552dcd13` |
| `GPT-5 Nano` | 17 Jun | 5 Sep | `e18d1b21-ddaa-4f23-9a38-e7310eac2139` |
| `GPT-5.3 Codex (medium reasoning)` | 17 Jun | 19 Jun | `e18d1b21-ddaa-4f23-9a38-e7310eac2139` |
| `Claude Opus 4.8 (max)` | 3 Jul | 3 Jul | `8332b8d1-e8a8-41ef-9223-e3aad452dcab` |
| `Claude Opus 5` | 1 Aug | 16 Aug | `e4fbbae7-276c-48d2-b4e3-8f1a7e8035e1` |
| `Claude Opus 5 (high)` | 23 Aug | 7 Sep | `cbbd407c-5b54-4b3a-bd4d-c376856e40d4` |
| `Claude Sonnet 5 (high)` | 23 Aug | 5 Sep | `cbbd407c-5b54-4b3a-bd4d-c376856e40d4` |

To inspect those pointers safely, select `ai_queries.id`, `conversation_id`, `start_ts`, `working_directory`, `model_id` and `output_status`; inspect only the `conversation_usage_metadata` subtree of the matching `agent_conversations.conversation_data`. Complete conversation JSON can contain authentication/session fields and is unnecessary for the script.

**Cursor is a smaller, less recoverable experiment.** A 22 May Warp request asks to install it, and [Cursor workspace metadata](../../../../../.config/Cursor/User/workspaceStorage/111d3a02859b58806f6256260939afcd/workspace.json) points at MoveBox. Preserved composer IDs exist, but no matching model/output records were recovered. You can say the project was opened in Cursor; the evidence does not identify a Cursor coding model. Similarly, mentions of Claude Code, Gemini CLI, Ollama or Qwen in prompts/decks are not enough to add confirmed usage eras.

## Art and animation lane

The strongest early service evidence is a **4 June 19:42 user statement** that they paid for **3DAIStudio** to generate characters and needed art direction/prompts. The same conversation attaches generated/rigged model files and discusses Blender, Mixamo and Cascadeur. The later asset filenames preserve multiple generator labels, but their modification times alone do not establish generation dates or whether the final game imported that exact export.

| Date anchor | Tool / model label | What can be established |
| --- | --- | --- |
| **4 June** user statements and files | **3DAIStudio**, `Tripo_P1_Game_Ready`, `Rigged_Prism` | User message **6156** names the paid service; message **6324** attaches two Prism-labelled rigged GLBs. Both inspected GLBs contain `asset.generator = "Tripo"` and a skin. This proves a rigged Tripo-exported artifact existed; “Prism” is the service/export label, not a proven distinct mesh backend. |
| **4–5 June** user workflow and files | **Blender**, **Mixamo**, **Cascadeur** | The user discusses choosing a Mixamo-compatible rig, opening the armature in Blender, failed Mixamo import and adding a Mixamo run animation. On 5 June they describe the desired 3DAIStudio → rigging/animation → Blender GLB → Godot flow. On **7 June 19:17**, message **10532** says they are working on the RMB in Cascadeur, split into startup/charge loop and attack. This is actual use beyond the downloaded Cascadeur archive. |
| **5 June** stored asset times; user supplies a file at 16:39 | `NanoBanana2_Edi`, `GPT_Image_2`, `Remesh_Hunyuan`, `Tripo_P1_Game_Ready` | Manki concept edits and multiple mesh/remesh alternatives survive. Message **6780** supplies the Hunyuan-labelled monkey GLB and asks about textures. Tripo-labelled mesh metadata names Tripo; the Hunyuan-labelled GLB's generator is Blender's exporter. Exact image-generation backend versions are not embedded in the inspected JPEG metadata. |
| **7 June, 17:59** | Hermes **`image_generate`** tool; **model unknown** | Actual assistant call **10515** asks for a cartoon monkey bomb image; tool result **10516** reports success. Arguments contain prompt/aspect ratio and result contains success/image, with no model identifier. Do not infer GPT Image or Imagen from the coding model or tool name. |
| **13 June** user statements and rabbit artifact | **Tripo**, **Mixamo**, **Blender** | Message **19731** explicitly mentions Tripo's PBR/no-PBR view; message **19744** discusses the generated model origin and possible Mixamo correction. The rabbit GLB names Tripo in `asset.generator`. The later discussion shows root-motion/scale cleanup in Blender rather than effortless one-click import. |
| **24–25 June** OMP experiment | **ComfyUI**, **HY-Motion** | The OMP setup session, user lines **358–848**, documents trying AI motion generation because manual Blender animation is difficult. Early errors name missing `Qwen3-8B` and `clip-vit-large-patch14` dependencies; an FBX output is later discussed as broken, but at line **848** you report a generated 5-second/150-frame motion and provide its retargeting debug. The experiment reaches output, then runs into orientation/height/retargeting issues. The surviving HY-Motion-named GLB contains one animation named `motion`, but its embedded generator is `THREE.GLTFExporter`. The failed dependency loads are not completed coding-model responses. |
| **July** stored asset times | `GPT_Image_2`, `Hunyuan_3D_v31`, **Mixamo**, `Prism_31` | FightGuy image refinements, a Hunyuan-labelled ninja GLB and `fightguy_mixamo.fbx` survive; Manki weapon/bomb/aerosol exports retain Prism labels. This is a set of art pipeline candidates/exports. Inspect the actual character asset catalog or import conversation before asserting a particular model became the shipped fighter. |
| **23 July** stored image time | `Imagen4` | A rabbit concept JPEG explicitly carries the export label. Its inspected metadata does not contain a model/version field; this is label evidence, not an independently authenticated Imagen API execution log. |
| **29–30 July** stored asset times | `Hunyuan_UV_Unfold`, `Rodin_25_Multi_Image` | Textured/UV and multi-image character exports survive. The Rodin-labelled GLB's generator is Blender's exporter. Model label, generation tool and final file exporter should remain distinct in narration. |
| **6–19 September** stored export times | `Tripo_P2_Game_Ready` | A muscular red-helmet character, sword and extracted sword/cape exports preserve later Tripo labels. The sword/cape GLBs name Tripo as exporter; the red-helmet GLB has been exported through Blender. These are useful visual examples of generating a model and then separate props. |
| **July–September** dated export filenames | **ChatGPT image exports** | Files explicitly named `ChatGPT Image Jul 18...`, `Aug 20...`, `Sep 19...`, and `Sep 24...` preserve art/store-reference work. Inspected PNG metadata gives a generic `image.png` title, without an exact generation model. The ChatGPT product label should not be converted automatically into a specific GPT Image version. |

### Exact art/animation artifact pointers

These links select representative surviving files. The long names themselves preserve the export labels.

- Early rigging: [Prism rigged GLB](../../../../../Downloads/sloparena/assets/Rigged_Model_Rigged_Prism_c5f47612.glb), [second Prism rigged GLB](../../../../../Downloads/sloparena/assets/Pixel_art_character_wearing_orange_and_black_attir_Rigged_Prism_d83e525e.glb). Both contain a skin and no animation clips in the inspected JSON chunk.
- Manki image edits: [NanoBanana2-labelled clothing edit](../../../../../Downloads/sloparena/monkey/Make_the_vest_and_shorts_a_deeper_nearbl_NanoBanana2_Edi_a0c78ef2c9cd.jpeg), [NanoBanana2-labelled scar edit](../../../../../Downloads/sloparena/monkey/Retirer_la_fine_ligne_de_cicatrice_le_lo_NanoBanana2_Edi_3587030bd532.jpeg), [GPT_Image_2-labelled reference sheet](../../../../../Downloads/sloparena/Study_all_uploaded_reference_images_and_GPT_Image_2_f633aa982606.jpeg).
- Manki meshes: [Hunyuan-labelled remesh](../../../../../Downloads/sloparena/monkey/Cartoonish_orange_monkey_with_mischievous_expressi_Remesh_Hunyuan_c72a6147.glb), [Tripo P1-labelled alternative](../../../../../Downloads/sloparena/monkey/1_Cartoonish_orange_monkey_with_mischievous_expressi_Tripo_P1_Game_Ready_ee3a1f03.glb).
- Bunny: [Tripo P1 rabbit](../../../../../Downloads/sloparena/rabbit/character_front_Anthropomorphic_rabbit_wearing_ninja_attire_Tripo_P1_Game_Ready_3dacf5af.glb), [Imagen4-labelled concept](../../../../../Downloads/sloparena/art/a_low_poly_agile_rabbit_humanoid_charact_Imagen4_7585f7be9485_3.jpeg).
- Motion experiment: [HY-Motion-named animation GLB](../../../../../Downloads/sloparena/new_manki/anims/hymotion_1782314338491.glb); the [OMP setup/animation conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl), lines 358, 449, 463, 514, 528 and 848, supplies the intent, dependency failures and generated-output troubleshooting. A Kimodo attempt at line 590 encounters a gated Llama dependency; that is attempted setup rather than proven completed Kimodo generation.
- FightGuy: [Hunyuan_3D_v31-labelled ninja](../../../../../Downloads/sloparena/fightguy/Render_the_main_subject_from_t_Stylized_ninja_character_with_intricate_tattoos_Hunyuan_3D_v31_e4cb0bf3.glb), [Mixamo FBX](../../../../../Downloads/fightguy_mixamo.fbx).
- More model alternatives: [Rodin_25_Multi_Image-labelled character](../../../../../Downloads/back_Character_model_wearing_red_and_black_attire_Rodin_25_Multi_Image_c4fdc75f.glb), [Hunyuan UV export](../../../../../Downloads/Colorful_polygonal_character_with_futuristic_acces_Hunyuan_UV_Unfold_27c753b0.fbx).
- Later props: [Prism_31-labelled monkey weapon](../../../../../Downloads/sloparena/dashboard_image_Colorful_toy_like_weapon_with_explosive_banana_pro_Prism_31_51a21609.glb), [Tripo P2 sword](../../../../../Downloads/sloparena/art/Cut_out_ONLY_the_sword_the_lon_Black_handled_double_edged_sword_with_a_pointed_bl_Tripo_P2_Game_Ready_6d3d3a7c.glb), [Tripo P2 cape](../../../../../Downloads/sloparena/art/Cut_out_ONLY_the_cape_the_larg_Abstract_black_cloak_with_geometric_shapes_Tripo_P2_Game_Ready_18d046bd.glb).
- Image-product provenance: [18 July ChatGPT image](../../../../../Downloads/sloparena/art/ChatGPT%20Image%20Jul%2018%2C%202026%2C%2011_07_31%20PM.png), [24 September ChatGPT image](../../../../../Downloads/sloparena/art/ChatGPT%20Image%20Sep%2024%2C%202026%2C%2001_14_16%20PM.png).

**Provenance trap worth showing:** a `Prism_31` filename can contain a Tripo-exported GLB; a `Hunyuan` or `Rodin` filename can contain a Blender-exported GLB. `asset.version = "2.0"` in these files means the **glTF format version**, not “version 2” of the AI model. This explains why export labels, GLB metadata and generation conversations should be combined.

## September: the developer's own explanation of the workflow

[ai_harness_omp.html](../../../../../Downloads/ai_harness_omp.html), slide 2, explicitly gives the reasons for OMP: low configuration, the needed models/sessions/skills/context, and ongoing updates. Slide 1 separates **harness** from **model**. Names such as Claude, GPT, Gemini, Codex, Cursor and Gemini CLI are examples on that slide, not proof each was used on SlopArena.

[ai_use_youtube_presentation.html](../../../../../Downloads/ai_use_youtube_presentation.html), slides 3–6 and 9–12, explains routing thinking and implementation, OMP as harness, Orca as IDE, and inspectable traces. The [nine-slide workflow deck](../../../../../Downloads/ai_development_workflow.html), slides 2–4 and 7–9, condenses the same idea. These files have stored mtimes on 7–8 September, without embedded authoring dates. They are prepared explanations/opinions, not measurements of model capability or proof the proposed video was published. Their full script pointers are in the [Downloads source guide](2026-10-14-downloads-source-guide.md).

## Memorable angles for a general audience

- **Your workflow developed alongside the game.** On 22 May you're designing networked MoveBox and also asking how to run local AI. The ambition and the tool search start together.
- **The thinker/worker split predates your later setup.** Pro delegates to three Flash sessions in Hermes on 14 June, months before the September presentation explains the same idea. Show the original audit next to that slide.
- **Changing model and changing tool are different decisions.** You carry DeepSeek from Hermes into OMP, then change the model mix while staying in OMP; later, 6.1 Sol appears in OMP before T3. That is a clear story even without a model-name roll call.
- **A concrete frustrating moment is more memorable than a ranking.** On 17 June you explicitly change to Sonnet after Flash gets confused by the hurtbox/model-size issue. Read your original message and describe what you needed from the assistant.
- **The art pipeline had plenty of cleanup.** Generated Manki/rabbit/FightGuy files lead into Blender, Mixamo, Cascadeur and retargeting problems. The funny visual beat is the gap between a promising generated model and a fighter that moves properly.

For a short recap, use the nine-row sequence above and choose two of these anecdotes. The smaller model trials can stay in the visual timeline/inventory for viewers who want detail.

## How this was checked

OMP model dates come from assistant `message.provider`/`message.model` on non-error, non-aborted outputs rather than just `model_change` events or session filenames. Identical response IDs are deduplicated across copied transcripts; records without an ID use a timestamp/content hash fallback. The search includes retained project-context session directories and nested workers, so generic setup work and some duplicated/resumed context may remain. It is not an exhaustive account census.

Hermes provides model settings at session level; Warp provides conversation usage labels; Codex provides rollout turn contexts and local thread metadata; T3 provides project/run records and originator metadata. These evidence types are kept separate in the CSV. Asset filenames and modification times provide weaker art provenance, and no exact first Antigravity generation model was recovered. Git co-author trailers are supporting attribution, not authoritative runtime model telemetry. Original logs and downloads were read without editing them.

## Hermes primary source pointers

Source database: [~/.hermes/state.db](../../../../../.hermes/state.db), opened read-only. The `sessions` table has `model`, `model_config`, timestamps, parent IDs and activity counters. The `messages` table has role/content/tool/timestamps but **no per-response model/provider field**. The inspected JSON export also has a top-level model and no model identifier on individual messages.

| Ref | Exact database/session pointer | Useful message(s) |
| --- | --- | --- |
| H1 | `sessions.id = 20260525_232352_7eb5ce`; [surviving JSON export](../../../../../.hermes/sessions/session_20260525_232352_7eb5ce.json) | `messages.id = 1`: just installed and asks for a Godot-game assistant; `27`: MoveBox sandbox. |
| H2 | `sessions.id = 20260610_192257_bf526a`, `model = deepseek-v4-pro` | `11228`: resume server development. |
| H3 | `sessions.id = 20260614_224528_a9fd61`; children `20260614_224715_20c1db`, `20260614_224716_b355bb`, `20260614_224716_1c7ee2` | `21930`: global SlopArena audit; child rows record Flash. |
| H4 | `sessions.id = 20260615_194654_0d2713` | `23506`: actual Godot MCP result names the SlopArena session/project. |
| H5 | `sessions.id = 20260615_233357_23d07a` | `23777`: asks whether Blender MCP is visible; `23781`: actual scene result. |
| H6 | `sessions.id = 20260617_172712_7bd098`; safe config fields record model `anthropic/claude-sonnet-4.6`, provider `openrouter` | `24422`–`24438`: hurtbox/model-size problem; `24432`: explicit switch after Flash became confused. |
| H7 | `sessions.id = 20260619_224606_8a14e9`; children `20260619_231424_e1d211`, `20260619_231424_c2eb9e`, `20260619_231424_61f79e` | `29867`/`29876`: ServerAbility plan; child rows record Flash. |
| Art / rigging | `sessions.id = 20260604_194101_a19379` | `6156`: paid 3DAIStudio; `6312`: rig choice; `6318`: Blender; `6324`: Prism GLBs; `6526`/`6591`: Mixamo run; `6629`: proposed Cascadeur/Blender pipeline; `6780`: Hunyuan-labelled Manki file. |
| Cascadeur / image tool | `sessions.id = 20260607_143904_d2433c` | `10515`/`10516`: successful image tool call/result with no model ID; `10532`: actual Cascadeur RMB work. |
| Rabbit / Tripo | `sessions.id = 20260613_123412_e0528d` | `19731`: Tripo rendering; `19744`: origin/Mixamo issue; `20198`–`20264`: Blender/root-motion/scale discussion. |

Compaction/resume sessions can copy old messages and assign fresh storage timestamps. Some topics also change within a session. The pointers above favor original user turns and substantive game work; raw session counts, cwd matches and copied summaries should not be presented as a clean model-usage census.

For safe model inspection, query selected rows rather than displaying complete configuration or authentication files:

```sql
SELECT id, title, model, parent_session_id,
       datetime(started_at, 'unixepoch') AS started_utc,
       api_call_count
FROM sessions
WHERE id IN ('20260525_232352_7eb5ce',
             '20260610_192257_bf526a',
             '20260617_172712_7bd098');
```

Antigravity's primary pointers are the [GDD metadata](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/gdd.md.metadata.json), [implementation metadata](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/implementation_plan.md.metadata.json), and [conversation_summaries.db](../../../../../.gemini/antigravity/conversation_summaries.db), row `conversation_id = 3dcc278c-adb8-4260-936a-fc0b307ccebb`. They establish activity and dates while leaving the model unknown.
