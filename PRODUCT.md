# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

Primary users are friends playing SlopArena online. They need to join a match, understand each fighter quickly, and complete short remote matches without requiring every player to operate hosting or networking infrastructure.

## Product Purpose

SlopArena is an open-source 3D platform fighter built around expressive movement, knockback, compact hero-style kits, and room for unexpected play. The immediate product goal is a playable friends demo that gets real remote feedback on four admitted cooked characters: FightGuy, Manki, Wibou, and Bonk.

Success means friends can enter a match, understand what happened, and have enough readable, expressive interaction to want another round. Package admission is not treated as proof of kit completeness or player acceptance.

## Positioning

SlopArena's distinctive mechanism is a server-authoritative 60 Hz simulation shared by the Unity client and GameServer. The same deterministic gameplay model supports training, prediction, reconciliation, rollback, and online matches while leaving presentation in the client.

The combat identity combines camera-relative 8-direction movement, jump arcs, double jumps, fast-fall, ledges, Dash, damage percent, Knockback, Hitstun, Hitstop, Combo Influence, Clash, Burst, and compact fighter kits with meaningful recovery, zoning, and finisher choices.

## Operating Context

The primary operating context is Unity 6 on desktop, with a local training flow and online matches through a GameServer. A master server manages browser, lobby, character-select, and results flow; it does not simulate matches.

Players move through the MatchFlow: Server Browser, Lobby Room, Character Select, Countdown, Fight, Results, and Lobby Room. Non-technical players should be able to use an OfficialServer; technical players may use HostAndPlay.

Gameplay runs at 60 Hz. The client renders authoritative state and semantic presentation events; the Shared simulation remains the gameplay authority.

## Capabilities and Constraints

- Each character uses a canonical 16-entry grid: grounded and aerial variants of normals `1`, `2`, `3`, `4` and specials `A`, `E`, `R`, `F`.
- Physical controls are input adapters and are not persisted move identities.
- New characters are package-native. Editable packages live under `client/Unity/Assets/CharacterPackages/<package>/` and cook into immutable runtime artifacts under `content-cooked/<package>/`.
- Match Content Catalogs pin exact package IDs, versions, dependencies, capability versions, and hashes.
- Raw authoring JSON is cook input, not the runtime contract.
- Server-side simulation is authoritative. Unity physics, animation callbacks, VFX, and audio are presentation or authoring aids only.
- Shared simulation code remains free of Unity types and engine physics queries.
- Current admitted demo roster: FightGuy, Manki, Wibou, and Bonk. Nilus and its legacy character execution path are retired.
- The longer-term Workshop direction permits approved deterministic primitives and package-owned assets, not arbitrary simulation code, native plugins, or direct Unity-path dependencies.
- The current implementation targets Unity 6 desktop. SlopArena also has a separate web presentation surface; the Impeccable platform value `adaptive` records that the product spans these first-party surfaces rather than asserting a mobile-native target.

## Brand Commitments

The shared identity is **low-poly underground toy-fight-club, with DIY internet-game energy**: scavenged construction, chunky silhouettes, curated asset-pack collage, and cool fighting imagery with jokes second. “Toy” is a construction metaphor, not a requirement for literal plastic or cute styling. The website is the strongest existing graphic reference, not a fully approved implementation.

Target graphic primary actions and selection use Action Yellow (`#FFCC22`). Archivo Black is the target shared name-mark and display voice; marketing pairs it with Space Mono. Native short utility labels may use mono where practical, while dense settings and HUD text should remain readable sans. Paper, Ink, and orange values remain local to each surface; existing native orange `#E05C2A` and marketing orange `#F05B35` are retained.

The canonical visual-language guide is `docs/design/visual-language.md`; art conventions remain authoritative for 3D and assets. The sibling repository `../SlopArena-web` is the web graphic reference.

**Status:** SlopArena is unreleased, developed by a solo developer, and is not on Steam. The player-facing identity rollout is implemented. Frontend pages use the user's authored paper background; Home has separate mode/poster/announcement columns. Chat is an absolute user-sized overlay in menus and gameplay and does not influence underlying composition. Settings and match overlays retain readable neutral fields. The separately approved gameplay HUD now has fight-card player panels with clear lives, four paired tickets displaying all eight moves with live ground/air names and effective bindings, shared damage-tier colors, and a persistent target marker with an optional overhead percentage. 3D arenas, existing navigation, gameplay data/authority, and networking are preserved. Connected-room, distributable-player, creator/Ability Lab, website, and release-copy migration are not claimed; the website still has the misleading Steam CTA.

## Evidence on Hand

- `README.md` contains the current product description, four-character demo status, architecture, development setup, and roster summary.
- `CONTEXT.md` defines the canonical domain vocabulary and settled mechanics.
- `docs/plans/2026-09-05-playable-demo-reset.md` defines the current demo priority.
- `docs/design/visual-language.md` defines the shared presentation grammar.
- `content-cooked/` contains cooked runtime artifacts for the admitted packages.
- `client/Unity/Assets/CharacterPackages/` contains package-native authoring sources and asset catalogs.
- The repository has no confirmed testimonials, press claims, benchmark claims, or external player-acceptance evidence. Future surfaces must not fabricate them.

## Product Principles

1. Make the next player action and the last gameplay event immediately understandable.
2. Preserve deterministic, server-authoritative gameplay across local and online paths.
3. Give each fighter a compact, readable identity with meaningful counterplay.
4. Prefer honest prototype energy over generic polish or unsupported claims.
5. Expand creator content through immutable, reviewable, deterministic packages.

## Accessibility & Inclusion

Gameplay and UI must not rely on color alone for status, player identity, or combat information. Preserve readable contrast, stable HUD information, clear focus and confirmation states, and reduced-motion behavior for non-gameplay presentation where the platform supports it.
