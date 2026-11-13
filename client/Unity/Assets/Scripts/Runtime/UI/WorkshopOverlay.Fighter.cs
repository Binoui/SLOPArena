#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
            int generation = workspaceGeneration;
            workspacePoll = _root.schedule.Execute(() => { if (generation == workspaceGeneration && IsOpen && _view == View.Fighter) PollWorkspace(); }).Every(250);
            FocusBody();
        }
        private void SelectWorkspace(string folder)
        {
            try
            {
                workspaceCancel?.Cancel(); workspaceCancel?.Dispose(); workspaceCancel = new CancellationTokenSource();
                cookSchedule = new FighterWorkspaceSchedule(); nextProbe = 0;
                workspaceWatcher?.Dispose(); workspaceWatcher = null;
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
            catch (Exception e) { Status(e.Message); }
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
        private async Task StartWorkspaceTraining()
        {
            var admitted = workspaceItem;
            string folder = workspaceFolder!;
            if (admitted?.Fighter == null || admitted.Item == null) throw new InvalidOperationException("Fix the cook diagnostics before starting Training.");
            var snapshot = await Task.Run(() => LocalFighterWorkspace.Read(folder));
            if (snapshot.Stamp != cookedStamp) throw new InvalidOperationException("Files changed. Wait for the next cook before Training.");
            var library = LocalFighterLibrary.Current.WithLocalItem(admitted.Fighter);
            var choice = admitted.Fighter.Choose();
            if (!library.TryPrepare(new[] { choice, library.Choose(library.PackageIds[0]) }, out var catalog, out var failure)) throw new InvalidOperationException(failure);
            var entry = catalog!.ResolvePackage(choice.Revision.PackageId)!;
            var diskLease = FighterWorkspaceCache.Pin(workspaceCookFolder!);
            MadeFighterPresentation.Lease lease;
            try { lease = await MadeFighterPresentation.PrepareAsync(entry, admitted.Item, _cancel.Token); }
            catch { diskLease.Dispose(); throw; }
            if (!IsOpen) { lease.Dispose(); diskLease.Dispose(); return; }
            MatchConfig.Reset(); MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(choice);
            FighterWorkspaceTraining.Begin(library, lease, folder, diskLease);
            Close(); SceneManager.LoadScene("Arena_Offline");
        }
    }
}
