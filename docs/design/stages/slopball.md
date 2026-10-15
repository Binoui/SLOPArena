---
key: slopball
display_name: Slopball Court
purpose: arcade
variant: static
intent: playful
---

# Slopball production contract

Follow the locked [design brief](slopball.design.md) for the underground sports
club. This is a cosmetic pass over the accepted solo court. Preserve the collision
source, baked arena, spawn markers, shot settings and normal fighter behavior.
The floor, net and shell source meshes retain their existing asset identities;
club surfaces and graphics are separate original presentation resources.

Collision source: `Assets/Stages/slopball/slopball.unity`. Runtime artifact:
`data/arenas/slopball.arena`. Cosmetic prefab:
`Assets/Resources/Stages/slopball.prefab`. The normal Arcade entry loads this
presentation through the existing stage path. The sender remains the existing
`Ball Sender` transform driven by Shared's shot origin.

Use original generated geometry/materials and existing project font resources,
with no external environment dependencies. Keep the prefab free of Colliders
and gameplay authority. Author local lighting, subtle concrete and floor finish,
teal/coral/mint padding, restrained markings, one club sign with a smiling ball
mascot and compact sender dressing with a friendly face.

Verify source/baked hashes before and after the pass, inspect the prefab with the
maintained stage command without rebaking, review full interior orbit/low views,
and check the production Arcade path in Play Mode. Record structural evidence
and captures in the ignored stage cache. Solo human visual review remains
separate from the normal PVP acceptance process.
