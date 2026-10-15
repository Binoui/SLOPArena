using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.UI;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Typed runtime UI commands for agent-driven frontend/HUD workflows. These replace the
    /// repeated <c>unity command eval</c> probes that previously drove FrontendController
    /// navigation, UI Toolkit button submission, Game View resolution/gizmo reflection, and HUD
    /// layout inspection. <c>eval</c> remains for novel probes these commands do not cover.
    /// </summary>
    public static class SlopArenaUiCommands
    {
        [CliCommand(
            "sloparena.ui.status",
            "Read live frontend/HUD state and buttons; optionally inspect a named element's layout and enclosing scroll view.",
            MainThreadRequired = true,
            Tags = new[] { "frontend/ui" })]
        public static SlopArenaUiStatusResult Status(
            [CliArg("element", "Exact UXML name of a live element whose layout to inspect; omit for aggregate status.")]
            string element = null,
            [CliArg("document", "Exact UIDocument name to disambiguate --element; requires --element.")]
            string document = null)
        {
            if (element != null && string.IsNullOrWhiteSpace(element))
                throw new ArgumentException("element must not be blank.");
            if (document != null && string.IsNullOrWhiteSpace(document))
                throw new ArgumentException("document must not be blank.");
            if (document != null && element == null)
                throw new ArgumentException("--document requires --element.");

            var documents = Resources.FindObjectsOfTypeAll<UIDocument>();
            var selected = element == null ? null : ReadNamedElement(documents, element, document);
            return new SlopArenaUiStatusResult
            {
                Success = true,
                Playing = EditorApplication.isPlaying,
                Paused = EditorApplication.isPaused,
                Frame = Time.frameCount,
                Scene = SceneManager.GetActiveScene().name,
                Frontend = ReadFrontend(),
                Viewport = ReadViewport(),
                Hud = ReadHud(),
                Buttons = ReadLiveButtons(documents),
                Element = selected
            };
        }

        [CliCommand(
            "sloparena.ui.navigate",
            "Navigate to a frontend page or mode, waiting for the resulting scene/page; optionally advance one paused frame.",
            MainThreadRequired = true,
            Tags = new[] { "frontend/ui" })]
        public static async Task<SlopArenaUiNavigateResult> Navigate(
            [CliArg("page", "FrontendPage name: Home, FighterSelect, StageSelect, Results, ServerBrowser, LobbyRoom, ArcadeSelect.")]
            string page = null,
            [CliArg("mode", "GameMode name: Training, Solo, PvP, Arcade. Returns through Home when the current page requires it.")]
            string mode = null,
            [CliArg("step", "Advance one frame while paused and wait for it to complete.")]
            bool step = false)
        {
            RequirePlayMode("sloparena.ui.navigate");
            bool hasPage = !string.IsNullOrWhiteSpace(page);
            bool hasMode = !string.IsNullOrWhiteSpace(mode);
            if (hasPage == hasMode)
                throw new ArgumentException("Provide exactly one of --page or --mode.");
            if (UiModalState.Presented)
                throw new InvalidOperationException("Close the active modal through its live controls before navigating.");

            bool stepped = step && EditorApplication.isPaused;
            FrontendPage expectedPage;
            if (hasPage)
            {
                if (!Enum.TryParse(page, true, out FrontendPage targetPage)
                    || !Enum.IsDefined(typeof(FrontendPage), targetPage))
                    throw new ArgumentException(
                        $"Unknown page '{page}'. Valid: {string.Join(", ", Enum.GetNames(typeof(FrontendPage)))}.");
                expectedPage = targetPage;
                if (FrontendController.IsFrontendActive && FrontendController.CurrentPage == FrontendPage.LobbyRoom
                    && targetPage != FrontendPage.LobbyRoom)
                    throw new InvalidOperationException("Leave the online room through its live leave/back control before navigating.");
                if (!FrontendController.IsFrontendActive && EditorApplication.isPaused && !step)
                    throw new InvalidOperationException(
                        "The frontend scene must load before navigation can complete, but the Editor is paused. " +
                        "Use --step to authorize exactly one frame; the command will not resume or restart the session.");
                FrontendController.Show(targetPage);
            }
            else
            {
                if (!Enum.TryParse(mode, true, out GameMode targetMode)
                    || !Enum.IsDefined(typeof(GameMode), targetMode))
                    throw new ArgumentException(
                        $"Unknown mode '{mode}'. Valid: {string.Join(", ", Enum.GetNames(typeof(GameMode)))}.");
                if (!FrontendController.IsFrontendActive)
                    throw new InvalidOperationException("Frontend scene is not active; use --page Home first.");
                expectedPage = targetMode == GameMode.Arcade ? FrontendPage.ArcadeSelect
                    : targetMode == GameMode.PvP ? FrontendPage.ServerBrowser : FrontendPage.FighterSelect;
                string modeButton = targetMode switch
                {
                    GameMode.Training => "shell-nav-training",
                    GameMode.Solo => "shell-nav-offline",
                    GameMode.Arcade => "shell-nav-arcade",
                    _ => "shell-nav-online"
                };
                var submitted = await Click(modeButton, step);
                stepped = submitted.Stepped;
            }

            await WaitForEditorUpdates(
                operation: "sloparena.ui.navigate",
                isComplete: () => SceneManager.GetActiveScene().name == FrontendController.SceneName
                    && FrontendController.IsFrontendActive
                    && FrontendController.CurrentContext != null
                    && FrontendController.CurrentPage == expectedPage,
                minimumUpdates: 1,
                requireNextFrame: hasPage && stepped,
                beforeWait: hasPage && stepped ? (Action)(() => EditorApplication.Step()) : null,
                timeoutDiagnostic: () => NavigationFailure(expectedPage, stepped));

            return new SlopArenaUiNavigateResult
            {
                Success = true,
                RequestedPage = page,
                RequestedMode = mode,
                Page = FrontendController.CurrentPage.ToString(),
                Mode = MatchConfig.Mode.ToString(),
                Stepped = stepped
            };
        }

        [CliCommand(
            "sloparena.ui.click",
            "Focus and submit an enabled, visible live UI Toolkit button by UXML name.",
            MainThreadRequired = true,
            Tags = new[] { "frontend/ui" })]
        public static async Task<SlopArenaUiClickResult> Click(
            [CliArg("name", "UXML name of the live UI Toolkit Button to submit.", Required = true)] string name,
            [CliArg("step", "Advance one frame while paused after submitting, and wait for completion.")]
            bool step = false)
        {
            RequirePlayMode("sloparena.ui.click");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.");

            var liveButtons = EnumerateLiveButtons();
            var matches = liveButtons
                .Where(candidate => candidate.Button.name == name)
                .ToList();
            var available = matches.Where(candidate => candidate.Enabled && candidate.Visible).ToList();
            if (matches.Count == 0)
                throw new ArgumentException(
                    $"No live attached Button named '{name}'. Enabled visible buttons: " +
                    string.Join(", ", VisibleButtonNames(liveButtons)) + ".");
            if (available.Count == 0)
                throw new InvalidOperationException(
                    $"Button '{name}' exists in live documents but is unavailable (enabled/visible): " +
                    string.Join(", ", matches.Select(candidate =>
                        $"{candidate.Document.name} (enabled={candidate.Enabled}, visible={candidate.Visible})")) + ".");
            if (available.Count > 1)
                throw new InvalidOperationException(
                    $"Button '{name}' matches {available.Count} enabled visible buttons in documents " +
                    string.Join(", ", available.Select(candidate => candidate.Document.name)) + "; ambiguous.");

            var selected = available[0];
            string documentName = selected.Document.name;
            selected.Button.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = selected.Button;
                selected.Button.SendEvent(submit);
            }

            bool stepped = step && EditorApplication.isPaused;

            await WaitForEditorUpdates(
                operation: "sloparena.ui.click",
                isComplete: null,
                minimumUpdates: 2,
                requireNextFrame: stepped,
                beforeWait: stepped ? (Action)(() => EditorApplication.Step()) : null,
                timeoutDiagnostic: () =>
                    $"UI layout did not settle after submitting '{name}' within 5 seconds. " +
                    "Inspect sloparena.ui.status and retry in a stable Play Mode session.");

            return new SlopArenaUiClickResult
            {
                Success = true,
                Name = name,
                Document = documentName,
                Page = FrontendController.IsFrontendActive ? FrontendController.CurrentPage.ToString() : null,
                Stepped = stepped
            };
        }

        [CliCommand(
            "sloparena.ui.viewport",
            "Select a Game View resolution and optionally toggle gizmos, then wait for completion.",
            MainThreadRequired = true,
            Tags = new[] { "frontend/ui" })]
        public static async Task<SlopArenaUiViewportResult> Viewport(
            [CliArg("width", "Game View width in pixels.")]
            int width = 1920,
            [CliArg("height", "Game View height in pixels.")]
            int height = 1080,
            [CliArg("gizmos", "'on' or 'off' for Game View gizmo drawing; omit to leave unchanged.")]
            string gizmos = null,
            [CliArg("step", "Advance one frame while paused after applying, and wait for completion.")]
            bool step = false)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("width and height must be positive.");
            bool? gizmosValue = ParseOnOff(gizmos);
            bool stepped = step && EditorApplication.isPaused;

            var gameViewType = GameViewType();
            var view = FindGameView();
            if (gameViewType == null || view == null)
                throw new InvalidOperationException("Game View window not found; open the Game View first.");

            var callback = gameViewType.GetMethod("SizeSelectionCallback",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (callback == null)
                throw new InvalidOperationException(
                    "Unity internal GameView.SizeSelectionCallback not found; select the resolution in the Game View menu manually.");
            int sizeIndex = FindOrAddSize(width, height);
            var parameters = callback.GetParameters();
            var arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                arguments[i] = parameters[i].ParameterType == typeof(int) ? (object)sizeIndex : null;
            callback.Invoke(view, arguments);

            if (gizmosValue.HasValue)
            {
                var gizmosProperty = gameViewType.GetProperty("drawGizmos",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gizmosProperty == null)
                    throw new InvalidOperationException("Unity internal GameView.drawGizmos not found.");
                gizmosProperty.SetValue(view, gizmosValue.Value);
            }

            view.Repaint();
            await WaitForEditorUpdates(
                operation: "sloparena.ui.viewport",
                isComplete: () => (int)gameViewType.GetProperty("selectedSizeIndex",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(view) == sizeIndex
                    && (!gizmosValue.HasValue || (bool)gameViewType.GetProperty("drawGizmos",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(view) == gizmosValue.Value),
                minimumUpdates: 1,
                requireNextFrame: stepped,
                beforeWait: stepped ? (Action)(() => EditorApplication.Step()) : null,
                timeoutDiagnostic: () =>
                    "Game View update did not complete within 5 seconds after applying the viewport settings.");

            return new SlopArenaUiViewportResult
            {
                Success = true,
                Width = width,
                Height = height,
                SizeIndex = sizeIndex,
                Gizmos = gizmosValue,
                Stepped = stepped,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                ScreenSizeMatches = Screen.width == width && Screen.height == height
            };
        }

        // ---- shared helpers --------------------------------------------------------

        private static void RequirePlayMode(string command)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException($"{command} requires Play Mode (use editor_play first).");
        }
        private static async Task WaitForEditorUpdates(
            string operation,
            Func<bool> isComplete,
            int minimumUpdates,
            bool requireNextFrame,
            Action beforeWait,
            Func<string> timeoutDiagnostic)
        {
            var completion = new TaskCompletionSource<bool>();
            double deadline = EditorApplication.timeSinceStartup + 5d;
            int observedUpdates = 0;
            int startingFrame = 0;

            void ObserveUpdate()
            {
                try
                {
                    observedUpdates++;
                    if (EditorApplication.timeSinceStartup >= deadline)
                    {
                        completion.TrySetException(new InvalidOperationException(timeoutDiagnostic()));
                        return;
                    }
                    bool stateComplete = isComplete == null || isComplete();
                    bool frameComplete = !requireNextFrame || Time.frameCount > startingFrame;
                    if (observedUpdates >= minimumUpdates && stateComplete && frameComplete)
                        completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(new InvalidOperationException(
                        $"{operation} failed while observing Editor updates: {ex.Message}", ex));
                }
            }

            EditorApplication.update += ObserveUpdate;
            try
            {
                startingFrame = Time.frameCount;
                beforeWait?.Invoke();
                await completion.Task;
            }
            finally
            {
                EditorApplication.update -= ObserveUpdate;
            }
        }

        private static string NavigationFailure(FrontendPage expectedPage, bool requireNextFrame)
        {
            var identity = FrontendController.Identity;
            string modeGate = identity == null ? "unknown" : identity.ModeGateClosed.ToString();
            string pausedHint = EditorApplication.isPaused ? " Use --step if one paused frame is authorized." : "";
            return
                $"Timed out waiting for navigation completion (expected page {expectedPage}, " +
                $"next frame required {requireNextFrame}): scene '{SceneManager.GetActiveScene().name}', " +
                $"frontend active {FrontendController.IsFrontendActive}, page {FrontendController.CurrentPage}, " +
                $"mode gate closed {modeGate}, modal presented {UiModalState.Presented}, frame {Time.frameCount}.{pausedHint}";
        }

        private static bool? ParseOnOff(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized is "on" or "true" or "1")
                return true;
            if (normalized is "off" or "false" or "0")
                return false;
            throw new ArgumentException($"Unknown gizmos value '{value}'. Use 'on' or 'off'.");
        }

        private static SlopArenaUiFrontendInfo ReadFrontend()
            => new SlopArenaUiFrontendInfo
            {
                Active = FrontendController.IsFrontendActive,
                Page = FrontendController.CurrentPage.ToString(),
                Mode = MatchConfig.Mode.ToString(),
                Arena = MatchConfig.ArenaName,
                ModalPresented = UiModalState.Presented,
                ModeGateClosed = FrontendController.Identity?.ModeGateClosed
            };

        private static SlopArenaUiViewportInfo ReadViewport()
        {
            var info = new SlopArenaUiViewportInfo
            {
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height
            };
            try
            {
                var gameViewType = GameViewType();
                var view = FindGameView();
                if (gameViewType == null || view == null)
                    return info;
                var selected = gameViewType
                    .GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(view);
                if (selected is int selectedIndex && TryGetSizeGroup(out var group))
                {
                    var texts = ReadSizeTexts(group);
                    if (texts != null && selectedIndex >= 0 && selectedIndex < texts.Length)
                        info.SizeText = texts[selectedIndex];
                }
                var gizmos = gameViewType
                    .GetProperty("drawGizmos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(view);
                if (gizmos is bool gizmosValue)
                    info.Gizmos = gizmosValue;
            }
            catch (Exception)
            {
                // Best-effort internal-API reads; status must not fail when Unity internals change.
            }
            return info;
        }

        private static SlopArenaUiHudInfo ReadHud()
        {
            var info = new SlopArenaUiHudInfo();
            var hud = UnityEngine.Object.FindFirstObjectByType<HUDManager>();
            var document = hud?.Document;
            if (document == null)
            {
                info.LayoutIssues.Add("HUD document is missing.");
                return info;
            }

            var panelSettings = document.panelSettings;
            if (panelSettings == null)
                info.LayoutIssues.Add("HUD panel settings are missing.");
            else
            {
                info.ReferenceResolution = panelSettings.referenceResolution.ToString();
                info.ScaleMode = panelSettings.scaleMode.ToString();
                info.Match = panelSettings.match;
            }

            var root = document.rootVisualElement;
            if (root == null)
            {
                info.LayoutIssues.Add("HUD root visual element is missing.");
                info.LayoutValid = false;
                return info;
            }
            info.Present = true;
            if (root.panel == null)
                info.LayoutIssues.Add("HUD root is not attached to a live panel.");
            if (TryGetBounds(root, out string rootBounds))
                info.RootBounds = rootBounds;
            else
                info.LayoutIssues.Add("HUD root has missing, non-finite, or zero-area geometry.");

            var board = root.Q<VisualElement>("player-billboard");
            if (board == null)
                info.LayoutIssues.Add("Player billboard is missing.");
            else
            {
                foreach (var card in board.Children())
                {
                    bool validBounds = TryGetBounds(card, out string bounds);
                    var cardInfo = new SlopArenaUiCardInfo
                    {
                        Name = card.name,
                        Display = card.resolvedStyle.display.ToString(),
                        Bounds = validBounds ? bounds : null,
                        LayoutValid = validBounds,
                        LayoutIssue = validBounds ? null : "Missing, non-finite, or zero-area geometry."
                    };
                    if (!validBounds)
                        info.LayoutIssues.Add($"Player card '{card.name}' has invalid geometry.");
                    foreach (var label in card.Query<Label>().ToList())
                        cardInfo.Labels.Add(new SlopArenaUiLabelInfo
                        {
                            Name = label.name,
                            Text = label.text,
                            ClassNames = label.GetClasses().ToList()
                        });
                    info.Cards.Add(cardInfo);
                }
            }

            var kitDiamonds = root.Q<VisualElement>("kit-diamonds");
            if (kitDiamonds == null)
            {
                info.KitDiamondsLayout = "missing";
                info.LayoutIssues.Add("Kit diamonds element is missing.");
            }
            else
            {
                info.KitDiamondsDisplay = kitDiamonds.resolvedStyle.display.ToString();
                if (kitDiamonds.resolvedStyle.display == DisplayStyle.None)
                    info.KitDiamondsLayout = "hidden";
                else if (TryGetBounds(kitDiamonds, out string bounds))
                {
                    info.KitDiamondsBounds = bounds;
                    info.KitDiamondsLayout = "valid";
                }
                else
                {
                    info.KitDiamondsLayout = "invalid";
                    info.LayoutIssues.Add("Kit diamonds have missing, non-finite, or zero-area geometry.");
                }
            }

            var targetLock = root.Q<VisualElement>("target-lock-indicator");
            if (targetLock == null)
            {
                info.TargetLockIndicatorLayout = "missing";
                info.LayoutIssues.Add("Target-lock indicator is missing.");
            }
            else
            {
                info.TargetLockIndicatorDisplay = targetLock.resolvedStyle.display.ToString();
                if (targetLock.resolvedStyle.display == DisplayStyle.None)
                    info.TargetLockIndicatorLayout = "hidden";
                else if (TryGetBounds(targetLock, out string bounds))
                {
                    info.TargetLockIndicatorBounds = bounds;
                    info.TargetLockIndicatorLayout = "valid";
                }
                else
                {
                    info.TargetLockIndicatorLayout = "invalid";
                    info.LayoutIssues.Add("Target-lock indicator has missing, non-finite, or zero-area geometry.");
                }
            }

            info.LayoutValid = info.LayoutIssues.Count == 0;
            return info;
        }

        private static bool TryGetBounds(VisualElement element, out string bounds)
        {
            bounds = null;
            if (element?.panel == null)
                return false;
            var rect = element.worldBound;
            if (!HasValidBounds(rect))
                return false;
            bounds = rect.ToString();
            return true;
        }

        private static bool HasValidBounds(Rect rect)
            => float.IsFinite(rect.x) && float.IsFinite(rect.y)
                && float.IsFinite(rect.width) && float.IsFinite(rect.height)
                && float.IsFinite(rect.xMax) && float.IsFinite(rect.yMax)
                && rect.width > 0f && rect.height > 0f;

        private static List<SlopArenaUiButtonInfo> ReadLiveButtons(UIDocument[] documents)
            => EnumerateLiveButtons(documents)
                .Select(candidate => new SlopArenaUiButtonInfo
                {
                    Name = candidate.Button.name,
                    Text = candidate.Button.text,
                    Document = candidate.Document.name,
                    Enabled = candidate.Enabled,
                    Visible = candidate.Visible,
                    Display = candidate.Button.resolvedStyle.display.ToString(),
                    Bounds = candidate.HasBounds ? candidate.Bounds.ToString() : null
                })
                .ToList();

        private static bool IsLiveDocument(UIDocument document)
        {
            if (document == null || !document.isActiveAndEnabled || EditorUtility.IsPersistent(document))
                return false;
            var scene = document.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && document.rootVisualElement?.panel != null;
        }

        private static bool IsOwnedByDocument(
            VisualElement element, UIDocument owner, UIDocument[] documents)
        {
            for (var ancestor = element; ancestor != null; ancestor = ancestor.parent)
            {
                foreach (var document in documents)
                    if (IsLiveDocument(document) && ancestor == document.rootVisualElement)
                        return document == owner;
            }
            return false;
        }

        private static SlopArenaUiElementInfo ReadNamedElement(
            UIDocument[] documents, string name, string documentName)
        {
            UIDocument selectedDocument = null;
            VisualElement selected = null;
            foreach (var document in documents)
            {
                if (!IsLiveDocument(document) || (documentName != null && document.name != documentName))
                    continue;
                var root = document.rootVisualElement;
                foreach (var candidate in root.Query<VisualElement>(name: name).Build())
                {
                    if (candidate.panel != root.panel || !IsOwnedByDocument(candidate, document, documents))
                        continue;
                    if (selected != null)
                        throw new InvalidOperationException(
                            $"Element '{name}' is ambiguous in live documents '{selectedDocument.name}' and '{document.name}'. " +
                            "Use --document to select a document containing one uniquely named element.");
                    selected = candidate;
                    selectedDocument = document;
                }
            }
            if (selected == null)
                throw new ArgumentException(
                    $"No live attached element named '{name}'" +
                    (documentName == null ? "." : $" in document '{documentName}'."));

            var worldBounds = ReadRect(selected.worldBound);
            var layoutBounds = ReadRect(selected.layout);
            float opacity = selected.resolvedStyle.opacity;
            var scroll = selected as ScrollView ?? selected.GetFirstAncestorOfType<ScrollView>();
            return new SlopArenaUiElementInfo
            {
                Name = selected.name,
                Document = selectedDocument.name,
                Type = selected.GetType().Name,
                PanelAttached = selected.panel != null,
                Enabled = selected.enabledInHierarchy,
                Display = selected.resolvedStyle.display.ToString(),
                Visibility = selected.resolvedStyle.visibility.ToString(),
                Opacity = float.IsFinite(opacity) ? opacity : (float?)null,
                AncestorVisible = IsEffectivelyVisible(selected),
                GeometryAvailable = worldBounds != null && layoutBounds != null,
                WorldBounds = worldBounds,
                LayoutBounds = layoutBounds,
                CenterReceivesHit = worldBounds != null && IsUnobscured(selected, selected.worldBound),
                OverflowClips = ReadOverflowClips(selected),
                Scroll = scroll == null ? null : ReadScroll(scroll)
            };
        }

        private static SlopArenaUiRectInfo ReadRect(Rect rect)
            => HasValidBounds(rect) ? new SlopArenaUiRectInfo
            {
                X = rect.x, Y = rect.y, Width = rect.width, Height = rect.height
            } : null;

        private static SlopArenaUiVectorInfo ReadVector(Vector2 value)
            => float.IsFinite(value.x) && float.IsFinite(value.y) ? new SlopArenaUiVectorInfo
            {
                X = value.x, Y = value.y
            } : null;

        private static SlopArenaUiRangeInfo ReadRange(float min, float max)
            => float.IsFinite(min) && float.IsFinite(max) && min <= max ? new SlopArenaUiRangeInfo
            {
                Min = min, Max = max
            } : null;

        private static SlopArenaUiScrollInfo ReadScroll(ScrollView scroll)
        {
            var viewport = ReadRect(scroll.contentViewport.worldBound);
            var content = ReadRect(scroll.contentContainer.worldBound);
            return new SlopArenaUiScrollInfo
            {
                Name = scroll.name,
                GeometryAvailable = viewport != null && content != null,
                ViewportBounds = viewport,
                ContentBounds = content,
                Offset = ReadVector(scroll.scrollOffset),
                HorizontalRange = ReadRange(scroll.horizontalScroller.lowValue, scroll.horizontalScroller.highValue),
                VerticalRange = ReadRange(scroll.verticalScroller.lowValue, scroll.verticalScroller.highValue)
            };
        }

        private static IReadOnlyList<SlopArenaUiOverflowClipInfo> ReadOverflowClips(VisualElement target)
        {
            List<SlopArenaUiOverflowClipInfo> clips = null;
            for (var ancestor = target.parent; ancestor != null; ancestor = ancestor.parent)
            {
                var overflow = ancestor.style.overflow;
                var scroll = ancestor.GetFirstAncestorOfType<ScrollView>();
                bool scrollViewport = scroll != null && ancestor == scroll.contentViewport;
                bool inlineClip = overflow.keyword == StyleKeyword.Undefined && overflow.value == Overflow.Hidden;
                if (!inlineClip && !scrollViewport)
                    continue;
                clips ??= new List<SlopArenaUiOverflowClipInfo>();
                clips.Add(new SlopArenaUiOverflowClipInfo
                {
                    Name = ancestor.name,
                    Type = ancestor.GetType().Name,
                    Source = scrollViewport ? "scroll-viewport" : "inline-overflow",
                    Bounds = ReadRect(ancestor.worldBound)
                });
            }
            return clips != null ? clips : Array.Empty<SlopArenaUiOverflowClipInfo>();
        }

        private static List<LiveButton> EnumerateLiveButtons(UIDocument[] documents = null)
        {
            var buttons = new List<LiveButton>();
            foreach (var document in documents ?? Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (!IsLiveDocument(document))
                    continue;
                var root = document.rootVisualElement;

                foreach (var button in root.Query<Button>().ToList())
                {
                    if (button.panel != root.panel)
                        continue;
                    var bounds = button.worldBound;
                    bool hasBounds = HasValidBounds(bounds);
                    buttons.Add(new LiveButton(
                        document,
                        button,
                        bounds,
                        hasBounds,
                        button.enabledInHierarchy,
                        hasBounds && IsEffectivelyVisible(button) && IsUnobscured(button, bounds)));
                }
            }
            return buttons;
        }

        private static bool IsEffectivelyVisible(VisualElement element)
        {
            float effectiveOpacity = 1f;
            for (var ancestor = element; ancestor != null; ancestor = ancestor.parent)
            {
                var style = ancestor.resolvedStyle;
                if (style.display == DisplayStyle.None
                    || style.visibility == Visibility.Hidden
                    || !float.IsFinite(style.opacity))
                    return false;
                effectiveOpacity *= style.opacity;
                if (effectiveOpacity <= 0.001f)
                    return false;
            }
            return true;
        }

        // Use the panel's actual hit testing: ancestor styles alone miss scroll clipping and
        // overlays/modals. Only submit a button whose center would receive a real click.
        private static bool IsUnobscured(VisualElement target, Rect bounds)
        {
            var picked = target.panel?.Pick(bounds.center);
            for (var element = picked; element != null; element = element.parent)
                if (element == target)
                    return true;
            return false;
        }

        private static IEnumerable<string> VisibleButtonNames(IEnumerable<LiveButton> liveButtons)
            => liveButtons
                .Where(candidate => candidate.Enabled && candidate.Visible)
                .Select(candidate => candidate.Button.name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct();

        private readonly struct LiveButton
        {
            public readonly UIDocument Document;
            public readonly Button Button;
            public readonly Rect Bounds;
            public readonly bool HasBounds;
            public readonly bool Enabled;
            public readonly bool Visible;

            public LiveButton(
                UIDocument document, Button button, Rect bounds, bool hasBounds, bool enabled, bool visible)
            {
                Document = document;
                Button = button;
                Bounds = bounds;
                HasBounds = hasBounds;
                Enabled = enabled;
                Visible = visible;
            }
        }

        // ---- Game View internals (the seam previously re-probed through eval) -------

        private static Type GameViewType()
            => typeof(Editor).Assembly.GetType("UnityEditor.GameView");

        private static EditorWindow FindGameView()
        {
            var gameViewType = GameViewType();
            if (gameViewType == null)
                return null;
            var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            var main = playModeView?.GetMethod("GetMainPlayModeView",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(null, null) as EditorWindow;
            if (main != null && gameViewType.IsInstanceOfType(main))
                return main;
            var views = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(window => gameViewType.IsInstanceOfType(window)).ToArray();
            if (views.Length > 1)
                throw new InvalidOperationException("Multiple Game Views exist without a main Play Mode View; select one before changing the viewport.");
            return views.FirstOrDefault();
        }

        private static bool TryGetSizeGroup(out object group)
        {
            group = null;
            try
            {
                var assembly = typeof(Editor).Assembly;
                var gameViewSizesType = assembly.GetType("UnityEditor.GameViewSizes");
                var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
                var instanceProperty = gameViewSizesType?.BaseType?.GetProperty("instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object sizes = instanceProperty?.GetValue(null);
                if (gameViewSizesType == null || groupType == null || sizes == null)
                    return false;
                group = gameViewSizesType.GetMethod("GetGroup")?.Invoke(
                    sizes, new object[] { Enum.Parse(groupType, "Standalone") });
                return group != null;
            }
            catch (Exception)
            {
                group = null;
                return false;
            }
        }

        private static string[] ReadSizeTexts(object group)
            => group.GetType().GetMethod("GetDisplayTexts")?.Invoke(group, null) as string[];

        private static int FindOrAddSize(int width, int height)
        {
            if (!TryGetSizeGroup(out var group))
                throw new InvalidOperationException("Unity internal GameViewSizes API not found.");

            int index = FindSizeIndex(group, width, height);
            if (index >= 0)
                return index;

            var assembly = typeof(Editor).Assembly;
            var sizeTypeType = assembly.GetType("UnityEditor.GameViewSizeType");
            var gameViewSizeType = assembly.GetType("UnityEditor.GameViewSize");
            var addCustomSize = group.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "AddCustomSize");
            var constructor = gameViewSizeType?.GetConstructor(new[]
            {
                sizeTypeType, typeof(int), typeof(int), typeof(string)
            });
            if (addCustomSize == null || constructor == null || sizeTypeType == null)
                throw new InvalidOperationException(
                    $"No Game View preset for {width}x{height} and Unity's AddCustomSize API was not found; select a preset in the Game View menu.");

            var sizeType = Enum.Parse(sizeTypeType, "FixedResolution");
            var customSize = constructor.Invoke(new object[] { sizeType, width, height, $"{width}x{height}" });
            try
            {
                addCustomSize.Invoke(group, new[] { customSize });
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(
                    $"Could not add a custom Game View size for {width}x{height}: {ex.InnerException?.Message ?? ex.Message}", ex);
            }

            index = FindSizeIndex(group, width, height);
            if (index >= 0)
                return index;
            var texts = ReadSizeTexts(group);
            if (texts != null && texts.Length > 0)
                return texts.Length - 1;
            throw new InvalidOperationException($"Could not resolve the Game View size for {width}x{height}.");
        }

        private static int FindSizeIndex(object group, int width, int height)
        {
            var texts = ReadSizeTexts(group);
            if (texts == null)
                return -1;
            string exact = $"{width}x{height}";
            for (int i = 0; i < texts.Length; i++)
            {
                var text = (texts[i] ?? "").Replace(" ", "");
                if (text.Equals(exact, StringComparison.OrdinalIgnoreCase)
                    || text.Contains($"({exact})", StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }

    public sealed class SlopArenaUiStatusResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("playing")] public bool Playing { get; set; }
        [JsonProperty("paused")] public bool Paused { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("frontend")] public SlopArenaUiFrontendInfo Frontend { get; set; }
        [JsonProperty("viewport")] public SlopArenaUiViewportInfo Viewport { get; set; }
        [JsonProperty("hud")] public SlopArenaUiHudInfo Hud { get; set; }
        [JsonProperty("buttons")] public List<SlopArenaUiButtonInfo> Buttons { get; set; }
        [JsonProperty("element", NullValueHandling = NullValueHandling.Ignore)] public SlopArenaUiElementInfo Element { get; set; }
    }

    public sealed class SlopArenaUiElementInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("document")] public string Document { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("panelAttached")] public bool PanelAttached { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("display")] public string Display { get; set; }
        [JsonProperty("visibility")] public string Visibility { get; set; }
        [JsonProperty("opacity")] public float? Opacity { get; set; }
        [JsonProperty("ancestorVisible")] public bool AncestorVisible { get; set; }
        [JsonProperty("geometryAvailable")] public bool GeometryAvailable { get; set; }
        [JsonProperty("worldBounds")] public SlopArenaUiRectInfo WorldBounds { get; set; }
        [JsonProperty("layoutBounds")] public SlopArenaUiRectInfo LayoutBounds { get; set; }
        [JsonProperty("centerReceivesHit")] public bool CenterReceivesHit { get; set; }
        [JsonProperty("overflowClips")] public IReadOnlyList<SlopArenaUiOverflowClipInfo> OverflowClips { get; set; }
        [JsonProperty("clippingCoverage")] public string ClippingCoverage => "inline-and-scroll-viewports";
        [JsonProperty("scroll", NullValueHandling = NullValueHandling.Ignore)] public SlopArenaUiScrollInfo Scroll { get; set; }
    }

    public sealed class SlopArenaUiRectInfo
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("width")] public float Width { get; set; }
        [JsonProperty("height")] public float Height { get; set; }
    }

    public sealed class SlopArenaUiVectorInfo
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
    }

    public sealed class SlopArenaUiRangeInfo
    {
        [JsonProperty("min")] public float Min { get; set; }
        [JsonProperty("max")] public float Max { get; set; }
    }

    public sealed class SlopArenaUiScrollInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("geometryAvailable")] public bool GeometryAvailable { get; set; }
        [JsonProperty("viewportBounds")] public SlopArenaUiRectInfo ViewportBounds { get; set; }
        [JsonProperty("contentBounds")] public SlopArenaUiRectInfo ContentBounds { get; set; }
        [JsonProperty("offset")] public SlopArenaUiVectorInfo Offset { get; set; }
        [JsonProperty("horizontalRange")] public SlopArenaUiRangeInfo HorizontalRange { get; set; }
        [JsonProperty("verticalRange")] public SlopArenaUiRangeInfo VerticalRange { get; set; }
    }

    public sealed class SlopArenaUiOverflowClipInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("bounds")] public SlopArenaUiRectInfo Bounds { get; set; }
    }

    public sealed class SlopArenaUiFrontendInfo
    {
        [JsonProperty("active")] public bool Active { get; set; }
        [JsonProperty("page")] public string Page { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; }
        [JsonProperty("arena")] public string Arena { get; set; }
        [JsonProperty("modalPresented")] public bool ModalPresented { get; set; }
        [JsonProperty("modeGateClosed")] public bool? ModeGateClosed { get; set; }
    }

    public sealed class SlopArenaUiViewportInfo
    {
        [JsonProperty("screenWidth")] public int ScreenWidth { get; set; }
        [JsonProperty("screenHeight")] public int ScreenHeight { get; set; }
        [JsonProperty("sizeText", NullValueHandling = NullValueHandling.Ignore)] public string SizeText { get; set; }
        [JsonProperty("gizmos", NullValueHandling = NullValueHandling.Ignore)] public bool? Gizmos { get; set; }
    }

    public sealed class SlopArenaUiHudInfo
    {
        [JsonProperty("present")] public bool Present { get; set; }
        [JsonProperty("layoutValid")] public bool LayoutValid { get; set; }
        [JsonProperty("layoutIssues")] public List<string> LayoutIssues { get; set; } = new List<string>();
        [JsonProperty("referenceResolution", NullValueHandling = NullValueHandling.Ignore)]
        public string ReferenceResolution { get; set; }
        [JsonProperty("scaleMode", NullValueHandling = NullValueHandling.Ignore)] public string ScaleMode { get; set; }
        [JsonProperty("match", NullValueHandling = NullValueHandling.Ignore)] public float? Match { get; set; }
        [JsonProperty("rootBounds", NullValueHandling = NullValueHandling.Ignore)] public string RootBounds { get; set; }
        [JsonProperty("kitDiamondsLayout", NullValueHandling = NullValueHandling.Ignore)] public string KitDiamondsLayout { get; set; }
        [JsonProperty("kitDiamondsBounds", NullValueHandling = NullValueHandling.Ignore)] public string KitDiamondsBounds { get; set; }
        [JsonProperty("kitDiamondsDisplay", NullValueHandling = NullValueHandling.Ignore)] public string KitDiamondsDisplay { get; set; }
        [JsonProperty("targetLockIndicatorLayout", NullValueHandling = NullValueHandling.Ignore)] public string TargetLockIndicatorLayout { get; set; }
        [JsonProperty("targetLockIndicatorBounds", NullValueHandling = NullValueHandling.Ignore)] public string TargetLockIndicatorBounds { get; set; }
        [JsonProperty("targetLockIndicatorDisplay", NullValueHandling = NullValueHandling.Ignore)] public string TargetLockIndicatorDisplay { get; set; }
        [JsonProperty("cards")] public List<SlopArenaUiCardInfo> Cards { get; set; } = new List<SlopArenaUiCardInfo>();
    }

    public sealed class SlopArenaUiCardInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("display")] public string Display { get; set; }
        [JsonProperty("bounds", NullValueHandling = NullValueHandling.Ignore)] public string Bounds { get; set; }
        [JsonProperty("layoutValid")] public bool LayoutValid { get; set; }
        [JsonProperty("layoutIssue", NullValueHandling = NullValueHandling.Ignore)] public string LayoutIssue { get; set; }
        [JsonProperty("labels")] public List<SlopArenaUiLabelInfo> Labels { get; set; } = new List<SlopArenaUiLabelInfo>();
    }

    public sealed class SlopArenaUiLabelInfo
    {
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("classNames")] public List<string> ClassNames { get; set; } = new List<string>();
    }

    public sealed class SlopArenaUiButtonInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("document")] public string Document { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("visible")] public bool Visible { get; set; }
        [JsonProperty("display")] public string Display { get; set; }
        [JsonProperty("bounds", NullValueHandling = NullValueHandling.Ignore)] public string Bounds { get; set; }
    }

    public sealed class SlopArenaUiNavigateResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("requestedPage", NullValueHandling = NullValueHandling.Ignore)] public string RequestedPage { get; set; }
        [JsonProperty("requestedMode", NullValueHandling = NullValueHandling.Ignore)] public string RequestedMode { get; set; }
        [JsonProperty("page")] public string Page { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; }
        [JsonProperty("stepped")] public bool Stepped { get; set; }
    }

    public sealed class SlopArenaUiClickResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("document")] public string Document { get; set; }
        [JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)] public string Page { get; set; }
        [JsonProperty("stepped")] public bool Stepped { get; set; }
    }

    public sealed class SlopArenaUiViewportResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
        [JsonProperty("sizeIndex")] public int SizeIndex { get; set; }
        [JsonProperty("gizmos", NullValueHandling = NullValueHandling.Ignore)] public bool? Gizmos { get; set; }
        [JsonProperty("stepped")] public bool Stepped { get; set; }
        [JsonProperty("screenWidth")] public int ScreenWidth { get; set; }
        [JsonProperty("screenHeight")] public int ScreenHeight { get; set; }
        [JsonProperty("screenSizeMatches")] public bool ScreenSizeMatches { get; set; }
        [JsonProperty("completion")] public string Completion => "settings-applied";
    }
}
