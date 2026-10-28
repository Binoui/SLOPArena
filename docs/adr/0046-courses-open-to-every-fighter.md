# ADR-0046: Slop Rally courses are racing maps for every fighter

**Status:** Accepted — 2026-10-28. The maintainer redirected courses from solo kit lessons to
multiplayer racing ("making fun and engaging maps that players will want to complete with their
friends, or try to find shortcuts") and approved this slice ("go for it").
**Supersedes:** the one-fighter-per-course rule of
[ADR-0040](0040-online-arcade-room-pick-and-match-format.md) slice 4 (Course Room admission
`format:arena:fighter`, the `course_fighter` Room rejection and the preset course fighter).
**Extends:** [ADR-0041](0041-couch-play-in-solo.md) (Local Play flow)

## Context

Each course in the mode formerly called Character Courses admits only the fighter it was built around. The lock lives in four places:
the baked course profile's allowed package list (`ArcadeStageDefinition.AllowsPackage`), the
`fighter` field of each course entry in `Resources/Stages/roster.json`, Master's Room admission
(`format:arena:fighter`, preset selection and `course_fighter`/`course_roster` checks) and the
client's Character Select, which offers only that fighter in a course Room. Local Play maps a
fighter to the first course that admits it, so FightGuy never sees Sanctuary once the Pier is
baked, and Bashie, Sir Bonk and the Salamander have no course.

`CourseFinishabilityTests` (2026-10-28) drives every roster fighter through every course with
jumps, air dodges and normals only; all 24 pairs finish with no deaths and no specials.

## Decision

1. **Any roster fighter plays any course** in Solo, Local Play and online Race/Co-op. Mirrors are
   allowed; online racers are told apart by their distinct Room colours, as today.
2. **Course stages carry no fighter list.** `AllowsPackage` admits every package for a course
   stage, as it already does for Manki Mayhem. Existing bakes still list one package; readers
   ignore that list for courses, so no rebake is needed. Course authoring writes an empty list on
   its next bake, and validation accepts an empty list for courses.
3. **Finishability is a gate.** Every course must pass `CourseFinishabilityTests` for every
   roster fighter with universal movement and normals. A new course adds its ordinary route to
   `CourseFinishability.Routes`; a new fighter is covered by the roster manifest.
4. **Local Play picks the course first:** Mode → Course → Fighters. The course list shows every
   baked course with a presentation, in `CharacterCourseMatch.Courses` order. Personal Bests stay
   keyed by course profile and fighter package.
5. **Tips name the route, not a kit.** Course tips describe ordinary routes and shortcuts without
   slot letters, since any fighter may read them.
6. **Online courses admit like Slopball.** `roster.json` and Master's `Room:AdmittedArcade` use
   `format:arena` for course entries. Character Select offers every admitted fighter in a course
   Room; Master validates admitted fighters and distinct colours. The GameHost keeps resolving
   each fighter's package against the match catalog.
7. **Compatibility.** The Room contract changes (an older client expects a preset fighter), so the
   client, GameHost and Master ship together under the next protocol number when this lands.

## Consequences

- The mode is named **Slop Rally** (maintainer pick, 2026-10-28). Course titles keep their themes
  (Fight Night Pier, Sanctuary Course, Kaboom Quarry, Neo Mall). `CharacterCourse*` and wire names
  remain unchanged. "Character Course" is a code and legacy name to avoid in player-facing text.
- Kit-shaped targets stay. Any attack breaks them, and multi-hit bonus targets take longer for
  fighters without a fitting special; the maintainer chose to keep required targets.
- Couch racing (P2–P4 on one screen with a leader camera) is the next slice and extends ADR-0041.
- Rejected: rebaking every course with the full roster list (a new fighter would need four
  rebakes) and a per-course "home fighter" that keeps kit tips (keeps the solo-lesson framing the
  maintainer moved away from).
