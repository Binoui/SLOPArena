# Shared Unity Editor coordination

## Runtime authority

Before any Unity interaction, read this protocol. Pipeline runtime ownership is the
single authority for the canonical host/project and Editor session. The old
`_bmad-output/editor-coordination/ownership.md` record is historical evidence, not
an assignment, lock or permission source. A terminal title, active pane, finished
conversation, absent reply or timeout does not prove availability or completion.

Resolve the approved project to its canonical path using the host's path tools.
A worktree or symlink is not Editor isolation; never substitute main for an invalid
explicit target. Run examples from the selected game checkout. Its usual Editor
project is `client/Unity`, and the shipped gateway is `scripts/unity-editor-gateway.ts`.

## Caller and commands

Use your own identity; do not copy an existing owner's identifiers. The gateway is
provider-agnostic: any agent can run it, and identity comes from one of these sources:

- **Orca:** supply your runtime-issued `ORCA_TERMINAL_HANDLE`. The gateway verifies
  that exact handle/incarnation with `orca-ide`; missing, mismatched or orphaned
  identity fails before claim. `ORCA_CLI_COMMAND` may point at your installed binary.
- **Launcher-configured pair (any assistant or runner):** the launcher/operator assigns
  `SLOPARENA_AGENT_ID` (1–100 ASCII letters/digits/dots/underscores/hyphens, starting
  with a letter/digit) and `SLOPARENA_AGENT_SESSION_ID` (a fresh UUID per launched
  session). Keep the pair stable within that session and distinct from other agents.
  The gateway records `agent:<id>`. This is configured identity, not independent
  harness attestation. Never reuse another session's pair.
- **Runtime-injected agent session:** without the sources above, the gateway uses the
  IDs an agent runtime injects into its own process:
  - **Codex:** `CODEX_THREAD_ID` and `CODEX_SESSION_ID` (both UUIDs) record
    `codex:<thread-id>`, or `t3:<thread-id>` inside the T3 harness (its
    `T3CODE_CLI_PATH` or `T3_MCP_BEARER_TOKEN` marker); the bearer is never identity.
  - **Claude Code:** a UUID `CLAUDE_CODE_SESSION_ID` records `claude:<session-id>`,
    with or without T3.

  A new runtime is one adapter in the gateway's `AGENT_RUNTIMES` list with tests.
  The authenticated Pipeline descriptor and lease token still govern command
  admission and release for every source.

An explicit Orca identity is verified first and never falls back after failure. An
explicit launcher pair comes next and applies in every harness. A runtime is selected
by its own variables; incomplete or invalid selected identity fails closed instead of
trying another source. Inside T3 without runtime IDs or a launcher pair, the gateway
fails before claim. No identity is guessed from the active pane.
The installed Unity CLI must be on PATH, or `UNITY_CLI_COMMAND` can name its executable.
Use Bun to run the gateway; Node 22.18+ can also run this TypeScript file directly.
Tool installation requires the existing project approval boundary.

Never infer the caller from an unqualified `orca-ide terminal show` or active pane,
copy another owner's identity, or send/check/ack mail as another terminal.

Before claiming, prepare and validate the source checkout and command plan locally:

```bash
bun scripts/unity-editor-gateway.ts \
  preflight --project-path client/Unity \
  --checkout-path . \
  --batch ./ready-import.json
```

`preflight` validates the explicit canonical Editor project, explicit game checkout
(including both Shared project files), and optional batch without claiming ownership
or issuing a Unity command. It reports tooling, source and Editor roots plus the
observed recovery state; missing or unreachable runtime status is `unknown`, never
assumed free. Preparation does not reserve the Editor. If source and Editor checkouts
are the same ordinary `client/Unity` layout, `--checkout-path` may be omitted and the
gateway derives the game root only after validating its Shared project files.
The Editor root must contain `Assets/` and `ProjectSettings/ProjectVersion.txt`.
A malformed or different-project Pipeline descriptor is rejected; only missing
discovery or unreachable status is an informational `unknown`.

The normal gateway prints this execution context once before attempting acquisition.
Execution context and ownership transition records on stderr use NDJSON; each status
record preserves the flat authoritative status fields. Stdout retains native command
results and the pretty-printed final status. Wait reports appear only for meaningful
ownership, operation, reason or state transitions; waiting remains interruptible.

```bash
# Orca only; other setups use their own identity source above.
export ORCA_TERMINAL_HANDLE='<your-own-runtime-issued-terminal-handle>'
# Observation only; no claim or Editor command.
bun scripts/unity-editor-gateway.ts \
  status --project-path client/Unity
# Your own bounded lease; waits automatically while another batch owns the Editor.
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity -- editor_status
```

Pass native command arguments after `--`, without a second `command` token.
The gateway invokes installed Unity CLI through a private authenticated forwarding
connection and enforces JSON/non-interactive routing. Do not supply connection,
proxy, detach or output flags, overwrite live discovery, use raw unowned commands,
or fall back to removed MCP routes.

For dependent commands, use `--batch <json-file>` with
`{"commands":[["editor_status"]],"restore":[]}`. Declare required restoration
in `restore`. Each agent waits, atomically claims its own lease, runs its own
commands/restoration, proves settlement, and releases automatically. Do not ask
another owner to insert ordinary verification into their runtime or retain a lease
for an entire conversation. Stdout is native-result JSON followed by final ownership
JSON; the exit code propagates transport, typed semantic, compile and native-test
failure. Visual claims still require inspecting requested captures.
Redirect gateway stdout to a file and read that file afterwards. Never pipe it into a reader
that can close early (`| head`, `| grep -m`, `| sed q`). On 2026-10-27, `--query capture | head`
killed the gateway with SIGPIPE before release. That left an orphaned `held` lease that
only `Recover Editor Lease...` could clear.

## Agent messaging

Use the gateway directly for routine discovery, compilation, inspection, scenarios,
captures and approved mode changes. Do not enumerate agents, ask who owns the Editor,
request permission from the current lease holder, or send progress/availability
messages. Busy ownership is handled by the gateway.

Message only for a concrete overlapping source write, an explicit task dependency
or handoff, or exceptional recovery that the runtime cannot resolve. Contact only
the affected writer or task owner; an Editor lease holder is not a coordinator.

## Availability, interruption and recovery

- Available means valid successful status with `state: free`, `settled: true`,
  `activeOperations: 0` and no owner. Observation is not acquisition; another
  waiter may win the atomic claim. Waiters are not promised FIFO ordering.
- `held`/`releasing` means wait in the gateway, not owner-inbox delegation.
  Only a definitively rejected claim can be retried after settled availability.
  If the holder's batch has outlived its own timeout while the Editor sits idle (no compiler
  process, nothing new in `Editor.log`), tell the user: a dialog or an unfocused Editor may be
  waiting. On 2026-10-22 a gateway auto-recompile hung that way for about 35 minutes inside another
  thread's capture batch, then cleared.
- Owner `human:owner` is the person at the Editor: batch `playtest` means they pressed Play,
  `hold` means `Window/Pipeline/Hold Editor for Me`. Wait in the gateway as for any lease; it
  ends when they press Stop or release the hold. Never ask them to stop. If your own tool timeout
  ends the wait, nothing was claimed: say the owner is playtesting and retry later. Their Play
  pressed during your batch is queued ahead of every agent and starts when your batch releases.
  Enter Play Mode with `editor_play`, not `EditorApplication.isPlaying` in `eval`: an
  unannounced Play during your lease counts as the owner's and is cancelled and queued.
- `blocked`, unknown/inconsistent status, missing discovery, ambiguous claim,
  process/session mismatch or unproven completion means stop, preserve evidence,
  and reconcile. Never replay a command or infer takeover from silence/timeouts.
- Native commands time out after 30 s in the CLI. Run test suites with
  `["run_tests", ..., "--async_tests", "true"]`; the gateway polls `test_status` for you. On
  2026-10-26 a synchronous Pipeline EditMode run (17 tests, ~6 min) timed out, blocked the lease
  and needed an Editor restart.
- Ctrl-C/SIGTERM before acquisition cancels that wait without executing or releasing
  another batch. After acquisition, settle actual work and declared restoration
  before release; caller timeout or cancellation alone is not completion.
- Reload retains the same lease/session for correlated completed or continuing work;
  uncertain work blocks recovery. An Editor-process restart invalidates old tokens.
  There is no automatic force-release or timeout takeover. Exceptional recovery requires
  independently proven completion or an explicitly authorized safe process restart.
- If a gateway has lost its token but the lease is still held and settled, a human may use
  `Window/Pipeline/Recover Editor Lease...` in Unity. It displays the owner/batch and
  requires confirmation. Recovery atomically rechecks the same session, lease and revision,
  rejects active command/host work or blocked state, and revokes the old token before
  normal acquisition resumes. Compilation/imports also prevent recovery. The action is
  unavailable to Pipeline command contexts and batchmode; agents never invoke it or
  automate its dialog. It does not cancel work, prove prior command success, or restore drafts.

## Source windows and preservation

Ownership serializes agent commands, not file writes, human input or automatic
Unity imports. A settled command lease is not proof of a stable source snapshot.
Honor any explicit source freeze already in force.

Routine gateway work does not require a source-window announcement or no-write
acknowledgments. Coordinate only when a known writer can change the inputs being
built/verified, copy a plugin, or trigger imports/reload during the operation, or
when acceptance explicitly requires a stable source snapshot. Unrelated agents
and isolated checkouts need no acknowledgment unless they copy into this Editor.

For that concrete conflict, agree the affected paths and a bounded no-write window
with the affected writer, record the saved-source marker (dirty saved files are
valid; do not substitute HEAD), and release the window after verification settles.
Prepare first; do not freeze writers while planning, waiting for ownership or doing
unrelated work. If a write invalidates the inputs or interrupts the scenario,
discard that proof and verify again after settlement; never claim snapshot stability
from ownership status alone.

Shared builds that copy the plugin are Editor-affecting even without another writer.
Acquire your own bounded lease before copying into an open project; coordinate
only affected concurrent writers as above. An explicit interactive `--hold` is
permitted for an approved external shell copy: perform only that bounded work,
let imports/compilation settle, then end the hold to release.
Do not nest a new gateway claim inside your own retained lease. Offline release/player
builds require a separately authorized exclusive window and a confirmed closed
project; never close another owner's Editor or start competing batchmode.

In eval scripts, save only the assets you changed (`AssetDatabase.SaveAssetIfDirty(asset)`).
`AssetDatabase.SaveAssets()` also writes every other dirty asset in the shared Editor,
including another thread's unsaved edits. When one of those assets is a package
dependency, the cook postprocessor then recooks the roster. On 2026-10-21 this flushed
Sir Bonk's plank material and recooked Bonk.

A lease does not expand task approval: cook, persistence, mode changes, installs,
commits, pushes, asset edits and process restart retain their existing permissions.
Record/preserve scene/mode, Lab draft/scenario/cursor/playback, camera and protected
files; reload may destroy transient presentation, so observe actual restoration.

Unity-related handoffs name any supplied planning/spec paths, approved checkout,
canonical Editor project and this protocol, requester/return identity, allowed
mutations, any explicit source freeze/conflict and observable acceptance. Separate
game sessions must load their repository instructions and Unity CLI reference;
do not copy a live ownership register into each worktree. Messaging follows the
Agent messaging section above, not a separate Editor-owner approval path.
