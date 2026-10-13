using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    public static class SlopArenaUiLayoutSelfTest
    {
        [MenuItem("Tools/SlopArena/Tests/Named UI Layout Inspection")]
        private static async void RunMenu()
        {
            try { Debug.Log(JsonConvert.SerializeObject(await Run())); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        public static async Task<object> Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run named-layout regression in Edit Mode; it will not change Editor mode.");

            var originalFocus = EditorWindow.focusedWindow;
            var originalScene = SceneManager.GetActiveScene();
            bool originalPaused = EditorApplication.isPaused;
            var scene = EditorSceneManager.NewPreviewScene();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            var window = ScriptableObject.CreateInstance<SlopArenaUiLayoutTestWindow>();
            var prefix = "named-layout-" + Guid.NewGuid().ToString("N");
            try
            {
                window.titleContent = new GUIContent("Named-layout regression");
                window.position = new Rect(40, 40, 360, 300);
                window.ShowUtility();
                var first = CreateDocument(scene, settings, window, prefix + "-first");
                var second = CreateDocument(scene, settings, window, prefix + "-second");
                var root = first.rootVisualElement;
                second.transform.SetParent(first.transform, false);
                second.rootVisualElement.RemoveFromHierarchy();
                root.Add(second.rootVisualElement);
                root.style.width = 320;
                root.style.height = 260;
                second.rootVisualElement.style.position = Position.Absolute;
                second.rootVisualElement.style.left = 250;
                second.rootVisualElement.style.top = 0;
                second.rootVisualElement.style.width = 60;
                second.rootVisualElement.style.height = 60;

                var scroll = new ScrollView(ScrollViewMode.Vertical) { name = prefix + "-scroll" };
                scroll.style.width = 160;
                scroll.style.height = 80;
                scroll.contentContainer.style.height = 260;
                var item = new VisualElement { name = prefix + "-item" };
                item.style.position = Position.Absolute;
                item.style.left = 10;
                item.style.top = 90;
                item.style.width = 40;
                item.style.height = 20;
                scroll.Add(item);
                root.Add(scroll);

                var hidden = new VisualElement();
                hidden.style.visibility = Visibility.Hidden;
                var hiddenChild = new VisualElement { name = prefix + "-hidden" };
                hiddenChild.style.width = 30;
                hiddenChild.style.height = 30;
                hidden.Add(hiddenChild);
                root.Add(hidden);
                var duplicate = prefix + "-duplicate";
                root.Add(new VisualElement { name = duplicate });
                root.Add(new VisualElement { name = duplicate });
                var shared = prefix + "-shared";
                root.Add(new VisualElement { name = shared });
                second.rootVisualElement.Add(new VisualElement { name = shared });
                var childOnly = prefix + "-child-only";
                second.rootVisualElement.Add(new VisualElement { name = childOnly });

                await WaitForLayout(() => item.panel != null && item.worldBound.width > 0f
                    && scroll.verticalScroller.highValue > 60f);
                var clipped = SlopArenaUiCommands.Status(item.name, first.name).Element;
                Check(clipped.GeometryAvailable && clipped.WorldBounds.Width == 40f
                    && clipped.WorldBounds.Height == 20f, "Named element did not expose its finite numeric size.");
                Check(!clipped.CenterReceivesHit && clipped.OverflowClips.Count > 0,
                    "An offscreen scroll item must report clipping context and must not claim center hit availability.");
                Check(clipped.Scroll != null && clipped.Scroll.VerticalRange != null
                    && clipped.Scroll.VerticalRange.Max > 60f, "Enclosing ScrollView range was not observed.");

                scroll.scrollOffset = new Vector2(0f, 60f);
                await WaitForLayout(() => Mathf.Abs(scroll.scrollOffset.y - 60f) < 0.1f
                    && Mathf.Abs(item.worldBound.y - (clipped.WorldBounds.Y - 60f)) < 0.5f);
                int beforeFrame = Time.frameCount;
                var revealed = SlopArenaUiCommands.Status(item.name, first.name).Element;
                Check(revealed.CenterReceivesHit, "Revealed item center did not receive the panel hit test.");
                Check(Mathf.Abs(revealed.WorldBounds.Y - clipped.WorldBounds.Y + 60f) < 0.5f
                    && revealed.LayoutBounds.Y == clipped.LayoutBounds.Y,
                    "Scrolling must change world position without changing the item's local layout position.");
                Check(revealed.Scroll.Offset.Y == 60f && scroll.scrollOffset.y == 60f
                    && Time.frameCount == beforeFrame, "Inspection changed scroll position or advanced a frame.");

                var hiddenResult = SlopArenaUiCommands.Status(hiddenChild.name, first.name).Element;
                Check(!hiddenResult.AncestorVisible && !hiddenResult.CenterReceivesHit,
                    "A hidden ancestor must make the descendant unavailable for center hits.");
                item.SetEnabled(false);
                Check(!SlopArenaUiCommands.Status(item.name, first.name).Element.Enabled,
                    "Inherited disabled state was not reported.");
                Expect<InvalidOperationException>(() => SlopArenaUiCommands.Status(duplicate, first.name));
                Expect<InvalidOperationException>(() => SlopArenaUiCommands.Status(shared));
                Check(SlopArenaUiCommands.Status(shared, second.name).Element.Document == second.name,
                    "The document selector did not disambiguate equal element names.");
                Expect<ArgumentException>(() => SlopArenaUiCommands.Status(prefix + "-missing"));
                Expect<ArgumentException>(() => SlopArenaUiCommands.Status(" "));
                Expect<ArgumentException>(() => SlopArenaUiCommands.Status(document: first.name));
                Expect<ArgumentException>(() => SlopArenaUiCommands.Status(childOnly, first.name));
                Check(SlopArenaUiCommands.Status(childOnly).Element.Document == second.name,
                    "A nested document element was attributed to its parent document.");
                Check(!EditorApplication.isPlaying && EditorApplication.isPaused == originalPaused
                    && SceneManager.GetActiveScene() == originalScene,
                    "Named-layout inspection changed Editor mode or the active scene.");
                return new
                {
                    success = true,
                    regression = "named-ui-layout",
                    scrollWorldDelta = clipped.WorldBounds.Y - revealed.WorldBounds.Y,
                    localLayoutUnchanged = revealed.LayoutBounds.Y == clipped.LayoutBounds.Y,
                    hiddenAncestorRejected = !hiddenResult.AncestorVisible,
                    nestedDocumentOwnership = true,
                    preservedEditorMode = true,
                    preservedActiveScene = true
                };
            }
            finally
            {
                window.Close();
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(settings);
                if (originalFocus != null) originalFocus.Focus();
            }
        }

        private static UIDocument CreateDocument(
            Scene scene, PanelSettings settings, EditorWindow window, string name)
        {
            var host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(host, scene);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            var root = document.rootVisualElement;
            if (root == null) throw new InvalidOperationException("Fixture UIDocument did not create its root.");
            root.RemoveFromHierarchy();
            window.rootVisualElement.Add(root);
            return document;
        }

        private static async Task WaitForLayout(Func<bool> complete)
        {
            var completion = new TaskCompletionSource<bool>();
            double deadline = EditorApplication.timeSinceStartup + 3d;
            int updates = 0;
            void Observe()
            {
                try
                {
                    if (++updates >= 2 && complete()) completion.TrySetResult(true);
                    else if (EditorApplication.timeSinceStartup >= deadline)
                        completion.TrySetException(new InvalidOperationException("Fixture UI layout did not settle."));
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            }
            EditorApplication.update += Observe;
            try { await completion.Task; }
            finally { EditorApplication.update -= Observe; }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + " from the invalid selector.");
        }
    }

    public sealed class SlopArenaUiLayoutTestWindow : EditorWindow { }
}
