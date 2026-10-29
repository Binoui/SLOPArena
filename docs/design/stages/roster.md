# Stage-select roster

The stage picker currently offers seven PVP maps:

| Stage | Current assessment |
| --- | --- |
| [Table Manners](table_manners.md) | Rebuilt 2026-10-16 as a kids' craft table with a centre cake; accepted 2026-10-17 |
| [Splash Deck](splash_deck.md) | Rebuilt 2026-10-16 as a fishing boat anchored in a Mediterranean harbour, keeping the barge's rated fight; accepted 2026-10-17 |
| [Slop Pit](slop_pit.md) | Rebuilt 2026-10-16 as a raised fight ring in an underground venue; accepted 2026-10-17 |
| [After Hours](after_hours.md) | Rebuilt 2026-10-16 as a broad neon rooftop; accepted 2026-10-17 |
| [Last Stop](last_stop.md) | Remade 2026-10-16 as Mind the Gap; accepted 2026-10-17 |
| [Log Off](log_off.md) | Rebuilt 2026-10-16 as a split stump with fungus shelves and a sunset campsite; accepted 2026-10-17 |
| [Hamster Habitat](hamster_habitat.md) | New 2026-10-27: two hamster bins joined by see-through tubes in Table Manners' bedroom at night; accepted from captures 2026-10-27, not yet played in a match |

[Training](training.md) remains the sole `trainingOnly` entry. Its round-2 Crash-Test Lab adds the Drill Yard, trench, low shelf and 4/8/13.5 m Recovery Range to the Crash Pad. It follows the [locked brief](training.design.md) of 2026-10-29 and awaits maintainer acceptance in Training; the production brief records far-island and automatic-ledge-entry deviations. License Test (Fighter's License) is the Tutorial's map and is never offered here.

Rec Center Roof, Picnic Panic, Industrial Rooftop and SciFi City Demo are hidden from the picker pending a rebuild or removal. Their baked arenas, presentation assets and authoring records are preserved. Slop Court was removed on 2026-10-16; Slop Pit is now the GameHost default arena.

[`client/Unity/Assets/Resources/Stages/roster.json`](../../../client/Unity/Assets/Resources/Stages/roster.json) is the one list: Stage Select reads its `picker` (and `trainingOnly`) stages, and each release writes Master's online admission (`Room__AdmittedArenas__*`) from the same `picker` list, as the [release procedure](../../../skills/sloparena-build-export/SKILL.md) describes. A listed map must ship whole: `ArenaShippingTests.PickerRoster_StagesShipWithArenaPrefabAndThumbnail` requires its baked PvP arena, Resources prefab and thumbnail in the repository. New maps enter the picker when the maintainer accepts them and they are added to that file.

A stage plays its own music loop, `Resources/Audio/Music/Stages/<stage key>.ogg`, when one is installed; the Character Courses, Slopball and License Test use the same convention. The loops are local Lyria tracks (see [credits](../../assets/credits-and-licenses.md)) cut by [`tools/music/loop_stage_music.py`](../../../tools/music/loop_stage_music.py). A stage without one, such as Training, plays a random match theme.

"Accepted" records the maintainer's verdict after playing the 2026-10-16 rebuilds ("the maps are much better", 2026-10-17). Their generated art and prefabs are committed with that acceptance; see each stage's brief for what was reviewed.
