using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using SlopArena.Client.Input;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// TopBar/Page/Social focus ownership with modal-first cancel routing.
    /// This router alone pumps the frontend panel; gameplay uses ChatOverlay.
    /// </summary>
    public sealed class FrontendFocusRouter : MonoBehaviour
    {
        private enum UiRegion { TopBar, Page, Social }
        private static readonly string[] SectionTabs = { "shell-nav-training", "shell-nav-local", "shell-nav-online" };

        public const string TopBarHintName = "shell-topbar-hint";

        public static FrontendFocusRouter? Instance { get; private set; }

        private FrontendShellView? _shell;
        private UiRegion _region = UiRegion.Page;
        // Last valid focused control per region; index = (int)UiRegion.
        private UiRegion _regionBeforeTopBar = UiRegion.Page;
        private readonly VisualElement?[] _regionFocus = new VisualElement?[3];
        private int _cancelConsumedFrame = -1;
        private Label? _regionHint;
        private Label? _topBarHint;
        private VisualElement? _chatRoot;
        private bool _chatRootResolved;
        private bool _bound;

        /// <summary>True while the Social region owns input on a shell page.</summary>
        public bool SocialRegionActive => _region == UiRegion.Social;

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            if (_shell?.Root != null)
            {
                _shell.Root.UnregisterCallback<NavigationCancelEvent>(OnShellNavigationCancel, TrickleDown.TrickleDown);
                _shell.Root.UnregisterCallback<ClickEvent>(OnShellClick);
                _shell.Root.UnregisterCallback<NavigationMoveEvent>(OnShellNavigationMove, TrickleDown.TrickleDown);
            }
            FrontendController.PageChanged -= OnPageChanged;
            _bound = false;
            _shell = null;
            _chatRoot = null;
            _chatRootResolved = false;
            _regionHint = null;
            _topBarHint = null;
            _region = UiRegion.Page;
            for (int i = 0; i < _regionFocus.Length; i++)
                _regionFocus[i] = null;
        }

        /// <summary>
        /// Binds the router to the bound shell view and registers the
        /// shell-owned one-time handlers on the stable root: the gamepad/
        /// keyboard cancel router and the page-change subscription. The
        /// frontend controller binds explicitly in Start, before the first
        /// page activation.
        /// </summary>
        public void Bind(FrontendShellView? shell)
        {
            if (_bound)
                return;
            if (shell?.Root == null)
            {
                enabled = false;
                return;
            }
            _shell = shell;
            _shell.Root.RegisterCallback<NavigationCancelEvent>(OnShellNavigationCancel, TrickleDown.TrickleDown);
            _shell.Root.RegisterCallback<ClickEvent>(OnShellClick);
            _shell.Root.RegisterCallback<NavigationMoveEvent>(OnShellNavigationMove, TrickleDown.TrickleDown);
            FrontendController.PageChanged += OnPageChanged;
            _bound = true;
            ApplyRegionFocusability();
        }

        private void OnPageChanged(FrontendPage page)
        {
            // A fresh page never inherits the departing page's remembered
            // focus (issue #218): detached elements are rejected on restore,
            // but the stale entry is cleared explicitly.
            _regionFocus[(int)UiRegion.Page] = null;
            if (_region == UiRegion.TopBar)
                _regionBeforeTopBar = UiRegion.Page;
            _region = UiRegion.Page;
            _chatRootResolved = false;
            ApplyRegionFocusability();
            UpdateHints();
        }

        private void Update()
        {
            var shell = FrontendController.Shell;
            if (shell?.Root == null || !FrontendController.IsFrontendActive)
                return;
            if (ChatInputGate.ExternalOverlaySuppressed)
                return; // Native Steam overlay owns input until every held control is released.
            if (HumanInputActions.IsCapturing)
                return; // Rebinding owns face buttons, including A/B.

            // Restore lost focus before A submits; B does not depend on focus.
            var focused = shell.Root.panel?.focusController?.focusedElement as VisualElement;
            if (!UiModalState.Presented && !GamepadUIBridge.IsAvailable(focused))
                FocusRegionDefault(_region);
            GamepadUIBridge.Pump(shell.Root.panel, cancel: false);
            if (SlopArena.Client.Input.CouchPads.MenuPad?.buttonEast.wasPressedThisFrame == true)
                HandlePageCancel();
            PollForward();
            PollPause();
            PollMenu();
            if (UiModalState.Presented)
            {
                UpdateHints();
                return;
            }
            bool editing = IsEditingAnyField(shell.Root.panel);
            if (!editing)
            {
                PollTopBarRoute();
                PollRegionSwitch();
                if (_region != UiRegion.Social) PollSections();
            }
            UpdateHints();
        }

        private void PollForward()
        {
            var pad = SlopArena.Client.Input.CouchPads.MenuPad;
            if (pad?.startButton.wasPressedThisFrame != true
                || pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame)
                return;
            var panel = _shell?.Root?.panel;
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (SettingsOverlay.Active is { IsOpen: true } settings)
            {
                GamepadUIBridge.Submit(settings.ForwardButton);
                return;
            }
            if (_shell?.IsMenuOpen == true)
            {
                GamepadUIBridge.Submit(_shell.Root?.Q<Button>("shell-menu-resume"));
                return;
            }
            if (FrontendController.Identity is { IsPresented: true } identity)
            {
                GamepadUIBridge.Submit(identity.ForwardButton);
                return;
            }
            if (SteamRoomJoinPrompt.IsConfirming)
            {
                GamepadUIBridge.Submit(SteamRoomJoinPrompt.ForwardButton);
                return;
            }
            if (UiModalState.Presented)
            {
                GamepadUIBridge.Submit(FrontendController.CurrentContext?.ModalForwardButton);
                return;
            }
            if (_region == UiRegion.Social || IsEditingAnyField(panel))
                return;
            var forward = FrontendController.CurrentContext?.ForwardButton;
            if (forward != null)
                GamepadUIBridge.Submit(forward);
            else if (_region == UiRegion.Page && focused is Button button
                && (FrontendController.CurrentPage == FrontendPage.Home
                    || FrontendController.CurrentPage == FrontendPage.ModeSelect
                    && button.ClassListContains("local-mode-card")))
                GamepadUIBridge.Submit(button);
        }

        private void PollPause()
        {
            if (Keyboard.current?.escapeKey.wasPressedThisFrame != true)
                return;
            // Settings handles Escape through its own NavigationCancelEvent.
            if (SettingsOverlay.Active is { IsOpen: true })
                return;
            HandlePageCancel();
            UpdateHints();
        }

        // ── Region switching ──────────────────────────────────────────────

        /// <summary>
        /// Q/Y switches between Page and Social on every frontend page,
        /// including Home's mode buttons. Skipped while a text field edits.
        /// </summary>
        private void PollRegionSwitch()
        {
            bool pad = SlopArena.Client.Input.CouchPads.MenuPad?.buttonNorth.wasPressedThisFrame == true;
            bool qKey = Keyboard.current?.qKey.wasPressedThisFrame == true;
            if (!pad && !qKey)
                return;
            UiRegion target = _region == UiRegion.Social ? UiRegion.Page : UiRegion.Social;
            SetActiveRegion(target, focusTarget: true);
            UISFX.PlayClick();
        }

        private void PollTopBarRoute()
        {
            bool key = Keyboard.current?.eKey.wasPressedThisFrame == true;
            if (!key)
                return;
            if (_region == UiRegion.TopBar)
                SetActiveRegion(_regionBeforeTopBar, focusTarget: true);
            else
                SetActiveRegion(UiRegion.TopBar, focusTarget: true);
            UISFX.PlayClick();
        }

        private void PollMenu()
        {
            if (CouchPads.MenuPad?.selectButton.wasPressedThisFrame != true)
                return;
            if (_shell?.IsMenuOpen == true && SettingsOverlay.Active is not { IsOpen: true })
                HandlePageCancel();
            else if (!UiModalState.Presented && _region != UiRegion.Social
                && !IsEditingAnyField(_shell?.Root?.panel))
                GamepadUIBridge.Submit(_shell?.Root?.Q<Button>("shell-menu-trigger"));
        }

        private void PollSections()
        {
            var pad = CouchPads.MenuPad;
            int step = pad?.rightShoulder.wasPressedThisFrame == true ? 1
                : pad?.leftShoulder.wasPressedThisFrame == true ? -1 : 0;
            if (step != 0 && _shell?.TopBar != null)
                GamepadUIBridge.Submit(AdjacentSection(_shell.TopBar, step));
        }

        internal static Button? AdjacentSection(VisualElement topBar, int step)
        {
            int current = ArrayIndexOfSelectedSection(topBar);
            int index = current < 0 ? (step > 0 ? 0 : SectionTabs.Length - 1)
                : (current + step + SectionTabs.Length) % SectionTabs.Length;
            return topBar.Q<Button>(SectionTabs[index]);
        }

        private static int ArrayIndexOfSelectedSection(VisualElement topBar)
        {
            for (int i = 0; i < SectionTabs.Length; i++)
                if (topBar.Q<Button>(SectionTabs[i])?.ClassListContains("shell-nav-tab--selected") == true)
                    return i;
            return -1;
        }

        private void OnShellNavigationMove(NavigationMoveEvent evt)
        {
            if (ChatInputGate.ExternalOverlaySuppressed || HumanInputActions.IsCapturing
                || UiModalState.Presented || IsEditingAnyField(_shell?.Root?.panel)
                || _region == UiRegion.Social)
                return;
            var pad = CouchPads.MenuPad;
            if (pad == null) return;
            if (_region == UiRegion.TopBar && evt.direction == NavigationMoveEvent.Direction.Down
                && (pad.dpad.down.isPressed || pad.leftStick.ReadValue().y < -0.5f))
            {
                _shell?.Root?.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
                SetActiveRegion(UiRegion.Page, focusTarget: true);
                return;
            }
            if (_region != UiRegion.Page || evt.direction != NavigationMoveEvent.Direction.Up
                || !(pad.dpad.up.isPressed || pad.leftStick.ReadValue().y > 0.5f)
                || _shell?.Root?.panel?.focusController?.focusedElement is not VisualElement before)
                return;
            var context = FrontendController.CurrentContext;
            // Let the native focus ring choose first. Staying put or wrapping down means the page's upper edge.
            _shell.Root.schedule.Execute(() =>
            {
                if (_region != UiRegion.Page || context != FrontendController.CurrentContext
                    || UiModalState.Presented || ChatInputGate.ExternalOverlaySuppressed
                    || HumanInputActions.IsCapturing || IsEditingAnyField(_shell?.Root?.panel))
                    return;
                var after = _shell?.Root?.panel?.focusController?.focusedElement as VisualElement;
                if (after != null && after != before && after.worldBound.center.y < before.worldBound.center.y)
                    return;
                if (!TryFocus(before)) return;
                SetActiveRegion(UiRegion.TopBar, focusTarget: true);
            });
        }

        /// <summary>
        /// Switch the active focus region. The inactive regions' controls
        /// stop being focusable, so navigation stays inside the active
        /// region; each region's last valid focus is remembered and restored
        /// on the next switch.
        /// </summary>
        private void SetActiveRegion(UiRegion region, bool focusTarget)
        {
            if (_region != region)
            {
                CaptureRegionFocus(_region);
                if (region == UiRegion.TopBar)
                    _regionBeforeTopBar = _region;
                _region = region;
                ApplyRegionFocusability();
            }
            if (focusTarget)
                FocusRegionDefault(region);
        }

        /// <summary>Remember a region's currently focused control, if the
        /// focus actually sits inside that region.</summary>
        private void CaptureRegionFocus(UiRegion region)
        {
            if (_shell?.Root?.panel?.focusController?.focusedElement is not VisualElement focused)
                return;
            var chatRoot = ResolveChatRoot();
            if (chatRoot != null && chatRoot.Contains(focused))
            {
                if (region == UiRegion.Social)
                    _regionFocus[(int)UiRegion.Social] = focused;
                return;
            }
            if (_shell.TopBar != null && _shell.TopBar.Contains(focused))
            {
                if (region == UiRegion.TopBar)
                    _regionFocus[(int)UiRegion.TopBar] = focused;
                return;
            }
            if (_shell.PageContentRoot != null && _shell.PageContentRoot.Contains(focused))
            {
                if (region == UiRegion.Page)
                    _regionFocus[(int)UiRegion.Page] = focused;
            }
        }

        /// <summary>Restore the remembered focus of a region, falling back to
        /// the region's conventional entry point.</summary>
        private void FocusRegionDefault(UiRegion region)
        {
            if (region != UiRegion.TopBar && TryFocus(_regionFocus[(int)region]))
                return;
            switch (region)
            {
                case UiRegion.TopBar:
                    int selected = _shell?.TopBar == null ? -1 : ArrayIndexOfSelectedSection(_shell.TopBar);
                    if (selected < 0 || !TryFocus(_shell?.TopBar?.Q<Button>(SectionTabs[selected])))
                        TryFocus(FindFirstFocusable(_shell?.TopBar));
                    break;
                case UiRegion.Social:
                    var chatRoot = ResolveChatRoot();
                    if (TryFocus(chatRoot?.Q<TextField>("chat-draft")))
                        return;
                    TryFocus(FindFirstFocusable(chatRoot));
                    break;
                default:
                    if (!TryFocus(MenuNavigation.PageInitialFocus))
                        TryFocus(FindFirstFocusable(_shell?.PageContentRoot, exclude: _shell?.SocialHost));
                    break;
            }
        }

        private static bool TryFocus(VisualElement? element)
        {
            if (element == null || !element.focusable || !GamepadUIBridge.IsAvailable(element))
                return false;
            element.Focus();
            return true;
        }

        private static VisualElement? FindFirstFocusable(VisualElement? root, VisualElement? exclude = null)
        {
            if (root == null)
                return null;
            for (int i = 0; i < root.hierarchy.childCount; i++)
            {
                var element = root.hierarchy[i];
                if (element == exclude || !GamepadUIBridge.IsAvailable(element))
                    continue;
                if (element.focusable)
                    return element;
                var child = FindFirstFocusable(element, exclude);
                if (child != null)
                    return child;
            }
            return null;
        }

        /// <summary>
        /// Region-local navigation: only the active region's controls are
        /// focusable. Chat overlays the page independently; the top bar
        /// toggles independently too.
        /// </summary>
        public void ApplyRegionFocusability()
        {
            var shell = _shell ?? FrontendController.Shell;
            if (shell?.Root == null || !FrontendController.IsFrontendActive)
                return;

            // The shell MENU and Settings live outside every region; while one is open,
            // spatial navigation must not escape to the page behind its backdrop.
            bool menu = shell.IsMenuOpen || SettingsOverlay.Active is { IsOpen: true };
            bool topBar = !menu && _region == UiRegion.TopBar;
            bool social = !menu && _region == UiRegion.Social;
            bool page = !menu && _region == UiRegion.Page;

            SetRegionFocusable(shell.TopBar, topBar);
            SetRegionFocusable(shell.PageContentRoot, page, exclude: shell.SocialHost);
            var chatRoot = ResolveChatRoot();
            SetRegionFocusable(chatRoot, social);
            if (chatRoot?.Q<ScrollView>("chat-history") is { } history)
                history.focusable = social;
        }

        /// <summary>Buttons and text fields are the only focusable controls
        /// in these surfaces; toggling exactly those keeps scroll views and
        /// labels out of the navigation ring.</summary>
        private static void SetRegionFocusable(VisualElement? root, bool focusable, VisualElement? exclude = null)
        {
            if (root == null)
                return;
            root.Query<Button>().ForEach(button =>
            {
                if (exclude == null || !exclude.Contains(button))
                    button.focusable = focusable;
            });
            root.Query<TextField>().ForEach(field =>
            {
                if (exclude == null || !exclude.Contains(field))
                    field.focusable = focusable;
            });
        }

        // ── Mouse follow ──────────────────────────────────────────────────

        /// <summary>
        /// Input follows the last-touched side: a click into the top bar, the
        /// page or the chat surface makes that region active, so mouse users
        /// get region-local navigation without the explicit switch.
        /// Registered once on the stable shell root.
        /// </summary>
        private void OnShellClick(ClickEvent evt)
        {
            var shell = _shell ?? FrontendController.Shell;
            if (shell?.Root == null || ChatInputGate.IsGameplayScene)
                return;
            if (evt.target is not VisualElement target)
                return;
            if (UiModalState.Presented || ChatInputGate.ExternalOverlaySuppressed)
                return;
            if (shell.TopBar != null && shell.TopBar.Contains(target))
            {
                SetActiveRegion(UiRegion.TopBar, focusTarget: false);
                return;
            }
            var chatRoot = ResolveChatRoot();
            if (chatRoot != null && chatRoot.Contains(target))
            {
                SetActiveRegion(UiRegion.Social, focusTarget: false);
                return;
            }
            bool inPage = shell.PageContentRoot != null && shell.PageContentRoot.Contains(target);
            bool changed = _region != UiRegion.Page;
            SetActiveRegion(UiRegion.Page, focusTarget: false);
            if (changed && inPage)
            {
                // The clicked page control only grabs focus while it was
                // focusable, which happened after its pointer-down; focus it
                // explicitly so directional input continues there.
                FocusClickedPageControl(target);
            }
        }

        private void FocusClickedPageControl(VisualElement target)
        {
            for (var element = target; element != null; element = element.parent)
            {
                if (element is Focusable && TryFocus(element))
                    return;
            }
        }

        // ── Cancel resolution ─────────────────────────────────────────────

        /// <summary>
        /// Escape toggles the shell menu; controller Back closes the active
        /// modal, otherwise invokes the current page's Back action.
        /// The handler on the stable shell root is the frontend entry point.
        /// </summary>
        private void OnShellNavigationCancel(NavigationCancelEvent evt)
        {
            if (!FrontendController.IsFrontendActive)
                return;
            if (ChatInputGate.ExternalOverlaySuppressed || HumanInputActions.IsCapturing)
            {
                evt.StopImmediatePropagation();
                return;
            }
            evt.StopImmediatePropagation();
            HandlePageCancel();
        }

        /// <summary>
        /// Cancels routed from a page root (via <see
        /// cref="MenuNavigation"/>) or from the shell root resolve the same
        /// layers once per frame: the frame marker dedupes the page-root
        /// handler, the shell-root handler and the keyboard Escape poll.
        /// </summary>
        public void HandlePageCancel()
        {
            if (ChatInputGate.ExternalOverlaySuppressed)
                return;
            if (_cancelConsumedFrame == Time.frameCount)
                return;
            _cancelConsumedFrame = Time.frameCount;
            ResolveCancelLayers();
        }


        private void ResolveCancelLayers()
        {
            if (SettingsOverlay.ClosedThisFrame)
                return;
            if (SettingsOverlay.Active is { IsOpen: true } settings)
            {
                settings.HandleBack();
                return;
            }
            var shell = FrontendController.Shell;
            if (shell?.IsMenuOpen == true)
            {
                shell.CloseMenu();
                return;
            }
            // Required identity entry and active rename remain the top layer.
            if (FrontendController.Identity is { IsPresented: true } identity)
            {
                identity.HandleCancel();
                return;
            }
            if (SteamRoomJoinPrompt.CancelActive())
                return;
            // Keyboard Escape retains the shell-menu shortcut.
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                shell?.OpenMenu();
                return;
            }
            // Controller Back closes a page-owned modal first.
            if (UiModalState.Presented && FrontendController.CurrentContext != null)
            {
                FrontendController.CurrentContext.InvokeModalAction();
                return;
            }
            // Leaving the top bar must not consume the page's Back action.
            if (_region == UiRegion.TopBar)
            {
                SetActiveRegion(UiRegion.Page, focusTarget: true);
            }
            // Layer 4: social interaction — one press leaves the conversation
            // (the compact cell stays visible) and returns the page.
            var chatRoot = ResolveChatRoot();
            if (_region == UiRegion.Social && chatRoot != null
                && _shell?.Root?.panel?.focusController?.focusedElement is VisualElement focused
                && chatRoot.Contains(focused))
            {
                ChatOverlay.Instance?.BlurChatEditors();
                SetActiveRegion(UiRegion.Page, focusTarget: true);
                return;
            }
            // B remains Back while a page text field is focused. Keyboard
            // Escape was handled above; it never discards a page editor.
            FrontendController.CurrentContext?.InvokeBackAction();
        }

        // ── Hints ─────────────────────────────────────────────────────────

        private void UpdateHints()
        {
            var shell = FrontendController.Shell;
            bool active = shell?.Root != null && FrontendController.IsFrontendActive;
            bool show = active && !UiModalState.Presented && !IsEditingAnyField(shell!.Root!.panel);

            if (_topBarHint == null && _shell?.Root != null)
                _topBarHint = _shell.Root.Q<Label>(TopBarHintName);
            if (_topBarHint != null)
            {
                _topBarHint.text = "E // TOP BAR";
                _topBarHint.style.display = show && !MenuInputMode.PadActive && _region != UiRegion.TopBar
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_regionHint == null)
            {
                var chatRoot = ResolveChatRoot();
                _regionHint = shell?.SocialHost?.Q<Label>("chat-region-hint");
                var chip = chatRoot?.Q<Button>("chat-open");
                if (chip != null) MenuPrompts.Stick(chip, MenuAction.Chat);
                if (_regionHint != null)
                    _regionHint.pickingMode = PickingMode.Ignore;
            }
            if (_regionHint == null)
                return;
            _regionHint.style.display = show && !MenuInputMode.PadActive ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
                return;
            _regionHint.text = _region == UiRegion.Page ? "Q // SOCIAL" : "Q // PAGE";
            // The chip floats just above the reserved shell cell so it never
            // covers the composer or the history feedback (issue #219). The
            // hint resolves only against the shell cell; gameplay has no
            // FocusRouter instance.
            _regionHint.style.top = -24;
            _regionHint.style.bottom = StyleKeyword.Auto;
            _regionHint.style.left = 0;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>The chat presenter's root inside the shell social host;
        /// resolved lazily because the presenter attaches after scene load.</summary>
        private VisualElement? ResolveChatRoot()
        {
            if (_chatRootResolved)
                return _chatRoot;
            var shell = _shell ?? FrontendController.Shell;
            _chatRoot = shell?.SocialHost?.Q<VisualElement>("chat-root");
            _chatRootResolved = _chatRoot != null;
            return _chatRoot;
        }

        /// <summary>True while any text field in the panel owns editing —
        /// name entry, direct address and the composer included, not just the
        /// chat fields (issue #220).</summary>
        private static bool IsEditingAnyField(IPanel? panel)
        {
            return panel?.focusController?.focusedElement is TextField;
        }

        /// <summary>Called by the shell identity view and the chat presenter
        /// when a modal closes or the social presentation changes, so the
        /// region focusability and hints re-apply without per-frame polling.
        /// </summary>
        public static void NotifyPresentationChanged()
        {
            Instance?.OnPresentationChanged();
        }

        /// <summary>
        /// Returns focus to the page region after an explicit social state
        /// change (hide) or after a modal closes
        /// from the top-bar route.
        /// </summary>
        public void RestorePageRegion()
        {
            SetActiveRegion(UiRegion.Page, focusTarget: true);
        }

        /// <summary>
        /// The identity modal closed: leave the top-bar interaction and
        /// restore where the player came from, then re-apply region state.
        /// </summary>
        public void NotifyModalClosed()
        {
            if (_region == UiRegion.TopBar)
                SetActiveRegion(_regionBeforeTopBar, focusTarget: true);
            ApplyRegionFocusability();
            UpdateHints();
        }

        private void OnPresentationChanged()
        {
            ApplyRegionFocusability();
            UpdateHints();
        }
    }
}
