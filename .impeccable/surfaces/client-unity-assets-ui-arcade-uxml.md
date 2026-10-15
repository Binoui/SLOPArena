---
version: 1
slug: "client-unity-assets-ui-arcade-uxml"
primary_target: "client/Unity/Assets/UI/Arcade.uxml"
related_targets: ["client/Unity/Assets/Resources/UI/Arcade.uss", "client/Unity/Assets/Scripts/Runtime/UI/ArcadeSelectController.cs", "client/Unity/Assets/UI/FrontendShell.uss", "client/Unity/Assets/Scripts/Runtime/UI/FrontendShellView.cs"]
---

# Arcade mode gallery

Mode: Operate. User approved the whole-page redesign, designed poster imagery,
and a composition that can accommodate additional Arcade modes.

The first glance shows the activity through a mode poster. A compact Back / Arcade
strip replaces the full-width introduction. The authored paper wallpaper, ink,
Archivo Black, portrait artwork, and action yellow preserve the frontend identity.

The left region is an independently scrollable poster gallery. One entry fills
the available gallery; additional entries use two columns at roomy density and
one column at compact density. Slopball is the current playable entry. Actual new
modes add their own content and launch bindings through the existing game flow;
this change does not introduce a mode registry or another execution path.

The right panel groups four portrait choices, the selected fighter's best,
collapsed past-version records, feedback, and Play. The two portrait rows fit
their available height. Names use narrow strips; selection has a check icon and
yellow, while focus uses the existing orange. Play remains outside scrolling.
The shell's footer is hidden only for Arcade; its chat remains an absolute overlay.

Slopball's illustration is `Assets/UI/Arcade/SlopballPoster.png`. It was generated
with the built-in imagegen tool from existing Manki art and the actual court as
references. It is an illustration, not a gameplay screenshot. The title and one
useful rule stay live UI, and the image fits without cropping its subjects.

Native evidence: `.impeccable/review/arcade-redesign-20261015/native-2c/` and
`native-2c.json`. Verification covered 1920×1080, 1366×768 at 100% and 140%,
all fighter buttons, per-fighter empty/existing records, archive expansion,
Home return, Slopball launch/countdown and return. Two/four-entry specimens were
temporary labelled layout samples. Record values are isolated test data.

Compilation passed. A pre-existing FallbackEditorWindow error required a scoped
Error Pause override for verification; its flag, Play Mode options, viewport,
UI scale/cache, record directory, fighter choice, and original AbilityLab scene
were restored. Physical controller and open/resized-chat acceptance remain manual.
