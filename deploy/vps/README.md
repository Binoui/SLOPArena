# Restricted VPS release

This is the current Steam Playtest backend on the dedicated Ubuntu 24.04 amd64
VPS. The workstation operator alias is `sloparena`; public Master HTTPS is
`https://master-test.sloparena.barakaslurp.fr`. It is separate from `deploy/local/`
and the historical Alfred home deployment. Keep credentials/runtime state
specific to this environment.

## Routine release and investigation

- Release: use [the resumable controller, isolated client build and coordinated CI workflow](../../docs/systems/release-pipeline.md). Steam default activation stays manual after VPS readiness.
- Match lag or frozen fighters: start with [MasterConsole diagnostics](#match-diagnostics), then the [two bounded process-log commands](#noninteractive-agent-log-access).
- Readiness: public `/ready` plus a fresh private MasterConsole server heartbeat. Full image/schema/backup status needs an authenticated operator sudo session.
- CI connectivity failure: **Check Playtest VPS** (`check-playtest-vps.yml`) checks the protected tunnel and refusal boundaries; it neither fetches match logs nor checks application readiness.

Bootstrap and CI provisioning below are one-time administration. Do not repeat
them for a normal release or log request. The ordinary SSH log grant does not
authorize passwordless status/deploy/rollback or arbitrary Docker commands.

## Operator prerequisites

1. Acquire a VPS and verify its operating-system image, public IPv4, optional IPv6, provider-console access, and SSH host-key fingerprint out of band. Keep console recovery working before restricting SSH. Back up existing target data before use.
2. Configure a sudo operator and key-authenticated SSH. Restrict management SSH to an operator-controlled source CIDR in host and provider firewalls. Verify a new SSH session before closing the recovery session. If locked out, recover through the provider console, not by exposing database or control ports.
3. Create test DNS records such as `MASTER_TEST_HOST` and `GAMEPLAY_TEST_HOST` that point to the selected VPS IPv4, not another environment. Public TCP 443 may reach Caddy; TCP 80 is for ACME/redirect. Keep IPv6 and provider firewall policy explicit and tested. Never open private Master/database/control ports or gameplay ports unless the reviewed deployment architecture requires them.
4. Publish compatible GameServer, Master and EF migration images under one release ID through their explicit workflows. Pin image digests and source revisions. An upload alone never deploys the VPS.
5. Keep database, JWT, registration/control, registry and storage credentials in private operator files outside Git. Use only credentials created for this environment. Store runtime files with restrictive permissions and grant only required read access to containers. Never put keys or runtime configuration in a release record, image, log, or chat.

The owner console stays local and reaches Master through a loopback-bound SSH
forward to its private container address. Keep `/owner` routes excluded from
public Caddy ingress, including case and encoded-path variants; the dedicated
`Console__Key` is additional authorization, not permission to publish the route.
Resolve the current private Master address after container replacement instead
of opening a public Master port. Use a separate VPS-only console key and preserve
the previous release's environment file for rollback.

## Bootstrap

Copy the deployment scripts to the verified fresh VPS, then run bootstrap once:

```bash
sudo deploy/vps/bootstrap.sh --confirm-vps \
  --ssh-source <management-ipv4-or-ipv6-cidr>
```

Bootstrap resets host UFW, checks the operator's key and OS, installs Ubuntu Docker/Compose packages, disables root/password SSH, and installs the Docker-aware ingress filter. It is not a command for a workstation or an existing home server. **Do not rerun bootstrap on an already prepared host:** it resets firewall rules and changes SSH authentication.

Before provider-firewall cutover, permit only management-source SSH and the reviewed public web ports. Keep IPv6 policy separate and closed unless it is reviewed and tested. If the SSH source rule locks you out, use the provider's out-of-band console, inspect `sudo ufw status numbered`, restore a rule for the operator's current source CIDR and configured SSH port, and confirm `sshd -t` before reloading SSH. Open a new key-authenticated SSH session before closing the recovery session. Never enable password/root SSH or open SSH to `0.0.0.0/0` as a shortcut.

## Release

This is the **manual privileged fallback**, used when the coordinated CI path is
unavailable. The installed script runs on the selected VPS in an authenticated
operator sudo session. Create a release JSON from `release.example.json` outside
Git with exact published digests, full source revisions, shared `release_id`,
target/compatible EF migrations, test DNS/VPS identity and private runtime file
paths. Preserve each record; the CLI saves a private copy. For a catalog change,
manually prepare a new Master environment file and reference it in the record;
the automatic environment copy belongs to the CI receiver, not `release.py`.
Require a fresh successful off-host backup before manual replacement.

```bash
sudo python3 /opt/sloparena/deploy/vps/release.py status --target-dir /var/lib/sloparena
sudo python3 /opt/sloparena/deploy/vps/release.py deploy \
  --target-dir /var/lib/sloparena --config /etc/sloparena/release.json
sudo python3 /opt/sloparena/deploy/vps/release.py status --target-dir /var/lib/sloparena
sudo python3 /opt/sloparena/deploy/vps/release.py rollback \
  --target-dir /var/lib/sloparena --release-id '<previous-compatible-release-id>'
```

The deployment lock serializes changes; image/digest/label, DNS and Compose checks precede writer disruption. Deployment and rollback of an active release also require fresh Master heartbeat evidence of zero active matches immediately before stopping writers; stale/unavailable registration is not zero. Schema changes stop writers, require a successful `pg_dump` before running the pinned EF bundle, then check the applied migration. Keep an off-host copy of the pre-migration backup before relying on disaster recovery. Do not use `down -v`, destructive prune, or runtime-config rsync.

Registry provenance uses Docker Engine pulls of each exact digest before checking
the local RepoDigest and application revision/version labels. Do not replace this
with standalone `docker manifest inspect`: Docker 29.1.3 rejects some valid OCI
indexes even when the registry response bytes match the pin. A cached local image
does not exempt a production release from registry-backed pull verification;
only disposable application-image fixtures have that explicit exemption.

A prior raw-UDP release record may remain readable for preflight, but it must not be restored through a Steam-only deployment as an insecure public fallback. If migration or compatibility checks fail, pause new matches and repair/roll forward.

## Restricted CI release account

This is **one-time privileged operator setup**, not something the workflow
performs. The host must already have an active release at `/var/lib/sloparena`,
private runtime files, registry pull access and working off-host backups.
The public Actions candidate contains only version/release/source identities,
allowlisted immutable image references, canonical catalog hash, HTTPS endpoint,
explicit schema compatibility and Steam upload identities. It cannot select
runtime paths, credentials, a state directory or arbitrary shell commands.

Install the reviewed helper versions into the existing root-owned code directory:

```bash
sudo install -o root -g root -m 0644 \
  deploy/vps/release.py deploy/vps/recovery.py deploy/vps/ci_release.py \
  /opt/sloparena/deploy/vps/
sudo install -o root -g root -m 0755 \
  deploy/vps/ci-release-ssh deploy/vps/ci-network-firewall \
  /opt/sloparena/deploy/vps/
sudo install -o root -g root -m 0755 deploy/vps/docker-firewall.sh \
  /usr/local/sbin/sloparena-vps-docker-firewall
sudo useradd --user-group --home-dir /var/lib/sloparena-ci --shell /bin/sh sloparena-ci
sudo install -d -o root -g root -m 0755 /var/lib/sloparena-ci
sudo install -d -o root -g sloparena-ci -m 0750 /var/lib/sloparena-ci/.ssh
```

Keep `/opt/sloparena`, its deployment directories, installed scripts/Compose
files and their ancestors root-owned and not writable by the account/operator.
Do not use a symlink into a writable Git checkout. The CI account must not join
the Docker group or receive general sudo access.

Before replacing an installed backup helper on a prepared host, inspect its
private storage configuration and service arguments. The current helper requires
the private `storage.json` and `--storage-config` described below; create them
from the existing environment's real values without changing its credentials.
Do not break a working backup timer by copying a new helper without its contract.

Create a dedicated CI key privately. Install its public key as the single line
below in `/var/lib/sloparena-ci/.ssh/authorized_keys`, owned by
`root:sloparena-ci`, mode `0640`. The root-owned home prevents replacement of the
key directory; the account's group can read but not rewrite the key:

```text
restrict,command="/opt/sloparena/deploy/vps/ci-release-ssh" ssh-ed25519 <dedicated-ci-public-key> sloparena-ci
```

Using `visudo -f /etc/sudoers.d/sloparena-ci-release`, grant only:

```sudoers
sloparena-ci ALL=(root) NOPASSWD: NOSETENV: /usr/bin/python3 -I /opt/sloparena/deploy/vps/ci_release.py
```

Keep that file root-owned, mode `0440`, and validate it with `visudo -cf`.
The arguments above are exact: no wildcards, interpreter variations,
`--validate-only`, extra arguments or `NOPASSWD: ALL`. Python isolated mode
excludes user-controlled imports/environment; the receiver adds only its trusted
installed sibling directory. The SSH wrapper accepts the literal remote command
`sloparena-release` and no arguments.

### Private CI ingress without weakening management SSH

Use a dedicated WireGuard peer, not a public CI SSH listener or an allowlist of
every hosted-runner network. The fixed profile is IPv4 UDP `51830`, interface
`sloparena-ci`, server `10.253.253.1/32`, and CI peer `10.253.253.2/32`.
The CI SSH daemon binds only `10.253.253.1:2223`. Preserve management SSH and its
source restriction. Do not reset UFW, restart management SSH or introduce a
provider default-deny cutover while provisioning this route.

Install WireGuard tools on the host after confirming the running kernel supports
the module; no kernel upgrade or restart is required. Generate separate
server/client keypairs privately. Keep the server configuration root-owned,
mode `0600`, at `/etc/wireguard/sloparena-ci.conf`:

```ini
[Interface]
Address = 10.253.253.1/32
ListenPort = 51830
PrivateKey = <private-server-key>
PreUp = /usr/local/sbin/sloparena-vps-docker-firewall
PreUp = /opt/sloparena/deploy/vps/ci-network-firewall up
PostDown = /opt/sloparena/deploy/vps/ci-network-firewall down

[Peer]
PublicKey = <client-public-key>
AllowedIPs = 10.253.253.2/32
```

Store the matching raw client configuration as the protected `playtest-vps`
secret `PLAYTEST_WIREGUARD_CONFIG`; it must contain only this server route:

```ini
[Interface]
Address = 10.253.253.2/32
PrivateKey = <private-client-key>

[Peer]
PublicKey = <server-public-key>
Endpoint = <approved-public-vps-ipv4>:51830
AllowedIPs = 10.253.253.1/32
PersistentKeepalive = 25
```

Never log/upload either configuration or retain unused temporary key files.
The runner helper writes private files, brings up the tunnel, and tears it down
on job settlement. Host INPUT admits only peer-to-server TCP `2223`, denies
other tunnel traffic in both families, and the existing Docker-aware firewall
drops tunnel forwarding before conntrack/bridge exceptions. Do not insert a
competing FORWARD hook ahead of its unique-first DOCKER-USER policy.

Permit encrypted IPv4 UDP `51830` in the host firewall. Review provider policy
without changing existing management, web or Steam relay traffic. Keep CI IPv6
closed. Allow only `10.253.253.2` to `10.253.253.1:2223` on `sloparena-ci`;
do not open CI SSH on the public interface.

Install the reviewed private listener assets:

```bash
sudo install -o root -g root -m 0644 deploy/vps/sshd_config.sloparena-ci \
  /etc/ssh/sshd_config.sloparena-ci
sudo install -o root -g root -m 0644 deploy/vps/sloparena-ci-ssh.service \
  /etc/systemd/system/sloparena-ci-ssh.service
sudo /usr/sbin/sshd -t -f /etc/ssh/sshd_config.sloparena-ci
sudo systemctl daemon-reload
sudo systemctl enable --now wg-quick@sloparena-ci.service
sudo systemctl enable --now sloparena-ci-ssh.service
sudo /opt/sloparena/deploy/vps/ci-network-firewall --verify
sudo /usr/local/sbin/sloparena-vps-docker-firewall --verify
```

The separate SSH service depends on WireGuard and never replaces `ssh.service`.
OpenSSH's readiness notification settles `systemctl start` after listener setup,
so initial private SSH verification cannot race daemon startup.
Set protected `PLAYTEST_SSH_HOST=10.253.253.1`, `PLAYTEST_SSH_PORT=2223` and
`PLAYTEST_SSH_USER=sloparena-ci` after listener/key verification. Pin the existing
trusted host's Ed25519 key as `[10.253.253.1]:2223` in
`PLAYTEST_SSH_KNOWN_HOSTS`; never disable host-key checking. Store the dedicated
CI private key in `PLAYTEST_SSH_PRIVATE_KEY`.

Run **Check Playtest VPS** on `main`, with the normal protected VPS approval.
It proves the hosted tunnel and pinned SSH route, malformed-candidate refusal,
shell/TTY/forwarding denial and private runner cleanup. No valid candidate,
client build, Steam upload or deployment is performed. Namespace packet tests
also prove forbidden INPUT/forwarding against live isolated listeners, without
changing the host's operational rules.

### Candidate handling and recovery

For local public-contract validation without root, Docker or private host state:

```bash
python3 -I deploy/vps/ci_release.py --validate-only < candidate-public.json
```

The receiver runs the existing off-host backup service, then acquires the release
lock and requires fresh successful evidence for the still-active release. It
copies the active Master environment to an immutable release-specific private
file, changing only `Room__CatalogHash` and, when the candidate carries
`admitted_arenas` (the Stage Select roster of the release commit), replacing every
`Room__AdmittedArenas__N` entry with that list. Other bytes, owner and mode are
preserved. Candidates without the field keep the active arena admission.
The prior environment remains unchanged for compatible recovery. The candidate
HTTPS endpoint must exactly match the active private test host. Identical
release-ID retries verify the active result; different content under an existing
ID is rejected.

The receiver emits only bounded public readiness/source/image/schema/Steam and
sanitized backup evidence. Private paths, environment contents and raw logs stay
on the VPS. On timeout or failure, use the authenticated operator's private
`release.py status`/events to reconcile before retrying. CI does not bootstrap a
host, grant schema-upgrade permission implicitly, bypass active fights, activate
a Steam build, or perform destructive database recovery.

### Updating installed helpers

The VPS has no repository checkout: copy the reviewed helpers there, then install
them. Copy from `origin/main` after the change merges, not from a working tree,
and install the three together, because `ci_release.py` imports the other two.
On the workstation, from the repository root:

```bash
git fetch origin
git archive origin/main deploy/vps/release.py deploy/vps/recovery.py deploy/vps/ci_release.py \
  | ssh sloparena 'rm -rf ~/sloparena-helpers && mkdir -m 0700 ~/sloparena-helpers && tar -xf - -C ~/sloparena-helpers'
```

Then in an operator session on the VPS (`ssh sloparena`):

```bash
cd ~/sloparena-helpers/deploy/vps
for f in release.py recovery.py ci_release.py; do cmp -s "$f" "/opt/sloparena/deploy/vps/$f" && echo "$f: same" || echo "$f: changes"; done
sudo install -o root -g root -m 0644 release.py recovery.py ci_release.py /opt/sloparena/deploy/vps/
for f in release.py recovery.py ci_release.py; do cmp -s "$f" "/opt/sloparena/deploy/vps/$f" && echo "$f: installed" || echo "$f: NOT installed"; done
cd && rm -rf ~/sloparena-helpers
```

If the first loop says `recovery.py` changes, check the backup-helper contract
above before installing it. A receiver older than the candidate rejects it before
the backup or the release lock, but the release step sees only an empty reply and
records an ambiguous failure. `release.py status` then still shows the previous
release: update the helpers and resume the same candidate.


## Disposable rehearsal

Use a fresh disposable target outside the repository and a private fixture directory. Create two release JSON records from `release.example.json` with different release IDs, matching image labels/source revisions, the same explicit compatible migration ID, and private test credentials. Locally built images are not published registry digests. For an isolated rehearsal only, use a loopback-only Compose override:

```yaml
services:
  caddy:
    ports: !override
      - "127.0.0.1:18080:80/tcp"
      - "127.0.0.1:18443:443/tcp"
```

The disposable override changes only Caddy's public ports. Do not publish gameplay UDP in the rehearsal. With that override saved as `<loopback.yaml>` and an explicit executable firewall fixture `<fixture>`, run:

```bash
python3 deploy/vps/release.py deploy --target-dir <absolute-disposable-state> \
  --config <first-release.json> --allow-disposable-host \
  --compose-override <loopback.yaml> --firewall-helper <fixture> \
  --allow-local-image-digests
python3 deploy/vps/release.py deploy --target-dir <absolute-disposable-state> \
  --config <second-compatible-release.json> --allow-disposable-host \
  --compose-override <loopback.yaml> --firewall-helper <fixture> \
  --allow-local-image-digests
python3 deploy/vps/release.py rollback --target-dir <absolute-disposable-state> \
  --release-id <first-release-id> --allow-disposable-host \
  --compose-override <loopback.yaml> --firewall-helper <fixture> \
  --allow-local-image-digests
```

These flags do **not** validate registry publication, host firewall, public DNS or provider firewall. Never use them for a live release. Inspect active release, schema, backup/checksum and Compose status after each step. Clean disposable volumes separately; never apply `down -v` to production state.

## Backup and restore

Keep an off-host PostgreSQL archive and test recovery in isolation before
enabling scheduled backups or replacing live services. Keep storage details
and credentials in private operator materials, never in Git. Confirm the
selected provider's access, lifecycle and deletion policy.

Before installing the updated backup service, place a root-owned 0600
`/etc/sloparena/private/storage.json` on the host with the selected provider's
actual values (do not put the filled file in Git):

```json
{"bucket":"<bucket>","endpoint":"https://<storage-endpoint>","region":"<signing-region>"}
```

The recovery CLI and timer both require `--storage-config`; update this
private file before upgrading the installed helper or timer. A missing or
publicly readable file fails closed. No timer run succeeds until it is present.

Run and inspect a backup before enabling the timer:

```bash
sudo python3 /opt/sloparena/deploy/vps/recovery.py backup \
  --target-dir /var/lib/sloparena \
  --credentials /etc/sloparena/private/<storage-curl-config> \
  --storage-config /etc/sloparena/private/storage.json
```

Restore to new containers on an internal-only network with no published ports and a new volume. Run `pg_restore --exit-on-error`, compare EF migration IDs and persisted records, and require an isolated Master `/ready` response. Never replace the live database automatically, copy a running PostgreSQL data directory, or expose restored credentials on a live route.

The isolated Master readiness fixture uses explicit Steam mode, Playtest AppID
5325920, and the `sloparena-playtest` identity. Its clearly non-production API
key is only startup configuration; the internal network has no external route
or published ports. This proves restored database readiness, not Steam ticket
admission. Do not copy live publisher credentials into the fixture or omit
`Auth:Mode`: current Master images reject an unspecified authentication mode.
The fixture inherits only `Room__*` admission fields from the selected private
Master environment. Production images require explicit room selectors and
catalog pins; do not rely on development `appsettings.json` defaults.

To verify recovery from an actual completed archive:

```bash
sudo python3 /opt/sloparena/deploy/vps/recovery.py restore \
  --target-dir /var/lib/sloparena \
  --credentials /etc/sloparena/private/<storage-curl-config> \
  --storage-config /etc/sloparena/private/storage.json \
  --key postgres/<backup-id>.dump
```

Inspect the isolated restore result and clean up only its reported test
containers, network and volume. Only after restore and readiness checks pass,
enable the daily backup timer:
```bash
sudo install -m 0644 /opt/sloparena/deploy/vps/sloparena-backup.service \
  /etc/systemd/system/sloparena-backup.service
sudo install -m 0644 /opt/sloparena/deploy/vps/sloparena-backup.timer \
  /etc/systemd/system/sloparena-backup.timer
sudo systemctl daemon-reload
sudo systemctl enable --now sloparena-backup.timer
sudo systemctl list-timers sloparena-backup.timer
```

Check `systemctl status sloparena-backup.service` and its journal on failure. Follow the configured retention policy and preserve pre-migration backups until recovery is verified.

## Operations and credentials

`release.py status` reports release/source/image identities, container state, application health/readiness, Master registration freshness, reported active-match count, filesystem use, and off-host backup status. GameServer readiness requires cooked content and a recent Master registration or heartbeat acknowledgement; `/health` is a listener check. A Master outage can make readiness fail without stopping active fights or dropping the registration token. A successful heartbeat restores readiness; a rejected heartbeat triggers re-registration.

Match count comes from the latest Master heartbeat, not a synchronous simulation
query. Logs are bounded and pre-replacement bundles are retained privately.
Keep raw Docker logs private, including after replacement; share only relevant
sanitized evidence, excluding tokens, credentials and chat bodies.

Replace credentials through the environment's documented private configuration and provider controls. Change one credential domain at a time, check readiness/registration, and revoke the old value only after the new path works. Changing a database password file alone does not change an existing database role. Replacing host registration credentials does not necessarily invalidate an already provisioned GameServer API token; follow the Master/GameServer enrollment procedure to invalidate and issue a new token safely. Do not remove an active registration during a match. Preserve provider-console recovery and management-source restrictions when changing SSH keys. Do not reuse restored GameServer registrations or tokens to enroll a new host.

A database restore does not undo schema migrations or reissue credentials. Restore only to a fresh isolated target unless an operator has separately approved and verified a recovery plan. Do not import another environment's in-memory lobby/chat state or runtime configuration.

### Match diagnostics

The hosted owner console at
`https://master-test.sloparena.barakaslurp.fr/console/` uses password login and a
backend-managed Master key. See [Hosted owner console](../../docs/systems/owner-console.md)
for its independent deployment and access boundary. The local path below remains
available for development and authorized agent automation.

The local MasterConsole at `http://127.0.0.1:4173` reaches Master through the
existing private SSH forward on `127.0.0.1:18080`. Its code/run instructions live
in the separate `SlopArena-console` checkout. If starting a tunnel, resolve the
current private Master address first; the verified address for this deployment
is `172.30.12.2:8080`, and it must be rechecked after replacement:

```bash
ssh -N -o ExitOnForwardFailure=yes \
  -L 127.0.0.1:18080:172.30.12.2:8080 sloparena
```

Reuse the existing console/tunnel when available. Enter the dedicated console
key only in its masked field; it is held in page memory and cleared on disconnect
or authorization failure. The operator key file is
`~/.local/state/sloparena-console/console.key`, outside Git with owner-only mode.
Do not expose `/owner` routes publicly or substitute a player JWT for this key.

Choose **Matches → a match → Pull match logs**. **Download diagnostics JSON**
preserves that snapshot; pull again to refresh an active match. It includes server
tick, mean/maximum tick work, scheduling delay/resets, timestamped Steam RTT and
receive/send quality, and recent lifecycle/connection/error events. Steam quality
is an estimate, not a packet-loss percentage. Client FPS/frame time and prediction
correction counts are not collected by this endpoint.

GameHost retains active matches plus the last 50 ended matches, with 256 events
per match and an overwritten-event count. Restart loses these records. Matches
from before deployment may remain in Master without any retained diagnostics.

| Result | Meaning |
| --- | --- |
| `200` | Captured typed snapshot; inspect its capture/sample times |
| `401` | Console key missing or wrong |
| `404` | Unknown match, or owner API disabled by missing configuration |
| `410` | Known match has no retained GameHost diagnostics |
| `502` | Invalid/oversized upstream response |
| `503` | GameHost unavailable or not provisioned for private control |

For an authorized automated export, use the fixed local proxy and read the key
into memory. From the game checkout, replace the match ID with a canonical GUID
from MasterConsole; the command writes a private snapshot, not credentials:

```python
import json
import os
from pathlib import Path
from uuid import UUID
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

match_id = "replace-with-match-GUID"
assert str(UUID(match_id)) == match_id
key = (Path.home() / ".local/state/sloparena-console/console.key").read_text().strip()
request = Request(
    f"http://127.0.0.1:4173/api/matches/{match_id}/diagnostics",
    headers={"X-Console-Key": key},
)
try:
    with urlopen(request, timeout=8) as response:
        data = response.read(512 * 1024 + 1)
except HTTPError as error:
    raise SystemExit(f"Match diagnostics returned HTTP {error.code}; see the result table.")
except URLError:
    raise SystemExit("Private console/tunnel unavailable.")
if len(data) > 512 * 1024:
    raise SystemExit("Diagnostics exceeded the response limit.")
snapshot = json.loads(data)
output = Path("build/diagnostics") / f"match-{match_id}.json"
output.parent.mkdir(parents=True, exist_ok=True)
fd = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
with os.fdopen(fd, "w") as saved:
    os.fchmod(saved.fileno(), 0o600)
    json.dump(snapshot, saved, indent=2)
    saved.write("\n")
print(output)
```

The private chain is console `/api/matches/<GUID>/diagnostics` → Master
`/owner/matches/<GUID>/diagnostics` → GameHost `/match/diagnostics/<GUID>`.
Master uses the provisioned host and private control credential, bounds the
response and projects safe fields. The owner key never goes in a URL or command
argument; do not dump the full owner observation just to find one match.

### Noninteractive agent log access

The configured workstation's `sloparena` SSH alias selects the approved VPS key and
`IdentityAgent /run/user/%i/ssh-agent.socket`. The existing systemd
`ssh-agent.socket` is enabled; agents do not need an inherited `SSH_AUTH_SOCK`.
After login or an agent restart, unlock the encrypted key in an operator terminal:

```bash
SSH_AUTH_SOCK="/run/user/$(id -u)/ssh-agent.socket" ssh-add ~/.ssh/sloparena_vps
```

On the verified VPS, `/etc/sudoers.d/sloparena-agent-logs` grants `binoui` only
these two exact remote commands without a sudo password:

```bash
ssh -o BatchMode=yes -o StrictHostKeyChecking=yes sloparena \
  'sudo -n -- /usr/bin/python3 -I /opt/sloparena/deploy/vps/release.py logs --target-dir /var/lib/sloparena --service game --tail 200'
ssh -o BatchMode=yes -o StrictHostKeyChecking=yes sloparena \
  'sudo -n -- /usr/bin/python3 -I /opt/sloparena/deploy/vps/release.py logs --target-dir /var/lib/sloparena --service master --tail 200'
```

Keep the policy root-owned, mode `0440`, with `NOSETENV` and no wildcard arguments.
The helper, Compose file and their parent directories must remain root-owned and
not writable by the operator. Python `-I` excludes user-controlled import paths
and Python environment variables. Changed arguments, arbitrary Python, status,
deploy and rollback retain normal sudo authentication; this grant does not add
Docker-group access or remove SSH-key authentication. Logs remain private.

## Live acceptance

From an independent external network, verify the intended HTTPS readiness and authentication flow. Exercise valid and invalid admission, hub transports, and the packaged client flow, including a completed match and rematch. Confirm SSH, raw Master, PostgreSQL, GameHost control and any non-public gameplay ports remain unreachable from unauthorized sources; test IPv4 and IPv6 separately. Verify certificate persistence, registration and readiness after restart. A firewall, readiness, backup or digest failure blocks release instead of permitting an unauthenticated fallback.

Record test release identifiers, actual observed results and remaining proof privately. A synthetic protocol probe, local rehearsal, successful backup, or readiness check alone is not proof of a packaged-client match or full recovery acceptance.
