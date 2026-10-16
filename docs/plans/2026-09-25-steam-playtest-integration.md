# Steam Playtest identity and gameplay on the VPS

**Status:** The Steam relay feasibility gate is accepted. A connection-age reconnect limit was removed from the implementation; keep an admitted match active for its intended duration. The hotfix and runtime validation history is preserved in private operator notes, not this public plan. Packaged Windows validation, admitted two-account PvP, a sustained match and outage/account-switch coverage remain acceptance requirements.

## Outcome and ownership

Two entitled accounts install a controlled SLOPArena **Playtest** build through Steam, authenticate without a second account, use the existing global/server/direct chat and browser/lobby/selection flow, fight on the dedicated OVH GameHost, see results, return to the lobby, and rematch. The VPS, not a player's computer or the home host, runs the authoritative Shared simulation. No player-created rooms, invitation system, new matchmaking, Workshop, extra VM, or monitoring platform.

| Owner | Responsibility |
| --- | --- |
| Steam | Playtest account identity and entitlement; SteamNetworkingSockets connection identity and delivery. |
| Master | Verify Steam web ticket and Playtest ownership; issue bounded application JWT; own chat/lobby, authoritative roster and match cancellation/result. |
| GameHost | Advertise current Steam identity through trusted registration; enforce roster-to-connection-to-entity binding; run existing authoritative match and clean up capacity. |
| Unity client | Keep one application session owner; connect to the assigned GameHost identity and render Shared state; never decide admission or gameplay. |

The **intended gameplay transport is SteamNetworkingSockets** via the Steamworks.NET C# binding, subject to the 2A proof below. `CreateListenSocketP2P`/`ConnectP2P` can connect to a dedicated server; the connection is relayed through Valve's network. This does not mean a player hosts the game. Relay latency on a dedicated host can be worse than direct IP, so OVH route and playability are acceptance evidence, not assumed benefits. No Steam dependency belongs in deterministic `src/Shared/`.

## Settled policy

- Public VPS online access is **Steam-only**. Explicit local/development mode may retain guest auth for development; offline Training does not require Steam or Master. Deployment profile alone is not the auth policy. No automatic guest or raw-UDP fallback.
- Playtest AppID, not the unreleased parent game's ownership, is checked by the Master. SteamPipe preview/upload used AppID `5325920` and depot `5325921`; operator confirmation of the Playtest settings remains a release gate. A publisher key remains only on the Master. 2A proved anonymous game-server login with the pinned native stack locally and in an isolated OVH container; confirm permitted dedicated-server redistributables before the production image.
- A successful Steam ticket and current ownership yield a **one-hour** SLOPArena JWT. Renewal requires a **new Steam web ticket and ownership verification**, not an indefinitely renewable old JWT. Signed-in users continue existing chat/lobby until their JWT expires during a Steam verification outage; new sessions and renewal then fail closed. An already authorized match continues and may admit the same rostered Steam identity to its reserved slot while the GameHost and match still exist. No new match may be launched with an expired application session.
- Production disables public guest issuance **and** rejects previously issued guest JWTs on REST and SignalR, including refresh. JWTs carry a provider claim; refresh preserves provider and never upgrades a guest. Never treat nickname, locally reported SteamID, or a JWT presented to a gameplay socket as proof of match-slot authorization. Preserve saved display names as presentation only; do not merge same-name guest and Steam users. On a Steam account change, clear the old client application session before authenticating the new account; do not reuse its chat/lobby connection.
- Only a master-authorized, Steam-verified roster member controls the GameHost entity assigned to that member. Same-account reconnect can reclaim a reserved slot **while the match is active**; close and invalidate the prior handle before accepting its packets. No promise of game-process relaunch recovery, simulation restoration or rollback-history migration.
- A match not filled by its roster expires **60 seconds after allocation**. Once active, if no players remain connected for 60 seconds, or exactly one remains with no connected opponents for 60 seconds, abort with **no competitive result**, notify the existing lobby flow and free the GameHost slot. Returning roster members reset the relevant absence timer while the match remains active. With at least two players still connected, the match continues; ordinary gameplay can end it normally. A GameHost process restart aborts affected matches and returns players to the lobby; it does not restore them.
- First publication uses an **operator-controlled Playtest branch** and a compatible Master/GameHost/client cutover. Old clients fail compatibility checks rather than reaching an insecure transport. If the public Steam cutover fails, pause new matches and repair/roll forward; do not enable public guest or raw UDP. The previous restricted build may be used for operator diagnosis, not public fallback. No upload, branch activation, deployment, firewall change or dependency installation is authorized by this plan alone.

## Cross-repository interfaces to specify together

1. **Authentication (client ↔ Master):** `POST /auth/steam` over HTTPS with `GetAuthTicketForWebApi` ticket after callback; Master calls `AuthenticateUserTicket` for the configured Playtest AppID and fixed backend identity string, derives SteamID only from verified response, checks current Playtest ownership and returns token/expiry/profile. Specify body limits, one-time ticket handling, error classes, timeouts, and log redaction for ticket, publisher key and outbound credential-bearing URLs. All SteamIDs must serialize losslessly and remain separate from provisioned host GUIDs and match-local entity IDs. Align refresh, SignalR authentication and account-switch behavior with this interface.
2. **Host registration (GameHost ↔ Master):** preserve the provisioned, trusted host GUID and existing private authenticated control path. Register the currently valid GameHost Steam networking identity/capabilities; refresh it when it changes, and do not advertise a stale identity. Keep DB/control endpoints private. The legacy advertised IP/UDP ports cannot stand in for a Steam identity.
3. **Match start (Master → GameHost):** one authoritative match ID, roster of verified SteamIDs and assigned entity IDs, chosen content/catalog, admission lifetime and compatibility version. The GameHost must acknowledge the match only when content and capacity are ready. Preserve the master-created match row/result relationship; cancellation must remove or mark a pre-created row unambiguously without reporting a winner or applying competitive results.
4. **Join descriptor (Master → assigned clients):** typed transport, GameHost Steam identity, match ID, virtual port and protocol/content compatibility. The client verifies the connected GameHost identity against the descriptor. Never overload the old IP field with a SteamID; the descriptor does not depend on future Room IDs.
5. **Connection admission (client ↔ GameHost):** one shared Steam listener and bounded pending-join request naming the match. Steam supplies the authenticated remote identity; GameHost checks match, roster, assigned entity, compatibility and admission lifetime before binding the connection handle. Reject non-rostered identities, stale/ended matches, conflicting second handles, mismatched content/protocol and packet-claimed entity IDs. Route messages only to that match. Use bounded queues into match threads, deterministic release of native messages/handles, and bounded deadlines for pending joins.
6. **Lifecycle (GameHost → Master → clients):** define normal result versus canceled/aborted match, cause and single logical notification. A 60-second waiting/abandonment expiry or GameHost restart must return users to the existing gameplay lobby without manufacturing a competitive winner. Room Server Chat remains independently authorized by the current RoomManager attachment; GameHost lifecycle never grants it. Match completion releases listener bindings and capacity. An already admitted match must not be terminated merely because Steam's web verification or the Master briefly becomes unavailable; new admission still needs a valid master-authorized roster and authenticated Steam identity.

Keep compatible `InputState`/state/event codecs and the existing `NetworkClient`/`MatchInstance` packet-consumer seams where they still fit. Input/state stays unreliable and tick-sequenced; admission and one-time control/completion use reliability deliberately. Do not repeatedly queue the already-broadcast result as reliable work. Preserve an honest ping display from observed connection data or bounded RTT measurement. Never move combat, prediction, rollback or content authority into Unity.

## Gates and sequencing

### 2A — Dedicated-host route proof (stop gate)

2A establishes that the pinned binding and native runtime initialize on Linux Editor and GameHost, the Windows player builds with its required Steam native library, and a real Steam identity connects a packaged Linux client to a dedicated GameHost over `CreateListenSocketP2P`/`ConnectP2P`. Record connection time, peer identity, relay route, RTT, loss and queue state for 60 Hz game-sized traffic. Keep VPS ingress restricted and database/control endpoints private; do not expose legacy raw UDP to make a probe work. If native loading, game-server login, authenticated identity or the dedicated-host route fails, stop before the gameplay transport rewrite. A synthetic local socket or Editor import alone does not pass this gate.

**Decision (2026-09-25):** the dedicated-host Linux-player relay proof below is sufficient to proceed with 2B. It does not certify Steam-installed Windows native loading, a second entitled account, a gameplay match or a direct-UDP RTT comparison. Check Windows install/launch and those end-to-end requirements in 2D ([#240](https://github.com/Binoui/SlopArena/issues/240)); record a direct-route comparison there if practical, but do not block 2B solely on an unmeasured baseline. Any unexpected Windows or gameplay failure remains a release blocker, not an implicit raw-UDP fallback.

**Local 2A evidence (not VPS acceptance, 2026-09-25):** Steamworks.NET `2025.164.1` UPM package and matching standalone Linux wrapper/`libsteam_api.so` from the verified release ZIP (SHA-256 `9412348cc404563be5a43a28347cfeda3c679ee044a14d87a507ed2d796a537d`). Linux Editor `SteamAPI.Init()` succeeded with the locally configured candidate AppID `5325920`; a separate Linux GameHost `GameServer.InitEx` anonymously logged on and opened a P2P listener. A locally built Linux player authenticated the GameHost Steam identity and exchanged 600/600 echoed 200-byte unreliable messages at 60 Hz, with 0% observed loss, 29.42 ms mean RTT and a relay POP reported. These are same-workstation observations, not a dedicated-host latency comparison. Unity CLI built Windows and Linux players; the Windows output includes `steam_api64.dll` and excludes `steam_appid.txt`, but it was not launched through Steam on Windows.

**Local container proof:** `libsteam_api.so` alone was insufficient in the clean GameHost runtime image: initialization failed because `steamclient.so` was absent. The supplied SDK 1.65 ZIP contains no `steamclient.so` and its `libsteam_api.so` is not mixed with the pinned 1.64 wrapper. Valve SteamCMD's Linux64 `steamclient.so` (SHA-256 `e74b17cd7849882c73fee6ab59124a533e48b63340d1ce37be0ce469bbb92dc2`) was staged only in an ignored, isolated proof archive, not committed or added to the normal image. With that runtime mounted read-only in the clean .NET container and **no published gameplay ports**, anonymous server login and a relay connection succeeded; a local Linux player received 600/600 echoes at 60 Hz with 29.50 ms mean RTT, 0% observed loss and a relay POP. Confirm the Playtest's permitted [dedicated-server redistributable settings](https://partner.steamgames.com/doc/sdk/api) and exact runtime before a production image.

**Dedicated-host relay proof:** The relay feasibility proof passed for the test setup after waiting for Steam certificate and relay readiness. Exact host identity, environment details, timing, packet metrics and incident records are retained in private operator notes. This is not Windows, two-account or gameplay acceptance.

### 2B — Steam-backed application session

Implement the authentication interface in both repositories, keeping one persistent chat/lobby session owner and a deliberately separate local/development path. Test stable same-account identity across launches, distinct accounts, account switch, display-name preservation, global/server/direct chat, invalid/reused/wrong-app tickets, missing entitlement, publisher/API outage, expired token, guest JWT rejection on REST/SignalR/refresh and fresh-ticket hourly renewal. Existing active matches do not depend on a successful renewal during a brief outage.

**2B runtime contract:** `POST /auth/steam` accepts JSON
`{"ticket":"<hex>"}`; `POST /auth/refresh` accepts the same payload with the
current bearer JWT. Both return `{token,steamId,expiresAt}` with numeric, lossless
SteamID in the existing client DTO. Unity requests each ticket via
`GetAuthTicketForWebApi("sloparena-playtest")`, waits for the callback, keeps the
ticket valid through the Master response and cancels it afterward. Development
guests can renew without a ticket only if the Master explicitly enables them;
public Steam sessions never use guest issuance or renewal. The packaged client
selects `https://<MASTER_TEST_HOST>` before startup; Editor-only
overrides and guest opt-in do not ship as public fallbacks. Package/Editor
verification is not two-account Steam-installed acceptance.

Master requires explicit `Auth:Mode=steam`, publisher `Steam:ApiKey`, verified
Playtest `Steam:AppId`, and `Steam:Identity=sloparena-playtest`; in
development `Auth:Mode=development-guest` enables the separate guest path.
JWT provider claims reject guest and legacy providerless tokens in Steam mode
on REST and SignalR. One-hour Steam renewal rechecks the same verified account
and current ownership; consumed ticket hashes persist in a new DB table with
a unique key. Run the `AddSteamAuthIdentity` migration before a Master cutover.
Valid-ticket ownership and same-account renewal still require a Steam-launched client run.

Treat `SteamUser.BLoggedOn() == false` as backend unavailability, **not** proof
of account switch. Only a different nonzero observed SteamID or a freshly
verified different account clears the existing match/session. Ticket renewal
may fail during an outage and chat ends at JWT expiry; already admitted
GameHost gameplay continues while its Steam connection remains healthy.
Master closes WebSocket hub connections at token expiry; the existing
LobbyClient reconnects with the current renewed JWT and revalidates remembered
Room Chat membership separately from physical GameServer/waiting-roster membership.

### 2C — Steam match routing and admission

Implement host registration, authoritative roster and typed descriptor together across repositories. Route two simultaneous matches on one GameHost listener; verify no cross-match traffic or stale prior-match input. Prove non-rostered user, entity spoof, occupied-slot takeover, expired match, duplicate join, incompatible protocol/content, brief same-account reconnect, 60-second waiting/absence abort and restart cancellation. Confirm capacity release and no false winner. Existing Shared simulation remains authoritative.

**2C wire cutover (compatible Master/GameHost/client change, not a
deployment):** trusted host registration publishes decimal-string `steamId`,
`protocolVersion: 2`, per-process `instanceId` GUID and lowercase SHA-256
`catalogHash` of the admitted immutable content map; heartbeat must match
all three. A changed identity or process instance requires fresh registration,
rotates the session token and cancels old open matches. A changed catalog
hash hides new matchmaking until re-registration but preserves the old
in-memory match catalogs and admitted matches under the same running host.
`POST /match/start` carries Master-created GUID `matchId`, ordered verified
`steamId`/`entityId`/`characterClass` roster, arena, stocks,
`protocolVersion: 2`, `virtualPort: 0`, the pinned `catalogHash` and a UTC
allocation deadline 60 seconds ahead. GameHost rejects catalog mismatch
before allocation, then returns its map, digest and logged-on Steam ID.
Master only broadcasts to those rostered clients a
typed `descriptor` with `transport: "steam-p2p"`, `serverSteamId` as a decimal
string, the same `matchId`, `virtualPort: 0`, `protocolVersion: 2`,
`contentHash`, and `admissionExpiresAtUtc`. No IP-overloaded Steam identity,
JWT gameplay admission or old UDP fallback in VPS mode. Development-only
UDP remains a separate explicit profile.

The Shared Steam frame contract uses reliable `Join=1`
(`matchId` GUID D ASCII, protocol UInt16 little-endian and lowercase
64-character SHA-256 digest), `Ack=2` (assigned entity UInt64 little-endian)
and `Deny=3` (reason). Unreliable `Input=0x10`, `State=0x11` and
`Event=0x12` prefix existing Shared payloads; `Result=0x13` is sent
reliably once rather than queued every tick. The client uses only an admitted
ACK for its own assigned entity; the GameHost binds input to the authenticated
Steam handle, not the packet's entity claim.

The single GameHost Steam listener accepts a bounded reliable join naming
`matchId`, protocol version and content digest, then binds the Steam-authenticated
remote SteamID to that match's entity slot. Subsequent inputs are scoped by
the bound connection, never by a packet-claimed entity. Normal completion
reports a result once; waiting/absence/restart sends a private authenticated
`match/cancel` with the same GUID and a reason, records cancellation without
winner/MMR, and notifies rostered lobby connections once. Master registration
rotation invalidates stale descriptors; already admitted matches do not need
new Steam web tickets during a temporary Master outage.

### 2D — Packaged Playtest and cutover

Build the Playtest Windows player via the installed Unity CLI/Pipeline, upload with `scripts/steam-playtest.sh`, and deploy pinned Master/GameHost image digests and migration bundle with `deploy/vps/release.py`. The older direct-Editor `scripts/build-release.sh` is not this Steam test build. The packaged player selects the VPS Master **before** authentication, never the home host or a tester shell override. The isolated SteamCMD game-server runtime mount is checksum-pinned for this test; confirm permitted dedicated-server redistribution before production packaging. Never distribute `steam_appid.txt`, publisher credentials or a guest/UDP fallback.

For friends outside a fixed IP, public IPv4 TCP 443 may reach the test Master
through host UFW and Docker-aware Caddy forwarding. TCP 80 is for ACME/redirect;
management SSH remains CIDR-restricted. Keep IPv6 policy explicit and tested;
do not publish GameHost UDP or private Master/control/database ports. Steam
tickets/entitlements and per-source POST quotas remain in force, but anonymous
Internet traffic and resource abuse are possible. Verify the network policy
from an independent source and confirm HTTPS readiness after each authorized
change; don't infer provider firewall behavior from host rules.

The Steam-installed **Windows** Playtest build must also prove Steam initialization, the expected GameHost identity, authenticated relay handshake and a short packet exchange on real Windows hardware. Its compiled `steam_api64.dll` is packaging evidence only. This check moved from 2A to the 2D acceptance ticket by explicit decision; do not report it as passed from the Linux route proof.

Run two real Steam accounts on packaged clients: install/launch → verified login → chat → browse/join → select fighter/stage → authoritative match → results → lobby → rematch. Check Steam unavailable at login, Master/Steam API outage during an existing match, non-rostered or stale join, account switching, duplicate connection, content/protocol mismatch, brief disconnect, GameHost restart and an abandoned waiting match. Record Playtest AppID/build IDs, client/server revisions/image digests, route/RTT, match/abort outcomes and remaining limitations. No production raw-UDP/guest fallback; exercise maintenance/roll-forward rather than treating a legacy public rollback as safe. Phase 1 #234 is closed as a historical handoff; its unverified observations are not retroactive Phase 2 proof.

Keep an admitted match active for at least 90 seconds with healthy state
traffic; no elapsed-connection-age timer may force a 30-second reconnect.
Stalled initial Steam connection and missing reliable join ACK must still
fail within their separate 30-second and 15-second deadlines. Simulate a
Steam backend outage separately from a confirmed account switch, and expire
a short-lived open WebSocket separately from gameplay to verify fresh-token
chat reconnection.

## Implementation tickets

| Gate | SLOPArena | MasterServer |
| --- | --- | --- |
| 2A | [#235 — runtime and VPS proof](https://github.com/Binoui/SlopArena/issues/235) | — |
| 2B | [#236 — client session](https://github.com/Binoui/SlopArena/issues/236) | [#14 — Steam auth](https://github.com/Binoui/SlopArena-MasterServer/issues/14) |
| 2C | [#237 — GameHost admission](https://github.com/Binoui/SlopArena/issues/237), [#238 — PvP connection](https://github.com/Binoui/SlopArena/issues/238) | [#15 — host/match contract](https://github.com/Binoui/SlopArena-MasterServer/issues/15) |
| 2D | [#239 — controlled candidate](https://github.com/Binoui/SlopArena/issues/239), [#240 — live acceptance](https://github.com/Binoui/SlopArena/issues/240) | Coordinated deployment/verification in #240 |

2A is a stop gate for the rest. 2B's Master and client work can proceed together after 2A; 2C's control payload must be agreed across both repositories before either side treats it as stable. Neither ticket creation nor this local document publishes a Steam branch or changes a deployed service.

## Sources and current seams

- [Valve SDR/dedicated-server routing](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay), [SteamNetworkingSockets](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets), [backend authentication/ownership](https://partner.steamgames.com/doc/features/auth), [Steam Playtest](https://partner.steamgames.com/doc/features/playtest), [Steamworks.NET](https://steamworks.github.io/).
- [`scripts/steam-playtest.sh`](../../scripts/steam-playtest.sh) names the candidate Playtest AppID/depot; verify against operator-owned Steamworks settings.
- [`ChatSession`](../../client/Unity/Assets/Scripts/Runtime/Network/ChatSession.cs) and [`MasterServerClient`](../../src/Shared/MasterServerClient.cs) own one Steam-backed application session with an explicit Editor guest path. [`NetworkClient`](../../client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs) uses Steam P2P for packaged PvP and keeps raw UDP only behind the Editor development opt-in; [`MatchControlServer`](../../src/Server/MatchControlServer.cs), [`MultiMatchOrchestrator`](../../src/Server/MultiMatchOrchestrator.cs), and [`MatchInstance`](../../src/Server/MatchInstance.cs) own authoritative match control, allocation and Steam input routing.
- Master pre-creates a match row before `POST /match/start`, pins its roster and catalog digest, and uses a private match-control credential (`Program.cs`, `Lobbies/HttpMatchLauncher.cs` in the separate repository). The Steam admission/descriptor code has been implemented; packaged two-account Steam acceptance remains required.
