---
status: accepted
---

# One frontend scene with independent persistent social UI

Consolidate SLOPArena's Home, ServerBrowser, LobbyRoom, Fighter Select, Stage Select and Results into one frontend scene with explicit page lifecycles, replacing scene-per-page navigation. Keep gameplay scenes separate: unload the frontend during gameplay and recreate it on return, while social/session state and presentation survive independently. This deliberately includes lifecycle migration in the flow/social rework so navigation has one explicit owner; later styling changes remain independent of navigation and scene topology.

## Considered alternatives

- **Persistent shared UI with existing menu scenes:** could provide the same continuous player experience with less immediate cancellation/subscription migration. Rejected for this rework because the user explicitly chose to consolidate the frontend flow now; scene consolidation is not claimed to be necessary for persistent chat or later styling.
- **Keep the frontend loaded alongside gameplay:** would retain page objects, but adds active-scene, camera, audio and hidden-page lifetime responsibilities. Rejected in favor of recreating the frontend and retaining only the state/presentation that actually must survive.

## Consequences

Scene destruction can no longer serve as menu-page cleanup. Page departure must remove callbacks and subscriptions, cancel/reconcile outstanding work, and prevent stale completions from navigating or retaining unwanted membership/server processes. Server-owned lobby authority stays distinct from ownership of a locally hosted server process; social readiness, GameServer membership and gameplay connectivity also remain distinct.

Do not create a second chat transport or conversation store. Only Display Name and dock preference persist across launches; conversations, drafts and scroll state remain launch-scoped within existing retention limits. The full approved behavior, mode-specific exits, scope exclusions and explicitly deferred live multi-client acceptance are in the [frontend/social design brief](../plans/2026-09-20-frontend-social-flow.md).

Accepted on 2026-09-20. This records an approved design, not an implemented migration or authorization to change runtime code.
