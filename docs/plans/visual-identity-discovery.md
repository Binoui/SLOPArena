# SLOPArena Visual Identity — Discovery Note

**Status:** Discovery direction retained. Execution is tracked in [`visual-production-roadmap.md`](visual-production-roadmap.md); validate open style choices in Unity before treating this as an art bible.

## Goal

Make the existing game attractive and coherent enough that its technical depth is visible through presentation. Establish a reusable visual language now; finished character models and community contribution infrastructure come later.

## Identity

SLOPArena is **mischievous, energetic, and slightly unruly**. It is not fully serious, but it should not be as broadly cartoon-styled as DKO. The name permits rough edges and absurdity; the presentation should make those feel intentional rather than unfinished.

Working phrase:

> A playful arena where strange fighters bring too much personality and questionable ideas.

The exact rendering family is **open**. Graphic-stylized 3D is the first hypothesis to prototype, not a locked direction.

- Strong silhouettes and readable value separation remain requirements regardless of style.
- Neutral gameplay stays clean; major hits and KOs may briefly become spectacular.
- Prefer fun, charm, and character expression over grit or danger.
- Test alternatives in the real gameplay camera before declaring an art style.

## Character anchors

### Manki — flagship identity

Manki is SLOPArena's flagship character: a devious monkey with personality and fun explosives, comparable in broad appeal to Ziggs without copying that design. His current model is unfinished, but his fantasy should anchor the tone:

- Mischief over heroism.
- Improvised explosives and dangerous gadgets.
- Expressive anticipation and reactions.
- Smoke, sparks, fire, debris, warning markings, and unstable energy.
- Comedy comes from intent and timing, not from making the whole world soft or childish.

The existing character brief supplies a strong working palette: burnt-orange fur, bright-red face, jean-blue overalls, yellow safety accents, and soot-black equipment. These are useful anchors even while the model changes.

### FightGuy — implementation testbed

FightGuy is currently the most practical visual testbed because his animations and gameplay are available. His model is unfinished and visually plain; it must not define the final character-design ceiling.

His role is the disciplined foil to Manki: serious martial-arts silhouettes and controlled blue-white ki, with red accents from the gi, belt, and headband. The contrast is intentional—SLOPArena's humor should come from the roster and situations, not force every fighter to be comedic.

Use him to validate shared systems:

- Character material treatment.
- Fighter/background separation.
- Universal light, heavy, launch, and KO effects.
- Movement dust and trails.
- HUD presentation.

### Wibou

Wibou provides the sharp/bladed contrast case: narrow glints, blade arcs, cleaner directional shapes, and restrained character-specific accents layered over the shared hit grammar.

## Shared combat-VFX grammar

Every character uses common rules for gameplay meaning:

- Effect timing follows the authoritative contact event.
- Shape and scale communicate light, medium, heavy, launch, defensive, and KO tiers.
- Effects align to attack or knockback direction.
- Major effects may persist through hitstop, then clear quickly.
- Ordinary combat must not obscure spacing or silhouettes.

Character identity is a second layer:

- Manki: fire, smoke, sparks, debris, unstable explosives.
- Wibou: blade arcs, glints, sharp fragments.
- FightGuy: compressed blunt-force shapes plus disciplined blue-white ki.
- Nilus: cold-violet void tears with a colder cyan rim; energy remains VFX rather than model geometry.

The character layer decorates the shared grammar; it does not replace gameplay readability.

## Visual reference findings

The generated Manki and FightGuy character sheets are exploratory mood references, not canon and not an art-style target. Useful ideas may be extracted independently:

- Manki benefits from warm explosive color, smoke, sparks, and chunky readable props.
- FightGuy benefits from a distinct blue-ki language and fast afterimages.
- Character identity should come from localized light and effects, not a permanent team-colored outline.
- Their gritty backgrounds, rendering finish, proportions, and overall seriousness are not decisions.

The first rim-light prototype improved silhouette readability but the constant amber/cyan edge is provisional. Test a much subtler shared rim—or no artificial rim—against the real stage palette before keeping it.

The player-facing stage roster is **Slop Court**, **Splash Deck**, **After Hours**, **Rec Center Roof**, and **Picnic Panic**. **Training Lab** is deliberately excluded from Stage Select. Their generated layouts and atmosphere are visual prototypes; eventual render shells must preserve the clean baked collision while replacing primitive surfaces with authored edge treatment, architecture, surrounding environment, sky, and depth layers.

Manki already has unattached bomb, bazooka, and aerosol meshes under `Assets/Art/Characters/manki/`. Their FBX materials currently have no base textures assigned, but the silhouettes import correctly. Original generated textures may exist outside the project and should be located before materials are rebuilt. Until then, any palette treatment is exploratory rather than canonical.

## Production strategy

Use a hybrid of generated, free, purchased, and custom assets. Unify them through materials, palette, lighting, scale, and VFX rather than expecting source assets to match automatically.

Current character models are placeholders. Do not block the visual prototype on final models or commissions. First prove what rendering and presentation can do with the existing game; afterward identify the assets that genuinely require replacement.

For any eventual official asset, retain author, source, license, editable source files, dependencies, modifications, and AI/tool provenance.

## First Unity prototype status

Judge every result in the real gameplay camera:

1. Neutral gameplay and representative arenas — reviewed in the normal gameplay camera.
2. Deliberate lighting and color treatment — first prototype implemented; not locked.
3. Reusable graphic character material — first prototype implemented; not locked.
4. Universal light, medium, heavy, and launch impact shapes — implemented.
5. Normal-attack accents — FightGuy rollout approved in the gameplay camera; other
   characters pending. Movement and landing feedback — first pass approved; optional
   Cartoon FX source setup documented for fresh checkouts.
6. Project-owner in-game approval is the visual gate.
7. Store, generated, or commissioned assets — select only after the prototype demonstrates a specific gap.

Success is a coherent moving sequence, not an isolated promotional screenshot.
