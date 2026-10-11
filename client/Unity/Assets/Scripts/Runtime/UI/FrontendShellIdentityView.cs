using System;
using System.Threading.Tasks;
using SlopArena.Client.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// The shared shell identity surface (issue #220). Owns identity
    /// presentation and the identity modal in the shell's modal host so the
    /// behavior is identical on every frontend page:
    ///
    /// - The top bar shows the display name with the true social connection
    ///   status (no fabricated presence).
    /// - First-run entry is mandatory and locally validated: no skip before a
    ///   locally valid name is accepted, and Solo/Training stay available
    ///   offline after acceptance.
    /// - Rename goes through the existing remote path with honest rejection
    ///   feedback and stays prohibited while joined to a GameServer.
    ///
    /// The view has no transport or store logic — all identity state lives
    /// in the persistent ChatSession. Mode-button gating reads
    /// <see cref="ModeGateClosed"/>; the gating no longer depends on a
    /// Home-local identity panel.
    /// </summary>
    public sealed class FrontendShellIdentityView : MonoBehaviour
    {
        private const string ModalName = "shell-identity-modal";

        private FrontendShellView? _shell;
        private ChatSession? _session;
        private VisualElement? _modal;
        private Label? _title;
        private Label? _help;
        private TextField? _field;
        private Button? _submit;
        private Label? _feedback;
        private Button? _cancelRoomJoin;
        private Button? _topBarIdentity;
        private bool _bound;
        private bool _presented;
        private bool _mandatory;
        private bool _joinProhibition;
        private bool _suppressFeedback;

        /// <summary>True while the identity modal is presented over the shell.</summary>
        public bool IsPresented => _presented;

        internal Button? ForwardButton => _presented ? _submit : null;

        /// <summary>
        /// True while no locally valid name is accepted yet. Pages read this
        /// for mode-button gating; it is no longer conditional on any
        /// page-owned panel existing (issue #220).
        /// </summary>
        public bool ModeGateClosed => _session == null || _session.NeedsDisplayName;

        private void OnEnable()
        {
            if (_session != null)
                _session.Changed += OnSessionChanged;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.Changed -= OnSessionChanged;
            if (_presented)
                UiModalState.Pop();
            _presented = false;
            _session = null;
            _modal = null;
            _topBarIdentity = null;
            _bound = false;
        }

        /// <summary>
        /// Binds to the bound shell view and presents the mandatory first-run
        /// entry when no locally valid name is accepted yet. The frontend
        /// controller calls this in Start, after the shell document binds.
        /// </summary>
        public void Bind(FrontendShellView? shell)
        {
            if (_bound)
                return;
            _shell = shell;
            if (shell?.Root == null)
            {
                enabled = false;
                return;
            }
            _topBarIdentity = shell.TopBar?.Q<Button>("shell-identity");
            if (_topBarIdentity != null)
                _topBarIdentity.clicked += () => OpenIdentitySurface(rename: true);
            _bound = true;
            TryBindSession();
            RenderTopBar();
            if (ModeGateClosed)
                OpenIdentitySurface(rename: false);
        }

        private void Update()
        {
            if (!_bound)
                return;
            if (TryBindSession())
                RenderTopBar();
        }

        private bool TryBindSession()
        {
            if (_session != null)
                return false;
            ChatSession? session = ChatSession.Instance;
            if (session == null)
                return false;
            _session = session;
            _session.Changed += OnSessionChanged;
            RenderTopBar();
            if (!_presented && ModeGateClosed)
                OpenIdentitySurface(rename: false);
            return true;
        }

        private void OnSessionChanged()
        {
            if (!isActiveAndEnabled)
                return;
            RenderTopBar();
            if (_presented)
                RenderModal();
            if (!_presented && ModeGateClosed)
                OpenIdentitySurface(rename: false);
        }

        // ── Top bar presentation ──────────────────────────────────────────

        /// <summary>
        /// The top bar identity: the display name with the true social
        /// connection status. No invented presence or counts.
        /// </summary>
        private void RenderTopBar()
        {
            if (_topBarIdentity == null)
                return;
            _topBarIdentity.EnableInClassList("shell-identity--connected",
                _session != null && !_session.NeedsDisplayName
                && !_session.SavedNameRejected && _session.IsConnected);
            _topBarIdentity.EnableInClassList("shell-identity--warning",
                _session != null && (_session.NeedsDisplayName || _session.SavedNameRejected));
            if (_session == null)
            {
                _topBarIdentity.text = "IDENTITY // STARTING";
                return;
            }
            string name = string.IsNullOrEmpty(_session.SavedDisplayName)
                ? "NO NAME"
                : _session.SavedDisplayName.ToUpperInvariant();
            string status;
            if (_session.NeedsDisplayName)
                status = "SET A NAME";
            else if (_session.SavedNameRejected)
                status = "NAME REJECTED";
            else if (_session.IsConnected)
                status = "CONNECTED";
            else
                status = "OFFLINE";
            _topBarIdentity.text = $"{name} // {status}";
            _topBarIdentity.enableRichText = false;
        }

        // ── Identity modal ────────────────────────────────────────────────

        /// <summary>
        /// Opens the shared identity surface. First-run entry is always
        /// mandatory; a rename request while joined to a GameServer presents
        /// the honest prohibition instead of a form. The modal suppresses
        /// read acknowledgement and region polling while presented.
        /// </summary>
        public void OpenIdentitySurface(bool rename)
        {
            if (!_bound || _presented || _session == null)
                return;
            if (UiModalState.Presented)
                return;
            _mandatory = _session.NeedsDisplayName;
            // Renaming is prohibited while joined to a GameServer (issue
            // #218): the surface explains instead of offering a form that
            // cannot succeed.
            _joinProhibition = !_mandatory && rename &&
                _session.ActiveLobby is { JoinedServerId: var serverId } && serverId != Guid.Empty;
            BuildModal();
            _modal?.SetDisplayed(true);
            _presented = true;
            UiModalState.Push();
            if (_field != null && !_joinProhibition)
                _modal?.schedule.Execute(() => { if (_field!.panel != null) _field.Focus(); }).StartingIn(0);
            FrontendFocusRouter.NotifyPresentationChanged();
        }

        /// <summary>
        /// One Back/Escape press on the topmost modal layer. The mandatory
        /// first-run entry consumes the press but cannot be dismissed; a
        /// rename surface closes without discarding anything (there is
        /// nothing to discard — the saved name is untouched until accepted).
        /// </summary>
        public void HandleCancel()
        {
            if (!_presented)
                return;
            if (_mandatory)
                return;
            Close();
        }

        private void Close()
        {
            if (!_presented)
                return;
            _presented = false;
            _modal?.SetDisplayed(false);
            UiModalState.Pop();
            // The modal owned editing; blur so no editor silently holds the
            // gate after the surface closes.
            _field?.Blur();
            FrontendFocusRouter.Instance?.NotifyModalClosed();
        }

        private void BuildModal()
        {
            var host = _shell?.ModalHost;
            if (host == null)
                return;
            if (_modal != null && _modal.panel != null)
            {
                RenderModal();
                return;
            }
            _modal = new VisualElement { name = ModalName };
            _modal.AddToClassList("shell-modal");
            // The backdrop blocks page interaction while the identity
            // surface is presented; the box is the centered panel.
            _modal.pickingMode = PickingMode.Position;

            var box = new VisualElement { name = "shell-identity-box" };
            box.AddToClassList("shell-modal-box");
            box.pickingMode = PickingMode.Position;
            _modal.Add(box);

            _title = new Label("DISPLAY NAME") { name = "shell-identity-title" };
            _title.AddToClassList("shell-modal-title");
            box.Add(_title);

            _help = new Label(string.Empty) { name = "shell-identity-help" };
            _help.AddToClassList("shell-modal-help");
            box.Add(_help);

            var row = new VisualElement { name = "shell-identity-row" };
            row.AddToClassList("shell-modal-row");
            box.Add(row);

            _field = new TextField { name = "shell-identity-field" };
            _field.maxLength = -1;
            _field.AddToClassList("shell-modal-field");
            row.Add(_field);

            _submit = new Button(SubmitIdentity) { name = "shell-identity-submit", text = "SET DISPLAY NAME" };
            _submit.AddToClassList("shell-modal-submit");
            row.Add(_submit);

            _feedback = new Label(string.Empty) { name = "shell-identity-feedback" };
            _feedback.AddToClassList("shell-modal-feedback");
            _cancelRoomJoin = new Button(() =>
            {
                _session?.CancelPendingRoomJoin();
                RenderModal();
            }) { name = "shell-identity-cancel-room-join", text = "CANCEL ROOM JOIN" };
            _cancelRoomJoin.AddToClassList("shell-modal-submit");
            _cancelRoomJoin.style.marginLeft = 0;
            box.Add(_cancelRoomJoin);
            box.Add(_feedback);

            _field.RegisterValueChangedCallback(_ => RenderModal());
            _field.RegisterCallback<KeyDownEvent>(evt =>
            {
                // One Enter submits exactly the intended action: the identity
                // entry. The press never reaches shell shortcuts.
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopImmediatePropagation();
                    SubmitIdentity();
                }
            });

            host.Add(_modal);
            RenderModal();
        }

        private void RenderModal()
        {
            if (_modal == null || _session == null)
                return;
            if (_title != null)
                _title.text = _mandatory ? "CHOOSE A DISPLAY NAME" : "CHANGE DISPLAY NAME";
            if (_help != null)
            {
                _help.text = _joinProhibition
                    ? "RENAMING IS UNAVAILABLE WHILE JOINED TO A GAMESERVER."
                    : _mandatory
                        ? _session.HasPendingRoomJoin
                            ? "Steam Room request saved. Choose a display name to connect, or cancel the Room join below. Local play remains available."
                            : "Your guest identity stays with this launch. Solo and Training work offline; chat connects when the network allows."
                        : "The name is sent to the chat service when it accepts changes. A rejection is reported here.";
            }
            if (_field != null)
            {
                bool editing = _joinProhibition || _field.panel?.focusController?.focusedElement == _field;
                if (!editing && _field.value != _session.SavedDisplayName)
                    _field.SetValueWithoutNotify(_session.SavedDisplayName);
                _field.SetEnabled(!_joinProhibition);
            }
            if (_submit != null)
            {
                _submit.text = _mandatory ? "SET DISPLAY NAME" : "RENAME";
                _submit.SetEnabled(!_joinProhibition && IsNameValid(_field?.value ?? string.Empty));
            }
            if (_cancelRoomJoin != null)
                _cancelRoomJoin.style.display = _mandatory && _session.HasPendingRoomJoin
                    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_feedback != null && !_suppressFeedback)
            {
                SetFeedback(_feedback, _session.Status);
            }
        }

        private async void SubmitIdentity()
        {
            var session = _session;
            if (session == null || _field == null)
                return;
            string raw = _field.value;
            if (!ChatTextValidation.TryValidateDisplayName(raw, out string value, out string error))
            {
                SetFeedback(_feedback, error);
                return;
            }
            if (_submit != null)
                _submit.SetEnabled(false);
            try
            {
                bool accepted = _mandatory
                    ? await session.AcceptDisplayNameLocallyAsync(value)
                    : await session.RenameAsync(value);
                if (accepted)
                {
                    _suppressFeedback = true;
                    Close();
                    _suppressFeedback = false;
                }
                else
                {
                    // Honest remote rejection: the failure is reported and
                    // never presented as success (issue #218).
                    RenderModal();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RenderModal();
            }
            finally
            {
                if (_presented && _submit != null)
                    _submit.SetEnabled(!_joinProhibition && IsNameValid(_field?.value ?? string.Empty));
            }
        }

        private static bool IsNameValid(string value) =>
            ChatTextValidation.TryValidateDisplayName(value, out _, out _);

        private static void SetFeedback(Label? label, string text)
        {
            if (label == null)
                return;
            label.enableRichText = false;
            label.text = text ?? string.Empty;
        }
    }
}
