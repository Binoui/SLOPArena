# Manki air 3: Double Fist Slam

Prepared by Codex, 15 October 2026. The owner selected the arm-clearance version of the [cartoon aerial experiment](2026-10-15-manki-cartoon-variants.md) for Manki's air `3`, then explicitly chose the animation's approved impact-23/end-62 timing over the previous kick's impact-16/end-51 timing.

## Accepted source

- `client/Unity/Assets/CharacterPackages/manki/Animations/manki_air3_double_fist.anim`
- `client/Unity/Assets/CharacterPackages/manki/Presentation/MankiDeformRig.prefab`
- `CharacterAssetCatalog.asset`: promoted rig and `anim.manki.a3` clip binding.
- `character.json`: air `3` is now **Double Fist Slam**, with impact 23, active ticks 23–30, duration 62, IASA 57, landing lag 12 and autocancel thresholds 6/44. Startup correction ends at 23.

The attack sphere follows `bone.right-hand` instead of the old kick's right foot. Its radius remains 0.5 m and its other offsets remain unchanged. Damage **8**, angle **65°**, base knockback **5**, growth **26**, stun **20**, hit group and direction are preserved. One hitbox is retained; double fists do not introduce a second damage event. The existing heavy-swing presentation is emitted from the right hand at tick 23.

The promoted rig preserves the original hierarchy and Avatar and adds unmapped deformation controls. It is required for the enlarged fists and held-pose arm extension. Those controls deform skin independently of the mapped hand anchors; fist enlargement does not automatically enlarge the authoritative hitbox or hurtboxes. No new runtime deformation code or gameplay movement was added. Other move sources and catalog clip bindings remain unchanged, including air `4`.

The old visual prefab and all experiment candidates remain recoverable. The promoted assets have no dependency on the experiment directory. The `.gitignore` exception makes this original project-authored animation available for versioning; nothing was committed or pushed as part of this work.

## Verification

- Existing rig compatibility: all 25 prior catalog bindings sampled at start/midpoint/end have identical corresponding skin-bone matrices on old and promoted rigs.
- Existing refinement evidence: all original 896 animation curves preserved, plus 18 deformation curves; ten idle/fall interruption fades return controls to neutral.
- Native inspect → cook → inspect → verify → roster refresh passed. Final Manki status is `valid`, `dirtyOrStale: false`, with matching source/cooked-source hashes and no diagnostics. Roster pins the new package.
- Native Ability Lab played the new semantic binding through the real PlayerRenderer/Animancer path with a valid Humanoid Avatar and no presentation diagnostics. Side captures cover entry, held load, strike, recovery and handoff. The capture request at duration endpoint 62 applies final authored stage tick 61, as specified by the Lab contract.
- Cooked pose positions match the actual Animancer trace across nine authoritative bones and 62 frames within **2.27e-7 m**. Other cooked pose tracks differ from committed baseline by at most **5.97e-7 m**; their frame counts remain unchanged. The initial Manki cook was already stale, so this is a comparison against committed payloads rather than a clean immediate pre-cook baseline.
- Native Shared scenarios at distances 0.8 m and 0.25 m miss a standing idle target from the Lab's fixed elevated aerial start. At 0.25 m, a shielding target registers one blocked contact at **attack tick 23** (scenario frame 22 / MatchTick 23), with zero damage as expected. This verifies activation and blocking; it does not establish unblocked hit coverage or game feel across jump heights. No radius or movement compensation was added to force a test hit.
- Current Unity error console is empty; gateway released free and settled. C# and Shared source were not changed by this task, so no recompile or broad engine test suite was required.

The existing `CharacterCookAssetPostprocessor.ProcessQueue` recooks all discovered packages after queued imports. During this integration it also refreshed FightGuy, Bonk and Wibou binding source hashes, manifest hashes and corresponding roster pins. Their client bindings differ only in `sourceHash`; runtime definitions and pose payloads are unchanged. These are canonical pipeline outputs, not hand-edited payloads.

Detailed before-source backups, command receipts, compatibility checks, pose comparisons and captures are under `artifacts/manki-air3-integration-20261015/` and `.ability-lab-cache/manki/air3-integrated-20261015-*`. Manual follow-up is to try air `3` at different jump heights in Training and assess hit coverage, visual fist/hitbox correspondence and landing feel.
