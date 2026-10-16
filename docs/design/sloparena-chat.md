# SLOPArena Chat

**Status:** The original Global/Direct chat foundation was approved under #206. Issue #243 retargets Server Chat to authenticated Master Room membership. Unity client Room-chat changes are implemented; native and real SignalR integration validation remain pending.

## Goal and scope

Provide SLOPArena-owned Global Chat, Server Chat, and online Direct Messages before Steam integration. Chat is separate from Steam chat and from authoritative gameplay.

Only the developer has played the game at the time of approval. Existing systems may change when needed; preserving current guest identities is not a requirement. Steam integration may reset every guest identity. Steam authentication and Steam social features are not part of this task.

Canonical terms are in the [multiplayer glossary](../../CONTEXT.md#pvp--multiplayer). This document records behavior and implementation constraints; the glossary records language.

## Channels and availability

| Channel | Audience | Joining and leaving |
| --- | --- | --- |
| Global Chat | All connected SLOPArena Chat participants, including players in menus and Training | Available throughout the game after chat connects |
| Server Chat | Members of the same Master Room | Starts when Room membership is admitted; ends on leave, revocation, or membership expiry |
| Direct Message | The sender and one selected, currently connected recipient | Started through the game-wide online-player picker; no offline delivery |

Server Chat is scoped to one Master Room, not a physical GameHost lobby or an individual match. Distinct Rooms have isolated Server Chat even when their members use the same physical GameHost.

A player has access only to the Server Chat for their currently attached Room membership. Knowing or guessing a Room ID does not grant access. Leaving or losing membership clears that Room conversation without disconnecting Global Chat or Direct Messages. Reconnect must revalidate current identity and Room attachment membership.

The online-player picker shows connected players by Display Name and Session Tag, including players in menus, Training, and other Rooms. It is not a friends list.

## Guest identity and display names

- Create a new guest identity for each game launch. Reuse that identity across temporary chat reconnects during the launch.
- Do not sign in as another guest when changing scenes or opening the Server Browser. Authentication renewal must not silently substitute another identity.
- Before first online/chat use, ask for a Display Name. Do not block Training or Solo while the player has not chosen one.
- Remember the name locally. On later launches, connect automatically with that name and a new guest identity.
- Use the same Display Name for chat, lobby, and match presentation. Do not introduce a chat-only alias.
- Duplicate names are allowed. A short Session Tag must distinguish players; Direct Messages and Personal Mute target identity, never name.
- Allow renaming only while the player is not joined to a GameServer. Preserve the guest identity and Session Tag, update current directory/presentation data, and save the new name locally.
- Earlier messages retain the name used when sent. Renaming does not reattribute old messages or evade Personal Mute.
- A player who relaunches is a new identity, even if the saved name is unchanged. An old Direct conversation must not automatically target that new identity.

Guest identity is not proof of a Steam identity. Use the existing authenticated identity path where it fits; do not create a second chat account system.

## Message rules and limits

These values were approved during the design interview.

| Rule | Value |
| --- | --- |
| Display Name length | 1–24 characters after trimming |
| Message length | At most 500 characters |
| Send rate | At most five sends per five seconds per guest, shared across all channels |
| Public Channel Backlog | Last 50 messages per Global/Server channel, in Master memory only |
| Local scrollback | Last 50 messages per channel or Direct conversation, during the current launch |
| Combat feed | At most three public-message lines; fade after eight seconds |

Render names and messages literally. URLs remain text. There are no clickable links, formatting, attachments, or previews. Empty messages and invalid names must fail validation. Client feedback and Master validation must agree on length handling, including Unicode. Rejected messages must not silently become different text.

The Master contract counts Unicode scalar values, not UTF-16 code units or grapheme
clusters. The client must use the same count. Master preserves accepted message
text, including literal line breaks and tabs; it rejects malformed Unicode and
other control characters.

Master determines sender identity, the current sender name, and message attribution. Client-supplied sender fields cannot impersonate another player. Validate Server Chat membership against the current authenticated identity and RoomManager attachment on the server; knowing a Room ID does not grant access. Physical GameServer membership never grants Server Chat.

On the existing authenticated SignalR connection, Server-channel `ChatMessage` and
`ServerChatState` carry `RoomId`; `ChatSnapshot.Server` remains the snapshot property.
`SendServer(roomId, text)` keeps the existing RPC name, and `ChatServerChanged`
delivers the Room ID with its messages or a null state when membership ends. The
Unity conversation key is `room:<guid>`; physical GameServer IDs never select it.

Enforce limits on Master, not only in the UI. Switching channels or opening another connection must not reset the guest's send allowance. Keep authentication/connection protections, but separate chat message limits from lobby/control traffic.

Bound total client storage, pending work, and server backlog storage as well as each individual history. Per-conversation limits are not permission to create an unbounded number of retained conversations. Operational logs must not become persistent chat-body storage.

## History, reconnect, and send feedback

Global Chat and Server Chat each provide up to 50 earlier messages when a participant joins. The backlog exists only in the current Master process; a Master restart clears it. It is not a permanent archive.

Clients retain bounded received history and drafts across scenes and temporary chat outages while the same identity and Room membership remain authorized. Leaving, losing membership, or switching Rooms discards that Room conversation. Already-received Direct Messages may remain in local scrollback for the launch; this is not an offline inbox. A later game launch starts with no saved conversations.

Reconnect automatically, but never automatically replay unsent or unconfirmed sends. Preserve the draft, show connection state, and let the player decide whether to resend. If acceptance is uncertain, do not falsely report definite success or failure.

A send confirmation means Master accepted the message, not that another person read it. There is no delivery/read-receipt system. A known-offline Direct recipient produces a clear failure; the message is not queued for a later login.

Use stable message identity to merge public backlog with local scrollback without duplicate lines. Keep histories separated by channel and Room ID. Leaving, losing membership, or switching Rooms clears the previous Room conversation; queued messages for that Room must not appear in a later Room.

Chat failure must not block local play or interrupt an already-running fight. Authentication and membership failures must be visible; they must not silently change identity, recipient, or destination.

## Personal controls

Provide Personal Mute for the current game launch. It hides the selected identity's messages and alerts across all channels. Match it by identity, not Display Name, so a rename does not bypass it.

Personal Mute is a local display control, not a ban or a delivery guarantee. A guest can evade it by relaunching under a new identity. This limitation is accepted for the pre-Steam design.

There are no reports, bans, moderation dashboard, or durable account-blocking system in this scope.

## Interface and input

Use one shared chat interface across menus, Training, Room Lobby, fights, results, and rematches. Preserve the existing [visual language](visual-language.md); this task does not authorize a redesign of unrelated menus or HUD elements.

The presenter reuses one `ChatOverlay` across Frontend and gameplay hosts. It has no
title header: Expand and Minimize icons sit immediately left of the top-right resize
grip, and the active direct conversation is named on its DIRECT tab. Identity stays in
the shell, while connection and send failures render as a line inside the message
history instead of below the composer. Dragging the frame's corner saves one shared
physical width/height in PlayerPrefs and reapplies it across scenes and launches,
clamped to each host's usable viewport. The menu chat keeps its bottom-left cell;
gameplay chat anchors to the left side of the HUD. Match entry still closes
interactive chat so retained presentation preferences never bypass input gating.

Expanded chat has separate Global and Server tabs, plus a Direct area with a conversation list. Keep the active destination visible beside the composer. Server Chat keeps the `SERVER CHAT` label and is available from the shared chat UI, including Room Lobby, only while Room membership is active. Do not silently redirect a draft when its Room or recipient becomes unavailable.

During combat, show a small fading feed of clearly labeled Global/Server messages authorized for the current Room. Apply the three-line/eight-second defaults without covering stocks, damage, move indicators, or other critical combat information.

Direct Messages use a quiet unread indicator. Do not put their text in the public fading feed or add notification sounds. Incoming messages must not steal keyboard focus.

While reading older messages, do not force-scroll to the newest message. Provide an explicit way to return to the newest messages.

### Combat typing

- Enter opens and focuses the composer.
- The next Enter submits the message and closes the composer, returning control to the fight.
- Escape closes without sending and retains the draft.
- While composing, suppress human gameplay input, camera input, and conflicting background shortcuts.
- Consume the opening/closing keys and clear buffered gameplay actions. Typing must not trigger attacks or other stale actions when chat closes.
- Do not pause the simulation, grant protection, or otherwise change gameplay rules. The fighter remains vulnerable while typing.

The send-and-close rule is specifically for active gameplay. UI focus, cursor ownership, and existing menus must remain consistent across transitions.

Replace local-only chat echoes, fabricated messages, and fabricated online counts with real state. An unavailable directory is not evidence that zero players are online; show connection/error state honestly.

## Implementation boundary and repository evidence

Use the existing Master repository and authenticated SignalR transport. No fourth repository, separate chat process, or raw-WebSocket rewrite is required. Keep chat outside Shared simulation and GameServer gameplay traffic. Server Chat access comes only from RoomManager's authenticated identity and current connection attachment; physical GameServer lobby membership is not authorization.

Use one game-wide owner for the authenticated guest session, connection lifecycle, and main-thread event delivery. Room membership is a state within that session, not the lifetime of all chat. Reuse existing patterns instead of adding a speculative identity framework or transport abstraction.

The following source facts were checked on 2026-09-16. They describe code, not a verified deployed service:

| Evidence | Implementation consequence |
| --- | --- |
| [LobbyClient](../../client/Unity/Assets/Scripts/Runtime/Network/LobbyClient.cs) already uses authenticated SignalR and forces long polling because of recorded standalone/Proton WebSocket failures | Reuse this transport; do not assume a raw WebSocket client is an equivalent replacement |
| [LobbyRoomUI](../../client/Unity/Assets/Scripts/Runtime/UI/LobbyRoomUI.cs) creates the current lobby connection; [ServerBrowserUI](../../client/Unity/Assets/Scripts/Runtime/UI/ServerBrowserUI.cs) performs guest sign-ins | Move lifetime ownership out of individual screens so scene changes cannot create extra guest identities or stop chat |
| [MasterServerClient](../../src/Shared/MasterServerClient.cs) already handles guest JWTs and numeric IDs | Reuse the authenticated player identity; the current field name `SteamId` does not mean a verified Steam user |
| [Master Program.cs](https://github.com/Binoui/SlopArena-MasterServer/blob/main/Program.cs) issues guest identities and applies a shared per-IP POST limit | Support chosen names and prevent chat traffic from consuming the control/authentication request budget; do not merely raise one shared limit |
| [Master LobbyHub](https://github.com/Binoui/SlopArena-MasterServer/blob/main/Hubs/LobbyHub.cs) owns authenticated lobby and Room membership | Derive Server Chat access from RoomManager identity+connection attachment; never trust a client-selected Room ID or physical server membership |
| [InputController](../../client/Unity/Assets/Scripts/Runtime/Input/InputController.cs) polls hardware directly | A focused text field alone cannot isolate gameplay input; cover all human-input and camera paths |
| [ResultsUI](../../client/Unity/Assets/Scripts/Runtime/UI/ResultsUI.cs) only appends local chat labels, and [Results.uxml](../../client/Unity/Assets/UI/Results.uxml) contains fabricated activity | Replace the placeholder behavior rather than treating it as an existing networked chat implementation |

Changes span the Unity client and Master repository. Keep their protocol compatible within the implemented change. No migration of current identities, Steam integration, gameplay-protocol rewrite, new dependency, publishing, or deployment is implied by this approved design.

### Master-only delivery

The Master implementation provides named guest sessions, same-identity token
renewal, presence, all three channel routes, public history, and separate quotas.
Its `README.md`, under **Chat client contract**, records the concrete HTTP/hub
methods, event payloads, error codes, resource ceilings, and reconnect rules.
The implementation uses the existing guest identity and hub, not a second account
or transport. It removes the unused Shared package dependency; the official
SignalR client dependency is test-only.

Headless checks use real SignalR long-polling clients through ASP.NET. The local
Kestrel smoke uses isolated EF InMemory storage and a test match-launch boundary.
This is not PostgreSQL deployment, real GameServer, or native Unity acceptance.
Deployment is not part of this delivery.

[#201](https://github.com/Binoui/SlopArena/issues/201) depends on the backend ticket.
It and [#202](https://github.com/Binoui/SlopArena/issues/202),
[#203](https://github.com/Binoui/SlopArena/issues/203),
[#204](https://github.com/Binoui/SlopArena/issues/204), and
[#205](https://github.com/Binoui/SlopArena/issues/205) stay open for client integration.

## Acceptance checks

End-to-end completion still requires these observable results. Master-only headless
checks do not satisfy the native UI, input, local mute, or scene-lifetime checks.

| Scenario | Required result |
| --- | --- |
| Players in menus, Training, and separate Rooms use Global Chat | All connected chat participants receive it, independent of scene or Room membership |
| Members of one Room use Server Chat | They share only that Room's Server Chat; another Room remains isolated even with overlapping physical GameHost assumptions |
| A player leaves, loses membership, or switches Rooms | Previous Room history and draft are cleared; late pushes are rejected; Global and Direct remain available |
| Physical GameServer lobby admission or `JOIN BY ADDRESS` | Gameplay admission still works, but grants no Server Chat access |
| A client reconnects or rejoins a Room | Revalidate membership and restore only that Room's authorized Master history; no old Room draft/history crosses |
| Two players use the same name | Session Tags distinguish them; Direct and mute select the intended identity, not a name match |
| A recipient is offline, or relaunches under the same name | No offline queue and no automatic retargeting of the old Direct conversation |
| A player renames outside a GameServer | Current name updates without changing identity/tag; earlier messages keep the old name; mute still applies; renaming while joined is rejected |
| A muted sender posts to Global, Server, or Direct | Their messages and alerts remain hidden during the muting player's current launch |
| A chat client reconnects | Global backlog merges by stable ID; active Room history is revalidated; drafts survive only while the same Room membership remains; no unconfirmed send is resent |
| Master restarts | Its public backlog is gone; local received history is not fabricated or silently presented as a new backlog |
| A sender exceeds limits, supplies spoofed sender fields, or guesses an unauthorized Room ID | Master enforces length/rate/membership and authentic attribution; other players' lobby/control actions remain usable |
| Text contains markup-like input or Unicode | It renders as literal text, with consistent validation; it cannot inject formatting or another sender identity |
| Chat opens, sends, and closes during a fight | Typing cannot move, attack, steer the camera, or activate background shortcuts; closing does not release buffered attacks; simulation and vulnerability continue |
| Two real clients exchange messages through the shared UI | Live delivery works across menus, Training, lobby/select, fight, results, and rematch; no fixture activity or local-only success is presented as real delivery |
| Native UI is inspected at 16:9 and approximately 2:1 | Feed, tabs, picker, draft, scrollback, and failure states remain usable without covering critical HUD information |

Follow the applicable [verification mode](../testing.md) and [Unity CLI workflow](../contributing/unity-cli.md) when verifying this client integration. Protocol checks do not replace native input/focus and real-client delivery checks.
