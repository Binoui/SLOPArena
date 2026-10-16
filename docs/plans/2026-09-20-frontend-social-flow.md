# Frontend and social flow rework

Status: design approved on 2026-09-20; runtime implementation not authorized by this design session.

## Goal and visual direction

Make SLOPArena feel like one connected place while players prepare and finish matches. Persistent social presence is the primary outcome, not merely removing chat overlap. This is a flow/social rework, not a final visual redesign.

Preserve the fight-poster identity in [the visual language](../design/visual-language.md) and [DESIGN.md](../../DESIGN.md), permitting targeted evolution of typography, density, decoration and composition. Shared framing must keep primary actions readable and outside the reserved social region. Do not introduce a large permanent navigation sidebar.

## Scope and architecture

- Consolidate Home, ServerBrowser, LobbyRoom, Fighter Select, Stage Select and Results into **one frontend scene** with explicit page activation and cleanup. Results is included, not a standalone exception.
- Shared UI owns framing, identity controls, page header/content/actions, social space and modal presentation. Individual pages supply their content and flow-specific actions.
- Gameplay scenes remain separate. Unload the frontend for gameplay and recreate it on return. Persistent social/session state and its presentation survive independently of the frontend scene.
- Replace scene-per-page navigation, rather than retaining the old full-screen page documents behind a persistent overlay. Preserve valid networking and flow semantics while moving their lifecycle boundaries.
- Keep existing social/session ownership, backend protocols, cooked content, match admission and server-authoritative gameplay. Lobby authority and ownership of a hosted server process remain distinct.
- Retain Training's direct Fighter Select → gameplay route; Solo uses Stage Select. Online preparation remains server-driven.
- Later styling changes must not require another navigation/lifecycle rewrite.

See [ADR-0032](../adr/0032-single-frontend-scene-with-persistent-social-ui.md) for the accepted architectural trade-off.

## Social presentation

> **Amendment (2026-09-21, #218/#219).** The bottom-left reserved conversation
> panel replaces the two presentations below: the right-hand Social Dock and
> the automatic compact replacement. The conversation panel is a permanent
> layout cell in the `FrontendShell` lower row — the shell reserves the cell
> structurally, so pages no longer need per-page offsets, a percentage split,
> or host-page discovery. The presenter attaches once to the shell's social
> host and keeps its state across page changes; an explicit expanded social
> view (not an automatic width replacement) is the only deliberate occlusion,
> using the same presenter. The architecture and nonvisual social/identity
> contracts of this brief — social/session ownership, read rules, draft and
> retention rules, identity rules, navigation and operation lifetime, return
destinations, and acceptance honesty — remain in force. The superseded
presentation baseline below is retained as historical context only.

### Superseded presentation baseline (historical)

The following two bullets describe the presentation baseline considered before the
2026-09-21 amendment. They are retained only to explain what #218/#219 replaced;
they are not current requirements:

- The pre-amendment roomy-layout proposal reserved a right-hand Social Dock in normal page layout, initially open, with an open/collapsed preference remembered across launches.
- The pre-amendment compact-layout proposal retained a social strip whose opened chat temporarily replaced page content; closing chat restored the page and its selections. That proposal also kept the compact presentation from overwriting the remembered roomy-layout preference.

### Menus and Results

The following social/session contracts remain current:
- Reuse Global Chat, Server Chat, Direct Messages, conversation switching, online-player access and composer capabilities. Server Chat follows current Master Room attachment, not physical GameServer membership or an individual match.
- Distinguish unavailable/disconnected directory state from a genuine zero-player result. Social readiness, GameServer membership and gameplay connectivity are distinct; do not present one as proof of the others.
- Within a launch, preserve the selected conversation, drafts and per-conversation scroll anchors through page changes and gameplay. Reconnect preserves Room state only while that same membership is revalidated; leaving/revocation discards the Room draft/history.
- If incoming messages evict the viewed history anchor from the existing 50-message buffer, return to newest with an explanation. Do not expand retention merely to preserve the anchor. The normal read rule then applies.
- Do not persist conversation history, drafts or scroll state to disk. Display Name and social-layout preference are the cross-launch preferences in this scope.

### Read state and combat

- A conversation clears unread only when selected, unobscured, at its newest messages and in the focused application. An underlying social view beneath a modal, an unfocused window, another selected conversation or an older scroll position does not satisfy this rule.
- A brief combat preview alone does not clear unread. Preserve personal mute behavior.
- During combat, show brief Global/Server message text and Direct Message unread indicators. Do not automatically expose private-message text.
- Interactive chat is explicitly opened. Merely displaying the brief feed never captures gameplay input.

## Identity

- Require a Display Name on first launch, with no skip-to-local-play action before a locally valid name is entered. Physical keyboard entry is acceptable and required; no virtual keyboard is included.
- Validate and save the chosen name locally even when services are unavailable. After local acceptance, Solo and Training remain available without waiting for the network.
- Apply the saved name remotely when connectivity is available. If rejected, report the failure and request correction; do not claim that social connection succeeded.
- Reload the saved name on future launches. A remembered Display Name is not a persistent account or player identity; Guest Session identity remains launch-scoped.
- Identity entry and later rename access belong to the shared identity surface, not inside hidden chat controls. Retain the existing prohibition on renaming while joined to a GameServer.
- Steam identity replacement is future work, not part of this delivery.

## Input and focus

- Support mouse, keyboard and full gamepad navigation across all scoped pages and social controls. Name and message text entry require a physical keyboard.
- Provide an explicit, visible Page/Social region-switch action. Navigation stays within the active region; remember each region's last valid focused control.
- Visibility is not input ownership. The social view may remain visible while the player selects fighters or stages.
- Back/Escape first closes the topmost modal. Otherwise, if interacting with chat, one press leaves editing/navigation and returns page focus without discarding the draft. Close an explicitly expanded social view before invoking page Back. Only a subsequent press invokes page Back.
- One cancel press performs one action. Restore focus to its valid origin; never retain focus or callbacks to a deactivated page.
- Interactive combat chat suppresses gameplay input while navigating or typing, not only when a text field has focus. Online simulation continues; this is not a pause request.
- Preserve held-input release protection when returning control, including after a match transition. Do not leak the opening/closing input into gameplay.
- Existing pause/training controls remain functional. Do not add a general settings surface or independently redesign pause policy as part of this work.

## Navigation and operation lifetime

### Page transitions

- Page activation/deactivation replaces scene-load teardown for event subscriptions, asynchronous operations, controls and callbacks.
- Back remains a flow-specific action, not a generic previous-page pop. Preserve leave-room, cancel-host and other operation-specific semantics.
- Back during hosting, joining or address lookup navigates away immediately, cancels work and ignores stale completions. If remote membership or hosting completes concurrently, reconcile and clean it up. Prevent conflicting new operations until cleanup is safe.
- Page teardown must neither retain an unwanted server process nor destroy one whose ownership was legitimately transferred. Never derive lobby authority solely from local process ownership.
- Server-driven menu changes update the underlying page while preserving the social view and draft. Close dialogs owned by the departed page and update visible flow context.
- Authoritative match entry wins over chat editing and overlays: enter gameplay, retain the unsent draft/conversation, exit interactive chat, never auto-send, and apply held-input release protection.
- Home intentionally resets match-preparation choices. Social state remains independent.

### Return destinations

| Event | Destination and state |
|---|---|
| Early Solo exit | Solo Fighter Select; retain local choices until Home is entered |
| Early Training exit | Training Fighter Select; retain local choices until Home is entered |
| Early PvP exit | ServerBrowser after leaving GameServer membership; Global/Direct chat remains alive |
| Completed local match, after Results | Home; reset preparation choices |
| Completed PvP match, after Results | Existing LobbyRoom if membership is valid; otherwise ServerBrowser with an explanation |

Do not silently restore online readiness from local preferences or invent a rematch protocol. Server snapshots remain authoritative for online roster, host role and selection state.

## Explicit non-goals

- Audio settings, settings cog/modal, volume persistence or audio-system redesign.
- Steam integration, a new account system, new chat backend, offline Direct Message delivery or disk-backed chat history.
- Gameplay, content packages, matchmaking or network protocol redesign.
- A permanent frontend scene running alongside gameplay; the frontend is recreated on return.
- A virtual keyboard or controller-only text composition.
- A full visual-language replacement or speculative reusable UI framework.
- Successful live multi-client online acceptance as a gate for this delivery; it is explicitly deferred, not implied by local proof.

## Acceptance and proof

Implementation acceptance requires the actual Unity surface, not browser mockups, source inspection or compilation alone. Use the installed Unity CLI/Pipeline workflow and the applicable local verification mode in [testing.md](../testing.md).

1. Inspect at 1280 × 720, 1280 × 800 and a roomy desktop viewport. Required actions remain legible and accessible; chat does not obscure them. Expanded/collapsed transitions preserve preference, page state and draft.
2. Exercise real controller navigation, keyboard focus, Page/Social switching, modal cancel and focus restoration. Text entry still uses a keyboard. One cancel input must not both leave chat and navigate Back.
3. Launch Solo and Training through the consolidated frontend, enter gameplay, leave to their mode-appropriate selection page and re-enter. Verify frontend recreation and no duplicate input/event subscriptions.
4. Verify available local Results flow returns Home; shared social presentation restores its menu state. Home resets preparation without erasing the current social session.
5. Exercise first launch, mandatory name validation, offline local saving, next-launch name restoration and available rejection/reconnect paths. A network failure after local name acceptance must not block local play.
6. Navigate with an unfinished draft and scrolled conversation; verify retention, unread/occlusion/focus rules and the history-eviction return-to-newest policy. Test public combat previews versus private unread indicators and no unintended sends.
7. Exercise cancellation/failure paths for browsing, address lookup, hosting and available joining operations, including departure before completion. Report which remote races could actually be exercised; do not substitute mocked success for live proof.
8. Verify scene/page transitions do not restart an already-playing menu track or duplicate chat/session connections. Match UI/pause uses its intended host, not whichever document is discovered first.

**Deferred proof:** a successful live host/guest preparation → match → Results → lobby loop, live host promotion/non-host authority, and server-driven transitions/races requiring multiple connected clients. These behaviors remain implementation requirements, but successful live multi-client verification is outside the selected acceptance gate. Report them as unverified until exercised.

## Historical code evidence behind the decisions

These source observations were recorded during design approval on 2026-09-20,
before implementation. They describe the historical baseline and are not claims
about current runtime behavior:

- `Runtime/UI/ChatOverlay.cs`: at that baseline, it collapsed on scene changes, attached to a scene document and captured gameplay when expanded; it did not own a reserved menu region.
- `Runtime/Network/ChatSession.cs`: at that baseline, it already owned launch-persistent social state and loaded saved names, but saved a new name only after remote acceptance.
- `Runtime/UI/ServerBrowserUI.cs` and `LobbyRoomUI.cs`: at that baseline, page lifetimes carried meaningful cancellation, subscription, membership and hosting cleanup.
- `Runtime/UI/MatchPauseMenu.cs`: at that baseline, unrestricted `UIDocument` discovery conflicted with adding a persistent document.
- `Runtime/World/MatchBase.cs` and `PvPMatch.cs`: at that baseline, early match exit changed to Training Stage Select; the agreed mode-specific routes deliberately replaced that behavior.
- `Runtime/UI/ResultsUI.cs`: at that baseline, Results had its own scene/document and routed Solo to Home, other modes to LobbyRoom.

Paths above are under `client/Unity/Assets/Scripts/`. Canonical social terminology is recorded in [CONTEXT.md](../../CONTEXT.md).
