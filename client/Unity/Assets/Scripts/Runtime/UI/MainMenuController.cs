using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Home mode actions and announcement skin; the shell owns shared navigation and identity.</summary>
    public class MainMenuController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;
        private VisualElement? _artworkColumn;
        private ReplayListOverlay? _replays;
        private WorkshopOverlay? _workshop;
        private VisualElement? _artwork;


        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            Workshop.ModelSwapLibrary.EndPreview();
            string? returningWorkspace = Workshop.FighterWorkspaceTraining.End();
            MatchConfig.Reset();
            if (_context == null)
            {
                Debug.LogError("[MainMenuController] No page context was injected.");
                enabled = false;
                return;
            }
            _context.Q<Button>("menu-online")!.clicked += () => FrontendController.StartMode(GameMode.PvP);
            _context.Q<Button>("menu-local")!.clicked += () => FrontendController.StartMode(GameMode.Solo);
            _context.Q<Button>("menu-training")!.clicked += () => FrontendController.StartMode(GameMode.Training);
            var tutorial = _context.Q<Button>("menu-tutorial")!;
            tutorial.clicked += () => FrontendController.StartMode(GameMode.Tutorial);
            // Newcomers land on the tutorial until they have finished it once.
            bool tutorialDone = TutorialView.HasCompleted;
            _context.Q<VisualElement>("menu-tutorial-new")?.style.SetDisplay(!tutorialDone);
            var discordButton = _context.Q<Button>("menu-discord")!;
            discordButton.clicked += OpenDiscord;
            _context.AddReleaseAction(() => discordButton.clicked -= OpenDiscord);
            MenuNavigation.Configure(_context, tutorialDone ? _context.Q<Button>("menu-training") : tutorial, ReturnToMainMenu);
            AddReplaysButton();
            var workshop = new Button(() =>
            {
                if (_workshop is { IsOpen: true }) return;
                VisualElement? host = _context.Q<Button>("menu-online");
                while (host?.parent?.parent != null) host = host.parent;
                if (host != null) _workshop = new WorkshopOverlay(host, _context);
            }) { name = "menu-workshop", text = "WORKSHOP" };
            workshop.AddToClassList("menu-action");
            _context.Q<Button>("menu-online")?.parent.Add(workshop);
            _context.AddReleaseAction(() => { workshop.RemoveFromHierarchy(); _workshop?.Close(); });
            if (returningWorkspace != null)
                workshop.schedule.Execute(() =>
                {
                    VisualElement? host = workshop;
                    while (host?.parent?.parent != null) host = host.parent;
                    if (host != null) _workshop = new WorkshopOverlay(host, _context);
                    _workshop?.OpenFighterWorkspace(returningWorkspace);
                });
            _artworkColumn = _context.Q<VisualElement>("menu-artwork-column");
            _artwork = _context.Q<VisualElement>("menu-artwork");
            _artworkColumn?.RegisterCallback<GeometryChangedEvent>(FitArtwork);
        }

        /// <summary>Replays appear on Home only when the Replay folder holds a file, so players without any never see it.</summary>
        private void AddReplaysButton()
        {
            var actions = _context.Q<Button>("menu-online")?.parent;
            if (actions == null || !Replays.ReplayDirectory.HasAnyReplay(Replays.ReplayDirectory.Resolve(Replays.ReplayDirectoryProbe.Current())))
                return;
            var button = new Button(OpenReplays) { name = "menu-replays", text = "REPLAYS" };
            button.AddToClassList("menu-action");
            actions.Add(button);
            _context.AddReleaseAction(() => { button.RemoveFromHierarchy(); _replays?.Close(); _replays = null; });
            if (Replays.ReplayLaunch.ReopenListOnReturn)
            {
                Replays.ReplayLaunch.ReopenListOnReturn = false;
                OpenReplays();
            }
        }

        private void OpenReplays()
        {
            if (_replays is { IsOpen: true }) return;
            // The frontend document's root, above the page fragment, so the list covers the shell.
            VisualElement? host = _context.Q<Button>("menu-online");
            while (host?.parent?.parent != null) host = host.parent;
            if (host != null) _replays = new ReplayListOverlay(host);
        }

        private void FitArtwork(GeometryChangedEvent _)
        {
            if (_artworkColumn == null || _artwork == null)
                return;
            var available = _artworkColumn.contentRect.size;
            if (available.x <= 0 || available.y <= 31)
                return;
            // Image aspect plus frame/padding: no cropped art or empty side slabs.
            const float imageAspect = 1438f / 810f;
            float width = Mathf.Min(Mathf.Min(1464f, available.x), (available.y - 31f) * imageAspect + 26f);
            _artwork.style.width = width;
            _artwork.style.height = (width - 26f) / imageAspect + 31f;
        }

        private void OnDisable()
        {
            _artworkColumn?.UnregisterCallback<GeometryChangedEvent>(FitArtwork);
            _artworkColumn = null;
            _artwork = null;
        }


        private static void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/VvfaxDCF6Z");
        }

        private static void ReturnToMainMenu()
        {
            // MainMenu is the root of this flow; Escape here is intentionally a no-op.
        }
    }
}
