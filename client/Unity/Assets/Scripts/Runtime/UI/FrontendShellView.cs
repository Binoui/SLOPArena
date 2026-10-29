using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Binds the stable FrontendShell document (issue #219): named hosts for
    /// page header/body, lower-row summary/actions, an absolute social
    /// overlay, modal host, and the one-time
    /// geometry/density registration on the stable shell root. This view has
    /// no lobby, network, navigation or social logic — the controllers keep
    /// those responsibilities.
    /// </summary>
    public sealed class FrontendShellView : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument = null!;

        // Windows narrower in width or height than these panel-space pixels
        // (physical pixels at the project's 96 dpi) use compact density:
        // 1280×720/800-class windows are compact, roomy desktop windows are
        // not (issue #218 size targets).
        private const float CompactMaxWidth = 1600f;
        private const float CompactMaxHeight = 860f;

        private VisualElement? _root;
        private VisualElement? _topBar;
        private VisualElement? _pageContextLabel;
        private VisualElement? _pageHeaderHost;
        private VisualElement? _pageBodyHost;
        private VisualElement? _socialHost;
        private VisualElement? _pageLowerHost;
        private VisualElement? _pageSummaryHost;
        private VisualElement? _pageActionsHost;
        private VisualElement? _modalHost;
        private VisualElement? _menuModal;
        private Button? _resumeButton;
        private Button? _settingsButton;
        private VisualElement? _restoreFocus;
        private PickingMode _originalModalPicking;
        private bool _menuOpen;
        private SettingsOverlay? _settingsOverlay;
        private VisualElement? _pageModalSection;
        private VisualElement? _lowerRow;
        private bool _compact;
        private bool _densityBound;
        private bool _densityGeometryApplied;
        private float _lastPanelScale;
        private float _preferredCellWidthPx;
        private float _preferredCellHeightPx;

        /// <summary>The stable shell root; null before the document binds.</summary>
        public VisualElement? Root => _root;

        /// <summary>The shell top bar row.</summary>
        public VisualElement? TopBar => _topBar;

        /// <summary>
        /// The page content container (header, body, lower-right cells). The
        /// chat presenter uses it as the Page focus region; it contains the
        /// social cell too, so focusability toggles exclude the social
        /// subtree.
        /// </summary>
        public VisualElement? PageContentRoot { get; private set; }

        /// <summary>The absolute bottom-left, user-sized conversation overlay.</summary>
        public VisualElement? SocialHost => _socialHost;


        /// <summary>The hidden host for modal surfaces.</summary>
        public VisualElement? ModalHost => _modalHost;

        private void OnEnable()
        {
            Bind();
        }

        private void Update()
        {
            if (_root != null) MenuPrompts.ApplyInputMode(_root);
            // ConstantPhysicalSize may change scale without changing the
            // shell's layout rect, so GeometryChangedEvent alone misses resizes.
            if (_root?.panel != null && !Mathf.Approximately(_root.panel.scaledPixelsPerPoint, _lastPanelScale))
                EvaluateDensity();
        }

        /// <summary>
        /// Binds the shell document once. The frontend controller binds
        /// explicitly before its first activation so binding never depends on
        /// Unity's component-order luck.
        /// </summary>
        public void Bind()
        {
            if (_root != null)
                return;
            var documentRoot = _uiDocument != null ? _uiDocument.rootVisualElement : null;
            if (documentRoot == null)
            {
                Debug.LogError("[FrontendShellView] UIDocument is missing or unbound; the shell frame cannot host pages.");
                enabled = false;
                return;
            }

            documentRoot.RegisterCallback<NavigationSubmitEvent>(SlopArena.Client.Input.CouchPads.BlockForeignPress, TrickleDown.TrickleDown);
            documentRoot.RegisterCallback<NavigationCancelEvent>(SlopArena.Client.Input.CouchPads.BlockForeignPress, TrickleDown.TrickleDown);
            documentRoot.RegisterCallback<NavigationMoveEvent>(SlopArena.Client.Input.CouchPads.BlockForeignPress, TrickleDown.TrickleDown);
            _root = documentRoot.Q<VisualElement>("frontend-shell");
            _topBar = documentRoot.Q<VisualElement>("shell-top-bar");
            _pageContextLabel = documentRoot.Q<VisualElement>("shell-page-context");
            _pageHeaderHost = documentRoot.Q<VisualElement>("page-header-host");
            _pageBodyHost = documentRoot.Q<VisualElement>("page-body-host");
            _socialHost = documentRoot.Q<VisualElement>("social-host");
            _pageLowerHost = documentRoot.Q<VisualElement>("page-lower-host");
            _pageSummaryHost = documentRoot.Q<VisualElement>("page-summary-host");
            _pageActionsHost = documentRoot.Q<VisualElement>("page-actions-host");
            _modalHost = documentRoot.Q<VisualElement>("modal-host");
            _menuModal = documentRoot.Q<VisualElement>("shell-escape-menu");
            _resumeButton = documentRoot.Q<Button>("shell-menu-resume");
            _settingsButton = documentRoot.Q<Button>("shell-menu-settings");
            var quitButton = documentRoot.Q<Button>("shell-menu-quit");
            var menuTrigger = documentRoot.Q<Button>("shell-menu-trigger");
            var settingsTopButton = documentRoot.Q<Button>("shell-settings");
            if (settingsTopButton != null) settingsTopButton.clicked += OpenSettingsFromTopBar;
            if (menuTrigger != null)
            {
                menuTrigger.clicked += ToggleMenu;
                MenuPrompts.Stick(menuTrigger, MenuAction.Menu);
            }
            var nav = documentRoot.Q<VisualElement>("shell-nav");
            if (nav != null) MenuPrompts.Flank(nav, MenuAction.PrevSection, MenuAction.NextSection);
            if (_resumeButton != null)
            {
                _resumeButton.clicked += CloseMenu;
                MenuPrompts.Stick(_resumeButton, MenuAction.Back);
            }
            if (_settingsButton != null) _settingsButton.clicked += OpenSettings;
            if (quitButton != null) quitButton.clicked += QuitGame;
            _settingsOverlay = gameObject.GetComponent<SettingsOverlay>() ?? gameObject.AddComponent<SettingsOverlay>();

            if (_root == null || _pageHeaderHost == null || _pageBodyHost == null || _socialHost == null
                || _pageLowerHost == null || _pageSummaryHost == null || _pageActionsHost == null
                || _modalHost == null)
            {
                Debug.LogError("[FrontendShellView] FrontendShell.uxml is missing required hosts; the shell frame cannot host pages.");
                enabled = false;
                return;
            }

            // The page content region spans the three page hosts; the social
            // cell sits inside the workspace too, so region operations must
            // not treat chat controls as page controls.
            PageContentRoot = documentRoot.Q<VisualElement>("shell-workspace");
            _lowerRow = documentRoot.Q<VisualElement>("shell-lower-row");

            RegisterDensityHandling();
        }

        /// <summary>
        /// Density handling registers once on the stable shell root (issue
        /// #218). Geometry and panel-scale changes update physical targets;
        /// unchanged frames do not touch layout.
        /// </summary>
        private void RegisterDensityHandling()
        {
            if (_densityBound || _root == null)
                return;
            _densityBound = true;
            _root.RegisterCallback<GeometryChangedEvent>(_ => EvaluateDensity());
            EvaluateDensity();
        }

        private void EvaluateDensity()
        {
            if (_root == null)
                return;
            var rect = _root.contentRect;
            // Density is decided from the shell's physical size (issue #218):
            // panel coordinates are scaled points, so convert through the
            // panel's scale factor before comparing against the targets.
            float scale = _root.panel?.scaledPixelsPerPoint ?? 1f;
            if (scale <= 0f)
                scale = 1f;
            float physicalWidth = rect.width * scale;
            float physicalHeight = rect.height * scale;
            if (physicalWidth <= 0f || physicalHeight <= 0f)
                return;
            bool compact = physicalWidth < CompactMaxWidth || physicalHeight < CompactMaxHeight;
            bool modeChanged = compact != _compact;
            bool scaleChanged = !Mathf.Approximately(scale, _lastPanelScale);
            _compact = compact;
            _lastPanelScale = scale;
            // The first usable layout must apply the class as well as geometry:
            // the initial roomy mode matches the default bool value.
            if (modeChanged || scaleChanged || !_densityGeometryApplied)
            {
                _root.EnableInClassList("frontend-shell--compact", compact);
                _root.EnableInClassList("frontend-shell--roomy", !compact);
                ApplyDensityGeometry();
            }
        }

        /// <summary>
        /// Applies the density's physical size targets (issue #218): ~56 px
        /// top bar and ~380×190 px conversation cell with ~16 px outer
        /// spacing compact; ~64 px, ~480×230 px, ~24 px roomy. PanelSettings
        /// run in scaled points, so targets divide by the panel's scale
        /// factor. Re-evaluated when the mode or panel scale changes, or on
        /// the first usable layout.
        /// </summary>
        private void ApplyDensityGeometry()
        {
            if (_root?.panel == null || _topBar == null || _socialHost == null
                || PageContentRoot == null)
                return;
            float scale = _root.panel.scaledPixelsPerPoint;
            if (scale <= 0f)
                scale = 1f;
            float requestedScale = ClientSettingsService.Instance.UiScale / 100f;
            float spacing = (_compact ? 16f : 24f) * requestedScale / scale;
            float barHeight = (_compact ? 56f : 64f) * requestedScale / scale;
            float defaultCellWidthPx = _compact ? 380f : 480f;
            float defaultCellHeightPx = _compact ? 190f : 230f;
            float availableWidthPx = _root.contentRect.width * scale / requestedScale;
            float availableHeightPx = _root.contentRect.height * scale / requestedScale;
            float maxWidthPx = Mathf.Max(120f, Mathf.Min(960f, availableWidthPx - 2f * (_compact ? 16f : 24f)));
            float maxHeightPx = Mathf.Max(120f, Mathf.Min(availableHeightPx * 0.65f,
                availableHeightPx - (_compact ? 56f : 64f) - 2f * (_compact ? 16f : 24f)));
            float cellWidth = Mathf.Clamp(_preferredCellWidthPx > 0f ? _preferredCellWidthPx : defaultCellWidthPx,
                Mathf.Min(300f, maxWidthPx), maxWidthPx) * requestedScale / scale;
            float cellHeight = Mathf.Clamp(_preferredCellHeightPx > 0f ? _preferredCellHeightPx : defaultCellHeightPx,
                Mathf.Min(180f, maxHeightPx), maxHeightPx) * requestedScale / scale;
            float topGap = (_compact ? 10f : 16f) * requestedScale / scale;

            _topBar.style.height = barHeight;
            _topBar.style.minHeight = barHeight;
            _topBar.style.paddingLeft = spacing;
            _topBar.style.paddingRight = spacing;

            PageContentRoot.style.paddingLeft = spacing;
            PageContentRoot.style.paddingRight = spacing;
            PageContentRoot.style.paddingTop = topGap;

            if (_lowerRow != null)
            {
                _lowerRow.style.marginTop = topGap;
                _lowerRow.style.paddingBottom = spacing;
            }

            _socialHost.style.position = Position.Absolute;
            _socialHost.style.left = spacing;
            _socialHost.style.right = StyleKeyword.Auto;
            _socialHost.style.top = StyleKeyword.Auto;
            _socialHost.style.bottom = spacing;
            _socialHost.style.width = cellWidth;
            _socialHost.style.height = cellHeight;
            _socialHost.style.marginRight = 0;
            _densityGeometryApplied = true;
        }

        /// <summary>Whether the shell currently renders compact density.</summary>
        public bool IsCompact => _compact;

        /// <summary>Set a shared physical chat size; zero restores density defaults.</summary>
        public void SetSocialCellSize(float widthPx, float heightPx)
        {
            _preferredCellWidthPx = widthPx;
            _preferredCellHeightPx = heightPx;
            ApplyDensityGeometry();
        }

        /// <summary>
        /// Removes mounted page sections. The social overlay, top
        /// bar, and shell-owned identity modal survive page navigation.
        /// </summary>
        public void ClearPageHosts()
        {
            _pageHeaderHost?.Clear();
            _pageBodyHost?.Clear();
            _pageSummaryHost?.Clear();
            _pageActionsHost?.Clear();
            _pageModalSection?.RemoveFromHierarchy();
            _pageModalSection = null;
        }



        /// <summary>Sets the concise page/mode context in the top bar.</summary>
        public void SetPageContext(string? text)
        {
            if (_pageContextLabel is Label label)
            {
                label.enableRichText = false;
                label.text = text ?? string.Empty;
            }
        }

        /// <summary>Update the shell's page context without hiding its persistent frame.</summary>
        public void ApplyPageMode(FrontendPage page)
        {
            _root?.EnableInClassList("frontend-shell--home", page == FrontendPage.Home);
            _root?.EnableInClassList("frontend-shell--stage", page == FrontendPage.StageSelect);
            _root?.EnableInClassList("frontend-shell--fighters", page == FrontendPage.FighterSelect);
            _root?.EnableInClassList("frontend-shell--local", page == FrontendPage.ModeSelect);
            ApplyDensityGeometry();
            SetPageContext(page switch
            {
                FrontendPage.Home => string.Empty,
                FrontendPage.FighterSelect => "MATCH SETUP // FIGHTER SELECT",
                FrontendPage.StageSelect => "MATCH SETUP // STAGE SELECT",
                FrontendPage.Results => "RESULTS",
                FrontendPage.ServerBrowser => "ONLINE // SERVER BROWSER",
                FrontendPage.LobbyRoom => "ONLINE // ROOM",
                FrontendPage.ModeSelect => "LOCAL PLAY // CHOOSE A MODE",
                _ => string.Empty
            });
        }

        /// <summary>The persistent chat host and page focus region.</summary>
        public bool TryGetChatHosts(out VisualElement socialHost, out VisualElement pageContentRoot)
        {
            socialHost = _socialHost!;
            pageContentRoot = PageContentRoot!;
            return _root != null && socialHost != null && pageContentRoot != null;
        }

        /// <summary>
        /// Clones the page's fragment source once for this activation and
        /// mounts its named sections into the shell hosts (issue #218 page
        /// contract): <c>page-header</c>, <c>page-body</c>, <c>page-summary</c>,
        /// <c>page-actions</c> — plus an optional <c>page-modal</c> section
        /// that mounts into the shell modal host as a page-owned modal
        /// (issue #221). Unnamed extra children mount into the body host with
        /// a warning — silent content loss is never acceptable.
        /// </summary>
        public FrontendPageContext MountPage(FrontendPage page, VisualTreeAsset fragment)
        {
            var context = new FrontendPageContext(page, this);
            if (_pageHeaderHost == null || _pageBodyHost == null || _pageSummaryHost == null
                || _pageActionsHost == null)
                return context;

            var clone = fragment.CloneTree();
            bool anySection = false;
            foreach (var (sectionName, host) in new (string, VisualElement?)[]
                     {
                         ("page-header", _pageHeaderHost),
                         ("page-body", _pageBodyHost),
                         ("page-summary", _pageSummaryHost),
                         ("page-actions", _pageActionsHost),
                         ("page-modal", _modalHost)
                     })
            {
                if (host == null)
                    continue;
                var section = clone.Q(sectionName);
                if (section == null)
                    continue;
                host.Add(section);
                if (sectionName == "page-modal")
                    _pageModalSection = section;
                context.AttachRoot(section);
                anySection = true;
            }

            if (!anySection)
            {
                Debug.LogWarning($"[{page}] Fragment exposes no named sections; mounting the whole fragment into the body host.");
                _pageBodyHost.Add(clone);
                context.AttachRoot(clone);
            }
            return context;
        }

        public bool IsMenuOpen => _menuOpen;

        public void ToggleMenu()
        {
            if (_menuOpen) CloseMenu();
            else OpenMenu();
        }

        public void OpenMenu()
        {
            if (_menuOpen || _modalHost == null || _menuModal == null
                || FrontendController.Identity is { IsPresented: true }
                || SettingsOverlay.Active is { IsOpen: true })
                return;
            _restoreFocus = _root?.panel?.focusController.focusedElement as VisualElement;
            _originalModalPicking = _modalHost.pickingMode;
            _modalHost.pickingMode = PickingMode.Position;
            _menuModal.BringToFront();
            _menuModal.AddToClassList("shell-escape-menu--open");
            _menuOpen = true;
            UiModalState.Push();
            FrontendController.FocusRouter?.ApplyRegionFocusability();
            _resumeButton?.Focus();
        }

        public void CloseMenu()
        {
            if (!_menuOpen) return;
            _menuOpen = false;
            _menuModal?.RemoveFromClassList("shell-escape-menu--open");
            if (_modalHost != null) _modalHost.pickingMode = _originalModalPicking;
            UiModalState.Pop();
            FrontendController.FocusRouter?.ApplyRegionFocusability();
            if (_restoreFocus?.panel != null) _restoreFocus.Focus();
            _restoreFocus = null;
        }

        private void OpenSettings()
        {
            if (_settingsOverlay == null || _modalHost == null || _menuModal == null || !_menuOpen)
                return;
            UISFX.PlayClick();
            _menuModal.RemoveFromClassList("shell-escape-menu--open");
            _settingsOverlay.Open(_modalHost, () =>
            {
                if (_menuOpen)
                {
                    _menuModal?.BringToFront();
                    _menuModal?.AddToClassList("shell-escape-menu--open");
                    _settingsButton?.Focus();
                }
            });
        }

        private void OpenSettingsFromTopBar()
        {
            if (_settingsOverlay == null || _modalHost == null || _menuOpen || UiModalState.Presented
                || FrontendController.Identity is { IsPresented: true })
                return;
            UISFX.PlayClick();
            var opener = _root?.panel?.focusController.focusedElement as VisualElement;
            _settingsOverlay.Open(_modalHost, () =>
            {
                FrontendController.FocusRouter?.ApplyRegionFocusability();
                if (opener?.panel != null) opener.Focus();
            });
            FrontendController.FocusRouter?.ApplyRegionFocusability();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDisable()
        {
            if (_menuOpen) UiModalState.Pop();
            _menuOpen = false;
            _menuModal = null;
            _resumeButton = null;
            _settingsButton = null;
            _restoreFocus = null;
            // The shell dies with the frontend scene; the stable-root
            // registrations and mounted content die with it. Chat re-hosts
            // through the presenter's own attach path on frontend recreation.
            _root = null;
            _topBar = null;
            _pageContextLabel = null;
            _pageHeaderHost = null;
            _pageBodyHost = null;
            _socialHost = null;
            _pageLowerHost = null;
            _pageSummaryHost = null;
            _pageActionsHost = null;
            _modalHost = null;
            _pageModalSection = null;
            _lowerRow = null;
            PageContentRoot = null;
            _densityBound = false;
            _densityGeometryApplied = false;
        }
    }

    /// <summary>Shared display helpers for shell cells.</summary>
    internal static class FrontendShellStyleExtensions
    {
        public static void SetDisplay(this IStyle style, bool displayed)
        {
            style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
