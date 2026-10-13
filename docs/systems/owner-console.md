# Hosted owner console

The first hosted console uses the existing Playtest HTTPS site at
`https://master-test.sloparena.barakaslurp.fr/console/`. Caddy requires the
owner's username and password for pages, assets and API requests. The console's
backend calls Master over the private Docker network and supplies its mounted
Master key. The browser never receives that key.

The console lives in the separate `SlopArena-console` repository. Its image,
Compose project and release receipt are independent of the Steam client and
Master/GameHost pair. The console image workflow tests local and hosted access,
packages a pinned Node 24 LTS runtime and runs a real Caddy authentication smoke
test after an image archive round trip before publishing the immutable image
digest. Private delivery uses the Actions image archive, verified against its
receipt checksum and image content ID, without VPS registry credentials.

## Runtime boundary

The common Caddyfile imports `/etc/caddy/console/*.caddy` inside the existing
Playtest host. The common Compose file mounts
`/var/lib/sloparena/private/console-caddy` read-only at that path. An empty
directory is supported, so a game deployment does not require a console.

The console has no published host port. It joins the existing private proxy
network, and Caddy forwards authenticated requests to `console:4173` after
stripping `/console/`, browser authorization and browser-supplied Master keys.
A separate private gateway token authenticates Caddy to the console backend.
Direct container requests without that token cannot read pages or owner APIs.
The credential-free health endpoint returns only process health.

Public Master `/owner` routes remain excluded. Game, database, match-control
ports, Steam admission and gameplay authority are unaffected.

## Provisioning and updates

Use the console repository's `deploy/deploy.py` with its exact CI image/source
receipt and checksum-pinned archive in an authenticated operator sudo session.
First provisioning asks for
an owner password through a masked terminal prompt, hashes it with Caddy via
stdin, copies the existing Master key privately and starts the separate console.
It validates Caddy before applying the gateway. Adding Caddy's configuration
mount requires a Caddy recreation and a fresh zero-active-match gate. Later
console updates preserve the mount and reload Caddy gracefully.

The procedure checks archive checksum, image content ID/source labels, nonroot container health,
anonymous/wrong-password refusal, authenticated observation and public Master
owner exclusion. It retains private runtime state and sanitized release receipts
under `/var/lib/sloparena-console`, preserves the previous configuration on
failure and verifies that Master/GameHost did not restart. It does not install
a new authentication service, deploy gameplay images or upload a Steam client.

Keep passwords, password hashes, gateway tokens and Master keys in private
operator files/interfaces. Owner login and the Master console key are distinct.
If the Master key rotates, rerun the console deployment to update its mounted
key. Never paste a Master key into the hosted browser.

## Local development

The loopback console and SSH tunnel remain available for development and agent
diagnostics. See the [VPS diagnostic export](../../deploy/vps/README.md#match-diagnostics)
for that path. Hosted observation starts automatically after owner login.
Pause/Resume clears and reloads captured data; it does not log out of HTTP Basic
authentication. End the authenticated browser session or clear its saved
credentials to log out.
