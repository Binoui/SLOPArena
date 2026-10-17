# Stage-select roster

The stage picker currently offers six PVP maps:

| Stage | Current assessment |
| --- | --- |
| [Table Manners](table_manners.md) | Rebuilt 2026-10-16 as a kids' craft table with a centre cake; maintainer playtest pending |
| [Splash Deck](splash_deck.md) | Rebuilt 2026-10-16 as a fishing boat anchored in a Mediterranean harbour, keeping the barge's rated fight; maintainer playtest pending |
| [Slop Pit](slop_pit.md) | Rebuilt 2026-10-16 as a raised fight ring in an underground venue; maintainer playtest pending |
| [After Hours](after_hours.md) | Rebuilt 2026-10-16 as a broad neon rooftop; maintainer playtest pending |
| [Last Stop](last_stop.md) | Remade 2026-10-16 as Mind the Gap; maintainer playtest pending |
| [Log Off](log_off.md) | Rebuilt 2026-10-16 as a split stump with fungus shelves and a sunset campsite; maintainer playtest pending |

Training remains available only in training mode.

Rec Center Roof, Picnic Panic, Industrial Rooftop and SciFi City Demo are hidden from the picker pending a rebuild or removal. Their baked arenas, presentation assets and authoring records are preserved. Slop Court was removed on 2026-10-16; Slop Pit is now the GameHost default arena.

`StageSelectController.IsInStageSelectRoster` owns this presentation list. Online Rooms additionally require Master admission through the private `Room__AdmittedArenas__*` pin; the release procedure in [sloparena-build-export](../../../skills/sloparena-build-export/SKILL.md) updates it when this list changes. A listed map still needs a loadable baked arena with collision and a matching Resources prefab to appear. New maps enter the picker when explicitly added to this list after review. These assessments record the maintainer's 2026-10-11 roster direction, not completed release or PVP acceptance gates.
