using System;
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
            var path = BakedContentPaths.ResolveArena(SlopballMatch.ArenaKey);
            var loaded = path == null ? null : ArenaBinaryFormat.LoadFromFile(path);
            if (loaded is not ArenaDefinition arena || arena.Arcade?.Slopball == null
                || Resources.Load<GameObject>("Stages/slopball") == null
                || !ClientSession.TryBuildLocalMatchCatalog(out _catalog, out var failure) || _catalog == null)
            {
                SetFeedback("Slopball content is unavailable. Return to the menu.");
                start.SetEnabled(false);
                MenuNavigation.Configure(_context, back, leave, start);
                return;
            }
            _arena = arena;
            foreach (var cls in MenuRoster.Classes)
            {
                var entry = _catalog.Resolve(cls);
                if (entry == null || !arena.Arcade.AllowsPackage(entry.Identity.PackageId)) continue;
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
            _selected = _catalog.Resolve(MatchConfig.PlayerClass) != null ? MatchConfig.PlayerClass : CharacterClass.FightGuy;
            Select(_selected);
            MenuNavigation.Configure(_context, grid.Q<Button>("arcade-fighter-" + _selected)
                ?? grid.Children().OfType<Button>().FirstOrDefault() ?? back, leave, start);
        }

        private void Select(CharacterClass fighter)
        {
            var entry = _catalog.Resolve(fighter);
            if (entry == null || !_arena.Arcade.AllowsPackage(entry.Identity.PackageId)) return;
            _selected = fighter;
            MatchConfig.PlayerClass = fighter;
            MatchConfig.PlayerPackageId = entry.Identity.PackageId;
            foreach (var button in _context.Q<VisualElement>("arcade-fighters").Children())
                button.EnableInClassList("char-card--selected", button.name == "arcade-fighter-" + fighter);
            _context.Q<Label>("arcade-best-fighter").text = entry.DisplayName.ToUpperInvariant() + " · PERSONAL BEST";
            try
            {
                _key = new("slopball", _arena.Arcade.ProfileId, entry.Identity.PackageId,
                    ArcadeRecordCompatibility.Slopball(_arena, entry));
                var store = ArcadeRecordsStore.Instance;
                var best = store.Book.Best(_key);
                _context.Q<Label>("arcade-best").text = best.HasValue
                    ? $"{best} {(best == 1 ? "RETURN" : "RETURNS")}" : "NO BEST YET";
                var archive = _context.Q<VisualElement>("arcade-archive");
                archive.Clear();
                foreach (var record in store.Book.Records.Where(x => x.Key.ChallengeId == _key.ChallengeId
                    && x.Key.ProfileId == _key.ProfileId && x.Key.FighterPackageId == _key.FighterPackageId
                    && x.Key != _key).OrderByDescending(x => x.AchievedUtc))
                    archive.Add(new Label($"{record.Value} returns · {record.AchievedUtc.Substring(0, 10)}"));
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
            if (!_context.Valid || _key == null || _catalog.Resolve(_selected) == null) return;
            MatchConfig.Mode = GameMode.Arcade;
            MatchConfig.ArenaName = SlopballMatch.ArenaKey;
            MatchConfig.IsHost = true;
#if UNITY_EDITOR
            UnityEditor.SessionState.SetString(ArcadeBallMatch.FighterSessionKey, _selected.ToString());
#endif
            SceneManager.LoadScene(SlopballMatch.SceneName);
        }
    }
}
