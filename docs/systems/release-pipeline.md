# Steam Playtest release

The current release target is the Windows Steam Playtest client and the VPS
Master/GameHost pair. Use `scripts/release.py` to prepare and resume a release.
It wraps `scripts/build-playtest-local.py` and **Release Steam Playtest**
(`release-playtest.yml`). Steam default activation remains a manual operator
step. There is no shared-Editor or IDE tool prerequisite for this release flow.

Read-only status and match-log requests use
[sloparena-ops](../../skills/sloparena-ops/SKILL.md) and
[the VPS log runbook](../../deploy/vps/README.md#operations-and-credentials).
[Testing and Verification](../testing.md) describes applicable checks;
its dated results are historical evidence, not a checklist to repeat.

## Release controller

The [approved controller design](../adr/0034-resumable-playtest-release-controller.md)
keeps one durable record at `build/playtest/<version>/candidate.json`. Prepare
from the selected game checkout; the Master checkout and approved source pins
are explicit. Omitting a revision selects that repository's published main;
it does not include dirty files or create/push a source commit.

```bash
python3 scripts/release.py prepare --version "$version" \
  --game-revision "$game_revision" \
  --master-root "$master_root" --master-revision "$master_revision"
python3 scripts/release.py run --version "$version"
```

`prepare` checks the protocol declarations from both exact source revisions,
Unity version/module availability and the allowed licensed input fingerprint.
It also requires one forward protocol bump when `src/Shared` changed since the
newest remote `playtest-client-*` tag; development changes never bump it.
`run` checks documentation/source completeness in its own clean checkout,
builds and verifies the client, publishes
it, dispatches a workflow with a durable request identity, follows that exact
run, downloads its receipts and approves its protected deployment when the
current account is permitted. It does not change canonical tracked files.
The selected game commit must contain the controller-compatible workflow and
producer; publish the tooling changes through the normal source process first.

Status reads the local record without waiting for a running controller. Refresh
reads CI and retains its sanitized receipts but does not approve a deployment:

```bash
python3 scripts/release.py status --version "$version"
python3 scripts/release.py status --version "$version" --refresh --json
python3 scripts/release.py resume --version "$version"
```

`run`/`resume` wait up to one hour for CI, stopping for a required reviewer or
Steam handoff. `--no-wait` returns after dispatch/reconciliation; later `resume`
continues. An interrupted local builder is not duplicated while its recorded
process is alive. An uncertain dispatch is reconciled by its request identity,
never blindly sent again. Completed build/publication/upload steps are retained.

After resolving the failure cause, `resume --retry-deploy` requests only the
failed deploy job. It requires a sanitized receipt proving restoration or a
pre-replacement refusal, a known prior active release and no failed migration.
The original client/upload and candidate are retained. Unknown transport/host
state requires reconciliation; a different source snapshot requires a new
candidate. Historical agent-written records remain evidence and are not
implicitly converted into controller authorization/state.

Retry uses GitHub's [job retry API](https://docs.github.com/en/rest/actions/workflow-runs#re-run-a-job-from-a-workflow-run).
The deploy job has no downstream upload job. A cached response for the old
attempt is not treated as the retry finishing; an uncertain retry stays pending
until reconciled. A definite HTTP rejection can be retried after its prerequisite
is fixed. Partial GitHub client publication can upload the missing verified asset
without recreating its tag or overwriting another asset.

The controller presents the exact BuildID/manifest and manual steps once the
compatible backend receipt succeeds. After the operator activates Steam:

```bash
python3 scripts/release.py verify-live --version "$version"
```

This uses installed SteamCMD with anonymous login and requires both default's
BuildID and depot manifest to match. It appends a separate observation, preserving
CI receipts. Credentials and confirmations stay in Steamworks/Steam.

Protocol 17 (three normals and the homing dash, ADR-0042) requires coordinated client, GameServer and Master versions. The
registration `catalogHash` still hashes only the schema-1 character catalog.
Each actual Match uses schema-2 content with an `arena` name and lowercase SHA-256
digest of the exact baked `.arena` bytes. Its `contentHash` binds that record and
the characters. GameServer returns `actualCatalogHash` separately; Master validates
both hashes and the requested arena before forwarding the content unchanged.
Clients verify their local arena bytes before prediction or Ready on Steam and
development UDP. Include a mismatched arena rejection in the protocol acceptance
pass; offline tests do not establish two-account Steam acceptance.

For startup, protocol or netcode changes, use two updated Steam accounts to
exercise countdown, match and rematch. Capture MasterConsole diagnostics. Record
the exercised result against the exact uploaded BuildID:

```bash
python3 scripts/release.py record-player-test --version "$version" \
  --outcome passed --build-id "$build_id" --countdown-passed \
  --match-id "$match_id" --rematch-id "$rematch_id" \
  --diagnostics "$match_diagnostics" --diagnostics "$rematch_diagnostics"
```

Use `--outcome failed --notes "observed failure"` for a failed player test.
Diagnostic exports are optional retained evidence; the record explicitly says
whether they were supplied. Deployment and Steam activation never mark player
acceptance passed. The CLI does not commit, push, install, activate Steam or
deploy the owner console. The remaining sections describe its maintained
primitives and the manual fallback.

### Timings and opt-in import reuse

`status` reports durable elapsed time for controller attempts, producer stages and
native cook/player work. Completed builds retain `timings.json` and
`native-timings.json`; GitHub job durations and queue time are separate from
Steam operator wait. Polling/resume preserves starts, and retries have separate
attempts. Historical records without measured starts do not acquire invented
durations. Native BuildReport steps can overlap and must not be summed.

Cold builds remain the default. To opt into the release-owned local cache:

```bash
python3 scripts/release.py prepare --version "$version" \
  --game-revision "$game_revision" \
  --master-root "$master_root" --master-revision "$master_revision" \
  --use-build-cache
python3 scripts/release.py run --version "$version"
```

With no compatible snapshot, the first build imports cold and seeds the cache
after successful verification and packaging. Subsequent compatible builds reuse
it. The choice is pinned when preparing the candidate; omitting the flag keeps
the build cold. Cache reuse shortens local import/build work; CI, upload and
manual Steam activation remain separate stages.

The selected published source must include the updated producer. The cache lives
under ignored `build/.playtest-import-cache/`, is private to the operator, copies
settled Library snapshots into independent projects, verifies snapshot bytes,
and keeps at most two compatibility snapshots. Unity version, host/target and the
`Packages` and `ProjectSettings` hashes determine compatibility. Source files do
not: after an ordinary commit or a changed local-only input, the build restores the
snapshot and Unity reimports each changed file and its dependents. Each snapshot
records its local-only inputs, and the attempt's `cache.json` lists the ones added,
removed or changed since. Invalid snapshots fall back to cold import. Every hit still
runs native package, catalog and player verification.

For accepted character changes, a valid authoring cook is local evidence. Its
advisory status and inspect provenance now identify the cook target and Unity
version; status also records package manifest/lock hashes. A warm Windows Editor
is not proof that a fresh Windows import reproduces its artifact fingerprints.
For character or cook changes, verify the committed cooks independently before
building a player:

```bash
python3 scripts/release.py certify-cook --version "$version"
```

Certification always imports cold, produces no player/archive, retains the native
report and licensed input fingerprint, and leaves the release build pending. With
the opt-in cache prepared, a successful certification seeds a settled snapshot
for the subsequent build. A failure retains the dependency report; use `cook`
below to produce a reviewable correction. A different source or licensed input
snapshot still requires preparation of a new candidate. Successful full builds
also retain `cook-verification.json` and `cook-certification.json`.
Routine releases already verify committed cooks during the build; they do not
need a separate `certify-cook` run.

The [timing/cache decision](../adr/0035-release-timing-and-import-cache.md)
records the approval and default. Native benchmark results are documented in
[the import investigation](../testing/release-import-cache.md).

### Cold Windows cook diagnostics

Every release verifies accepted cooks in its isolated Win64 project before player
compilation, including when import caching is enabled. Diagnostic cooks always
import cold. A failure retains a complete roster report, expected/observed hashes,
and dependency changes when a matching advisory local cook snapshot is available.
Local Library status is diagnostic evidence, not a replacement for verification.

```bash
python3 scripts/release.py cook --version "$version"
```

This performs a native cook/verification in another disposable Windows project,
retains corrected payloads and `cook-correction.patch`, and builds/uploads no
player. Review the correction, follow the normal cook/source commit and push
boundary, then prepare a new version from the corrected published SHA. The
subsequent independent release verification must reproduce those exact accepted
artifacts. Do not silently adopt the diagnostic output into an existing release.

## 1. Select the source

Use an approved game commit published on `main`, an approved full Master SHA
reachable from its `main`, and a new `0.2.0-playtest.N` version. Reuse existing
release/source approvals; ask only for a missing authorization. Commits, pushes
and installations still require authorization. Preserve unrelated edits rather
than resetting or pulling a dirty checkout.

Run the following from the canonical game checkout, filling in the three
release values. The placeholder `N` must be replaced with a new numeric suffix.
A separate clean worktree keeps ongoing canonical work out of the release:

```bash
version=0.2.0-playtest.N
game_revision='<approved-full-game-SHA>'
master_revision='<approved-full-Master-SHA>'
game_root="$(git rev-parse --show-toplevel)"
local_project="$game_root/client/Unity"
build_checkout="$(mktemp -d /tmp/sloparena-release.XXXXXX)"
git worktree add --detach "$build_checkout" "$game_revision"
cd "$build_checkout"
```

An existing clean checkout at the approved SHA is also sufficient. The builder
requires clean source and `HEAD` equal to `--source-revision`. It refuses an
existing publish-ready archive; do not overwrite an old artifact or move its tag.
If game `main` changes before workflow dispatch, select/build the new approved
snapshot with a new version; the action rejects a client for a different commit.
MasterConsole is a separate application/repository with an independent container
image and authenticated gateway. It runs on alfred and is rebuilt by the Slop
Factory deploy; see [owner console](owner-console.md). Local mode
remains available for development. This workflow builds game and Master backends;
it does not deploy the console or a website.

## 2. Build the Windows client locally

```bash
python3 scripts/build-playtest-local.py --version "$version" \
  --source-revision "$game_revision" \
  --endpoint https://master-test.sloparena.barakaslurp.fr \
  --local-unity-project "$local_project"
```

The wrapper uses the installed licensed Unity `6000.0.78f1`, a fresh isolated
project, and allowed gitignored licensed assets/packages from `local_project`
(including Animancer). It compiles Shared only in the isolated project and
selects Windows before asset import. The canonical Editor can stay open; the
wrapper does not copy its tracked edits, mutate its plugin/staging, or require
an Editor lease or another build approval. It neither installs Unity/modules
nor activates a license. A missing editor/module/license/dependency is a real
prerequisite to resolve, not a reason to close somebody else's Editor.

The producer checks accepted cooked-package freshness, exact roster payloads,
persisted generated animation catalogs and their rig/clip/presentation bindings.
It verifies the compiled endpoint, source/version, Shared/native library bytes,
credits/licenses and excluded private/dev files. Failed builds retain diagnostic
logs but produce no publish-ready archive. These checks do not prove a player
launch or two-account match.

| Local output | Purpose |
| --- | --- |
| `build/playtest/<version>/SLOPArena-<version>.tar.gz` | The only client asset to publish to GitHub |
| `build/playtest/<version>/<attempt>/client.json` | Packaged identity and build result |
| `build/playtest/<version>/<attempt>/handoff.json` | Actual output/log paths and settlement |
| `build/playtest/<version>/<attempt>/release/SLOPArena-<version>/` | Packaged Windows player for local inspection/testing |
| Remaining attempt logs/dependency receipts | Private local diagnostic evidence; keep out of release assets |

Package or toolchain changes can invalidate accepted cook provenance. Resolve
freshness through [the package cook/admission workflow](../characters/adding-a-new-character.md)
before selecting the release SHA. Compare native dependency receipts when an
isolated Windows import disagrees with an old Editor cache; do not relabel an old
cook, bypass admission, or silently ship uncommitted recook output.

## 3. Publish the client and dispatch one release

After the build succeeds:

```bash
gh release create "playtest-client-$version" \
  "build/playtest/$version/SLOPArena-$version.tar.gz" \
  --target "$game_revision" --prerelease --latest=false \
  --title "Local Playtest client $version" \
  --notes "Verified Windows client from game commit $game_revision. Steam activation follows VPS deployment."
gh workflow run release-playtest.yml --ref main \
  -f "version=$version" -f "master_revision=$master_revision"
```

The client tag must point to the dispatched game `main` SHA. The coordinator
checks the GitHub archive digest, safe archive members and actual packaged
content against that source. It then tests/builds/publishes immutable GameHost,
Master and migration images, uploads Playtest app **5325920** / depot **5325921**,
and assembles the exact public candidate. CI requires a successful BuildID and
matching new depot manifest; upload does not make the build live.

Follow the exact dispatched run, not merely the newest run in the repository:

```bash
gh run view "$run_id" --repo Binoui/SlopArena
gh run download "$run_id" --repo Binoui/SlopArena \
  --name playtest-upload-receipts --dir "build/playtest/$version/ci/upload"
```

Set `run_id` to that run's numeric ID. Approve the protected `playtest-vps` job
using the already authorized release scope if the account/environment policy
allows it. If GitHub requires a different reviewer, that reviewer completes the
protected job; do not ask the user to reapprove the release itself.

## 4. Let CI deploy and retain the receipt

The protected job opens the existing WireGuard route and sends the bounded
candidate through pinned-host SSH to the fixed restricted VPS receiver. The
receiver runs a fresh off-host backup, validates its receipt against the active
release, and supplies the private runtime configuration. It copies the active
Master environment into an immutable release-specific private file, changing
only `Room__CatalogHash` and the `Room__AdmittedArenas__*` entries, which it
rewrites from the candidate's `admitted_arenas`: the Stage Select roster
(`client/Unity/Assets/Resources/Stages/roster.json`) of the release commit. Old
settings remain available for compatible recovery. Routine CI releases
therefore need no manual admission-environment edit. A receiver installed
before 2026-10-17 rejects the roster field; update the installed helpers once
([steps](../../deploy/vps/README.md#updating-installed-helpers)) after the change
merges and before the first release that carries it.

The guarded `release.py` enforces fresh zero-active-match registration under the
release lock immediately before replacing existing Master/GameHost writers.
Stale/unavailable registration is not zero. Digest/source/profile/schema checks,
readiness and fresh post-deploy registration are built in. CI independently
checks public HTTPS `/ready` and tears down its private runner tunnel on exit.
Do not repeat these steps manually beside a running release or restart services
to make an investigation easier.

`migration_from` is empty by default. A schema upgrade requires the approved
current migration and target; the guarded path takes a pre-migration dump and
runs the pinned bundle with writers stopped. An uploaded backup does not prove
that it can be restored. Do not restore an incompatible old application/schema.

After deploy succeeds:

```bash
gh run download "$run_id" --repo Binoui/SlopArena \
  --name playtest-release-receipt --dir "build/playtest/$version/ci/deployment"
curl --fail --silent --show-error --max-time 10 \
  https://master-test.sloparena.barakaslurp.fr/ready
```

`release.json` contains the candidate, observed deployment result, backup/schema,
service readiness, registration, exact Steam identity and the promotion URL.
Require that successful compatible backend result before activating Steam.
`/health`, a published image or an uploaded client alone is insufficient.

Updated restricted receivers also return `release.json` on a failed operation:
the allowlisted failure stage/reason, prior active release and rollback outcome
are available through the existing CI channel. Private commands, raw exceptions
and credentials stay on the host. An old receiver, malformed response or failed
SSH transport produces an explicitly ambiguous receipt and cannot authorize a
retry. Updating the installed root-owned receiver remains a separate operator
maintenance action through the VPS runbook.

## 5. Activate Steam default by hand

Open [Steamworks Builds](https://partner.steamgames.com/apps/builds/5325920).
For the **exact BuildID in the upload/deployment receipt**:

1. Select **default**, then **Preview Change**.
2. Verify app/depot and the receipt's manifest, then **Set Build Live Now**.
3. Accept the browser's confirmation dialog, if shown, and complete any mobile
   confirmation in Steam. Keep passwords and codes in that interface.
4. Reload/observe the branch table: default must show that BuildID and manifest.
   An open dialog, button click or mobile prompt is not successful activation.

This step can use the operator's normal browser. Preview automation is optional;
if it times out, hand over the exact BuildID and continue independent work.
Do not repeatedly retry automation, ask for Steam codes, or add another login
when the operator is already authenticated.

An optional independent observation uses the installed SteamCMD without builder
credentials. Its app-info branch named `public` corresponds to Steamworks
`default`; check `depots.branches.public.buildid` and depot `5325921`'s public
manifest `gid`:

```bash
umask 077
steamcmd +login anonymous +app_info_update 1 +app_info_print 5325920 +quit \
  > "build/playtest/$version/steam-appinfo.log" 2>&1
```

If metadata still shows the old build, first check for an unaccepted browser or
mobile confirmation, then allow propagation and read again. Record the bounded
branch/manifest result or a cropped Steamworks proof, excluding unrelated rows,
account details and passwords. Do not publish the raw SteamCMD session log.

## Completion and resuming

Keep local `build/playtest/<version>/candidate.json` with source/image pins,
package/catalog identity, uploaded BuildID/manifest, backend result, observed
live branch and any unexercised player checks. CI's `steamDefaultActivated: false`
means the workflow did not activate Steam; retain that original receipt and
record the later manual activation separately. Report upload, deploy and live
activation independently.

Both players update through Steam, then exercise a match and rematch. Capture
new match diagnostics in MasterConsole. Backend tests and a successful Windows
build do not prove the reported lag feels better; state whether real gameplay
was exercised. Do not rerun unrelated historical checks to finish the release.

Copy the retained archive, attempt evidence and CI receipts out before removing
a task-owned build worktree. Remove only settled task-owned staging/helpers and
privileged sessions. Keep the existing private console/tunnel for monitoring.
On ambiguous deployment, inspect private host status/events before another
mutation. A release ID is immutable: rerunning the whole pipeline can produce
a different Steam BuildID/digest and be rejected; resume the existing candidate
instead of overwriting it.

## One-time CI configuration

No self-hosted runner is required. Create the environments **before** provisioning
their secrets. Select **Selected branches and tags** with exactly one **Branch**
rule named `main` in each environment, and require an authorized reviewer for
`playtest-vps`. The preflight rejects absent environments, wildcard/tag rules,
additional allowed branches or missing VPS reviewers before publishing images.
Keep workflow/source changes behind the repository's normal review.
For a private Master repository, enable reusable-workflow access from the game
repository in Master's Actions settings.

| Scope | Name | Value/authority |
|---|---|---|
| Game repository secret | `MASTER_RELEASE_TOKEN` | Binoui-owned token able to read Master source and publish its GHCR packages; a classic PAT needs `repo` for private checkout and `write:packages`. Keep it out of artifacts. |
| `playtest-build` variable | `PLAYTEST_MASTER_URL` | Approved HTTPS Master endpoint, currently `https://master-test.sloparena.barakaslurp.fr`; must equal the compiled client endpoint and private VPS test host. |
| `playtest-build` variable | `STEAM_BUILD_USER` | Dedicated Steam account with permission to upload only this Playtest app/depot. |
| `playtest-build` secret | `STEAM_CONFIG_VDF` | Single-line base64 (`base64 -w0` on Linux) of the builder account's authenticated SteamCMD `config.vdf`; obtain/renew it in a private local SteamCMD session with Steam Guard, not in chat or CI logs. |
| `playtest-vps` variables | `PLAYTEST_SSH_HOST`, `PLAYTEST_SSH_PORT`, `PLAYTEST_SSH_USER` | Private WireGuard endpoint `10.253.253.1`, port `2223`, restricted account `sloparena-ci`. |
| `playtest-vps` secrets | `PLAYTEST_SSH_PRIVATE_KEY`, `PLAYTEST_SSH_KNOWN_HOSTS` | Dedicated noninteractive CI key and out-of-band-verified OpenSSH host-key lines (including `[host]:port` for a nondefault port). Never disable host-key checking. |
| `playtest-vps` secret | `PLAYTEST_WIREGUARD_CONFIG` | Raw dedicated client WireGuard configuration, with private peer key and only `10.253.253.1/32` allowed. Keep it out of logs/artifacts; the workflow creates/removes it privately on the hosted runner. |

Install the forced-command account and root-owned helper using the
[VPS CI setup](../../deploy/vps/README.md#restricted-ci-release-account).
The existing VPS profile, private files, registry pull authorization, firewall
policy and successful off-host backup service must already be configured.
The protected deployment job opens a dedicated WireGuard link, connects only to
private CI SSH, then tears the tunnel down even on failure. The host admits the
encrypted IPv4 UDP endpoint, restricts tunnel INPUT to the pinned CI SSH peer,
and denies forwarding into Docker/backend networks. Do not widen management SSH,
expose CI SSH publicly, enable a new provider default-deny policy, or rerun
bootstrap merely to accommodate CI.

Receipts are sanitized; Steam session files, raw SteamCMD output, private VPS
environment files and Docker logs are not uploaded. A failed/ambiguous deploy is
not success: inspect private host status/events before another action. Use a new
workflow run for a new candidate. A release ID is immutable, so rerunning the full
pipeline after deployment can produce a different Steam BuildID/image digest and
be rejected instead of overwriting the old release.

Use **Check Playtest VPS** (`check-playtest-vps.yml`) on `main` when diagnosing CI access to verify the tunnel,
pinned SSH key and candidate/shell/TTY/forwarding refusals independently of a
release. It uses the same protected `playtest-vps` approval and submits no valid
candidate, builds no client, uploads nothing to Steam and performs no deployment.
The TTY probe requires OpenSSH's explicit PTY-refusal message and exit 255:
forced PTY refusal aborts the client before the forced command can return 126.

## Manual backend fallback

Use this only when the normal CI path is unavailable and an operator has the
necessary authenticated sudo session. It does not run alongside the coordinator.
Detailed configuration, bootstrap, backup and guarded recovery commands live in
[the VPS runbook](../../deploy/vps/README.md). The installed helper is
`/opt/sloparena/deploy/vps/release.py`; normal agent SSH grants only the two
[bounded log commands](../../deploy/vps/README.md#noninteractive-agent-log-access),
not passwordless status/deploy/rollback.

Publish the compatible images through the game `gameserver-image.yml` and Master
`container-images.yml` workflows using one approved `release_id`. Retain their
exact digests/source/schema receipts. Prepare a private release record from
`deploy/vps/release.example.json`. If the catalog changes, make a new private
Master environment file and point the record to it, preserving the old file;
manual `release.py` does not perform the CI receiver's environment copy.

Require a fresh successful off-host backup before manual replacement. The CLI
enforces the fresh zero-match gate when replacing an active release. It also
backs up before migrations; these are different backup proofs. Invoke guarded
deploy/recovery and verify the resulting readiness/registration through the
runbook. Keep all configuration and privileged authentication out of chat/Git.
For a manual Steam upload, `scripts/steam-playtest.sh <version>` previews and
`--upload` uploads using the installed SteamCMD and authenticated builder account;
both leave branch activation to the operator. Do not run that upload again for
a client already uploaded by CI.

## Alternate targets

The older ZIP/GitHub and Alfred paths are explicit alternatives. For ZIP,
`scripts/build-release.sh <0.2.0-demo.N>` runs against the selected Unity project,
which must be separately authorized and closed. It produces the Windows ZIP
with a bundled development server and `build/minipc/`; it does not deploy a host.
The script restores saved settings/staging on normal success/failure. After a
forced stop, recover only that run's saved inputs once it has settled; preserve
unrelated changes. GitHub ZIP publication, when requested, uses
[the release notes template](../release/RELEASE_NOTES.template.md).

[Production Hosting](production-hosting.md) records the historical Alfred home
setup. Its rsync, router and raw-UDP procedures are not the Steam/VPS release
flow. Use it only when Alfred is explicitly selected and its current homelab
configuration has been checked.

## Builder maintenance and CI boundaries

For a focused generated-catalog regression, inject
`scripts/ci/PlaytestReleaseBuilder.cs` and
`scripts/ci/PlaytestReleaseBuilderSelfTest.cs` into a disposable,
dependency-complete Editor project's `Assets/Editor`. In Edit Mode invoke
`PlaytestReleaseBuilderSelfTest.Run("/absolute/path/to/client.bindings")` for
each admitted roster package through the applicable Unity command route. The
self-test removes its synthetic assets in `finally`; it checks persisted catalog
and binding refusal paths, not a packaged player or Steam match.

Game `ci.yml` checks docs and Shared/Server. Independent image workflows and
Master `build.yml` do not deploy a host or activate Steam. The coordinated
`release-playtest.yml` publishes images, uploads the client and deploys the VPS,
but still leaves default activation manual. Website publication is separate.
