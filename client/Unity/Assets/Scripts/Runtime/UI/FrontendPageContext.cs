using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// A page controller that owns no document of its own: the shell clones
    /// the page's fragment source, mounts its sections, and injects this
    /// context into the controller while the page GameObject is still
    /// inactive (issue #219). Activation then invokes the controller's
    /// normal Unity <c>OnEnable</c> path.
    /// </summary>
    public interface IFrontendPageController
    {
        /// <summary>Injects the per-activation page context. Called only
        /// while the page GameObject is inactive, immediately before
        /// activation.</summary>
        void InjectPageContext(FrontendPageContext context);
    }

    /// <summary>
    /// The per-activation page context (issue #218 implementation seam): the
    /// active page's owned visual sections, the shell surfaces, and the
    /// validity token. One context per activation; contexts from a departing
    /// activation are released before the next page mounts and never reused.
    /// </summary>
    public sealed class FrontendPageContext
    {
        private readonly List<VisualElement> _ownedRoots = new();
        private readonly List<Action> _releaseActions = new();
        private readonly FrontendShellView? _shell;

        /// <summary>The page this context was created for.</summary>
        public FrontendPage Page { get; }

        /// <summary>Shell surfaces shared across pages; never cleared by page
        /// teardown. Null only if the shell document failed to bind.</summary>
        public FrontendShellView? Shell => _shell;

        /// <summary>
        /// The page-owned section roots as mounted into the shell hosts
        /// (header/body/summary/actions). Queries must go through
        /// <see cref="Q{T}"/>; the mounted sections are no longer one
        /// subtree, so shell-wide searches are invalid.
        /// </summary>
        public IReadOnlyList<VisualElement> OwnedRoots => _ownedRoots;

        /// <summary>
        /// True while this context is the active page's live context. A
        /// departing activation is invalidated immediately; late work (async
        /// completions, scheduled callbacks) checks this before touching the
        /// page or publishing changes.
        /// </summary>
        public bool Valid => FrontendController.CurrentContext == this;

        internal FrontendPageContext(FrontendPage page, FrontendShellView? shell)
        {
            Page = page;
            _shell = shell;
        }

        /// <summary>
        /// Searches only the page-owned section roots. Returns the match when
        /// exactly one exists, null when the id is absent, and rejects
        /// ambiguous ids loudly: duplicated ids inside page fragments are a
        /// bug that must fail during integration, not resolve silently.
        /// </summary>
        public T? Q<T>(string name) where T : VisualElement
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Page queries require an element name.", nameof(name));

            T? only = null;
            for (int i = 0; i < _ownedRoots.Count; i++)
            {
                var found = _ownedRoots[i].Q<T>(name);
                if (found == null)
                    continue;
                if (only != null)
                    throw new InvalidOperationException(
                        $"[{Page}] Ambiguous page query '{name}' matched multiple elements.");
                only = found;
            }
            return only;
        }

        private Action? _backAction;
        private Action? _modalCloseAction;

        /// <summary>The current page's primary Continue/Ready/Play control.
        /// Start submits this button only while it is visible and enabled.</summary>
        public Button? ForwardButton { get; set; }

        /// <summary>Primary validation control for the page-owned modal.</summary>
        public Button? ModalForwardButton { get; set; }

        /// <summary>
        /// The page-owned modal's close action while a page modal is
        /// presented in the shell modal host (issue #221). The shell focus
        /// router resolves it as the topmost modal layer, after the shell
        /// identity surface. Cleared on release and by the page itself when
        /// the modal closes.
        /// </summary>
        internal void SetModalAction(Action? close) => _modalCloseAction = close;

        /// <summary>Invokes the page modal's close action when the shell
        /// cancel router resolves to the page-modal layer.</summary>
        internal void InvokeModalAction() => _modalCloseAction?.Invoke();

        /// <summary>
        /// The page's flow-specific Back action, registered through
        /// <see cref="MenuNavigation"/>. The shell focus router invokes it as
        /// the last cancel layer. Cleared on release.
        /// </summary>
        public Action? BackAction => _backAction;

        /// <summary>Invokes the page's Back action when the shell cancel
        /// router resolves to the page layer.</summary>
        internal void InvokeBackAction() => _backAction?.Invoke();

        /// <summary>
        /// Registers cleanup that runs when the context is released (page
        /// departure). Unsubscribe shared-shell registrations here — page
        /// controllers' Unity OnDisable still owns service/operation cleanup.
        /// </summary>
        public void AddReleaseAction(Action release)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));
            _releaseActions.Add(release);
        }

        internal void AttachRoot(VisualElement root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            _ownedRoots.Add(root);
        }

        /// <summary>Called by <see cref="MenuNavigation.Configure"/> when the
        /// page registers its flow-specific Back action.</summary>
        internal void SetBackAction(Action? back) => _backAction = back;

        /// <summary>
        /// Page teardown (issue #218 activation sequence): run release
        /// actions first — while every mounted element is still attached —
        /// then forget the roots. Shell, social and global identity UI are
        /// never cleared here.
        /// </summary>
        internal void Release()
        {
            for (int i = _releaseActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _releaseActions[i].Invoke();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
            _releaseActions.Clear();
            _ownedRoots.Clear();
            _backAction = null;
            _modalCloseAction = null;
            ForwardButton = null;
            ModalForwardButton = null;
        }
    }
}
