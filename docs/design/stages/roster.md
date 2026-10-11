# Stage-select roster

The stage picker currently offers five PVP maps:

| Stage | Current assessment |
| --- | --- |
| Table Manners | Best current map |
| Splash Deck | Good |
| Slop Pit | Acceptable |
| After Hours | Acceptable |
| [Last Stop](last_stop.md) | New first pass; maintainer playtest pending |

Training remains available only in training mode.

Slop Court, Rec Center Roof, Picnic Panic, Industrial Rooftop and SciFi City Demo are hidden from the picker pending a rebuild or removal. Their baked arenas, presentation assets and authoring records are preserved. Slop Court also remains the server's compatibility default; this picker cleanup does not change server admission or remove files.

`StageSelectController.IsInStageSelectRoster` owns this presentation list. A listed map still needs a loadable baked arena with collision and a matching Resources prefab to appear. New maps enter the picker when explicitly added to this list after review. These assessments record the maintainer's 2026-10-11 roster direction, not completed release or PVP acceptance gates.
