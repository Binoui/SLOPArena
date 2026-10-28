using System;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Small shared focus/back contract for pre-match screens.</summary>
    public static class MenuNavigation
    {
        /// <summary>
        /// The active page's conventional initial focus, registered by
        /// Configure (issue #215): the Page/Social region switch falls back to
        /// it when the page region has no remembered focus. Null when a
        /// screen configures without an initial button (e.g. modals).
        /// </summary>
        public static VisualElement? PageInitialFocus { get; private set; }

        /// <summary>
        /// Clears the stale page initial focus (issue #220): page departure
        /// invalidates it explicitly, so a superseded focus entry can never
        /// be restored after a navigation or a social/modal switch.
        /// </summary>
        public static void ClearPageInitialFocus() => PageInitialFocus = null;


        /// <summary>
        /// Fragment-mounted pages register on their page-owned section roots
        /// instead of one document root (issue #219): the mounted sections
        /// are no longer a single subtree, so shell-wide registration is
        /// impossible. The registrations live on the page context and are
        /// unregistered when the context releases, so repeated navigation
        /// never accumulates callbacks on the stable shell hosts.
        /// </summary>
        public static void Configure(FrontendPageContext context, Button? initialButton, Action backAction,
            Button? forwardButton = null)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            ConfigureShared(initialButton);
            // The page context owns Back; shell routing resolves modal,
            // expanded social and top-bar layers before this action.
            context.SetBackAction(backAction);
            context.ForwardButton = forwardButton;

            var roots = context.OwnedRoots;
            foreach (var root in roots)
                root.Query<Button>(className: "btn-back").ForEach(button =>
                {
                    if (button.text == "BACK" || button.name == "btn-back")
                        MenuPrompts.Stick(button, MenuAction.Back);
                });
            EventCallback<ClickEvent> clickHandler = _ => UISFX.PlayClick();
            EventCallback<NavigationCancelEvent> cancelHandler = evt => OnScreenCancel(evt, backAction);
            for (int i = 0; i < roots.Count; i++)
            {
                roots[i].RegisterCallback(clickHandler);
                roots[i].RegisterCallback(cancelHandler);
            }

            context.AddReleaseAction(() =>
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    roots[i].UnregisterCallback(clickHandler);
                    roots[i].UnregisterCallback(cancelHandler);
                }
            });

            if (initialButton != null)
            {
                var initial = initialButton;
                roots[0].schedule.Execute(() =>
                {
                    if (!context.Valid)
                        return;
                    if (!UiModalState.Presented
                        && FrontendController.FocusRouter?.SocialRegionActive != true
                        && initial.panel != null)
                        initial.Focus();
                    foreach (var root in roots)
                        root.Query<ScrollView>().ForEach(sv => sv.scrollOffset = Vector2.zero);
                });
            }
        }

        private static void ConfigureShared(Button? initialButton)
        {
            if (initialButton != null)
                PageInitialFocus = initialButton;

            // Pre-match screens own the pointer. Match cameras may have left it locked.
            UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            UISFX.PlayMenuMusic();
        }

        private static void OnScreenCancel(NavigationCancelEvent evt, Action? backAction)
        {
            // Stop here so a modal or nested screen cannot also invoke its parent back action.
            evt.StopImmediatePropagation();
            if (ChatInputGate.SuppressShortcuts)
                return;
            // Escape opens the shell menu; controller Back closes the active
            // dialog or navigates back. One press never departs two layers.
            if (FrontendController.IsFrontendActive && FrontendController.FocusRouter is { } router)
            {
                router.HandlePageCancel();
                return;
            }
            backAction?.Invoke();
        }
    }
}
