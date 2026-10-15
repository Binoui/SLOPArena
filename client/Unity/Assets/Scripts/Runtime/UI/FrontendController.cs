using System;
using SlopArena.Client.Network;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.UI
{
    /// <summary>Pages hosted by the single frontend scene (ADR-0032, issues #210, #212).</summary>
    public enum FrontendPage
    {
        Home,
        FighterSelect,
        StageSelect,
        Results,
        ServerBrowser,
        LobbyRoom,
        ArcadeSelect
    }

    /// <summary>
    /// Shared frontend shell (ADR-0032, issues #210, #212). One scene hosts
    /// Home, Fighter Select, Stage Select, Results, Server Browser and Lobby
    /// Room; explicit page activation/deactivation replaces scene-per-page
    /// navigation. Gameplay scenes stay separate: the frontend unloads for a
    /// match and is recreated on return through a pending page.
    ///
    /// All page fragments mount into the stable FrontendShell hosts once
    /// per activation through a per-activation <see cref="FrontendPageContext"/>.
    /// Navigation retains sole ownership of routing, pending returns and gameplay handoff.
    /// </summary>
    public sealed class FrontendController : MonoBehaviour
    {
        public const string SceneName = "Frontend";

        [SerializeField] private GameObject _homePage = null!;
        [SerializeField] private GameObject _fighterSelectPage = null!;
        [SerializeField] private GameObject _stageSelectPage = null!;
        [SerializeField] private GameObject _resultsPage = null!;
        [SerializeField] private GameObject _serverBrowserPage = null!;
        [SerializeField] private GameObject _lobbyRoomPage = null!;
        [SerializeField] private GameObject _arcadePage = null!;
        [SerializeField] private FrontendShellView? _shell;
        [SerializeField] private VisualTreeAsset? _homeFragment;
        [SerializeField] private VisualTreeAsset? _fighterSelectFragment;
        [SerializeField] private VisualTreeAsset? _stageSelectFragment;
        [SerializeField] private VisualTreeAsset? _resultsFragment;
        [SerializeField] private VisualTreeAsset? _serverBrowserFragment;
        [SerializeField] private VisualTreeAsset? _lobbyRoomFragment;
        [SerializeField] private VisualTreeAsset? _arcadeFragment;

        private FrontendShellIdentityView? _identity;
        private FrontendFocusRouter? _focusRouter;
        private Button? _navHome;
        private Button? _navTraining;
        private Button? _navOffline;
        private Button? _navArcade;
        private Button? _navOnline;
        private ChatSession? _navSession;

        private static FrontendController? _instance;
        private static FrontendPage? _pendingPage;
        private FrontendPage _current;
        private FrontendPageContext? _currentContext;
        private int _generation;
        /// <summary>Raised after the active page switches; the shell's persistent guests can bind once.</summary>
        public static event Action<FrontendPage>? PageChanged;

        /// <summary>The currently active page; Home before the shell starts.</summary>
        public static FrontendPage CurrentPage =>
            _instance != null ? _instance._current : FrontendPage.Home;

        /// <summary>True while the frontend shell is loaded (issue #214).</summary>
        public static bool IsFrontendActive => _instance != null;

        /// <summary>The live page context of the active activation.</summary>
        public static FrontendPageContext? CurrentContext => _instance?._currentContext;

        /// <summary>The stable shell view; null outside the frontend scene.</summary>
        public static FrontendShellView? Shell => _instance?._shell;

        /// <summary>
        /// The shared shell identity surface (issue #220); null outside the
        /// frontend scene. Mode gating reads it instead of any page-owned
        /// identity panel.
        /// </summary>
        public static FrontendShellIdentityView? Identity => _instance?._identity;

        /// <summary>The shell focus router (issue #220); null outside the frontend scene.</summary>
        public static FrontendFocusRouter? FocusRouter => _instance?._focusRouter;


        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_navSession != null)
                _navSession.Changed -= RenderNavigation;
            if (_instance == this)
                _instance = null;
        }

        private void Start()
        {
            _shell?.Bind();
            _identity = GetComponent<FrontendShellIdentityView>();
            _focusRouter = GetComponent<FrontendFocusRouter>();
            _identity?.Bind(_shell);
            _focusRouter?.Bind(_shell);
            BindNavigation();
            Show(_pendingPage ?? FrontendPage.Home);
            _pendingPage = null;
        }

        private void Update()
        {
            if (_navSession == null && ChatSession.Instance is { } session)
            {
                _navSession = session;
                _navSession.Changed += RenderNavigation;
                RenderNavigation();
            }
        }

        private void BindNavigation()
        {
            var bar = _shell?.TopBar;
            _navHome = bar?.Q<Button>("shell-wordmark");
            _navTraining = bar?.Q<Button>("shell-nav-training");
            _navOffline = bar?.Q<Button>("shell-nav-offline");
            _navArcade = bar?.Q<Button>("shell-nav-arcade");
            _navOnline = bar?.Q<Button>("shell-nav-online");
            if (_navHome != null) _navHome.clicked += NavigateHome;
            if (_navTraining != null) _navTraining.clicked += () => NavigateMode(GameMode.Training);
            if (_navOffline != null) _navOffline.clicked += () => NavigateMode(GameMode.Solo);
            if (_navArcade != null) _navArcade.clicked += () => NavigateMode(GameMode.Arcade);
            if (_navOnline != null) _navOnline.clicked += () => NavigateMode(GameMode.PvP);
        }

        private void NavigateHome()
        {
            if (UiModalState.Presented || _current == FrontendPage.Home)
                return;
            if (_current == FrontendPage.ServerBrowser
                || _current == FrontendPage.ArcadeSelect
                || (_current == FrontendPage.FighterSelect
                    && MatchConfig.Mode is GameMode.Solo or GameMode.Training))
                _currentContext?.InvokeBackAction();
        }

        private bool CanSwitchMode() =>
            _identity is { ModeGateClosed: false }
            && (_current is FrontendPage.Home or FrontendPage.ServerBrowser or FrontendPage.ArcadeSelect
                || (_current is FrontendPage.FighterSelect or FrontendPage.StageSelect
                    && MatchConfig.Mode is GameMode.Solo or GameMode.Training));

        private void NavigateMode(GameMode mode)
        {
            if (!CanSwitchMode() || UiModalState.Presented)
                return;
            if ((_current == FrontendPage.ServerBrowser && mode == GameMode.PvP)
                || (_current == FrontendPage.ArcadeSelect && mode == GameMode.Arcade)
                || (_current is FrontendPage.FighterSelect or FrontendPage.StageSelect
                    && MatchConfig.Mode == mode))
                return;

            // Activating Home runs its existing reset and the departing page's
            // cleanup before entering a different mode.
            if (_current != FrontendPage.Home)
                Show(FrontendPage.Home);
            StartMode(mode);
        }

        public static void StartMode(GameMode mode)
        {
            if (_instance == null || _instance._current != FrontendPage.Home
                || _instance._identity is not { ModeGateClosed: false } || UiModalState.Presented)
                return;
            MatchConfig.Mode = mode;
            MatchConfig.IsHost = mode != GameMode.PvP;
            Show(mode == GameMode.Arcade ? FrontendPage.ArcadeSelect
                : mode == GameMode.PvP ? FrontendPage.ServerBrowser : FrontendPage.FighterSelect);
        }

        private void RenderNavigation()
        {
            bool home = _current == FrontendPage.Home;
            bool canPlay = home && _identity is { ModeGateClosed: false };
            bool canSwitchMode = CanSwitchMode();
            bool canReturnHome = _currentContext?.BackAction != null
                && (_current == FrontendPage.ServerBrowser
                    || _current == FrontendPage.ArcadeSelect
                    || (_current == FrontendPage.FighterSelect
                        && MatchConfig.Mode is GameMode.Solo or GameMode.Training));
            _navHome?.SetEnabled(home || canReturnHome);
            _navTraining?.SetEnabled(canSwitchMode);
            _navOffline?.SetEnabled(canSwitchMode);
            _navArcade?.SetEnabled(canSwitchMode);
            _navOnline?.SetEnabled(canSwitchMode);
            _currentContext?.Q<Button>("menu-online")?.SetEnabled(canPlay);
            _currentContext?.Q<Button>("menu-solo")?.SetEnabled(canPlay);
            _currentContext?.Q<Button>("menu-training")?.SetEnabled(canPlay);
            _currentContext?.Q<Button>("menu-arcade")?.SetEnabled(canPlay);

            bool inSetupOrResults = _current is FrontendPage.FighterSelect or FrontendPage.StageSelect
                or FrontendPage.Results;
            bool online = _current is FrontendPage.ServerBrowser or FrontendPage.LobbyRoom
                || (inSetupOrResults && MatchConfig.Mode == GameMode.PvP);
            bool training = inSetupOrResults && MatchConfig.Mode == GameMode.Training;
            bool offline = inSetupOrResults && MatchConfig.Mode == GameMode.Solo;
            bool arcade = _current == FrontendPage.ArcadeSelect
                || (inSetupOrResults && MatchConfig.Mode == GameMode.Arcade);
            _navTraining?.EnableInClassList("shell-nav-tab--selected", training);
            _navOffline?.EnableInClassList("shell-nav-tab--selected", offline);
            _navArcade?.EnableInClassList("shell-nav-tab--selected", arcade);
            _navOnline?.EnableInClassList("shell-nav-tab--selected", online);
        }

        /// <summary>
        /// Navigate to a frontend page. Inside the frontend scene this
        /// deactivates the current page (its cleanup runs) and activates the
        /// target; from any other scene — gameplay, browser, lobby — it queues
        /// the target page and (re)loads the frontend scene.
        /// </summary>
        public static void Show(FrontendPage page)
        {
            var shell = _instance;
            if (shell == null)
            {
                _pendingPage = page;
                SceneManager.LoadScene(SceneName);
                return;
            }
            shell.Activate(page);
        }

        /// <summary>
        /// The activation/teardown sequence (issue #218 contract): the
        /// departing activation is invalidated immediately, the departing
        /// controller is disabled through its Unity lifecycle so its cleanup
        /// runs while its visual references still exist, fragments are
        /// removed and the departing context released, the destination
        /// fragments are cloned and mounted and their context injected while
        /// the destination controller is inactive, and only then is the
        /// destination generation set and the controller activated. A newer
        /// navigation generation from a nested redirect suppresses the stale
        /// page-changed publication.
        /// </summary>
        private void Activate(FrontendPage page)
        {
            _generation++;
            // The current page deactivates first so a same-page Show() still
            // re-runs the page's OnEnable cleanup/rebuild (issue #213), and
            // so re-entering Home resets preparation only on an explicit
            // activation (issue #210).
            _homePage?.SetActive(false);
            _fighterSelectPage?.SetActive(false);
            _stageSelectPage?.SetActive(false);
            _resultsPage?.SetActive(false);
            _serverBrowserPage?.SetActive(false);
            _lobbyRoomPage?.SetActive(false);
            _arcadePage?.SetActive(false);

            // Fragments come down and the departing context releases while
            // its elements are still attached (release actions may close
            // page-owned dialogs). Shell, social and identity UI survive.
            _currentContext?.Release();
            _currentContext = null;
            // Stale initial focus is cleared on page departure (issue #220):
            // a scheduled old-page focus call can never steal focus after a
            // navigation or a social/modal switch.
            MenuNavigation.ClearPageInitialFocus();
            _shell?.ClearPageHosts();

            var fragment = page switch
            {
                FrontendPage.Home => _homeFragment,
                FrontendPage.FighterSelect => _fighterSelectFragment,
                FrontendPage.StageSelect => _stageSelectFragment,
                FrontendPage.Results => _resultsFragment,
                FrontendPage.ServerBrowser => _serverBrowserFragment,
                FrontendPage.LobbyRoom => _lobbyRoomFragment,
                FrontendPage.ArcadeSelect => _arcadeFragment,
                _ => null
            };
            if (fragment == null)
            {
                Debug.LogError($"[FrontendController] No fragment source assigned for {page}; page stays blank.");
            }
            else
            {
                var context = _shell!.MountPage(page, fragment);
                _currentContext = context;
                if (PageObject(page) is not { } pageObject
                    || pageObject.GetComponent<IFrontendPageController>() is not { } controller)
                    Debug.LogError($"[FrontendController] {page} has no {nameof(IFrontendPageController)}; its fragment renders but the page stays inert.");
                else
                    controller.InjectPageContext(context);
            }

            // The destination/current-page generation is set before the
            // controller activates (issue #218), so the controller's context
            // is already valid inside its OnEnable.
            _current = page;
            _shell?.ApplyPageMode(page);

            int generationAtMount = _generation;
            PageObject(page)?.SetActive(true);


            // A nested Show() from the activation (e.g. Fighter Select
            // without a lobby connection) supersedes this activation: never
            // publish the stale page-changed event.
            if (_generation != generationAtMount || _current != page)
                return;
            PageChanged?.Invoke(page);
            RenderNavigation();
        }

        private GameObject PageObject(FrontendPage page) => page switch
        {
            FrontendPage.Home => _homePage,
            FrontendPage.FighterSelect => _fighterSelectPage,
            FrontendPage.StageSelect => _stageSelectPage,
            FrontendPage.Results => _resultsPage,
            FrontendPage.ServerBrowser => _serverBrowserPage,
            FrontendPage.LobbyRoom => _lobbyRoomPage,
            FrontendPage.ArcadeSelect => _arcadePage,
            _ => null!
        };
    }
}
