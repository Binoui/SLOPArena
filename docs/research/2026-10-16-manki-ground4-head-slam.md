# Manki ground 4: Head Slam

Prepared by Codex, 16 October 2026. The supplied reference establishes a brief
backward wind-up, a forward/downward skull strike and a low follow-through.
The owner authorized replacing ground `4` and adjusting its gameplay data.

## Accepted source and clock

- `client/Unity/Assets/CharacterPackages/manki/Animations/manki_ground4_head_slam.anim`
- `CharacterAssetCatalog.asset`: `anim.manki.g4` binds the package-local clip.
- `character.json`: ground `4` is **Head Slam**.

| Beat | Tick / source frame at 60 fps and 1× |
| --- | ---: |
| Current grounded idle entry | 0 |
| Backward head/chest load | 1–6 |
| Forward/downward acceleration | 7–10 |
| First authoritative contact | 10 |
| Active interval | 10–14 |
| Recoil and unfold | 15–34 |
| Idle handoff / IASA | 35 |
| Clip endpoint / ability duration | 40 |

The hitbox is one 0.5 m sphere centered on `bone.head`, replacing the double
kick's foot-to-foot capsule. Damage **14**, angle **28°**, base knockback **9**,
growth **42**, stun **26**, hit group and direction are preserved. Startup aim
correction ends at tick 10. One heavy swing event follows the head at tick 10;
the separate foot accent is removed. No movement operation was added.

All other explicit slots, aliases and character fields match the saved source.
The original rig, FBX and importer are unchanged. The new clip contains neutral
curves for the existing arm/fist deformation controls and corresponding copied
finger curves; it needs no new rig controls or runtime code.

## Authoring and conversion

The current catalog prefab and idle were sampled through an owned Unity gateway
lease. The existing Manki inverse-bind conversion calibrated the original
66-joint FBX to that prefab with a maximum bind-position residual of 0.000612 mm.
Existing deformation controls required mapping copied skin fingers back to their
original semantic hierarchy for conversion, then supplying curves to both branches.

The current Codex turn authored the poses through rig-aware numerical calculations,
stepped blocking, constrained two-bone IK and monotone primary-motion interpolation.
There was **no additional GenRA provider/CLI model request**. The motion reused the
already installed, pinned GenRA parser, quaternion interpolation and USD writer,
followed by the established ordinary FBX → Humanoid plus original-path FK pipeline.
No upstream code or dependencies were changed or installed.

Quaternion data avoids a measured Euler round-trip error near curled-finger
singularities. The final GenRA world-position residual is **0.00145 mm**. The
actor root stays fixed; a small pelvis dip/backward shift supports the torso hinge
while IK keeps both feet planted. Source ankle drift is below 0.0015 mm. Side and
gameplay-angle semantic sheets and complete 40-frame, 60 fps previews were inspected
before import.

The original valid Manki Avatar is reused. Hybrid import preserves the 44 unmapped
finger/tail/helper joints and the current copied finger branches. The resulting clip
has 908 curves and 40 baked frames, giving runtime speed **1×**. FBX timestamps were
canonicalized to 60 Hz, with a maximum correction of 59.6 ns. A constant Humanoid body
origin calibration matches the actually playing idle. Endpoint curves use the actual
idle pose; final entry and IASA skin-bone positions differ by at most **0.414 mm**.
Actual Animancer ankle drift remains below **0.434 mm** over the complete action.

## Verification

- Native cook → inspect → verify → roster refresh passed with no diagnostics.
  Manki is `valid`, `dirtyOrStale: false`, with matching source/cooked-source hashes.
- All nine real cooked bone tracks match actual Animancer playback across all 40
  frames within **2.18e-7 m**. Every other Manki pose track is byte-equivalent to
  the saved pre-task pose values; frame counts are unchanged.
- Eight interruption fades into idle/fall finish at full target weight and return
  to the reference skin pose. Air `3`'s enlarged fists/arm deformation clear when
  blending into Head Slam.
- Native Shared scenarios at 0.8 m produce exactly one accepted hit for 14 damage,
  or one blocked contact for zero damage, at attack tick / MatchTick **10**
  (scenario frame 9). A 3 m target produces no contact. All scenarios recover.
- Seven focused Shared tests pass: canonical compilation, pinned roster admission
  and the existing ground/air normal contact/recovery cases, including ground `4`.
- Actual-prefab playback and native Lab contact captures were inspected. The Lab's
  `Live draft` label describes its transient source preview; its scenario results
  are real Shared simulation. Persisted poses are independently compared above.
- Unity is ready/stopped with no current error-console entries. No C# or Shared
  implementation changed, so no forced Editor recompile or broad engine suite was run.

The initial package was already stale, so the pre-change native scenario was
unavailable. Integration recooked the intended package. The existing automatic cook
postprocessor also refreshed Bonk/FightGuy/Wibou binding source hashes and resulting
manifest/roster identities. Their runtime definitions and pose payloads are unchanged;
their bindings differ only in `sourceHash`.

Detailed source backups, authoring scripts, GenRA data, transition receipts,
actual-prefab frame sequences/videos and tests are under
`artifacts/manki-g4-head-slam-20261016/`. Lab captures are under
`.ability-lab-cache/manki/head-slam-20261016-hit/` and the corresponding `-block/`.
The ignored root `TESTING-UNITY.md` contains the manual checklist. Human Training
feel, movement-carry situations and online matches remain unexercised. No commit
or push was performed.

## Finger curl correction after review

The owner identified a reversed finger curl that the initial wide-view review
missed. Close actual-skin views confirmed that the reused fist pose folded the
fingers toward the backs of both hands. The native clip now curls them toward
the measured palm normal and brings each thumb across the closed fingers.

Only the 30 finger joints and their existing copied skin branches change:
240 rotation curves, over ticks 1–34. The other 668 curves are exactly preserved,
as are key times, endpoint values, binding GUID, rig, gameplay source and catalog.
The full action and hand close-ups were reviewed. Eight idle/fall interruption
fades pass; the prior air-3 deformation resets correctly. Actual playback differs
from the prior non-finger pose by less than 6e-7 per transform component from
floating-point evaluation. Cooked poses and runtime gameplay JSON are
**byte-for-byte unchanged**, preserving the prior contact/recovery test evidence.

Cook, inspect, verify and roster refresh pass; Manki is current and Lab is paused
on ground `4`, tick 10, with no animation diagnostics. The final console also
contains an unrelated `SLOPArena/StageNeon` shader error for undeclared `SampleSH`
at `Assets/Shaders/SlopArenaStageNeon.shader:152`; this animation correction does
not claim a clean whole-project console.

The bounded patch, preservation checks, hand comparison, full updated playback
and before-source backup are under the original evidence directory's `finger-fix/`.
The root `preview.html` and normal-speed MP4s now show the corrected hands;
earlier versions remain in `finger-fix/before-*`. Reproduction applies
`finger-fix/apply-finger-correction.cs` after the original native conversion.

## Process change requested after acceptance

The owner accepted the corrected result and requested animation review before
creating the move in SLOPArena data. Future new/reworked motion follows the
[maintained review checkpoint](../../skills/sloparena-animation-authoring/SKILL.md#user-review-before-move-integration).
For a Head Slam candidate, show the unbound actual-Manki playback, its hand
articulation and proposed impact/recovery clock first; user acceptance precedes
the `ground.4` source edit, live binding and accepted cook.
