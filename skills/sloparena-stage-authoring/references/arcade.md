# Arcade fixtures and courses: stage-authoring reference

Read this with the [stage-authoring workflow](../SKILL.md) when the stage is a solo
Arcade fixture (ball lab, Slopball court) or a Character Course. Normal PvP stages
do not need it.

The approved [Arcade design](../../../docs/design/arcade.md) and
[ADR-0037](../../../docs/adr/0037-arcade-shared-objects-and-unified-stage-artifact.md)
extend the same bake pipeline with explicit solo stage purpose. A solo prototype
uses its approved Arcade scope and locked fixture brief; the 2–4-player PVP design
and human-review requirements apply to normal PVP stages.

For the [Phase 1 ball lab](../../../docs/design/arcade-phase-1.md) and
[Phase 2 Slopball court](../../../docs/design/arcade-phase-2.md), AuthoringAids may
contain one `ArcadeStageAuthoring` component and its empty BallSpawn child marker.
The component is cook input only. It compiles an immutable profile and allowed
fighter package IDs into the same `.arena` artifact as geometry. No runtime
Unity component or raw JSON decides ball behavior.

Slopball additionally compiles its deterministic shot profile, net/floor planes and
serve gap into typed section v2. Section v3 adds authored linear ball drag and
an explicit optional Slopball payload. Section v4 adds optional launcher spawn
half-width/depth around the empty ball marker; the visible launcher follows the
Shared shot origin between serves. Readers retain v1/v2 with zero drag and v1–v3
with fixed origins. Unchanged profiles retain their existing serialized bytes.
The static net is baked ball collision and is excluded from the fighter/projectile
ground heightmap. Shared validates serve arcs against the court.

Character Courses compile typed section v5 inside the same arena v6 artifact (section v6
when a course has bonus targets that take several hits; one-hit courses keep v5 bytes).
`ArcadeStageAuthoring.CharacterCourse` selects targets, checkpoints with sensor
volumes/respawns and cumulative target prerequisites, and a finish volume instead
of a ball/Slopball profile. Markers must be empty children under AuthoringAids;
Shared validates identities, volumes and prerequisites, while the baker also
checks grounded checkpoint respawns and blast-bound placement. Keep IDs stable
when moving markers. Ball sections v1–v4 retain their existing serialization.
See the [Sanctuary production contract](../../../docs/design/stages/fightguy_sanctuary.md)
and [Phase 4 commands](../../../docs/design/arcade-phase-4.md#authoring-and-native-commands).

A new course needs no Editor code of its own. Keep the shell and the markers in
`tools/blender/<key>/layout.py`, add `shell.course(profile, packages, targets, checkpoints,
finish)` to its `--unity-shell` document and run `sloparena.stage.shell-apply --create` once:
it creates the source scene and writes `ArcadeStageAuthoring` with empty markers, which later
applies move with the layout. Add the course to `CharacterCourseMatch.Courses` (title, finish
name, tips) so Arcade offers it to the fighters its profile admits; every course plays in the
one course scene. [Kaboom Quarry](../../../tools/blender/manki_quarry/README.md) works this way.
A target tuple may add a hit count, `(id, position, radius, hits)`, for a bonus target
([Fight Night Pier](../../../tools/blender/fightguy_pier/README.md)); give it a `TargetLooks`
entry with `CourseBonusTarget` damage stages in the course's presentation set. Before baking,
run the route tests on the shell JSON through `CourseShellArena`
(`SLOPARENA_COURSE_SHELL=<shell json>`), and keep each checkpoint sensor clear of the targets
it requires.
A kill plane exactly 1 m under the spawn floor trips `ArenaShippingTests`' unbaked-heightmap
check; keep the water a little lower.

A challenge's feel (targets, ball, cues, sounds) is presentation, not stage art: a
`PresentationBuilderKit.ForFolder` builder bakes it into the stage's `Presentation/`
folder plus a Resources set, and a runtime class plays it from Shared state and events
after each tick. See `SanctuaryCoursePresentationBuilder` with `CharacterCoursePresentation`
([course feel pass](../../../docs/design/arcade-phase-4.md#feel-pass--2026-10-18)) and
`SlopballPresentationBuilder` with `SlopballPresentation`
([Slopball feel pass](../../../docs/design/stages/slopball.md#feel-pass--2026-10-18)).
Clips come from `tools/sfx/*_clips.py`. Pitch concepts first: paint over real
`sloparena.photo.live` captures and show the sounds as a short slideshow video.

Arcade sources require one grounded `Spawn_01`; normal sources retain four empty
spawn markers. Other scripts/components remain rejected by the source validator.
Arcade heightmaps/ground checks use upward-facing surfaces so the inward-facing
ceiling of a closed court is not interpreted as ground.

The writer emits v6 for an Arcade section and preserves byte-identical v5 exports
for ordinary stages. Readers retain v1–v5 support. Arcade-only stages are excluded
by normal stage selection and PvP admission, even when discovered in the same
arena directory.

`sloparena.arcade.phase1.prepare` creates missing original primitive source/visual
assets and the dedicated test scene, then calls the maintained stage baker.
Its ignored Resources prefab can be recreated through that command. Inspect the
result with `sloparena.stage.inspect`; keep captures/reports under the normal
gitignored cache. Human acceptance is solo play/camera/ball feel, recorded separately
from structural inspection and PVP acceptance.
