#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Client.Network;
using SlopArena.Client.Workshop;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed class WorkshopOverlay
    {
        public sealed class PublishSettings
        {
            public ulong Id { get; set; }
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public int Visibility { get; set; } = 2;
        }
        private readonly VisualElement _root;
        private readonly ScrollView _body;
        private readonly Label _status;
        private readonly FrontendPageContext _context;
        private readonly WorkshopLibrary _library = ModelSwapLibrary.Library;
        private readonly SteamWorkshopClient _steam = new();
        private readonly CancellationTokenSource _cancel = new();
        private bool _busy;
        private bool SteamReady => ChatSession.Instance?.SteamAvailable == true && _steam.Available;
        public bool IsOpen => _root.parent != null;

        public WorkshopOverlay(VisualElement host, FrontendPageContext context)
        {
            _context = context;
            _root = new VisualElement { name = "workshop-screen" };
            _root.style.position = Position.Absolute;
            _root.style.left = _root.style.right = 35;
            _root.style.top = _root.style.bottom = 35;
            _root.style.backgroundColor = new Color(.07f, .08f, .11f);
            _root.style.paddingLeft = _root.style.paddingRight = 20;
            _root.style.paddingTop = _root.style.paddingBottom = 12;
            host.Add(_root);
            _root.BringToFront();
            UiModalState.Push();
            context.SetModalAction(Close);
            var title = new Label("WORKSHOP / MODEL SWAPS");
            title.style.fontSize = 28;
            _root.Add(title);
            var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row;
            _root.Add(tabs);
            Button first = Button(tabs, "LOCAL LIBRARY", Local);
            Button(tabs, "IMPORT FROM DISK", Import);
            Button(tabs, "MY ITEMS", () => Run(() => SteamList(true)));
            Button(tabs, "SUBSCRIBED", () => Run(() => SteamList(false)));
            Button(tabs, "STEAM PAGE", () => { if (SteamReady) SteamWorkshopClient.OpenPage(); else Status(SteamWorkshopClient.Unavailable); });
            Button(tabs, "CLOSE", Close);
            _status = new Label(); _status.style.whiteSpace = WhiteSpace.Normal; _root.Add(_status);
            _body = new ScrollView(); _body.style.flexGrow = 1; _root.Add(_body);
            Local();
            _root.schedule.Execute(() => first.Focus());
            _root.schedule.Execute(() => { if (_busy && _steam.UploadHandle != UGCUpdateHandle_t.Invalid) Status(_steam.Progress()); }).Every(200);
        }
        public void Close()
        {
            if (_busy) { Status("Wait for the current operation to finish."); return; }
            if (!IsOpen) return;
            _cancel.Cancel(); _keyboard?.Dispose();
            _root.RemoveFromHierarchy(); UiModalState.Pop(); _context.SetModalAction(null);
            FrontendController.FocusRouter?.NotifyModalClosed();
        }
        private static Button Button(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.minHeight = 38; button.style.marginBottom = 5; parent.Add(button); return button;
        }
        private TextField Field(string label, string value)
        {
            var field = new TextField(label) { value = value }; field.style.minHeight = 32; _body.Add(field);
            Button(_body, "EDIT " + label.ToUpperInvariant() + " WITH CONTROLLER", () => Keyboard(field));
            return field;
        }
        private Callback<GamepadTextInputDismissed_t>? _keyboard;
        private void Keyboard(TextField field)
        {
            field.Focus();
            if (ChatSession.Instance?.SteamAvailable != true) { Status("Use the Steam Deck keyboard (Steam + X) or a keyboard to edit this field."); return; }
            _keyboard?.Dispose();
            _keyboard = Callback<GamepadTextInputDismissed_t>.Create(result =>
            {
                if (result.m_bSubmitted && SteamUtils.GetEnteredGamepadTextInput(out string text, result.m_unSubmittedText + 1)) field.value = text;
                _keyboard?.Dispose(); _keyboard = null;
            });
            if (!SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, field.label, 1024, field.value))
                Status("Open the Steam Deck keyboard with Steam + X.");
        }
        private void Status(string text) { _status.text = text; Debug.Log("[Workshop] " + text); }
        private async void Run(Func<Task> action)
        {
            if (_busy) return;
            _busy = true; _body.SetEnabled(false);
            try { await action(); }
            catch (Exception e) { Status(e.Message); Debug.LogException(e); }
            finally { _busy = false; _body.SetEnabled(true); }
        }
        private void Local()
        {
            if (_busy) return;
            _body.Clear(); Status(SteamReady ? "Local models keep the base fighter's moves and hitboxes." : SteamWorkshopClient.Unavailable);
            Button(_body, "TAKE OFF PLAYER 1 MODEL", () => { ModelSwapLibrary.Wear(1, null); Status("Player 1 wears the built-in model."); });
            var folders = Directory.GetDirectories(_library.ItemsFolder);
            if (folders.Length == 0) _body.Add(new Label("Your library is empty. Import a rigged GLB to start."));
            foreach (string folder in folders)
            {
                try
                {
                    var item = WorkshopLibrary.Verify(folder);
                    var state = Settings(item);
                    Button(_body, (state.Title.Length == 0 ? Path.GetFileName(folder) : state.Title) + " / " + item.Manifest.BaseFighterPackageId, () => Details(item));
                }
                catch (Exception e) { _body.Add(new Label(Path.GetFileName(folder) + " / Refused / " + e.Message)); }
            }
        }
        private void Import()
        {
            if (_busy) return;
            _body.Clear(); Status("Put a rigged .glb in " + _library.ImportFolder + ". Bone maps may sit beside it as humanoid.json or <name>.humanoid.json.");
            Button(_body, "OPEN IMPORT FOLDER", () => Application.OpenURL(new Uri(_library.ImportFolder + Path.DirectorySeparatorChar).AbsoluteUri));
            var path = Field("GLB path", "");
            foreach (string file in Directory.GetFiles(_library.ImportFolder, "*.glb"))
                Button(_body, Path.GetFileName(file), () => path.value = file);
            var bases = MenuRoster.Classes.Where(c => ModelSwapBases.Accepts(MenuRoster.PackageId(c) ?? "", out _)).ToArray();
            var fighter = new DropdownField("Base fighter", bases.Select(MenuRoster.Name).ToList(), 0); _body.Add(fighter);
            var title = Field("Title", "");
            var creator = Field("Creator", ChatSession.Instance?.SteamAvailable == true ? SteamFriends.GetPersonaName() : "Creator");
            var license = Field("License", "All rights reserved");
            var attribution = Field("Attribution", "");
            var ai = new Toggle("AI assisted"); _body.Add(ai);
            Button(_body, "IMPORT AND CHECK", () => Run(async () =>
            {
                if (bases.Length == 0) throw new InvalidOperationException("No admitted fighter accepts model swaps.");
                if (!path.value.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a .glb file.");
                string source = Path.GetFullPath(path.value);
                string map = Path.ChangeExtension(source, ".humanoid.json");
                if (!File.Exists(map)) map = Path.Combine(Path.GetDirectoryName(source)!, "humanoid.json");
                byte[] bytes = await Task.Run(() => WorkshopLibrary.ReadCapped(source, SlopArena.Shared.WorkshopGlbLimits.Default.MaxFileBytes));
                string? boneMap = File.Exists(map) ? System.Text.Encoding.UTF8.GetString(WorkshopLibrary.ReadCapped(map, 65536)) : null;
                using var model = await Load(bytes, boneMap);
                var item = _library.Save(bytes, boneMap, MenuRoster.PackageId(bases[fighter.index])!, creator.value, license.value, attribution.value, ai.value);
                var settings = new PublishSettings { Title = string.IsNullOrWhiteSpace(title.value) ? Path.GetFileNameWithoutExtension(source) : title.value };
                SaveSettings(item, settings);
                RenderPreview(model, PreviewPath(item));
                Details(item); Status("Import accepted. Ready to preview or wear.");
            }));
        }
        private static async Task<ImportedSwapModel> Load(byte[] bytes, string? map)
        {
            var result = await ModelSwapImporter.ImportAsync(bytes, map, new ModelSwapTarget(1, Color.white, AnimatorCullingMode.AlwaysAnimate), CancellationToken.None);
            if (result.Model == null) throw new InvalidDataException(result.Refusal!.ToString());
            return result.Model;
        }
        private string PreviewPath(WorkshopLibraryItem item) => Path.Combine(_library.Root, Path.GetFileName(item.Folder) + ".png");
        private static void RenderPreview(ImportedSwapModel model, string path)
        {
            var root = model.Root!; root.transform.position = new Vector3(10000, 10000, 10000);
            model.Show();
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidDataException("No visible mesh for preview.");
            var bounds = renderers[0].bounds; foreach (var r in renderers) { bounds.Encapsulate(r.bounds); r.gameObject.layer = 30; }
            var cameraObject = new GameObject("Workshop portrait camera"); var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(512, 512, 24); var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .14f, .19f);
                camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.25f;
                camera.transform.position = bounds.center + new Vector3(0, .1f, -5); camera.transform.LookAt(bounds.center);
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(cameraObject); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image); }
        }
        private PublishSettings Settings(WorkshopLibraryItem item) => File.Exists(_library.StatePath(item))
            ? JsonSerializer.Deserialize<PublishSettings>(File.ReadAllText(_library.StatePath(item)))! : new PublishSettings { Title = Path.GetFileName(item.Folder) };
        private void SaveSettings(WorkshopLibraryItem item, PublishSettings settings) => File.WriteAllText(_library.StatePath(item), JsonSerializer.Serialize(settings));
        private void Details(WorkshopLibraryItem item)
        {
            _body.Clear();
            _body.Add(new Label("MODEL SWAP / " + item.Manifest.BaseFighterPackageId));
            var settings = Settings(item);
            var title = Field("Title", settings.Title); var description = Field("Description", settings.Description);
            var visibility = new DropdownField("Visibility", new System.Collections.Generic.List<string> { "Public", "Friends only", "Private", "Unlisted" }, settings.Visibility); _body.Add(visibility);
            Button(_body, "WEAR ON PLAYER 1", () => { ModelSwapLibrary.Wear(1, item); Status("Player 1 will wear this model on " + item.Manifest.BaseFighterPackageId + ". Saved for next launch."); });
            Button(_body, "PREVIEW IN TRAINING", () =>
            {
                if (_busy) return;
                Close(); MatchConfig.Reset(); MatchConfig.Mode = GameMode.Training;
                MatchConfig.PlayerPackageId = item.Manifest.BaseFighterPackageId;
                MatchConfig.PlayerClass = MenuRoster.Classes.Single(c => MenuRoster.PackageId(c) == item.Manifest.BaseFighterPackageId);
                MatchConfig.ModelSwaps[1] = new ModelSwapChoice(item.GlbPath, item.Manifest.BaseFighterPackageId);
                SceneManager.LoadScene("Arena_Offline");
            });
            var guidelines = new Toggle("I have read the content guidelines");
            _body.Add(new Label("Credit your sources, respect creators, and mark AI-assisted work. Rights claims follow Steam's takedown process."));
            _body.Add(guidelines);
            Button(_body, settings.Id == 0 ? "PUBLISH TO STEAM" : "UPDATE STEAM ITEM " + settings.Id, () => Run(async () =>
            {
                if (!SteamReady) throw new InvalidOperationException(SteamWorkshopClient.Unavailable);
                if (!guidelines.value) throw new InvalidOperationException("Read and accept the content guidelines before publishing.");
                settings.Title = title.value; settings.Description = description.value; settings.Visibility = visibility.index; SaveSettings(item, settings);
                string preview = PreviewPath(item);
                if (!File.Exists(preview))
                {
                    using var model = await Load(WorkshopLibrary.ReadCapped(item.GlbPath, SlopArena.Shared.WorkshopGlbLimits.Default.MaxFileBytes),
                        File.Exists(Path.Combine(item.Folder, "humanoid.json")) ? File.ReadAllText(Path.Combine(item.Folder, "humanoid.json")) : null);
                    RenderPreview(model, preview);
                }
                ulong id = await _steam.Publish(settings.Id, item.Folder, preview, title.value, description.value,
                    (ERemoteStoragePublishedFileVisibility)visibility.index,
                    created => { settings.Id = created; SaveSettings(item, settings); }, SteamWorkshopClient.OpenPage);
                Status("Published item " + id + ". Updates keep this Steam item ID.");
            }));
            if (settings.Id != 0) Button(_body, "OPEN ITEM ON STEAM", () => { if (SteamReady) SteamWorkshopClient.OpenPage(settings.Id); });
        }
        private async Task SteamList(bool published)
        {
            _body.Clear();
            if (!SteamReady) { Status(SteamWorkshopClient.Unavailable); return; }
            Status("Reading Steam items...");
            var items = await _steam.List(published);
            foreach (var item in items)
            {
                _body.Add(new Label(item.Title + " / " + item.Id + " / " + item.State));
                Button(_body, "OPEN ON STEAM", () => SteamWorkshopClient.OpenPage(item.Id));
                Button(_body, "DOWNLOAD AND VERIFY", () => Run(async () =>
                {
                    var installed = await _steam.Download(item.Id);
                    using var model = await Load(WorkshopLibrary.ReadCapped(installed.GlbPath, SlopArena.Shared.WorkshopGlbLimits.Default.MaxFileBytes),
                        File.Exists(Path.Combine(installed.Folder, "humanoid.json")) ? System.Text.Encoding.UTF8.GetString(WorkshopLibrary.ReadCapped(Path.Combine(installed.Folder, "humanoid.json"), 65536)) : null);
                    _library.RememberInstalled(installed);
                    Details(installed); Status("Downloaded and verified " + item.Id + ". Ready to wear.");
                }));
            }
            Status(items.Count + " Steam items. Download to verify and wear.");
        }
    }
}
