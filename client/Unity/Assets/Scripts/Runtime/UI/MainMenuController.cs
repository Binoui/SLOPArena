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
        private VisualElement? _artwork;


        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            MatchConfig.Reset();
            if (_context == null)
            {
                Debug.LogError("[MainMenuController] No page context was injected.");
                enabled = false;
                return;
            }
            _context.Q<Button>("menu-online")!.clicked += () => FrontendController.StartMode(GameMode.PvP);
            _context.Q<Button>("menu-solo")!.clicked += () => FrontendController.StartMode(GameMode.Solo);
            _context.Q<Button>("menu-training")!.clicked += () => FrontendController.StartMode(GameMode.Training);
            var discordButton = _context.Q<Button>("menu-discord")!;
            discordButton.clicked += OpenDiscord;
            _context.AddReleaseAction(() => discordButton.clicked -= OpenDiscord);
            MenuNavigation.Configure(_context, _context.Q<Button>("menu-online"), ReturnToMainMenu);
            _artworkColumn = _context.Q<VisualElement>("menu-artwork-column");
            _artwork = _context.Q<VisualElement>("menu-artwork");
            _artworkColumn?.RegisterCallback<GeometryChangedEvent>(FitArtwork);
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
