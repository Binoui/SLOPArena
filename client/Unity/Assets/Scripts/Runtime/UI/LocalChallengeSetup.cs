using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Network;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class LocalChallengeSetup
    {
        private FrontendPageContext _context;
        private MatchContentCatalog? _catalog;
        private ArenaDefinition _arena;
        private CharacterClass _selected;
        private readonly bool _courseSelected;
        private ArenaDefinition? _slopballArena;
        private CharacterCourseInfo? _course;
        private ArcadeRecordKey? _key;

        public LocalChallengeSetup(FrontendPageContext context)
        {
            _context = context;
            _courseSelected = MatchConfig.LocalMode == LocalPlayMode.CharacterCourse;
            ClientSession.TryBuildLocalMatchCatalog(out _catalog, out _);
            _slopballArena = LoadArena(SlopballMatch.ArenaKey);
            if (_courseSelected)
                foreach (var course in AvailableCourses())
                    if (course.Info.Key == MatchConfig.ArenaName)
                    { _course = course.Info; _arena = course.Arena; break; }
            if (!_courseSelected && _slopballArena.HasValue) _arena = _slopballArena.Value;
        }

        public static bool Available(LocalPlayModeInfo mode)
        {
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out _) || catalog == null) return false;
            if (mode.HasStageStep) return MenuRoster.Classes.Length > 0;
            if (mode.Seats == LocalSeatPolicy.FourSeats) return LoadArena(mode.Arena)?.Arcade?.Mayhem != null;
            if (mode.Mode == LocalPlayMode.CharacterCourse)
                return AvailableCourses().Any() && MenuRoster.Classes.Any(cls => catalog.Resolve(cls) != null);
            return LoadArena(mode.Arena) is ArenaDefinition ball && ball.Arcade?.Slopball != null
                && MenuRoster.Classes.Any(cls => catalog.Resolve(cls) is { } entry && ball.Arcade.AllowsPackage(entry.Identity.PackageId));
        }

        public bool Admits(CharacterClass fighter)
            => _catalog?.Resolve(fighter) is { } entry && (_courseSelected ? _course != null && _arena.Arcade?.Course != null
                : _slopballArena?.Arcade?.Slopball != null && _arena.Arcade!.AllowsPackage(entry.Identity.PackageId));

        public void Bind()
        {
            var tips = _context.Q<Toggle>("arcade-course-tips")!;
            tips.style.display = _courseSelected ? DisplayStyle.Flex : DisplayStyle.None;
            tips.SetValueWithoutNotify(CharacterCourseMatch.MovementTipsEnabled);
            tips.RegisterValueChangedCallback(e => CharacterCourseMatch.MovementTipsEnabled = e.newValue);
        }

        private static ArenaDefinition? LoadArena(string key)
        {
            var path = BakedContentPaths.ResolveArena(key);
            if (path == null || !HasStageVisual(key)) return null;
            return ArenaBinaryFormat.LoadFromFile(path);
        }

        private static readonly Dictionary<string, bool> StageVisuals = new(StringComparer.Ordinal);

        /// <summary>Whether Resources holds the stage's visual prefab. Loading one pulls in all its meshes and
        /// textures, so a player build checks each stage once per session; the Editor rechecks as stages change.</summary>
        internal static bool HasStageVisual(string name)
        {
            if (Application.isEditor) return Resources.Load<GameObject>("Stages/" + name) != null;
            if (!StageVisuals.TryGetValue(name, out bool exists))
                StageVisuals[name] = exists = Resources.Load<GameObject>("Stages/" + name) != null;
            return exists;
        }

        /// <summary>Baked courses with a presentation, in the course menu's authored order.</summary>
        public static IEnumerable<(CharacterCourseInfo Info, ArenaDefinition Arena)> AvailableCourses()
        {
            foreach (var info in CharacterCourseMatch.Courses)
                if (LoadArena(info.Key) is ArenaDefinition arena && arena.Arcade?.Course != null)
                    yield return (info, arena);
        }

        public void Select(CharacterClass fighter)
        {
            var entry = _catalog?.Resolve(fighter);
            if (entry == null || !Admits(fighter)) return;
            _selected = fighter;
            MatchConfig.PlayerClass = fighter;
            MatchConfig.PlayerPackageId = entry.Identity.PackageId;
            _context.Q<Label>("arcade-selected-mode")!.text = _courseSelected ? _course!.Title : "SLOPBALL";
            var poster = _context.Q<VisualElement>("local-record-poster")!;
            var texture = Resources.Load<Texture2D>("UI/Stages/" + (_courseSelected ? _course!.Key : SlopballMatch.ArenaKey));
            if (texture != null) poster.style.backgroundImage = new StyleBackground(texture);
            else poster.style.backgroundImage = StyleKeyword.Null;
            poster.EnableInClassList("local-poster--course", _courseSelected);
            poster.EnableInClassList("local-poster--slopball", !_courseSelected);
            _context.Q<Label>("arcade-best-fighter")!.text = entry.DisplayName.ToUpperInvariant() + " · PERSONAL BEST";
            try
            {
                _key = new(_courseSelected ? "character-course" : "slopball", _arena.Arcade!.ProfileId, entry.Identity.PackageId,
                    _courseSelected ? ArcadeRecordCompatibility.Course(_arena, entry) : ArcadeRecordCompatibility.Slopball(_arena, entry));
                var store = ArcadeRecordsStore.Instance;
                var best = store.Book.Best(_key);
                _context.Q<Label>("arcade-best")!.text = best.HasValue
                    ? _courseSelected ? CharacterCourseView.FormatTime(best.Value) : $"{best} {(best == 1 ? "RETURN" : "RETURNS")}" : "NO BEST YET";
                var archive = _context.Q<VisualElement>("arcade-archive")!;
                archive.Clear();
                foreach (var record in store.Book.Records.Where(x => x.Key.ChallengeId == _key.ChallengeId
                    && x.Key.ProfileId == _key.ProfileId && x.Key.FighterPackageId == _key.FighterPackageId
                    && x.Key != _key).OrderByDescending(x => x.AchievedUtc))
                    archive.Add(new Label($"{(_courseSelected ? CharacterCourseView.FormatTime(record.Value) : record.Value + " returns")} · {record.AchievedUtc.Substring(0, 10)}"));
                _context.Q<Foldout>("arcade-older-bests")!.style.display = archive.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                SetFeedback(store.Feedback);
                _context.Q<Button>("btn-select")!.SetEnabled(true);
                RefreshSeats();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                SetFeedback("This fighter's Arcade record profile is unavailable. Choose another fighter.");
                _context.Q<Button>("btn-select")!.SetEnabled(false);
                Debug.LogError("[Arcade] " + e.Message);
            }
        }

        private void SetFeedback(string message)
        {
            var label = _context.Q<Label>("arcade-feedback")!;
            label.text = message ?? "";
            label.style.display = string.IsNullOrWhiteSpace(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void RefreshSeats()
        {
            if (!_courseSelected) return;
            bool scramble = MatchConfig.SoloSeats.Any(s => s.Kind == SeatKind.LocalPlayer);
            _context.Q<Button>("btn-select")!.text = scramble ? "START SCRAMBLE" : "START COURSE";
            _context.Q<VisualElement>("local-record-panel")!.style.SetDisplay(!scramble);
        }

        public void Launch()
        {
            if (!_context.Valid || _key == null || _catalog?.Resolve(_selected) == null || _courseSelected && _course == null) return;
            MatchConfig.Mode = GameMode.Arcade;
            MatchConfig.ArenaName = _courseSelected ? _course!.Key : SlopballMatch.ArenaKey;
            MatchConfig.IsHost = true;
#if UNITY_EDITOR
            UnityEditor.SessionState.SetString(ArcadeBallMatch.FighterSessionKey, _selected.ToString());
#endif
            LocalPlayModes.Current.Launch();
        }
    }
}
