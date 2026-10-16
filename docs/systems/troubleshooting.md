# Troubleshooting — Steam Playtest and VPS

The current target is the `sloparena` VPS, with Master at
`https://master-test.sloparena.barakaslurp.fr`. Use
[sloparena-ops](../../skills/sloparena-ops/SKILL.md) for the compressed
investigation flow and [the VPS runbook](../../deploy/vps/README.md) for access.
Release through [the current pipeline](release-pipeline.md); a log request does
not require a release, container restart or shared-Editor session.

## Start with evidence from the reported match

1. Record approximate UTC time, match ID, installed client version and symptom.
2. In local MasterConsole select **Matches → a match → Pull match logs**, then
   **Download diagnostics JSON**. Pull again for a fresh active-match snapshot.
3. Correlate server tick work/scheduling, peer sample times/RTT/quality and recent
   lifecycle/error events with bounded process logs and client `Player.log`.
4. For service status, use public `/ready` and the console's server heartbeat.
   A 15-second-old heartbeat is stale; container uptime and `/health` are not
   admission proof. Full image/schema/backup status requires operator sudo.

```bash
curl --fail --silent --show-error --max-time 10 \
  https://master-test.sloparena.barakaslurp.fr/ready
```

**Check Playtest VPS** checks the protected CI tunnel and SSH refusal boundaries;
it does not check live application readiness or retrieve a match's logs.

## Where current logs live

| Evidence | Access |
| --- | --- |
| Per-match timing, peer health and lifecycle events | [Private MasterConsole diagnostics](../../deploy/vps/README.md#match-diagnostics) |
| GameHost process log | [Exact permitted VPS command](../../deploy/vps/README.md#noninteractive-agent-log-access), `--service game --tail 200` |
| Master process log | Same grant, `--service master --tail 200` |
| Windows player | `%USERPROFILE%\AppData\LocalLow\SLOPArena\SLOPArena\Player.log` (case-insensitive, so it also finds older installs) |
| Proton player | Selected Steam library: `steamapps/compatdata/5325920/pfx/drive_c/users/steamuser/AppData/LocalLow/SLOPArena/SLOPArena/Player.log`; installs from before the SLOPArena rename keep `LocalLow/SlopArena/SlopArena/` |
| Linux Unity Editor | `~/.config/unity3d/Editor.log` |

The current SSH log grant needs an unlocked VPS key, but no sudo password.
Use those exact commands instead of requesting privileged `status`/Docker access
before fetching logs. Keep raw captures private and report relevant sanitized
lines; client logs can contain authentication details. Copy `Player.log` before
another launch overwrites it. A bounded process tail may no longer cover the
reported match.

GameHost retains active matches plus its last 50 ended matches, with 256 recent
events per match; restart loses the records. Match diagnostics `410` means a
known match has no retained logs, not that it ran cleanly. See
[the result table](../../deploy/vps/README.md#match-diagnostics) for authorization,
unknown-match and unavailable-upstream responses.

## Steam match loads but both fighters freeze before countdown

If `Player.log` repeatedly reports `[NetworkClient] Steam connection closed:
Invalid or spoofed Steam control frame` while `[PvP]` stays at `serverTick=0`,
check the Steam startup control handler before changing clock estimation.
The client sends reliable Bootstrap requests after admission until it receives
the server Clock acknowledgement. Rejecting Bootstrap as though only Ready were
valid disconnects the peer before the full-roster readiness barrier can complete.

The bound Steam handler must accept Bootstrap idempotently without marking the
player Ready; `MatchInstance` already sends baseline and clock controls to bound
peers. Ready still requires the same connection, entity and authoritative clock
validation. Unreliable, malformed, wrong-version, spoofed and client-sent Clock
frames remain invalid. The server fix uses the existing protocol and requires
a deployed GameHost update; editing this checkout does not repair a running VPS.

When modifying the startup handler, run the focused regression/rejection checks:

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj --nologo -p:SkipUnityPluginCopy=true \
  --filter 'FullyQualifiedName~SteamBootstrapBeforeReady|FullyQualifiedName~SteamStartupControls|FullyQualifiedName~SteamReadyRetains'
```

## PvP jerks during hits after startup succeeds

The protocol-6 match investigated on 2026-10-11 completed normally but the opponent's
hit animation logged 440 repeated restarts without a new hitstun entry. Protocol 6
omitted HitstunTicks from CharacterStatePacket, leaving raw opponent states at zero;
the renderer's one-tick-countdown test also treated repeated/skipped snapshots as
new hits. Protocol 7 carries the timer and an authoritative HitReactionSequence.
New launches advance the sequence; rendering restarts only on entry or a changed
sequence. Replicated HitstunTicks also enables existing position smoothing.

In MasterConsole select Matches → a match → Pull match logs. Inspect tick work
against the 16.67 ms budget, scheduling delays/resets, timestamped Steam RTT and
connection-quality estimates. Download the snapshot when needed. Retention is
GameHost process-local: active matches plus 50 ended matches, 256 recent events
per match, lost on restart. A missing/expired log is not evidence of a clean match.
These are safe structured match records, not a dump of raw process/authentication
logs. Client FPS/frame time and prediction correction counts remain uncollected.

Regression coverage uses the real snapshot codec and the production presentation
policy, including repeated/skipped frames, shorter re-hits and hitstop. Keep client, GameHost and Master protocol-compatible; protocol 6 clients cannot join
protocol 7 hosts. If a new protocol-7 match still snaps, capture new evidence rather
than assuming the earlier omission is still the cause.

## Projectile aim diverges after camera turns

Yaw is a circular direction, not a value to saturate at the signed-short limit.
Human input must normalize both facing and aim yaw before encoding signed
centidegrees; resolve an active ability's aim override before that conversion.
For example, camera yaw 450° must arrive as 90°, not 327.67°. Pitch retains its
separate ±90° clamp, and the packet layout does not change.

Run `Tools/SlopArena/Tests/Training Sequence Parser` in the Editor for the human
input/wire regression. For visual acceptance, hold Manki R (controller LB+X /
Square), turn the aim camera through full rotations in both directions, and
release: the rocket should follow the camera, including vertical aim. Updating
source does not update the installed Steam player; use the approved release
pipeline before retesting the published build.

## Registration stale or admission unavailable

Check the selected VPS profile, Steam login/listener, Master readiness and
registration/heartbeat logs. Current GameHost retries transient Master failures
and re-registers after a rejected/lost heartbeat identity. A Master restart alone
does not require a GameHost restart. Permanent credential/profile rejection must
be corrected through the deployment's private configuration, not by disabling
authentication or exposing raw UDP/control ports.

Compare the installed Steam client with the live default BuildID, deployed image
source revisions, protocol and catalog. A pushed source change or successful
Steam upload does not prove default activation. Use the deployment receipt and
Steamworks branch table; check for an unaccepted browser/mobile confirmation
before assuming a stale public app-info result is just propagation.

## Build and source checks

Routine Steam builds use the isolated local wrapper in
[Release Pipeline](release-pipeline.md#2-build-the-windows-client-locally). The
canonical Editor can remain open. `dotnet build` alone does not compile Unity
runtime scripts or prove a Windows player. A missing Windows Build Support
module/license is a build prerequisite, not an operational log failure; software
installation still requires authorization.

For `UnityEditor` references leaking into player runtime code, guard editor-only
APIs with `#if UNITY_EDITOR` and rebuild the isolated player. If a package fails
freshness/admission, resolve its accepted cook/pins before selecting the release
source; do not bypass that refusal.

The older `build-release.sh` ZIP path runs against its selected closed project.
It backs up/restores settings and staging on normal exit. After a forced stop,
first confirm settlement and recover only that run's saved inputs. Never
blanket-reset the user's assets/settings to recover a release.

## Historical Alfred and development findings

[Historical home hosting](production-hosting.md) records the 2026-08-02 Alfred
router/UFW, raw-UDP, rsync, private configuration and backup procedures. They are
not current VPS instructions. Its old addresses, resolver status, arena counts
and binary layouts must not be assumed current.

Older findings remain useful when the same evidence appears:

- A home Join hang was caused by UFW despite a working router forward. Revalidate
  that explicitly selected host's topology; Steam VPS gameplay uses Steam P2P.
- Home Master configuration was lost through `rsync --delete`/`--delete-excluded`.
  Preserve private configuration and use the selected host's runbook; CI VPS
  releases do not rsync application binaries or environments.
- False parallel-test 429s came from a static rate-limit tracker. It is now a
  per-app DI singleton; inspect that scope if the isolated test regression returns.
- Recursive Master publish output came from nested test `bin/obj` content globs.
  Use the maintained container context or an isolated publish output and check
  the actual error, rather than treating every deployment failure as this bug.

Historical release reports and old successful tests are evidence, not a reason
to repeat all their checks for an unrelated change.
