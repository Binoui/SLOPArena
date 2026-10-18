using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Network;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed class ArcadeSelectController : MonoBehaviour, IFrontendPageController
    {
        private FrontendPageContext _context;
        private MatchContentCatalog _catalog;
        private ArenaDefinition _arena;
        private CharacterClass _selected;
        private static bool _courseSelected;
        private ArenaDefinition? _slopballArena;
        /// <summary>Baked courses with a presentation, in menu order; each fighter plays the first that admits it.</summary>
        private readonly List<(CharacterCourseInfo Info, ArenaDefinition Arena)> _courses = new();
        private CharacterCourseInfo _course;
        private ArcadeRecordKey _key;

        public void InjectPageContext(FrontendPageContext context) => _context = context;

        private void OnEnable()
        {
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Arcade;
            var start = _context.Q<Button>("arcade-play");
            var back = _context.Q<Button>("btn-back");
            Action leave = () => FrontendController.Show(FrontendPage.Home);
            back.clicked += leave;
            start.clicked += Launch;
            var grid = _context.Q<VisualElement>("arcade-fighters");
            grid.Clear();
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            foreach (var root in _context.OwnedRoots) if (style != null) root.styleSheets.Add(style);
            var modes = _context.Q<ScrollView>("arcade-modes");
            modes.EnableInClassList("arcade-mode-gallery--single", modes.contentContainer.childCount == 1);
            _slopballArena = LoadArena(SlopballMatch.ArenaKey);
            _courses.Clear();
            foreach (var info in CharacterCourseMatch.Courses)
                if (LoadArena(info.Key) is ArenaDefinition courseArena && courseArena.Arcade?.Course != null)
                    _courses.Add((info, courseArena));
            var ballMode = _context.Q<Button>("arcade-mode-slopball");
            var courseMode = _context.Q<Button>("arcade-mode-course");
            bool ballReady = _slopballArena?.Arcade?.Slopball != null;
            bool courseReady = _courses.Count > 0;
            ballMode.SetEnabled(ballReady); courseMode.SetEnabled(courseReady);
            ballMode.clicked += () => SelectMode(false);
            courseMode.clicked += () => SelectMode(true);
            var tips = _context.Q<Toggle>("arcade-course-tips");
            tips.SetValueWithoutNotify(CharacterCourseMatch.MovementTipsEnabled);
            tips.RegisterValueChangedCallback(e => CharacterCourseMatch.MovementTipsEnabled = e.newValue);
            if (!ClientSession.TryBuildLocalMatchCatalog(out _catalog, out var failure) || _catalog == null || !ballReady && !courseReady)
            {
                SetFeedback("Arcade content is unavailable. Return to the menu.");
                start.SetEnabled(false);
                MenuNavigation.Configure(_context, back, leave, start);
                return;
            }
            SelectMode(_courseSelected && courseReady || !ballReady);
        }

        private static ArenaDefinition? LoadArena(string key)
        {
            var path = BakedContentPaths.ResolveArena(key);
            if (path == null || Resources.Load<GameObject>("Stages/" + key) == null) return null;
            return ArenaBinaryFormat.LoadFromFile(path);
        }

        /// <summary>The course this fighter plays: the first listed course whose profile admits it.</summary>
        private bool TryCourse(MatchContentEntry entry, out CharacterCourseInfo info, out ArenaDefinition arena)
        {
            foreach (var course in _courses)
                if (course.Arena.Arcade.AllowsPackage(entry.Identity.PackageId))
                { info = course.Info; arena = course.Arena; return true; }
            info = null; arena = default; return false;
        }

        private bool Admits(MatchContentEntry entry)
            => _courseSelected ? TryCourse(entry, out _, out _) : _arena.Arcade.AllowsPackage(entry.Identity.PackageId);

        private void SelectMode(bool course)
        {
            if (_catalog == null || (course ? _courses.Count == 0 : !_slopballArena.HasValue)) return;
            _courseSelected = course; _key = null; _course = null;
            if (!course) _arena = _slopballArena.Value;
            _context.Q<Button>("arcade-mode-slopball").EnableInClassList("arcade-mode-card--selected", !course);
            _context.Q<Button>("arcade-mode-course").EnableInClassList("arcade-mode-card--selected", course);
            _context.Q<Label>("arcade-selected-mode").text = course ? "CHARACTER COURSES" : "SLOPBALL";
            _context.Q<Button>("arcade-play").text = course ? "PLAY COURSE" : "PLAY SLOPBALL";
            _context.Q<Toggle>("arcade-course-tips").style.display = course ? DisplayStyle.Flex : DisplayStyle.None;
            var grid = _context.Q<VisualElement>("arcade-fighters"); grid.Clear();
            foreach (var cls in MenuRoster.Classes)
            {
                var entry = _catalog.Resolve(cls);
                if (entry == null || !Admits(entry)) continue;
                var button = new Button(() => Select(cls)) { name = "arcade-fighter-" + cls };
                button.AddToClassList("char-card");
                var portrait = new VisualElement { pickingMode = PickingMode.Ignore };
                portrait.AddToClassList("char-portrait");
                var texture = Resources.Load<Texture2D>("UI/Portraits/" + cls);
                if (texture != null) portrait.style.backgroundImage = new StyleBackground(texture);
                var name = new Label(entry.DisplayName.ToUpperInvariant()); name.AddToClassList("char-card-name");
                button.Add(portrait); button.Add(name); grid.Add(button);
                var check = new VisualElement { pickingMode = PickingMode.Ignore };
                check.AddToClassList("char-card-check");
                button.Add(check);
            }
            var current = _catalog.Resolve(MatchConfig.PlayerClass);
            _selected = current != null && Admits(current) ? MatchConfig.PlayerClass : CharacterClass.FightGuy;
            Select(_selected);
            MenuNavigation.Configure(_context, grid.Q<Button>("arcade-fighter-" + _selected)
                ?? grid.Children().OfType<Button>().FirstOrDefault() ?? _context.Q<Button>("btn-back"),
                () => FrontendController.Show(FrontendPage.Home), _context.Q<Button>("arcade-play"));
        }

        private void Select(CharacterClass fighter)
        {
            var entry = _catalog.Resolve(fighter);
            if (entry == null || !Admits(entry)) return;
            if (_courseSelected)
            {
                TryCourse(entry, out _course, out var courseArena);
                _arena = courseArena;
                _context.Q<Label>("arcade-selected-mode").text = _course.Title;
            }
            _selected = fighter;
            MatchConfig.PlayerClass = fighter;
            MatchConfig.PlayerPackageId = entry.Identity.PackageId;
            foreach (var button in _context.Q<VisualElement>("arcade-fighters").Children())
                button.EnableInClassList("char-card--selected", button.name == "arcade-fighter-" + fighter);
            _context.Q<Label>("arcade-best-fighter").text = entry.DisplayName.ToUpperInvariant() + " · PERSONAL BEST";
            try
            {
                _key = new(_courseSelected ? "character-course" : "slopball", _arena.Arcade.ProfileId, entry.Identity.PackageId,
                    _courseSelected ? ArcadeRecordCompatibility.Course(_arena, entry) : ArcadeRecordCompatibility.Slopball(_arena, entry));
                var store = ArcadeRecordsStore.Instance;
                var best = store.Book.Best(_key);
                _context.Q<Label>("arcade-best").text = best.HasValue
                    ? _courseSelected ? CharacterCourseView.FormatTime(best.Value) : $"{best} {(best == 1 ? "RETURN" : "RETURNS")}" : "NO BEST YET";
                var archive = _context.Q<VisualElement>("arcade-archive");
                archive.Clear();
                foreach (var record in store.Book.Records.Where(x => x.Key.ChallengeId == _key.ChallengeId
                    && x.Key.ProfileId == _key.ProfileId && x.Key.FighterPackageId == _key.FighterPackageId
                    && x.Key != _key).OrderByDescending(x => x.AchievedUtc))
                    archive.Add(new Label($"{(_courseSelected ? CharacterCourseView.FormatTime(record.Value) : record.Value + " returns")} · {record.AchievedUtc.Substring(0, 10)}"));
                _context.Q<Foldout>("arcade-older-bests").style.display = archive.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                SetFeedback(store.Feedback);
                _context.Q<Button>("arcade-play").SetEnabled(true);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                SetFeedback("This fighter's Arcade record profile is unavailable. Choose another fighter.");
                _context.Q<Button>("arcade-play").SetEnabled(false);
                Debug.LogError("[Arcade] " + e.Message);
            }
        }

        private void SetFeedback(string message)
        {
            var label = _context.Q<Label>("arcade-feedback");
            label.text = message ?? "";
            label.style.display = string.IsNullOrWhiteSpace(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Launch()
        {
            if (!_context.Valid || _key == null || _catalog.Resolve(_selected) == null || _courseSelected && _course == null) return;
            MatchConfig.Mode = GameMode.Arcade;
            MatchConfig.ArenaName = _courseSelected ? _course.Key : SlopballMatch.ArenaKey;
            MatchConfig.IsHost = true;
#if UNITY_EDITOR
            UnityEditor.SessionState.SetString(ArcadeBallMatch.FighterSessionKey, _selected.ToString());
#endif
            SceneManager.LoadScene(_courseSelected ? CharacterCourseMatch.SceneName : SlopballMatch.SceneName);
        }
    }
}
