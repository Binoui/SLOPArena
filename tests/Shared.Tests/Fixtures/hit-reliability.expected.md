# FightGuy hit reliability

Package `fixture` 1.2.3, cooked `0123456789ab`. Idle standing targets; ground reach comes from the real simulation (5 cm steps), aerial and sampling columns from the cooked hitbox geometry with the attacker fixed in place.

## Ground moves

| Move | Playback | Active ticks | Touch at 1 m | Reach vs FightGuy | Reach vs Manki | At 30° | Flags |
|---|---:|---:|---:|---|---|---|---|
| ground.3 Blade <swing> × | 1.50× | 3 | 2 | 0.30–0.55 m, 0.90–2.10 m | never | differs | clip plays 1.50× authored speed |

## Aerial moves

Target feet height relative to the attacker's feet that any active tick can touch (searched −3 to +2 m).

| Move | Playback | Active ticks | vs FightGuy | vs Manki | Flags |
|---|---:|---:|---|---|---|

## Multi-hit coverage

Bearings (of 24, 15° apart) at which each hit of a multi-hit ground move touches a standing target, from the poses the engine tests, with the attacker fixed and the target never knocked away. A hit with zero everywhere cannot land on that target.

| Move | Ticks | Damage | Radius | FightGuy 1.0 m | Manki 1.5 m |
|---|---|---:|---:|---:|---:|
| ground.3 | 4–6 | 7 | 0.20 | 3 | 0 |

## Arc (ground moves)

Side view of where an idle target is touched by any active tick, from the cooked hitbox geometry with the attacker fixed: rows are the target's feet height above the floor (3 m at the top, 0 = standing), columns its distance along the attacker's facing from 2.5 m behind to 3.5 m ahead, 25 cm apart. `#` touched, `.` not, `|` the attacker's own column.

ground.3 Blade <swing> × vs FightGuy:

```
1.00 .#.
0.00 ###
     -2.5      0         +2.5 
```

## Bone and weapon sampling

Grid positions (0.5–3 m ahead, ±40°) where a standing FightGuy-sized target lies in the swing's path between ticks but no tested pose touches it. Tested poses are each tick's pose plus the swept poses the engine tests between active ticks, so Untested should stay near zero.

| Move | Anchor | Radius | Max tip step / tick | Target feet | Swept | Untested |
|---|---|---:|---:|---:|---:|---:|
| ground.3 | `hand→tip` | 0.20 | 0.40 m | 0 m | 80 | 25% |
