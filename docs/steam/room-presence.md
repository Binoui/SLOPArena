# Steam Room presence — Playtest operator handoff

Issue #255 publishes the current Master Room to Steam Rich Presence. `connect` is
`+slop_room <Room GUID in D format>` only when the Master confirms a joinable
Room and the authenticated application session is usable. It is a locator,
not admission. Room grouping persists through selection, match and rematch;
Join Game from friends/invites is routed by issue #256 through the existing
Steam-authenticated Master Room flow; Steam IDs and Room IDs alone never grant
admission. Local Training has no Room presence. Neither Steamworks setting
below is configured by the client code.

## Operator actions (not performed by the implementation)

- [ ] In Steamworks, select **SLOPArena Playtest AppID 5325920** (not the
  parent app). Open **Edit Steamworks Settings → Community → Rich Presence**.
- [ ] Upload [`rich_presence_english.vdf`](rich_presence_english.vdf) as the
  English rich-presence localization and **publish** the Steamworks changes.
  Verify `#SlopArenaRoom` resolves to a visible string in the modern friends
  list. `SetRichPresence` returning true or `status` displaying in game info
  alone does not prove this.
- [ ] Check [Valve's rich-presence tester](https://steamcommunity.com/dev/testrichpresence)
  while signed into Steam on the web, then inspect the actual friends UI with
  **two entitled Playtest accounts** and installed clients. Observe Room
  name/phase/member count and grouping across Lobby → Character Select →
  Stage Select → Match Starting → In Match → Results/rematch (Results may remain
  onscreen while the Master has already returned the Room to Lobby).
- [ ] Check Join Game appears for a joinable Lobby, disappears on fullness,
  selection, match, leave, lost membership and application-session expiry,
  and returns after resynchronization or rematch. Check reconnect, account
  teardown, normal shutdown and offline Training. This is a human acceptance
  gate, not proven by Editor compilation or the localization file.
- [ ] Under **Installation → General** for **SLOPArena Playtest AppID 5325920**,
  enable **Use launch command line** and publish the Steamworks settings.
  [Valve's `GetLaunchCommandLine` documentation](https://partner.steamgames.com/doc/api/ISteamApps#GetLaunchCommandLine)
  requires this setting to deliver launch data through Steam rather than the
  operating-system command line. Do not apply the setting to the parent AppID.
- [ ] With two entitled accounts and Steam-installed clients, verify a warm
  Join Game and a cold Join Game (including first-use display-name setup)
  enter the advertised Room and its Server Chat. Test a second Steam URL
  while the client runs; duplicate notifications must not produce duplicate joins.
- [ ] Check full/selecting/in-match/deleted targets, malformed and manually
  constructed Room IDs, another-Room confirm/cancel/phase race, local Training
  confirm/cancel, same-Room request, and an active online fight. Exercise
  disconnect/reconnect, account change and an uncertain leave outcome. Record
  Windows and Proton platform, Steam BuildID, installed build, published
  settings and observations; Editor parser tests do not prove either launch path.
- [ ] **After #257's local implementation is verified**, run the two-player
  installed-client social session (deferred at the user's request). From a
  joinable public Room, any member—not just the leader—opens **INVITE FRIEND**
  and selects a Steam friend in the native overlay. Cancel once and verify
  membership, selections, focus and input recover without pausing simulation.
  Merely opening this dialog does **not** prove delivery, read or acceptance.
- [ ] Accept the invitation with the recipient closed, then already running.
  Confirm both paths end in the sender's existing Master Room and Server Chat.
  Separately verify friends-list Join Game while already running; it must not
  depend on the in-Room invite button. Then play selection → stage → real match
  → Results → same Room → rematch.
- [ ] Exercise disabled Steam overlay; full, preparing, match-in-progress and
  deleted Rooms; invitation sent before a Room becomes unjoinable; leadership
  changes; same-Room request; another-Room confirmation; and a request during
  an online fight. The native dialog reserves no seat and its stale payload
  still goes through normal Master authentication and admission.
- [ ] Test installed **Windows and Proton** clients and record Steam BuildID,
  Master/GameHost versions, Playtest Steamworks settings and observed join,
  chat and focus outcomes. Mark unavailable hardware/accounts pending; Unity
  compile, injected callback tests and a single-client overlay do not close
  this gate.
- [ ] Do not upload a build, activate a branch, deploy a server or mutate
  Steamworks settings as part of code implementation. Publish settings and
  distributables only after the operator accepts the installed-client test.

[Valve's API](https://partner.steamgames.com/doc/api/ISteamFriends#SetRichPresence)
requires a valid uploaded `steam_display` token for modern friends-list text;
[Enhanced Rich Presence](https://partner.steamgames.com/doc/features/enhancedrichpresence)
documents grouping and the web tester. The localization file is source to
upload; shipping it with Unity alone does not publish the token.
