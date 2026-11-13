#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using Steamworks;
using SlopArena.Client.Workshop;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed partial class WorkshopOverlay
    {
        private string? workspaceFolder;
        private string? cookedStamp;
        private FighterWorkspaceSchedule cookSchedule = new();
        private string? workspaceCookFolder;
        private double nextProbe;
        private CancellationTokenSource? workspaceCancel;
        private bool polling;
        private int workspaceGeneration;
        private FileSystemWatcher? workspaceWatcher;
        private long workspaceRevision;
        private long observedRevision = -1;
        private IVisualElementScheduledItem? workspacePoll;
        private LocalFighterItemResult? workspaceItem;
        private LocalFighterWorkspace? measuredSource;
        private Button? workspaceOffset;
        private Label? workspaceDiagnostics;
        private Button? workspaceTry;
        private TextField? workspacePath;
        private void StopWorkspace()
        {
            workspaceCancel?.Cancel(); workspaceCancel?.Dispose(); workspaceCancel = null;
            workspacePoll?.Pause(); workspacePoll = null;
            workspaceWatcher?.Dispose(); workspaceWatcher = null;
            workspaceGeneration++;
            cookSchedule = new FighterWorkspaceSchedule(); workspaceCookFolder = null;
            workspaceFolder = null; cookedStamp = null; workspaceItem = null;
        }
        public async void OpenFighterWorkspace(string folder)
        {
            while (IsOpen && _busy) await Task.Yield();
            if (!IsOpen) return;
            FighterWorkspace(); SelectWorkspace(folder);
        }
        private void FighterWorkspace()
        {
            if (_busy) return;
            BeginView(View.Fighter);
            string root = Path.Combine(_library.Root, "Fighters");
            Directory.CreateDirectory(root);
            _body.Add(Text("Put package.json, character.json, card.json, cook.json and your GLB in one folder. Package ID must start with local."));
            Button(_body, "OPEN WORKSPACES FOLDER", () => Application.OpenURL(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri));
            var path = Field("Fighter source folder", ""); workspacePath = path;
            Button(_body, "USE THIS FOLDER", () => SelectWorkspace(path.value));
            foreach (string folder in Directory.GetDirectories(root))
                Button(_body, Path.GetFileName(folder), () => { path.value = folder; SelectWorkspace(folder); });
            Button(_body, "OPEN SOURCE FOLDER", () =>
            {
                if (workspaceFolder != null) Application.OpenURL(new Uri(workspaceFolder + Path.DirectorySeparatorChar).AbsoluteUri);
            });
            Button(_body, "COOK AGAIN", () => { cookSchedule.Request(); workspaceItem = null; workspaceTry?.SetEnabled(false); });
            workspaceOffset = Button(_body, "COOK TO MEASURE IDLE SOLE", () => Run(async () =>
            {
                if (measuredSource == null || workspaceFolder == null) return;
                var source = measuredSource; string folder = workspaceFolder;
                await Task.Run(() => source.ApplyModelYOffset(folder));
                cookSchedule.Request(); workspaceItem = null; workspaceTry?.SetEnabled(false);
            }));
            workspaceOffset.SetEnabled(false);
            workspaceDiagnostics = Text("Choose a folder. Saves cook automatically after files settle.");
            workspaceDiagnostics.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(workspaceDiagnostics);
            workspaceTry = Button(_body, "TRY IN TRAINING", () => Run(StartWorkspaceTraining));
            workspaceTry.SetEnabled(false);
            Button(_body, "PUBLISH LATEST COOK", () => Run(FighterPublishView));
            int generation = workspaceGeneration;
            workspacePoll = _root.schedule.Execute(() => { if (generation == workspaceGeneration && IsOpen && _view == View.Fighter) PollWorkspace(); }).Every(250);
            var installedStatus = Text(string.Join("\n", InstalledFighters.StatusRows));
            installedStatus.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(installedStatus);
            installedStatus.schedule.Execute(() => installedStatus.text = string.Join("\n", InstalledFighters.StatusRows)).Every(500);
            FocusBody();
        }
        private void SelectWorkspace(string folder)
        {
            try
            {
                workspaceCancel?.Cancel(); workspaceCancel?.Dispose(); workspaceCancel = new CancellationTokenSource();
                cookSchedule = new FighterWorkspaceSchedule(); nextProbe = 0;
                workspaceWatcher?.Dispose(); workspaceWatcher = null;
                workspaceItem = null; workspaceFolder = null; workspaceTry?.SetEnabled(false); workspaceOffset?.SetEnabled(false); measuredSource = null;
                workspaceFolder = Path.GetFullPath(folder);
                if (workspacePath != null) workspacePath.value = workspaceFolder;
                cookedStamp = null; workspaceItem = null;
                workspaceTry?.SetEnabled(false); workspaceOffset?.SetEnabled(false); measuredSource = null;
                observedRevision = -1;
                try
                {
                workspaceWatcher = new FileSystemWatcher(workspaceFolder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                workspaceWatcher.Changed += (_, _) => Interlocked.Increment(ref workspaceRevision);
                workspaceWatcher.Created += (_, _) => Interlocked.Increment(ref workspaceRevision);
                workspaceWatcher.Deleted += (_, _) => Interlocked.Increment(ref workspaceRevision);
                workspaceWatcher.Renamed += (_, _) => Interlocked.Increment(ref workspaceRevision);
                workspaceWatcher.Error += (_, _) => Interlocked.Increment(ref workspaceRevision);
                workspaceWatcher.EnableRaisingEvents = true;
                }
                catch (Exception) { workspaceWatcher?.Dispose(); workspaceWatcher = null; }

                Status("Watching " + workspaceFolder);
                PollWorkspace();
            }
            catch (Exception e) { workspaceDiagnostics!.text = "Error workspace.path " + folder + "/cook.json: " + e.Message; Status("Needs fixes"); }
        }
        private async void PollWorkspace()
        {
            if (polling || _busy || workspaceFolder == null) return;
            long revision = Interlocked.Read(ref workspaceRevision);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextProbe && revision == observedRevision && cookSchedule.Requested == cookSchedule.Handled) return;
            nextProbe = now + 1; observedRevision = revision;
            polling = true;
            string folder = workspaceFolder;
            int generation = workspaceGeneration;
            var cancel = workspaceCancel!.Token;
            LocalFighterWorkspace? snapshot = null;
            string probe = "";
            try
            {
                probe = await Task.Run(() => LocalFighterWorkspace.Probe(folder), cancel);
                if (!IsOpen || generation != workspaceGeneration || folder != workspaceFolder || cancel.IsCancellationRequested) return;
                if (cookSchedule.Pending != probe)
                {
                    cookSchedule.Observe(probe, Time.realtimeSinceStartupAsDouble);
                    workspaceItem = null; workspaceTry!.SetEnabled(false); workspaceOffset?.SetEnabled(false);
                    Status("Files changed. Waiting for saves to finish..."); return;
                }
                if (!cookSchedule.Observe(probe, Time.realtimeSinceStartupAsDouble)) return;
                cookSchedule.Begin(probe);
                snapshot = await Task.Run(() => LocalFighterWorkspace.Read(folder), cancel);
                Status("Cooking fighter. You can keep using the menu...");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = await Task.Run(() => FighterWorkspaceCache.Cook(Path.Combine(_library.Root, "FighterCooks"), folder, snapshot, cancel), cancel);
                string after = await Task.Run(() => LocalFighterWorkspace.Probe(folder), cancel);
                if (!IsOpen || generation != workspaceGeneration || folder != workspaceFolder || cancel.IsCancellationRequested) return;
                if (after != probe) { cookSchedule.Observe(after, Time.realtimeSinceStartupAsDouble); return; }
                cookedStamp = snapshot.Stamp; workspaceItem = result.Admission; workspaceCookFolder = result.Folder;
                measuredSource = snapshot;
                workspaceOffset!.SetEnabled(snapshot.SuggestedModelYOffset.HasValue);
                workspaceOffset.text = snapshot.SuggestedModelYOffset.HasValue ? $"APPLY SUGGESTED modelYOffset {snapshot.SuggestedModelYOffset.Value:0.0000} (idle sole)" : "No sole suggestion available";
                var diagnostics = result.Diagnostics.OrderBy(d => d.Severity == CharacterDiagnosticSeverity.Error ? 0 : 1)
                    .ThenBy(d => LocalFighterWorkspace.DiagnosticPath(d), StringComparer.Ordinal)
                    .GroupBy(d => (d.Severity, File: DiagnosticFile(d)))
                    .Select(group => group.Key.File + " / " + group.Key.Severity + "\n" + string.Join("\n", group.Select(d => LocalFighterWorkspace.DiagnosticText(d) + (d.Code == "value.out-of-range" && d.Message == "Expected byte." ? "\nUse a whole number from 0 to 255." : ""))));
                workspaceDiagnostics!.text = string.Join("\n\n", diagnostics);
                bool accepted = workspaceItem?.Fighter != null;
                if (workspaceDiagnostics.text.Length == 0) workspaceDiagnostics.text = accepted ? "Fighter admitted. Ready for Training." : "Cook refused.";
                workspaceTry!.SetEnabled(accepted);
                Status($"{(accepted ? "Admitted" : "Needs fixes")} / {watch.Elapsed.TotalMilliseconds:0} ms");
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (IsOpen && generation == workspaceGeneration && folder == workspaceFolder)
                {
                    cookedStamp = snapshot?.Stamp;
                    workspaceItem = null; workspaceTry?.SetEnabled(false);
                    workspaceDiagnostics!.text = "Error workspace.cook " + Path.Combine(_library.Root, "FighterCooks") + ": " + e.Message + " Press Cook again after fixing the problem.";
                    Status("Needs fixes");
                }
            }
            finally { polling = false; }
        }
        private static string DiagnosticFile(CharacterDiagnostic diagnostic)
        {
            string path = LocalFighterWorkspace.DiagnosticPath(diagnostic);
            foreach (string file in new[] { "package.json", "character.json", "cook.json", "card.json" })
                if (path.StartsWith(file, StringComparison.Ordinal)) return file;
            int glb = path.IndexOf(".glb", StringComparison.Ordinal);
            if (glb >= 0) return path.Substring(0, glb + 4);
            return path == "portable" || diagnostic.Code.StartsWith("glb.", StringComparison.Ordinal) || diagnostic.Code.StartsWith("item.pose-bake.", StringComparison.Ordinal) ? "GLB / poses.bin" : path;
        }
        private static byte[] FighterPreview(Texture2D portrait)
        {
            var target = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(portrait, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                image.Apply();
                return image.EncodeToJPG(85);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(image); }
        }
        private async Task FighterPublishView()
        {
            if (!SteamReady) throw new InvalidOperationException(SteamWorkshopClient.Unavailable);
            var admitted = workspaceItem;
            string sourceFolder = workspaceFolder ?? throw new InvalidOperationException("Choose a workspace first.");
            if (admitted?.Fighter == null || admitted.Item == null) throw new InvalidOperationException("Wait for an admitted cook before publishing.");
            var source = await Task.Run(() => LocalFighterWorkspace.Read(sourceFolder));
            if (source.Stamp != cookedStamp) throw new InvalidOperationException("Files changed. Wait for the next cook.");
            string statePath = Path.Combine(_library.Root, WorkshopLibrary.ItemKey(sourceFolder) + ".fighter-publish.json");
            var settings = await Task.Run(() => WorkshopPublishSettings.Read(statePath, admitted.Fighter.Choose().Revision.PackageId));
            WorkshopSteamItem? current = settings.Id == 0 ? null : await _steam.GetItem(settings.Id);
            if (current != null) WorkshopPublishEdits.RequireOwner(current.Owner, SteamUser.GetSteamID().m_SteamID);
            string beforeTitle = current?.Title ?? settings.Title, beforeDescription = current?.Description ?? settings.Description;
            int beforeVisibility = current?.Visibility ?? settings.Visibility;
            var library = LocalFighterLibrary.Current.WithLocalItem(admitted.Fighter);
            library.TryGetCatalog(out var catalog, out _);
            using var lease = await MadeFighterPresentation.PrepareAsync(catalog!.ResolvePackage(admitted.Fighter.Choose().Revision.PackageId)!, admitted.Item, _cancel.Token);
            var portrait = lease.Presentation.Art.Portrait() ?? throw new InvalidOperationException("A portrait is required for publishing.");
            byte[] previewBytes = FighterPreview(portrait);
            string preview = Path.Combine(_library.Root, WorkshopLibrary.ItemKey(sourceFolder) + ".fighter-preview.jpg");
            await Task.Run(() => File.WriteAllBytes(preview, previewBytes));
            if (!IsOpen) return;
            ClearBody();
            var title = Field("Title", beforeTitle); var description = Field("Description", beforeDescription);
            var visibility = Choice("Visibility", new System.Collections.Generic.List<string> { "Public", "Friends only", "Private", "Unlisted" }, beforeVisibility);
            _body.Add(Text("Credit your sources, respect creators, and mark AI-assisted work. Rights claims follow Steam's takedown process."));
            var guidelines = new Toggle("I have read the content guidelines"); _body.Add(guidelines);
            Button(_body, settings.Id == 0 ? "PUBLISH FIGHTER" : "UPDATE FIGHTER " + settings.Id, () => Run(async () =>
            {
                if (!guidelines.value) throw new InvalidOperationException("Read and accept the content guidelines before publishing.");
                var latestSource = await Task.Run(() => LocalFighterWorkspace.Read(sourceFolder));
                if (latestSource.Stamp != source.Stamp) throw new InvalidOperationException("Files changed. Return to Make a fighter and publish the latest cook.");
                var saved = await Task.Run(() => WorkshopPublishSettings.Read(statePath, beforeTitle));
                if (saved.Id != settings.Id) throw new InvalidOperationException("Published ID changed. Reopen publishing before updating.");
                var edits = settings.Id == 0 ? new WorkshopPublishEdits(title.value, description.value, visibility.Index)
                    : WorkshopPublishEdits.Changed(beforeTitle, beforeDescription, beforeVisibility, title.value, description.value, visibility.Index);
                settings.Title = title.value; settings.Description = description.value; settings.Visibility = visibility.Index;
                await Task.Run(() => File.WriteAllText(statePath, JsonSerializer.Serialize(settings)));
                ulong id = await _steam.PublishFighter(settings.Id, sourceFolder, admitted.Item, preview, edits,
                    created => { settings.Id = created; File.WriteAllText(statePath, JsonSerializer.Serialize(settings)); }, SteamWorkshopClient.OpenPage);
                Status("Published fighter " + id + ". The workspace keeps its local identity.");
            }));
            Button(_body, "BACK TO WORKSPACE", () => { FighterWorkspace(); SelectWorkspace(sourceFolder); });
        }
        private async Task StartWorkspaceTraining()
        {
            var admitted = workspaceItem;
            string folder = workspaceFolder!;
            if (admitted?.Fighter == null || admitted.Item == null) throw new InvalidOperationException("Fix the cook diagnostics before starting Training.");
            var snapshot = await Task.Run(() => LocalFighterWorkspace.Read(folder));
            if (snapshot.Stamp != cookedStamp) throw new InvalidOperationException("Files changed. Wait for the next cook before Training.");
            var library = LocalFighterLibrary.Current.WithLocalItem(admitted.Fighter);
            var choice = admitted.Fighter.Choose();
            var cpu = library.ChooseTrainingCpu();
            if (!library.TryPrepare(new[] { choice, cpu }, out var catalog, out var failure)) throw new InvalidOperationException(failure);
            var entry = catalog!.ResolvePackage(choice.Revision.PackageId)!;
            var diskLease = FighterWorkspaceCache.Pin(workspaceCookFolder!);
            MadeFighterPresentation.Lease lease;
            try { lease = await MadeFighterPresentation.PrepareAsync(entry, admitted.Item, _cancel.Token); }
            catch { diskLease.Dispose(); throw; }
            if (!IsOpen) { lease.Dispose(); diskLease.Dispose(); return; }
            MatchConfig.Reset(); MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(choice);
            MatchConfig.Seat(2).SetFighter(cpu);
            FighterWorkspaceTraining.Begin(library, lease, folder, diskLease);
            Close(); SceneManager.LoadScene("Arena_Offline");
        }
    }
}
