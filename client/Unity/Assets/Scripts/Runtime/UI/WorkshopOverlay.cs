#nullable enable
using System;
using System.Collections.Generic;
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
        private enum View { Local, Import, Published, Subscribed }
        private readonly VisualElement _root;
        private readonly ScrollView _body;
        private readonly Label _status;
        private readonly FrontendPageContext _context;
        private readonly WorkshopLibrary _library = ModelSwapLibrary.Library;
        private readonly SteamWorkshopClient _steam = SteamWorkshopClient.Shared;
        private readonly CancellationTokenSource _cancel = new();
        private readonly Dictionary<View, Button> _tabs = new();
        private readonly Dictionary<TextField, Button> _editButtons = new();
        private bool _busy;
        private bool _closed;
        private View _view;
        private bool SteamReady => ChatSession.Instance?.SteamAvailable == true && _steam.Available;
        public bool IsOpen => !_closed;
        public WorkshopOverlay(VisualElement host, FrontendPageContext context)
        {
            _context = context;
            _root = new VisualElement { name = "workshop-screen" };
            _root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Workshop"));
            _root.style.position = Position.Absolute;
            _root.style.left = _root.style.right = 35;
            _root.style.top = _root.style.bottom = 35;
            _root.style.backgroundColor = new Color(.07f, .08f, .11f);
            _root.style.color = Color.white;
            _root.style.paddingLeft = _root.style.paddingRight = 20;
            _root.style.paddingTop = _root.style.paddingBottom = 12;
            host.Add(_root); _root.BringToFront();
            UiModalState.Push(); context.SetModalAction(HandleBack);
            var title = Text("WORKSHOP / MODEL SWAPS"); title.style.fontSize = 28; _root.Add(title);
            var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; _root.Add(tabs);
            _tabs[View.Local] = Button(tabs, "LOCAL LIBRARY", Local);
            _tabs[View.Import] = Button(tabs, "IMPORT FROM DISK", Import);
            _tabs[View.Published] = Button(tabs, "MY ITEMS", () => Run(() => SteamList(true)));
            _tabs[View.Subscribed] = Button(tabs, "SUBSCRIBED", () => Run(() => SteamList(false)));
            Button(tabs, "STEAM PAGE", () => { if (SteamReady) SteamWorkshopClient.OpenPage(); else Status(SteamWorkshopClient.Unavailable); });
            Button(tabs, "CLOSE", Close);
            _status = Text(""); _root.Add(_status);
            _body = new ScrollView(); _body.style.flexGrow = 1; _root.Add(_body);
            Local();
            _root.schedule.Execute(() =>
            {
                if (IsOpen && (_busy || _steam.UploadHandle != UGCUpdateHandle_t.Invalid) && _steam.Progress().Length > 0) _status.text = _steam.Progress();
            }).Every(200);
        }
        private void HandleBack()
        {
            var panel = _root.panel;
            var popup = panel?.visualTree.Q(className: "unity-base-dropdown__container-outer");
            if (popup != null) { popup.RemoveFromHierarchy(); FocusBody(); return; }
            var focused = panel?.focusController.focusedElement as VisualElement;
            foreach (var pair in _editButtons)
                if (focused != null && pair.Key.Contains(focused)) { pair.Key.Blur(); pair.Value.Focus(); return; }
            Close();
        }
        public void Close()
        {
            if (_closed) return;
            _closed = true; _cancel.Cancel(); _keyboard?.Dispose(); _keyboard = null;
            _root.RemoveFromHierarchy(); UiModalState.Pop(); _context.SetModalAction(null);
            FrontendController.FocusRouter?.NotifyModalClosed();
        }
        private static Label Text(string value) => new(value) { enableRichText = false };
        private static Button Button(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text, enableRichText = false };
            button.style.minHeight = 38; button.style.marginBottom = 5; parent.Add(button); return button;
        }
        private void BeginView(View view)
        {
            _view = view; _body.Clear(); _editButtons.Clear();
            foreach (var tab in _tabs) tab.Value.EnableInClassList("workshop-tab--selected", tab.Key == view);
        }
        private void ClearBody() { _body.Clear(); _editButtons.Clear(); }
        private void FocusBody()
        {
            if (!IsOpen) return;
            _root.schedule.Execute(() =>
            {
                if (!IsOpen) return;
                var button = _body.Query<Button>().ToList().FirstOrDefault(b => b.enabledInHierarchy && b.resolvedStyle.display != DisplayStyle.None);
                (button ?? _tabs[_view]).Focus();
            });
        }
        private TextField Field(string label, string value)
        {
            var field = new TextField(label) { value = value }; field.labelElement.enableRichText = false;
            field.style.minHeight = 32; _body.Add(field);
            var edit = Button(_body, "EDIT " + label.ToUpperInvariant() + " WITH CONTROLLER", () => Keyboard(field));
            _editButtons[field] = edit;
            field.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (evt.direction != NavigationMoveEvent.Direction.Up && evt.direction != NavigationMoveEvent.Direction.Down) return;
                evt.StopImmediatePropagation(); field.Blur(); edit.Focus();
            }, TrickleDown.TrickleDown);
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
                if (IsOpen && result.m_bSubmitted && SteamUtils.GetEnteredGamepadTextInput(out string text, result.m_unSubmittedText + 1)) field.value = text;
                _keyboard?.Dispose(); _keyboard = null;
                if (IsOpen && _editButtons.TryGetValue(field, out var edit)) edit.Focus();
            });
            if (!SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, field.label, 1024, field.value)) Status("Open the Steam Deck keyboard with Steam + X.");
        }
        private void Status(string text)
        {
            if (!IsOpen) return;
            _status.text = text; Debug.Log("[Workshop] " + text);
        }
        private async void Run(Func<Task> action)
        {
            if (_busy || !IsOpen) return;
            _busy = true; _body.SetEnabled(false); Status("Working...");
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (IsOpen) { Status(e.Message); Debug.LogWarning("[Workshop] Refused: " + e.Message); } }
            finally { _busy = false; if (IsOpen) { _body.SetEnabled(true); FocusBody(); } }
        }
        private string BaseName(string id)
        {
            foreach (var fighter in MenuRoster.Classes) if (MenuRoster.PackageId(fighter) == id) return MenuRoster.Name(fighter);
            return id;
        }
        private void Local()
        {
            if (_busy) return;
            Run(async () =>
            {
                BeginView(View.Local); Status("Reading local library...");
                var rows = await Task.Run(() =>
                {
                    _library.EnsureDirectories();
                    var result = new List<(WorkshopLibraryItem? Item, WorkshopPublishSettings? Settings, string Error)>();
                    foreach (var folder in Directory.GetDirectories(_library.ItemsFolder))
                    {
                        try { var item = WorkshopLibrary.Verify(folder); result.Add((item, Settings(item), "")); }
                        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { result.Add((null, null, Path.GetFileName(folder) + " / Refused / " + e.Message)); }
                    }
                    return result;
                });
                await ModelSwapLibrary.Ready;
                if (!IsOpen) return;
                Button(_body, "TAKE OFF PLAYER 1 MODEL", () => Run(async () => { await ModelSwapLibrary.Wear(1, null); Status("Player 1 wears the built-in model."); }));
                if (rows.Count == 0) _body.Add(Text("Your library is empty. Import a rigged GLB to start."));
                foreach (var row in rows)
                {
                    if (row.Item == null) { _body.Add(Text(row.Error)); continue; }
                    var item = row.Item; var settings = row.Settings!;
                    bool worn = MatchConfig.ModelSwaps.TryGetValue(1, out var choice) && string.Equals(choice.GlbPath, item.GlbPath, StringComparison.OrdinalIgnoreCase);
                    string label = $"{settings.Title} / {BaseName(item.Manifest.BaseFighterPackageId)} / {item.Manifest.Creator} / {(worn ? "WORN" : "NOT WORN")} / {(settings.Id == 0 ? "LOCAL" : "PUBLISHED " + settings.Id)}";
                    Button(_body, label, () => Run(() => Details(item)));
                }
                Status((SteamReady ? "Local models keep the base fighter's moves and hitboxes." : SteamWorkshopClient.Unavailable)
                    + (_steam.LastOperation.Length > 0 ? " / " + _steam.LastOperation : ""));
            });
        }
        private void Import()
        {
            if (_busy) return;
            Run(async () =>
            {
                BeginView(View.Import);
                string[] files = await Task.Run(() => { _library.EnsureDirectories(); return Directory.GetFiles(_library.ImportFolder, "*.glb"); });
                if (!IsOpen) return;
                Status("Put a rigged .glb in " + _library.ImportFolder + ". Use <name>.humanoid.json for a bone map in this shared folder.");
                Button(_body, "OPEN IMPORT FOLDER", () => Application.OpenURL(new Uri(_library.ImportFolder + Path.DirectorySeparatorChar).AbsoluteUri));
                var path = Field("GLB path", ""); foreach (var file in files) Button(_body, Path.GetFileName(file), () => path.value = file);
                var bases = MenuRoster.Classes.Where(c => ModelSwapBases.Accepts(MenuRoster.PackageId(c) ?? "", out _)).ToArray();
                var fighter = new DropdownField("Base fighter", bases.Select(MenuRoster.Name).ToList(), bases.Length == 0 ? -1 : 0); _body.Add(fighter);
                var title = Field("Title", ""); var creator = Field("Creator", ChatSession.Instance?.SteamAvailable == true ? SteamFriends.GetPersonaName() : "Creator");
                var license = Field("License", "All rights reserved"); var attribution = Field("Attribution", ""); var ai = new Toggle("AI assisted"); _body.Add(ai);
                Button(_body, "IMPORT AND CHECK", () => Run(async () =>
                {
                    if (fighter.index < 0 || fighter.index >= bases.Length) throw new InvalidOperationException("No admitted fighter accepts model swaps.");
                    if (!path.value.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a .glb file.");
                    string source = path.value;
                    string baseId = MenuRoster.PackageId(bases[fighter.index])!;
                    string creatorValue = creator.value, licenseValue = license.value, attributionValue = attribution.value;
                    bool aiValue = ai.value;
                    var payload = await Task.Run(() => (Glb: WorkshopLibrary.ReadCapped(Path.GetFullPath(source), SlopArena.Shared.WorkshopGlbLimits.Default.MaxFileBytes), BoneMap: WorkshopLibrary.ImportBoneMap(Path.GetFullPath(source))));
                    if (!IsOpen) return;
                    using var model = await Load(payload.Glb, payload.BoneMap);
                    var item = await Task.Run(() => _library.Save(payload.Glb, payload.BoneMap, baseId, creatorValue, licenseValue, attributionValue, aiValue));
                    var settings = new WorkshopPublishSettings { Title = string.IsNullOrWhiteSpace(title.value) ? Path.GetFileNameWithoutExtension(source) : title.value };
                    await Task.Run(() => SaveSettings(item, settings));
                    if (!IsOpen) return;
                    await RenderPreview(model, PreviewPath(item));
                    await Details(item); Status("Import accepted. Ready to preview or wear.");
                }));
            });
        }
        private async Task<ImportedSwapModel> Load(byte[] bytes, string? map, bool verified = false)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            if (!verified)
            {
                var check = await Task.Run(() => SlopArena.Shared.WorkshopGlbPrecheck.Check(bytes, SlopArena.Shared.WorkshopGlbLimits.Default));
                if (!check.IsAccepted) throw new InvalidDataException(string.Join(" ", check.Diagnostics.Select(d => d.Message)));
            }
            _cancel.Token.ThrowIfCancellationRequested();
            var result = await ModelSwapImporter.ImportAsync(bytes, map, new ModelSwapTarget(1, Color.white, AnimatorCullingMode.AlwaysAnimate), _cancel.Token, filePrechecked: true);
            if (result.Model == null) throw new InvalidDataException(result.Refusal!.Message);
            return result.Model;
        }
        private string PreviewPath(WorkshopLibraryItem item) => Path.Combine(_library.Root, WorkshopLibrary.ItemKey(item.Folder) + ".png");
        private static async Task RenderPreview(ImportedSwapModel model, string path)
        {
            var root = model.Root!; root.transform.position = new Vector3(10000, 10000, 10000);
            model.Show();
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidDataException("No visible mesh for preview.");
            var bounds = renderers[0].bounds; foreach (var r in renderers) { bounds.Encapsulate(r.bounds); r.gameObject.layer = 30; }
            var cameraObject = new GameObject("Workshop portrait camera"); var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            var target = new RenderTexture(512, 512, 24); var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .14f, .19f);
                camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.25f;
                camera.transform.position = bounds.center + new Vector3(0, .1f, 5); camera.transform.LookAt(bounds.center);
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply(); byte[] encoded = image.EncodeToPNG(); await Task.Run(() => File.WriteAllBytes(path, encoded));
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(cameraObject); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image); }
        }
        private WorkshopPublishSettings Settings(WorkshopLibraryItem item) => WorkshopPublishSettings.Read(_library.StatePath(item), Path.GetFileName(item.Folder));
        private void SaveSettings(WorkshopLibraryItem item, WorkshopPublishSettings settings) => File.WriteAllText(_library.StatePath(item), JsonSerializer.Serialize(settings));
        private async Task Details(WorkshopLibraryItem item)
        {
            var settings = await Task.Run(() => Settings(item));
            WorkshopSteamItem? current = null;
            bool orphaned = false;
            if (settings.Id != 0 && SteamReady)
            {
                try { current = await _steam.GetItem(settings.Id); orphaned = current.Owner != SteamUser.GetSteamID().m_SteamID; if (orphaned) current = null; }
                catch (WorkshopItemUnavailableException) { orphaned = true; }
            }
            if (!IsOpen) return;
            ClearBody(); _body.Add(Text("MODEL SWAP / " + BaseName(item.Manifest.BaseFighterPackageId)));
            string beforeTitle = current?.Title ?? settings.Title, beforeDescription = current?.Description ?? settings.Description;
            int beforeVisibility = current?.Visibility ?? settings.Visibility;
            var title = Field("Title", beforeTitle); var description = Field("Description", beforeDescription);
            var visibility = new DropdownField("Visibility", new List<string> { "Public", "Friends only", "Private", "Unlisted" }, beforeVisibility); _body.Add(visibility);
            Button(_body, "WEAR ON PLAYER 1", () => Run(async () => { await ModelSwapLibrary.Wear(1, item); Status("Player 1 will wear this model on " + BaseName(item.Manifest.BaseFighterPackageId) + ". Saved for next launch."); }));
            Button(_body, "PREVIEW IN TRAINING", () => Run(async () =>
            {
                var matches = MenuRoster.Classes.Where(c => MenuRoster.PackageId(c) == item.Manifest.BaseFighterPackageId).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("This base fighter is missing or ambiguous in the current roster. Choose another item.");
                await ModelSwapLibrary.Ready;
                if (!IsOpen) return;
                MatchConfig.Reset(); MatchConfig.Mode = GameMode.Training;
                MatchConfig.PlayerPackageId = item.Manifest.BaseFighterPackageId; MatchConfig.PlayerClass = matches[0];
                ModelSwapLibrary.Preview(item); Close(); SceneManager.LoadScene("Arena_Offline");
            }));
            if (!_library.IsCreatorItem(item)) return;
            if (orphaned) { OfferNew(item, settings); return; }
            var guidelines = new Toggle("I have read the content guidelines");
            _body.Add(Text("Credit your sources, respect creators, and mark AI-assisted work. Rights claims follow Steam's takedown process.")); _body.Add(guidelines);
            var publish = Button(_body, settings.Id == 0 ? "PUBLISH TO STEAM" : "UPDATE STEAM ITEM " + settings.Id, () => Run(async () =>
            {
                if (!SteamReady) throw new InvalidOperationException(SteamWorkshopClient.Unavailable);
                if (!guidelines.value) throw new InvalidOperationException("Read and accept the content guidelines before publishing.");
                var latest = await Task.Run(() => Settings(item));
                if (latest.Id != settings.Id)
                {
                    await Details(item); Status("The published item changed while this view was open. Review its current fields before updating."); return;
                }
                if (!IsOpen) return;
                var edits = settings.Id == 0 ? new WorkshopPublishEdits(title.value, description.value, visibility.index)
                    : WorkshopPublishEdits.Changed(beforeTitle, beforeDescription, beforeVisibility, title.value, description.value, visibility.index);
                settings.Title = title.value; settings.Description = description.value; settings.Visibility = visibility.index;
                string statePath = await Task.Run(() => { SaveSettings(item, settings); return _library.StatePath(item); });
                string preview = PreviewPath(item);
                bool exists = await Task.Run(() => File.Exists(preview));
                if (!IsOpen) return;
                if (!exists)
                {
                    var verified = await Task.Run(() => WorkshopLibrary.Verify(item.Folder));
                    var payload = verified.Payload(item.Manifest.BaseFighterPackageId);
                    using var model = await Load(payload.Glb, payload.BoneMap, verified: true); await RenderPreview(model, preview);
                }
                try
                {
                    ulong id = await _steam.Publish(settings.Id, item.Folder, preview, edits,
                        created => { settings.Id = created; File.WriteAllText(statePath, JsonSerializer.Serialize(settings)); }, SteamWorkshopClient.OpenPage);
                    Status("Published item " + id + ". Updates keep this Steam item ID.");
                }
                catch (WorkshopItemUnavailableException) { OfferNew(item, settings); throw; }
            }));
            publish.SetEnabled(!_steam.IsPublishing(item.Folder));
            publish.schedule.Execute(() => { if (IsOpen) publish.SetEnabled(!_steam.IsPublishing(item.Folder)); }).Every(200);
            if (settings.Id != 0) Button(_body, "OPEN ITEM ON STEAM", () => { if (SteamReady) SteamWorkshopClient.OpenPage(settings.Id); });
        }
        private void OfferNew(WorkshopLibraryItem item, WorkshopPublishSettings settings)
        {
            if (!IsOpen) return;
            _body.Add(Text("This Steam item is gone or is not yours. Publish as a new item to continue."));
            Button(_body, "PUBLISH AS A NEW ITEM", () => Run(async () =>
            {
                settings.Id = 0; await Task.Run(() => SaveSettings(item, settings));
                await Details(item); Status("The old Steam ID was cleared. Review the fields and publish a new item.");
            }));
        }
        private async Task SteamList(bool published)
        {
            BeginView(published ? View.Published : View.Subscribed);
            if (!SteamReady) { Status(SteamWorkshopClient.Unavailable); return; }
            Status("Reading Steam items...");
            var items = await _steam.List(published);
            if (!IsOpen) return;
            foreach (var item in items)
            {
                _body.Add(Text(item.Title + " / " + item.Id + " / " + item.State));
                Button(_body, "OPEN ON STEAM", () => SteamWorkshopClient.OpenPage(item.Id));
                Button(_body, "DOWNLOAD AND VERIFY", () => Run(async () =>
                {
                    var installed = await _steam.Download(item.Id);
                    if (!IsOpen) return;
                    var payload = installed.Payload();
                    using var model = await Load(payload.Glb, payload.BoneMap, verified: true);
                    await Task.Run(() => _library.RememberInstalled(installed));
                    await Details(installed); Status("Downloaded and verified " + item.Id + ". Ready to wear.");
                }));
            }
            Status(items.Count + " Steam items. Download to verify and wear.");
        }
    }
}
