using System;
using System.Collections.Generic;
using SlopArena.Client.Input;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// The tutorial checklist: current lesson, its objectives with the player's actual bindings,
    /// the end card, and Skip/Restart entries in the existing pause panel.
    /// </summary>
    internal sealed class TutorialView : IDisposable
    {
        private const string CompletedKey = "sloparena.tutorial.completed";
        private readonly CharacterDefinition _def;
        private readonly TutorialStageLayout _layout;
        private readonly VisualElement _live, _objectives, _results, _stamp;
        private readonly Label _eyebrow, _title, _hint, _stampWord, _stampDetail;
        private readonly Button _training;
        private readonly List<Button> _pauseButtons = new();
        private readonly List<(VisualElement Row, Label Key, Label Amount)> _rows = new();
        private int _shownLesson = -1, _bindingRevision = -1;
        private bool _gamepad;
        // The PASSED stamp slams in, holds, then fades, in screen space (never in the world).
        private const float StampIn = 0.12f, StampHold = 1.05f, StampOut = 0.3f;
        private float _stampAge = -1f;

        /// <summary>True once any tutorial run has reached the end card on this machine.</summary>
        public static bool HasCompleted => PlayerPrefs.GetInt(CompletedKey, 0) == 1;
        public static void MarkCompleted() { PlayerPrefs.SetInt(CompletedKey, 1); PlayerPrefs.Save(); }

        public TutorialView(VisualElement hud, CharacterDefinition def, TutorialStageLayout layout, Action skip, Action restart,
            Action training, Action leave)
        {
            _def = def; _layout = layout;
            var style = Resources.Load<StyleSheet>("UI/Tutorial");
            _live = new VisualElement { name = "tutorial-live", pickingMode = PickingMode.Ignore };
            _live.AddToClassList("tutorial-live");
            if (style != null) _live.styleSheets.Add(style);
            _eyebrow = Add(_live, "", "tutorial-eyebrow");
            _title = Add(_live, "", "tutorial-title"); _title.name = "tutorial-title";
            _hint = Add(_live, "", "tutorial-hint");
            _objectives = new VisualElement { pickingMode = PickingMode.Ignore };
            _live.Add(_objectives);
            Add(_live, "ESC / START  ·  PAUSE TO SKIP OR RESTART A LESSON", "tutorial-footer");
            hud.Add(_live);

            _results = new VisualElement { name = "tutorial-results" };
            _results.AddToClassList("tutorial-results");
            if (style != null) _results.styleSheets.Add(style);
            Add(_results, "FIGHTER'S LICENSE", "tutorial-eyebrow");
            Add(_results, "LICENSE GRANTED", "tutorial-results-title");
            Add(_results, "Every test passed. Practise more in Training, or jump into a match.", "tutorial-hint");
            var actions = new VisualElement(); actions.AddToClassList("tutorial-actions");
            _training = new Button(training) { name = "tutorial-training", text = "TRAINING" };
            _training.AddToClassList("btn-primary");
            var menu = new Button(leave) { name = "tutorial-menu", text = "MENU" };
            menu.AddToClassList("btn-secondary");
            actions.Add(_training); actions.Add(menu); _results.Add(actions);
            _results.style.display = DisplayStyle.None;
            hud.Add(_results);

            _stamp = new VisualElement { name = "tutorial-stamp", pickingMode = PickingMode.Ignore };
            _stamp.AddToClassList("tutorial-stamp");
            if (style != null) _stamp.styleSheets.Add(style);
            var plate = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.AddToClassList("tutorial-stamp-plate");
            _stampDetail = Add(plate, "", "tutorial-stamp-detail");
            _stampWord = Add(plate, "", "tutorial-stamp-word");
            _stamp.Add(plate);
            _stamp.style.display = DisplayStyle.None;
            hud.Add(_stamp);

            // The pause panel already exists (MatchBase builds it before the match starts).
            var box = hud.Q<VisualElement>(className: "match-pause-box");
            if (box != null)
            {
                var leaveButton = box.Q<Button>("pause-leave-match");
                foreach (var (label, action) in new[] { ("SKIP LESSON", skip), ("RESTART LESSON", restart) })
                {
                    var button = new Button(() => { MatchPauseMenu.Active?.SetPaused(false); action(); })
                        { text = label, name = "pause-" + label.ToLowerInvariant().Replace(' ', '-') };
                    button.AddToClassList("match-pause-action");
                    button.style.width = 240; button.style.height = 46; button.style.fontSize = 20; button.style.marginBottom = 10;
                    int index = leaveButton != null ? box.IndexOf(leaveButton) : -1;
                    if (index >= 0) box.Insert(index, button); else box.Add(button);
                    _pauseButtons.Add(button);
                }
            }
        }

        public void ShowLesson(TutorialProgress progress)
        {
            _shownLesson = progress.LessonIndex;
            var lesson = progress.Lesson;
            _eyebrow.text = $"FIGHTER'S LICENSE  ·  TEST {progress.LessonIndex + 1} / {progress.Lessons.Count}";
            (_title.text, _hint.text) = Describe(lesson.Id, _gamepad);
            _objectives.Clear(); _rows.Clear();
            foreach (var objective in lesson.Objectives)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("tutorial-objective");
                var check = new VisualElement { pickingMode = PickingMode.Ignore }; check.AddToClassList("tutorial-check");
                row.Add(check);
                var key = Add(row, "", "tutorial-key");
                Add(row, Describe(objective.Id), "tutorial-text");
                var amount = Add(row, "", "tutorial-amount");
                _objectives.Add(row);
                _rows.Add((row, key, amount));
            }
            _bindingRevision = -1;
        }

        public void Present(TutorialProgress progress, bool paused, bool finished, bool betweenLessons)
        {
            UpdateStamp(paused);
            _live.style.display = paused || finished ? DisplayStyle.None : DisplayStyle.Flex;
            if (finished || progress.LessonIndex != _shownLesson) return;
            bool gamepad = HumanInputActions.LastUsedGamepad && UnityEngine.InputSystem.Gamepad.current != null;
            bool prompts = gamepad != _gamepad || _bindingRevision != HumanInputActions.BindingRevision;
            _gamepad = gamepad; _bindingRevision = HumanInputActions.BindingRevision;
            var objectives = progress.Lesson.Objectives;
            for (int i = 0; i < _rows.Count; i++)
            {
                var (row, key, amount) = _rows[i];
                if (prompts)
                {
                    key.text = Prompt(objectives[i].Id, gamepad);
                    key.style.display = key.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (prompts && i == 0) _hint.text = Describe(progress.Lesson.Id, gamepad).Hint;
                bool done = progress.IsComplete(i);
                row.EnableInClassList("tutorial-objective--done", done);
                amount.text = done ? "" : Amount(objectives[i], progress.Amount(i));
            }
            _live.EnableInClassList("tutorial-live--passed", betweenLessons);
        }

        public void ObjectivesCompleted(int mask)
        {
            UISFX.PlayClick();
            for (int i = 0; i < _rows.Count; i++)
                if ((mask & (1 << i)) != 0) _rows[i].Row.AddToClassList("tutorial-objective--done");
        }

        /// <summary>The lesson's stamp: PASSED, or LICENSE GRANTED after the last test.</summary>
        public void ShowStamp(TutorialProgress progress)
        {
            bool license = progress.Finished;
            _stampWord.text = license ? "LICENSE GRANTED" : "PASSED";
            _stampDetail.text = license ? "FIGHTER'S LICENSE"
                : $"TEST {progress.LessonIndex + 1} / {progress.Lessons.Count}  ·  {Describe(progress.Lesson.Id, _gamepad).Title}";
            _stamp.EnableInClassList("tutorial-stamp--license", license);
            _stampAge = 0f;
            AnimateStamp(0f);
            UISFX.PlayCountdownBeat("PASSED");
        }

        private void UpdateStamp(bool paused)
        {
            if (_stampAge < 0f) return;
            if (!paused) _stampAge += Time.unscaledDeltaTime;
            if (_stampAge >= StampIn + StampHold + StampOut) _stampAge = -1f;
            else AnimateStamp(_stampAge);
            _stamp.style.display = _stampAge < 0f || paused ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void AnimateStamp(float t)
        {
            float k = Mathf.Clamp01(t / StampIn);
            float scale = Mathf.Lerp(1.8f, 1f, k * k);
            float fade = t < StampIn + StampHold ? k : 1f - (t - StampIn - StampHold) / StampOut;
            _stamp.style.scale = new Scale(new Vector3(scale, scale, 1f));
            _stamp.style.opacity = Mathf.Clamp01(fade);
            _stamp.style.display = DisplayStyle.Flex;
        }

        public void ShowFinished()
        {
            UISFX.PlaySoloFinish();
            _stampAge = -1f;
            _stamp.style.display = DisplayStyle.None;
            _live.style.display = DisplayStyle.None;
            _results.style.display = DisplayStyle.Flex;
            _training.Focus();
        }

        public void Dispose()
        {
            _live.RemoveFromHierarchy(); _results.RemoveFromHierarchy(); _stamp.RemoveFromHierarchy();
            foreach (var button in _pauseButtons) button.RemoveFromHierarchy();
        }

        /// <summary>
        /// Juggle openers that a Shared probe confirmed against the dummy at 30% (2026-10-17;
        /// Bonk's on 2026-10-18): starter, follow-up and whether to jump in between. Re-check them
        /// when a kit changes (docs/design/tutorial.md); an unknown fighter gets the generic hint.
        /// </summary>
        private static readonly Dictionary<CharacterClass, (byte Starter, byte FollowUp, bool Jump)> ComboOpeners = new()
        {
            [CharacterClass.FightGuy] = (AbilitySlots.Slot3, AbilitySlots.Slot3, true),
            [CharacterClass.Manki] = (AbilitySlots.Slot2, AbilitySlots.R, false),
            [CharacterClass.Wibou] = (AbilitySlots.Slot3, AbilitySlots.Slot3, false),
            [CharacterClass.Bonk] = (AbilitySlots.Slot3, AbilitySlots.Slot1, true),
            [CharacterClass.Bashie] = (AbilitySlots.Slot2, AbilitySlots.R, false), // Manki's borrowed kit (2026-10-22)
        };

        private string ComboHint(bool gamepad)
        {
            const string lead = "Launch the dummy, then hit it again before it lands.";
            if (!ComboOpeners.TryGetValue(_def.Class, out var opener)) return lead + " Cooldowns are off here.";
            string starter = $"{MoveName(opener.Starter)} [{SlotKey(opener.Starter, gamepad)}]";
            string followUp = $"{MoveName(opener.FollowUp, opener.Jump)} [{SlotKey(opener.FollowUp, gamepad)}]";
            return $"{lead} Try {starter}, then {(opener.Jump ? "jump and " : "")}{followUp}. Cooldowns are off here.";
        }

        private (string Title, string Hint) Describe(TutorialLessonId lesson, bool gamepad) => lesson switch
        {
            TutorialLessonId.MoveAndLook => ("MOVE & LOOK", "You always move relative to the camera."),
            TutorialLessonId.Jump => ("JUMP", "Tap jump for a short hop, hold it for a full jump. You get one more jump in the air."),
            TutorialLessonId.FastFall => ("FAST FALL", "Fast falling gets you back on the ground sooner."),
            TutorialLessonId.Attacks => ("ATTACKS", "Four buttons, four different moves."),
            TutorialLessonId.Aerials => ("AERIALS", "The same buttons do different moves in the air."),
            TutorialLessonId.Specials => ("SPECIALS", "Specials hit harder but need to recharge. E also helps you get back to the stage."),
            TutorialLessonId.Recovery => ("RECOVERY", AimedRecovery
                ? "There are no ledges to grab. Use your air jump, then hold E, aim at the stage and release."
                : "There are no ledges to grab. Use your air jump and E to get back."),
            TutorialLessonId.ShieldAndDodge => ("SHIELD & DODGE", "Shield blocks attacks from every side. In the air, the same button dodges the way you push."),
            TutorialLessonId.GrabAndThrow => ("GRAB & THROW", "Grab from up close, then throw."),
            TutorialLessonId.Combo => ("COMBO", ComboHint(gamepad)),
            TutorialLessonId.Knockout => ("KNOCKOUT", "The higher the damage %, the farther they fly. Knock them off the stage to win."),
            _ => (lesson.ToString().ToUpperInvariant(), ""),
        };

        private string Describe(TutorialObjectiveId objective) => objective switch
        {
            TutorialObjectiveId.Run => "Run around",
            TutorialObjectiveId.LookAround => "Turn the camera",
            TutorialObjectiveId.StandOnBlock => "Jump onto a block",
            TutorialObjectiveId.DoubleJump => "Jump again in mid-air",
            TutorialObjectiveId.ReachTallBlock => "Stand on the "
                + _layout.TallBlockHeight.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " m block",
            TutorialObjectiveId.FastFall => "While falling, press Down to fast fall",
            TutorialObjectiveId.HitNormal1 => "Hit the dummy with " + MoveName(AbilitySlots.Slot1),
            TutorialObjectiveId.HitNormal2 => "Hit the dummy with " + MoveName(AbilitySlots.Slot2),
            TutorialObjectiveId.HitNormal3 => "Hit the dummy with " + MoveName(AbilitySlots.Slot3),
            TutorialObjectiveId.HitNormal4 => "Hit the dummy with " + MoveName(AbilitySlots.Slot4),
            TutorialObjectiveId.LandAerials => "Hit the dummy with two different aerials",
            TutorialObjectiveId.UseSpecialA => "Use " + MoveName(AbilitySlots.A),
            TutorialObjectiveId.UseSpecialE => "Use " + MoveName(AbilitySlots.E),
            TutorialObjectiveId.UseSpecialR => "Use " + MoveName(AbilitySlots.R),
            TutorialObjectiveId.UseSpecialF => "Use " + MoveName(AbilitySlots.F),
            TutorialObjectiveId.RecoverToStage => "Drop off an open edge and make it back",
            TutorialObjectiveId.BlockAttacks => "Hold Shield to block the dummy's attacks",
            TutorialObjectiveId.AirDodge => "Press Shield in the air to dodge",
            TutorialObjectiveId.Grab => "Grab the dummy",
            TutorialObjectiveId.Throw => "Press any attack to throw it",
            TutorialObjectiveId.Combo => "Hit the dummy again before it lands",
            TutorialObjectiveId.Knockout => "Knock the dummy off the stage",
            _ => objective.ToString(),
        };

        /// <summary>Some recovery specials aim before they launch; the hint then explains the hold.</summary>
        private bool AimedRecovery
            => (_def.GetSlotAbility(AbilitySlots.E - 1, airborne: true) ?? _def.GetSlotAbility(AbilitySlots.E - 1))?.AimMode
                is { } mode && mode != AimMode.None;

        private string MoveName(byte wireSlot, bool airborne = false)
        {
            var spec = _def.GetSlotAbility(wireSlot - 1, airborne) ?? _def.GetSlotAbility(wireSlot - 1, !airborne);
            return spec != null && !string.IsNullOrWhiteSpace(spec.Name) ? spec.Name : "that move";
        }

        private static string SlotKey(byte wireSlot, bool gamepad) => wireSlot switch
        {
            AbilitySlots.Slot1 => Key("Slot1", gamepad), AbilitySlots.Slot2 => Key("Slot2", gamepad),
            AbilitySlots.Slot3 => Key("Slot3", gamepad), AbilitySlots.Slot4 => Key("Slot4", gamepad),
            AbilitySlots.A => Special("SlotA", "Slot1", gamepad), AbilitySlots.E => Special("SlotE", "Slot3", gamepad),
            AbilitySlots.R => Special("SlotR", "Slot2", gamepad), AbilitySlots.F => Special("SlotF", "Slot4", gamepad),
            _ => "",
        };

        private static string Amount(TutorialObjective objective, float amount) => objective.Id switch
        {
            TutorialObjectiveId.Run => $"{Mathf.FloorToInt(amount)} / {objective.Goal:0} m",
            TutorialObjectiveId.LookAround => $"{Mathf.FloorToInt(amount)}° / {objective.Goal:0}°",
            _ => objective.Goal > 1f ? $"{Mathf.FloorToInt(amount)} / {objective.Goal:0}" : "",
        };

        private static string Prompt(TutorialObjectiveId objective, bool gamepad) => objective switch
        {
            TutorialObjectiveId.Run => gamepad ? "L-STICK"
                : Key("MoveUp", false) + Key("MoveLeft", false) + Key("MoveDown", false) + Key("MoveRight", false),
            TutorialObjectiveId.LookAround => gamepad ? "R-STICK" : "MOUSE",
            TutorialObjectiveId.StandOnBlock or TutorialObjectiveId.DoubleJump or TutorialObjectiveId.ReachTallBlock
                or TutorialObjectiveId.RecoverToStage => Key("Jump", gamepad),
            TutorialObjectiveId.FastFall => Key("Down", gamepad),
            TutorialObjectiveId.HitNormal1 => Key("Slot1", gamepad),
            TutorialObjectiveId.HitNormal2 => Key("Slot2", gamepad),
            TutorialObjectiveId.HitNormal3 => Key("Slot3", gamepad),
            TutorialObjectiveId.HitNormal4 => Key("Slot4", gamepad),
            TutorialObjectiveId.LandAerials or TutorialObjectiveId.Throw
                => Key("Slot1", gamepad) + (gamepad ? " " : "-") + Key("Slot4", gamepad),
            TutorialObjectiveId.UseSpecialA => Special("SlotA", "Slot1", gamepad),
            TutorialObjectiveId.UseSpecialE => Special("SlotE", "Slot3", gamepad),
            TutorialObjectiveId.UseSpecialR => Special("SlotR", "Slot2", gamepad),
            TutorialObjectiveId.UseSpecialF => Special("SlotF", "Slot4", gamepad),
            TutorialObjectiveId.BlockAttacks or TutorialObjectiveId.AirDodge => Key("Shield", gamepad),
            TutorialObjectiveId.Grab => gamepad ? Key("SpecialModifier", true) + " + " + Key("Shield", true) : Key("Grab", false),
            _ => "",
        };

        /// <summary>Specials are their own keys on keyboard and the modifier plus a face button on gamepad.</summary>
        private static string Special(string keyboardAction, string face, bool gamepad)
            => gamepad ? Key("SpecialModifier", true) + " + " + Key(face, true) : Key(keyboardAction, false);

        private static string Key(string action, bool gamepad)
        {
            string group = gamepad ? HumanInputActions.GamepadGroup : HumanInputActions.KeyboardMouseGroup;
            int index = HumanInputActions.BindingIndex(action, group);
            if (index < 0) return "";
            string label = HumanInputActions.BindingLabel(action, group).ToUpperInvariant();
            return gamepad ? label
                : InputPromptAtlas.KeyboardDisplayLabel(HumanInputActions.Get(action).bindings[index].effectivePath, label);
        }

        private static Label Add(VisualElement parent, string text, string css)
        { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(css); parent.Add(label); return label; }
    }
}
