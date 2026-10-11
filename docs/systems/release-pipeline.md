# Release Pipeline — Steam Playtest + VPS

The default release target is the Windows **Steam Playtest** client plus the
restricted VPS Master/GameHost pair, driven by **Release Steam Playtest**
(`.github/workflows/release-playtest.yml`). Steam default-build activation remains
manual. ZIP/GitHub and Alfred home deployment are explicit alternatives.

The Actions artifacts retain `candidate-public.json`, client/upload receipts, and
`release.json`; the job summary links the exact Steam BuildID to promote. Manual
fallback receipts remain under `build/playtest/<version>/`. Historical release
records in [Testing and Verification](../testing.md) are not proof of today's
branch, service readiness, registration or match count.

## GitHub Actions — routine release

Push the approved source to game `main`, then build the client on the licensed
workstation from that exact clean commit:

```bash
version=0.2.0-playtest.7
revision="$(git rev-parse HEAD)"
python3 scripts/build-playtest-local.py --version "$version" \
  --source-revision "$revision" --endpoint https://master-test.sloparena.barakaslurp.fr
gh release create "playtest-client-$version" \
  "build/playtest/$version/SlopArena-$version.tar.gz" \
  --target "$revision" --prerelease --latest=false \
  --title "Local Playtest client $version" \
  --notes "Verified Windows client from game commit $revision. Not a Steam activation or VPS deployment."
gh workflow run release-playtest.yml --ref main -f "version=$version"
```

The local wrapper creates a fresh isolated checkout and runs the installed Unity
`6000.0.78f1` using the workstation's existing Hub activation. It does not activate
a license, copy into the open canonical Editor, or require Unity credentials in
GitHub. The launcher selects Windows before importing assets so platform-sensitive
dependency hashes match the Windows cook. The producer verifies authoring freshness and package admission, then
regenerates each roster character's Unity animation catalog from its verified
staged `client.bindings` before building the player. The generated catalogs live
under `Assets/Resources/Generated/CharacterPackages/`; they are required client
assets, separate from the four cooked simulation payloads. The build rejects
missing, duplicate, wrong-package, source-hash-mismatched or rigless persisted
catalogs. Generation also rejects unresolved rig, clip, weapon and presentation
references. Editor development catalogs cannot satisfy this persisted-asset gate.
Endpoint, version, native-library, exact cooked payload and attribution checks
remain mandatory.
`scripts/ci/PlaytestReleaseBuilder.cs` remains outside the canonical asset tree;
it is injected only into the isolated build project. Failed builds produce no
publish-ready archive; their local logs are retained for diagnosis.

The workstation must also contain the required licensed, gitignored Unity inputs
(including `Packages/com.kybernetik.animancer`). The wrapper copies ignored local
assets/packages into the isolated project, excluding generated Shared/cook/temp
outputs, hidden files and debug logs. It never symlinks to the open Editor or
replaces committed source. Dependency hashes stay in private local evidence;
restricted source assets and build logs are not GitHub release assets.
When launching from a separate clean checkout while canonical work is dirty,
pass `--local-unity-project /absolute/path/to/SlopArena/client/Unity` to select
those read-only local dependencies explicitly. Uncommitted tracked edits are
not copied or shipped.

For a focused catalog regression without building a player, inject the producer
and `scripts/ci/PlaytestReleaseBuilderSelfTest.cs` into `Assets/Editor` of a
disposable, dependency-complete Editor checkout. In Edit Mode, invoke
`PlaytestReleaseBuilderSelfTest.Run("/absolute/path/to/client.bindings")` for an
admitted package, using the approved gateway's `eval` command. Repeat
for each roster package. The self-test creates only a unique synthetic catalog
and removes its assets in `finally`; it exercises generation, bound rig/source
resolution, missing/stale/rigless/duplicate refusal and unresolved-binding
failure without replacing a valid catalog. It does not prove player packaging.

Package/toolchain input changes can invalidate accepted cook provenance even
when gameplay and poses are unchanged. Resolve freshness through the approved
[package cook/verify/admission workflow](../characters/adding-a-new-character.md)
and publish the updated package/roster pins **before** selecting the client source
commit. The build never silently recooks, relabels or bypasses that acceptance.
Compare native cook dependency receipts when a fresh import disagrees with a
previous Editor cache. Check runtime definitions and baked pose bytes separately
from provenance: unchanged gameplay/poses do not make stale `client.bindings`
source hashes or package/roster pins acceptable. After an approved native recook,
verify again in a fresh isolated release import before uploading.

The action first requires the matching `playtest-client-<version>` prerelease
tag to point to the **dispatched main commit**. It checks the archive's GitHub
SHA-256 digest, refuses unsafe or unexpected archive members and verifies the
actual packaged client against that source before building backend images.
Pushing another commit requires a new matching local client; do not reuse an old
binary by relabeling its receipt or moving an existing artifact tag.
Use a new version and client-asset tag for each new source snapshot.
Client-asset prereleases do not trigger backend image publication themselves.
The run pins a full Master commit reachable from Master `main`; optional
`master_revision` selects an approved full SHA, not a branch/tag.

Only after client verification and all backend builds pass does SteamCMD upload depot **5325921** for app
**5325920**. A zero process exit alone is insufficient: the coordinator requires
one successful app BuildID, one new depot manifest, and matching generated depot
metadata. Steam upload does **not** activate the default build.

The protected `playtest-vps` job sends a bounded public candidate over pinned-host
SSH to one forced command. The VPS supplies private runtime configuration, checks
fresh successful off-host backup evidence and fresh zero-match registration under
the release lock, verifies immutable digests/source labels, and uses the existing
guarded migration/deploy/compatible-recovery path. After readiness and registration
pass, CI records the public receipt and independently checks HTTPS `/ready`.
`migration_from` is empty by default; a schema upgrade requires the operator to
explicitly name the current migration and approve the target migration. It does
not authorize incompatible rollback or destructive database recovery.

**Final human action:** open the Steamworks builds link in the successful job
summary and set the recorded **BuildID** live on the Playtest default branch.
No workflow code activates a Steam branch. Valve's minimum builder permissions
are broader than upload-only; keep default promotion a separate operator action.
Then exercise the packaged-client match/rematch acceptance described below.

### One-time configuration

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

Use the manual **Check Playtest VPS** workflow on `main` to verify the tunnel,
pinned SSH key and candidate/shell/TTY/forwarding refusals independently of a
release. It uses the same protected `playtest-vps` approval and submits no valid
candidate, builds no client, uploads nothing to Steam and performs no deployment.
The TTY probe requires OpenSSH's explicit PTY-refusal message and exit 255:
forced PTY refusal aborts the client before the forced command can return 126.

The numbered sections below are the **manual fallback**. Do not repeat their
build/upload/deploy steps alongside a running CI release.


## 1. Choose source and release identities

Resolve the approved game checkout to its canonical path. An explicit worktree
wins; otherwise use the current game checkout or the workspace's `SlopArena/`
link. Keep the separate Master checkout and planning workspace distinct, load
each repository's instructions, and run commands with the appropriate checkout
as `cwd`. Do not switch/pull/reset a dirty checkout as release preflight.

Use client version `0.2.0-playtest.<n>` and one operator `release_id` across
GameHost, Master and migration image publication. Record full source revisions
for each repository; the version, image release ID, Steam BuildID and depot
manifest are different identities. Examples below assume `VERSION`, `RELEASE_ID`,
`GAME` and `WORKSPACE` have been set to those approved values/absolute paths.

Inspect saved-source and staged changes before building. Agree the bounded source
snapshot with the user; building a dirty checkout and labeling it only with HEAD
is not auditable. Commit/push/install require their own authorization. Coordinate
other source writers before Unity imports or plugin copies. A surprise write
invalidates the source proof.

Run applicable checks from [Testing and Verification](../testing.md); do not
copy historical test counts into a new result. Check the current cooked roster
and package freshness before packaging. The roster manifest owns package IDs,
not a second hand-written roster list. Raw authoring JSON, manual animation
configs and source skeletons are not runtime release inputs.

## 2. Build and verify the client

Follow [Unity CLI](../contributing/unity-cli.md) and the
[shared Editor coordination protocol](file:///home/binoui/Documents/projects/sloparena-workspace/docs/unity-editor-coordination.md).
For an open Editor, use the canonical gateway with your own runtime-issued
`ORCA_TERMINAL_HANDLE`; do not infer it from the active pane. A command lease
does not prevent automatic imports: coordinate a stable saved-source window.
Missing caller identity, blocked ownership or uncertain settlement stops the
operation. Never close someone else's Editor or start competing batchmode.

Acquire your own bounded interactive gateway hold before external Shared builds
that copy the plugin or staging mutations. Snapshot the existing settings and
StreamingAssets first, including ignored skeletons and metadata; finish the copy,
let imports/compilation settle, then release. Do not nest another gateway claim
inside a retained hold. See the protocol for interruption/restoration rules.

Set the approved client version in saved project settings. Stage the current
cooked roster and its package payloads into the client, preserving pre-existing
local staging for restoration. Steam needs the client cooked content; do not add
the ZIP workflow's bundled home server or raw authoring files just because that
alternate script stages them. Build through the native command:

```bash
bun "$WORKSPACE/scripts/unity-editor-gateway.ts" \
  --project-path "$GAME/client/Unity" -- build \
  --target StandaloneWindows64 \
  --outputPath "$GAME/build/release/SlopArena-$VERSION/SlopArena.exe"
bun "$WORKSPACE/scripts/unity-editor-gateway.ts" \
  --project-path "$GAME/client/Unity" -- build_status
```

Observe the build result/report, not only submission success. Before releasing
source staging, prove the build has settled. Restore the exact saved staging and
settings after it settles, under your own bounded hold when the Editor is open.
Preserve unrelated Unity re-serialization; never blanket-reset files from Git.

Verify the actual extracted build:

- Compare `SlopArena_Data/StreamingAssets/content-cooked/roster/manifest.json`
  and every roster-selected `manifest.json`, `character.runtime.json`, `poses.bin`
  and `client.bindings` byte-for-byte against the approved cooked snapshot.
- Load the packaged payloads through `CookedCharacterPackageLoader` and
  `MatchContentCatalogBuilder`. Require successful admission and compute the
  canonical `SteamMatchDescriptor.HashContent` over the serialized content-handle
  map. A file checksum of the roster alone is **not** this catalog hash.
- Check the compiled client Master endpoint against the selected HTTPS VPS;
  record Shared/player assembly and Steam native-library hashes. Do not reuse a
  prior DLL simply because its filename/version matches.
- Include `CREDITS.txt` from `CREDITS.md` and the required license/attribution
  files. Scan the release for credentials, local configuration, source skeletons,
  developer PDBs and Burst debug output. Inspect the upload mappings/exclusions.
- Record warnings/errors, source pins, exact content/catalog identity and staging
  restoration. A successful build/content load is not a player launch or match.
- Launch the actual packaged player in Training and Solo for every roster
  character. Require both player and NPC to appear, animate and move; check the
  player log for catalog/reference failures. Editor Training success and cooked
  payload admission alone do not prove that Unity catalogs shipped.

Keep task-owned probes temporary; retain their decisive output, not one-off
operator scripts containing private host/configuration details.

## 3. Publish compatible images

Game and Master image publication are independent. With approved source already
published on each repository's `main`, dispatch from that repository:

```bash
# Game checkout:
gh workflow run gameserver-image.yml --ref main -f release_id="$RELEASE_ID"
# Master checkout:
gh workflow run container-images.yml --ref main -f release_id="$RELEASE_ID"
```

Wait for the exact runs, inspect their source revisions, and download their
receipts. The GameServer workflow tests Shared/Server and exercises a real
`slop_court` match-start/content admission inside the image before publishing.
The Master workflow tests the service and verifies migrations/readiness on an
isolated database. Those smoke paths do not prove Steam player admission.

Retain `gameserver-image.txt` and the Master/migration release record. Use exact
published `repository@sha256:<digest>` references, full source revisions, the
shared release ID, and explicit target/compatible migrations in the private
operator release JSON. Do not deploy mutable tags, local Docker IDs, or images
from unrelated schema revisions. Image publication alone deploys nothing.

## 4. Authenticate and prepare the VPS

Use the [VPS runbook](../../deploy/vps/README.md) as the authority for private
runtime paths, release JSON schema, backup/migration, ingress and recovery.
Verify the selected host and SSH key fingerprint; do not copy another environment's
runtime configuration. The human authentication gates below apply to manual
fallback, not the separately provisioned forced-command CI account.

Four human gates are independent:

| Gate | Where | What it authorizes |
|---|---|---|
| Encrypted SSH-key unlock | Visible local operator terminal/agent | SSH login; not sudo |
| VPS sudo password | Authenticated interactive VPS terminal | Privileged release/configuration work |
| SteamCMD account login/Steam Guard | Visible local upload terminal | Depot upload; not branch activation |
| Steamworks mobile confirmation | Operator's Steam app | Requested branch activation |

Never request passwords/codes in chat, put them in command arguments or logs,
or assume browser login proves SteamCMD is authenticated. Label the operator
terminals; when awaiting input, retain the candidate and state exactly which
gate remains. Inspect an ambiguous terminal delivery before sending it again.

The private Master `Room__CatalogHash` must match the candidate's canonical
catalog hash when cooked identity changes. Copy the active release's private
Master environment to a **new release-specific file**, for example
`/etc/sloparena/private/master.<release_id>.env`. Replace only `Room__CatalogHash`
in that copy, preserving every credential, unrelated line, owner and restrictive
mode. Set the candidate release JSON's `runtime.master_env_file` to this new path.
Leave the previous release's referenced environment file unchanged: automatic
recovery restores its **path**, not overwritten file contents. Retain the
release-specific private files for guarded compatible rollback.

Do not paste or copy these environments into Git, receipts or public artifacts.
Do not restart Master early: guarded deployment applies the new admission file
with the compatible pair. A changed catalog still requires explicit client/image,
private configuration and schema compatibility checks before rollback.

Prepare the private release record from `deploy/vps/release.example.json` with
all required runtime file paths. The server uses explicit `vps` profile, approved
host identity, private Master/control URLs and separate registration/control keys.
The GameHost has one Steam P2P listener and **no published gameplay UDP ports**;
`publicIp` is browser metadata, not Steam connection identity. Use the runbook's
checksum-pinned Steam redistributable mount and verify redistribution permission
before changing packaging. Development UDP smoke is not a public fallback.

## 5. Upload without activating

The existing script targets Playtest AppID **5325920**, depot **5325921** and
requires an already available SteamCMD executable plus a build-authorized account.
Set `STEAMCMD` to that executable and `STEAM_BUILD_USER` to the account name;
credentials are entered interactively. Preview, then upload from the game checkout:

```bash
scripts/steam-playtest.sh "$VERSION"
scripts/steam-playtest.sh "$VERSION" --upload
```

Preview still invokes SteamCMD/login but does not upload. The upload emits a
BuildID and depot manifest; keep the relevant logs/VDF under the candidate's
evidence directory. Neither invocation changes a Steam branch. Upload may precede
VPS deployment, but default activation must wait for compatible backend proof.

## 6. Deploy and prove compatibility

On the selected VPS, the installed tooling lives at
`/opt/sloparena/deploy/vps/`. Read status first. Require both application readiness
checks, fresh host registration, expected current schema/image/source identities,
and a fresh **zero-active-match** observation immediately before replacement.
Match count is heartbeat-reported, not a synchronous simulation query;
`release.py` does not implement this operator zero-match gate. If freshness or
zero-match proof is absent, stop rather than interrupting players.

Require a fresh successful off-host backup and enough disk space. On the current
installed host the operator invokes `sloparena-backup.service`, then checks
`last-offhost-backup.json` and `last-backup-attempt.json` through status. Schema
changes additionally require the guarded pre-migration dump and pinned EF bundle.
A successful backup upload is not evidence of a successful restore.

```bash
sudo python3 /opt/sloparena/deploy/vps/release.py status \
  --target-dir /var/lib/sloparena
sudo python3 /opt/sloparena/deploy/vps/release.py deploy \
  --target-dir /var/lib/sloparena --config /etc/sloparena/release.json
sudo python3 /opt/sloparena/deploy/vps/release.py status \
  --target-dir /var/lib/sloparena
```

The guarded CLI validates digests/labels, DNS, Compose and migration compatibility
before writer disruption. After deployment, observe exact active release/image
and source pins, Master/GameHost readiness, fresh registered Steam host/protocol
and catalog hash, schema and backup results. Verify the public HTTPS endpoint
independently, including rejection of unauthorized admission. `/health` alone is
a listener check, not readiness. Current GameServer binaries retry transient
Master outages and recover after missing/rejected heartbeat identity; a Master
restart alone does not require a GameHost restart.

A failed gate blocks Steam activation. Use the runbook's guarded compatible
rollback or repair/roll-forward; never expose legacy UDP, disable authentication,
restore an incompatible schema/configuration, or use destructive volume cleanup.

## 7. Activate and observe Steam default

In authenticated [Steamworks builds](https://partner.steamgames.com/apps/builds/5325920),
select **default** for the exact uploaded BuildID, preview the change, check the
new depot manifest, then request **Set Build Live Now**. Complete any requested
Steam mobile app confirmation. Submission or a confirmation prompt is not proof
that the branch changed.

Observe the successful activation and the current branch table again: default
must show the candidate BuildID and manifest. Preserve a cropped screenshot and
bounded branch observation in the candidate directory. Exclude account settings,
branch passwords and unrelated rows. Leave `test` and other branches unchanged
unless separately requested.

Record the final candidate with source/image pins, catalog/package identity,
Steam BuildID/manifest/default observation, VPS readiness/registration,
backup/schema results, and any unexercised flow. Remove task-owned staging/helpers,
close task-owned privileged/authentication sessions and stop temporary SSH agents.
Do not delete reusable evidence or unrelated files.

A packaged two-account match/rematch, valid Steam-ticket admission, external
firewall exposure tests, and a fresh isolated backup restore are separate proofs.
Only claim each if exercised; see the VPS runbook's live acceptance criteria.

## Explicit alternatives: ZIP/GitHub and Alfred

For friends-only ZIP releases, use `0.2.0-demo.<n>`. With a separately authorized,
confirmed-closed Unity project, `scripts/build-release.sh <version>` produces
`build/release/SlopArena-<version>.zip`: Windows player, embedded self-contained
server, arenas, admitted cooked roster/payloads, `README.txt` and `HOSTING.txt`.
It also publishes the framework-dependent Linux server under `build/minipc/`.
Master is separate; this script does not deploy any host.

The script saves `ProjectSettings.asset` and existing staging to a private
`build/.release-stage.*` backup and restores saved bytes/directories through its
EXIT trap on normal success or failure. It rejects unstaged settings changes,
not every possible staged change. Forced termination can prevent cleanup;
prove settlement before recovering that run's backup. Do not blanket `git checkout`
or delete existing StreamingAssets/meta files; Unity asset re-serialization
outside the script's saved inputs must be compared to the pre-build snapshot.

Only when GitHub publication is requested, create the matching release with the
ZIP and approved notes from `docs/release/RELEASE_NOTES.template.md`. Include the
current attribution/license files. Do not silently create tags/releases as part
of Steam upload.

For an explicitly selected Alfred deployment, follow
[Production Hosting](production-hosting.md) and its current homelab runbook.
The legacy home binaries need explicit `development` profiles before upgrading.
GameServer replacement uses `scripts/deploy-server.sh`; Master is independently
published/rsynced. Preserve private configuration and avoid destructive rsync
options. Restart replaced binaries, not GameHost solely because Master restarted.

## CI boundaries

Game `ci.yml` checks documentation, builds Shared/Server and runs their suites.
`gameserver-image.yml` and Master `container-images.yml` publish tested immutable
images independently. Master `build.yml` supplies its own build/test/publish
artifacts. None deploys the VPS/home backend or activates a Steam branch.
Website publication is separate and is not implied by releasing game/backend.
