# Steam store page text

The store page's short description and "About This Game" section, ready to paste into
Steamworks (Store Presence > Description). Banner text is baked into the image, so each
banner's section repeats its point in the text under it for translation and screen readers.

## Banners and loops

`tools/store_page/sections.json` lists the sections: label, headline, screenshot, framing,
and for some a looping clip. `tools/store_page/store_page.py` builds the files.

- `banners` renders the 1170×240 banners (`NN-id.png`) in the picked style, Tape (2026-11-05).
  `board` renders all three styles side by side for a new pick.
- `shoot <section>` films a section's clip through the Unity Editor gateway. It reshoots the
  section's `scenarios/social/specs/` spec at 2× the 1170×360 clip size with a narrower lens,
  as a numbered take, then prints KO ticks and writes a contact sheet. Pick `window` from those.
- `encode` cuts each section's window from its newest take into `NN-id.mp4` (60 fps, CRF 28).

Specs written before the 2026-11-01 normals renumbering press old labels; a clip's `presses`
maps them (old 3 and 4 are now 2 and 3). Choreography can also drift as kits change, so check
each new take's sheet. On 2026-11-05 the Last Stop jetpack, Hamster Habitat joust and Table
Manners slam specs no longer played as written, so Stages has no loop yet.

Upload the PNGs and MP4s as extra assets with the names below. Insert each loop with the
description editor's media button directly under its section's paragraph (`01-brawl.mp4` under
Brawl, `04-online.mp4` under Online), so Steam writes the video tag itself.

Facts to keep current: five playable fighters (Bashie and Dusty are WIP), the seven stages
in `client/Unity/Assets/Resources/Stages/roster.json` `picker`, and the fighter one-liners
in `MenuRoster.Description`. Write the name SLOPArena.

## Short description

259 of 300 characters.

```
SLOPArena is a free 3D platform fighter. Rack up damage, launch your friends off the stage, and settle it online with rollback netplay. Five fighters, seven stages, Slopball and obstacle courses. Made by one developer, open source, and proudly a little janky.
```

## About This Game

```
[img]{STEAM_APP_IMAGE}/extras/01-brawl.png[/img]
SLOPArena is a 3D platform fighter. Every hit raises your damage, and the higher it climbs, the further the next hit sends you. Knock your friends off the stage before they do it to you.

[img]{STEAM_APP_IMAGE}/extras/02-fighters.png[/img]
Five fighters, each with a full moveset and four specials.
[list]
[*][b]FightGuy[/b]. Loves punching stuff. But also kicking stuff.
[*][b]Manki[/b]. There's nothing a few explosives can't fix.
[*][b]Wibou[/b]. He studied the blade. A lot.
[*][b]Sir Bonk[/b]. Sir Bonk do bonky bonks.
[*][b]Nyx[/b]. A witch with another side.
[/list]
More fighters are on the way.

[img]{STEAM_APP_IMAGE}/extras/03-stages.png[/img]
Seven stages to fight on and fall off: Table Manners, Splash Deck, Slop Pit, After Hours, Last Stop, Log Off and Hamster Habitat.

[img]{STEAM_APP_IMAGE}/extras/04-online.png[/img]
Online fights use rollback netplay, so matches stay responsive. Open a room, invite friends through Steam, chat, and watch the replay afterwards.

[img]{STEAM_APP_IMAGE}/extras/05-modes.png[/img]
Not every fight needs a winner.
[list]
[*][b]Slopball[/b]. Keep the ball in play under the floodlights and chase your best return combo.
[*][b]Courses[/b]. Break every target and reach the finish as fast as you can.
[*][b]Training and the Fighter's License[/b]. Learn the moves before the slop learns you.
[/list]

[img]{STEAM_APP_IMAGE}/extras/06-free.png[/img]
SLOPArena is free to play and open source, made by one developer. The code is on GitHub. It's still in playtest: expect jank, report it, and watch it get fixed.
```
