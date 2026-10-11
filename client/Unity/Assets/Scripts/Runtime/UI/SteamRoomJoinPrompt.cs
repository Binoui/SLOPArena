using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Session-owned confirmation and passive notice surfaces for Steam room joins.</summary>
    public sealed class SteamRoomJoinPrompt
    {
        private static SteamRoomJoinPrompt? _active;
        private static int _closedFrame = -1;
        internal static bool ClosedThisFrame => _closedFrame == Time.frameCount;
        public static bool IsConfirming => _active != null;
        internal static Button? ForwardButton => _active?._acceptButton;
        public static bool CancelActive()
        {
            if (_active == null) return false;
            _active.Close(invokeCancel: true);
            return true;
        }
        private VisualElement? _surface;
        public bool IsPresented => _active == this && _surface?.panel != null;
        private VisualElement? _host;
        private VisualElement? _restoreFocus;
        private PickingMode _originalPickingMode;
        private Button? _acceptButton;
        private Button? _cancelButton;
        private MatchPauseMenu? _pauseMenu;
        private bool _wasPaused;
        private bool _ownsModal;
        private Action? _accept;
        private Action? _cancel;
        private EventCallback<NavigationCancelEvent>? _cancelHandler;
        private string? _pendingNotice;
        private bool _pendingGameplay;

        /// <summary>Shows one confirmation, or returns false without changing UI when unavailable.</summary>
        public bool TryConfirm(string title, string message, Action accept, Action cancel, bool training)
        {
            if (_surface != null || accept == null || cancel == null)
                return false;

            VisualElement? host;
            if (training)
            {
                _pauseMenu = MatchPauseMenu.Active;
                var document = UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document;
                if (_pauseMenu == null || document == null || !document.isActiveAndEnabled
                    || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                {
                    _pauseMenu = null;
                    return false;
                }
                host = document.rootVisualElement;
                _wasPaused = _pauseMenu.IsPaused;
                _restoreFocus = host.panel?.focusController.focusedElement as VisualElement;
                if (!_wasPaused)
                    _pauseMenu.SetPaused(true);
            }
            else
            {
                var shell = FrontendController.Shell;
                host = shell?.ModalHost;
                if (!FrontendController.IsFrontendActive || host == null || host.panel == null
                    || UiModalState.Presented)
                    return false;
                _restoreFocus = host.panel?.focusController.focusedElement as VisualElement;
                _originalPickingMode = host.pickingMode;
                host.pickingMode = PickingMode.Position;
            }

            _host = host;
            _accept = accept;
            _cancel = cancel;
            _surface = BuildSurface(title, message);
            _cancelHandler = OnNavigationCancel;
            _surface.RegisterCallback(_cancelHandler, TrickleDown.TrickleDown);
            host.Add(_surface);
            UiModalState.Push();
            _ownsModal = true;
            _active = this;
            _acceptButton?.Focus();
            return true;
        }

        /// <summary>Shows a non-modal notice on the active frontend or gameplay HUD.</summary>
        public void Notice(string message, bool gameplay)
        {
            Clear();
            if (gameplay)
            {
                var document = UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document;
                _host = document != null ? document.rootVisualElement : null;
            }
            else
            {
                _host = FrontendController.IsFrontendActive ? FrontendController.Shell?.Root : null;
            }
            if (_host?.panel == null)
            {
                _pendingNotice = message;
                _pendingGameplay = gameplay;
                return;
            }

            var notice = new Label(message) { name = "steam-room-join-notice" };
            notice.AddToClassList("shell-modal-help");
            notice.style.position = Position.Absolute;
            notice.style.left = 12;
            notice.style.right = 12;
            notice.style.top = 12;
            notice.style.paddingLeft = 12;
            notice.style.paddingRight = 12;
            notice.style.paddingTop = 8;
            notice.style.paddingBottom = 8;
            notice.pickingMode = PickingMode.Ignore;
            _surface = notice;
            _host.Add(notice);
            notice.schedule.Execute(() =>
            {
                if (ReferenceEquals(_surface, notice)) Clear();
            }).StartingIn(8000);
        }

        /// <summary>Removes owned UI and restores any Training pause state.</summary>
        public void Clear()
        {
            _pendingNotice = null;
            Close();
        }

        public void Tick()
        {
            if (_pendingNotice == null) return;
            bool gameplay = _pendingGameplay && !FrontendController.IsFrontendActive;
            var host = gameplay
                ? UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document?.rootVisualElement
                : FrontendController.Shell?.Root;
            if (host?.panel == null) return;
            var message = _pendingNotice;
            _pendingNotice = null;
            Notice(message, gameplay);
        }

        private VisualElement BuildSurface(string title, string message)
        {
            var surface = new VisualElement { name = "steam-room-join-confirmation" };
            surface.AddToClassList("shell-modal");
            surface.pickingMode = PickingMode.Position;

            var box = new VisualElement { name = "steam-room-join-box" };
            box.AddToClassList("shell-modal-box");
            box.pickingMode = PickingMode.Position;
            surface.Add(box);

            var heading = new Label(title) { name = "steam-room-join-title" };
            heading.AddToClassList("shell-modal-title");
            var help = new Label(message) { name = "steam-room-join-help" };
            help.AddToClassList("shell-modal-help");
            box.Add(heading);
            box.Add(help);

            var row = new VisualElement { name = "steam-room-join-row" };
            row.AddToClassList("shell-modal-row");
            box.Add(row);
            _acceptButton = new Button(() => Close(invokeAccept: true)) { text = "JOIN" };
            _cancelButton = new Button(() => Close(invokeCancel: true)) { text = "CANCEL" };
            _acceptButton.AddToClassList("shell-modal-submit");
            _cancelButton.AddToClassList("shell-modal-submit");
            _cancelButton.AddToClassList("shell-modal-submit--secondary");
            row.Add(_acceptButton);
            row.Add(_cancelButton);
            return surface;
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopImmediatePropagation();
            Close(invokeCancel: true);
        }

        private void Close(bool invokeAccept = false, bool invokeCancel = false)
        {
            if (_surface == null)
                return;
            var surface = _surface;
            _surface = null;
            if (_cancelHandler != null)
                surface.UnregisterCallback(_cancelHandler, TrickleDown.TrickleDown);
            surface.RemoveFromHierarchy();
            _acceptButton = null;
            _cancelButton = null;
            _cancelHandler = null;

            if (_ownsModal)
            {
                _closedFrame = Time.frameCount;
                if (_active == this) _active = null;
                UiModalState.Pop();
                _ownsModal = false;
            }
            if (_host != null && _host == FrontendController.Shell?.ModalHost)
                _host.pickingMode = _originalPickingMode;
            if (_pauseMenu != null && !_wasPaused)
                _pauseMenu.SetPaused(false);
            if (_restoreFocus?.panel != null)
                _restoreFocus.Focus();
            if (_host != null && _host == FrontendController.Shell?.ModalHost)
                FrontendFocusRouter.Instance?.NotifyModalClosed();

            var callback = invokeAccept ? _accept : invokeCancel ? _cancel : null;
            _accept = null;
            _cancel = null;
            _host = null;
            _restoreFocus = null;
            _pauseMenu = null;
            callback?.Invoke();
        }
    }
}
