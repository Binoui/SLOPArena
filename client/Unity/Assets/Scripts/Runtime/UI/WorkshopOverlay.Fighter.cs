#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private LocalFighterWorkspace? pendingSource;
        private string? cookedStamp;
        private double stableSince;
        private bool polling;
        private int workspaceGeneration;
        private IVisualElementScheduledItem? workspacePoll;
        private LocalFighterItemResult? workspaceItem;
        private LocalFighterWorkspace? measuredSource;
        private Button? workspaceOffset;
        private Label? workspaceDiagnostics;
        private Button? workspaceTry;
        private void StopWorkspace()
        {
            workspacePoll?.Pause(); workspacePoll = null;
            workspaceGeneration++;
            workspaceFolder = null; pendingSource = null; cookedStamp = null; workspaceItem = null;
        }
        public void OpenFighterWorkspace(string folder)
        {
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
            var path = Field("Fighter source folder", "");
            Button(_body, "USE THIS FOLDER", () => SelectWorkspace(path.value));
            foreach (string folder in Directory.GetDirectories(root))
                Button(_body, Path.GetFileName(folder), () => { path.value = folder; SelectWorkspace(folder); });
            Button(_body, "OPEN SOURCE FOLDER", () =>
            {
                if (workspaceFolder != null) Application.OpenURL(new Uri(workspaceFolder + Path.DirectorySeparatorChar).AbsoluteUri);
            });
            Button(_body, "COOK AGAIN", () => { cookedStamp = null; pendingSource = null; workspaceItem = null; workspaceTry?.SetEnabled(false); });
            workspaceOffset = Button(_body, "COOK TO MEASURE IDLE SOLE", () => Run(async () =>
            {
                if (measuredSource == null || workspaceFolder == null) return;
                var source = measuredSource; string folder = workspaceFolder;
                await Task.Run(() => source.ApplyModelYOffset(folder));
                cookedStamp = null; workspaceItem = null; workspaceTry?.SetEnabled(false);
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
                workspaceFolder = Path.GetFullPath(folder);
                pendingSource = null; cookedStamp = null; workspaceItem = null;
                workspaceTry?.SetEnabled(false); workspaceOffset?.SetEnabled(false); measuredSource = null;
                Status("Watching " + workspaceFolder);
                PollWorkspace();
            }
            catch (Exception e) { Status(e.Message); }
        }
        private async void PollWorkspace()
        {
            if (polling || _busy || workspaceFolder == null) return;
            polling = true;
            string folder = workspaceFolder;
            int generation = workspaceGeneration;
            try
            {
                var snapshot = await Task.Run(() => LocalFighterWorkspace.Read(folder));
                if (!IsOpen || generation != workspaceGeneration || folder != workspaceFolder) return;
                if (pendingSource?.Stamp != snapshot.Stamp)
                {
                    pendingSource = snapshot; stableSince = Time.realtimeSinceStartupAsDouble;
                    workspaceItem = null; workspaceTry!.SetEnabled(false);
                    Status("Files changed. Waiting for saves to finish..."); return;
                }
                if (snapshot.Stamp == cookedStamp || Time.realtimeSinceStartupAsDouble - stableSince < .75) return;
                Status("Cooking fighter. You can keep using the menu...");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = await Task.Run(() =>
                {
                    var cooked = snapshot.Cook();
                    if (cooked.Item == null) return (Admission: (LocalFighterItemResult?)null, cooked.Diagnostics);
                    string output = Path.Combine(_library.Root, "FighterCooks", WorkshopItemManifestCodec.HashPayload(System.Text.Encoding.UTF8.GetBytes(snapshot.Stamp)));
                    Directory.CreateDirectory(output);
                    foreach (var file in cooked.Item) File.WriteAllBytes(Path.Combine(output, file.Key), file.Value);
                    var admitted = LocalFighterItems.Read(output);
                    return (Admission: (LocalFighterItemResult?)admitted, Diagnostics: cooked.Diagnostics.Concat(admitted.Diagnostics).ToArray() as System.Collections.Generic.IReadOnlyList<CharacterDiagnostic>);
                });
                var after = await Task.Run(() => LocalFighterWorkspace.Read(folder));
                if (!IsOpen || generation != workspaceGeneration || folder != workspaceFolder) return;
                if (after.Stamp != snapshot.Stamp) { pendingSource = after; stableSince = Time.realtimeSinceStartupAsDouble; return; }
                cookedStamp = snapshot.Stamp; workspaceItem = result.Admission;
                measuredSource = snapshot;
                workspaceOffset!.SetEnabled(snapshot.SuggestedModelYOffset.HasValue);
                workspaceOffset.text = snapshot.SuggestedModelYOffset.HasValue ? $"APPLY SUGGESTED modelYOffset {snapshot.SuggestedModelYOffset.Value:0.0000} (idle sole)" : "No sole suggestion available";
                var diagnostics = result.Diagnostics.OrderBy(d => d.Severity == CharacterDiagnosticSeverity.Error ? 0 : 1)
                    .ThenBy(d => LocalFighterWorkspace.DiagnosticPath(d), StringComparer.Ordinal)
                    .GroupBy(d => (d.Severity, File: DiagnosticFile(d)))
                    .Select(group => group.Key.File + " / " + group.Key.Severity + "\n" + string.Join("\n", group.Select(LocalFighterWorkspace.DiagnosticText)));
                workspaceDiagnostics!.text = string.Join("\n\n", diagnostics);
                bool accepted = workspaceItem?.Fighter != null;
                if (workspaceDiagnostics.text.Length == 0) workspaceDiagnostics.text = accepted ? "Fighter admitted. Ready for Training." : "Cook refused.";
                workspaceTry!.SetEnabled(accepted);
                Status($"{(accepted ? "Admitted" : "Needs fixes")} / {watch.Elapsed.TotalMilliseconds:0} ms");
            }
            catch (Exception e)
            {
                if (IsOpen && generation == workspaceGeneration && folder == workspaceFolder)
                { workspaceItem = null; workspaceTry?.SetEnabled(false); workspaceDiagnostics!.text = "Source folder / " + e.Message; }
            }
            finally { polling = false; }
        }
        private static string DiagnosticFile(CharacterDiagnostic diagnostic)
        {
            string path = LocalFighterWorkspace.DiagnosticPath(diagnostic);
            foreach (string file in new[] { "package.json", "character.json", "cook.json", "card.json" })
                if (path.StartsWith(file, StringComparison.Ordinal)) return file;
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
            var lease = await MadeFighterPresentation.PrepareAsync(entry, admitted.Item, _cancel.Token);
            if (!IsOpen) { lease.Dispose(); return; }
            MatchConfig.Reset(); MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(choice);
            FighterWorkspaceTraining.Begin(library, lease, folder);
            Close(); SceneManager.LoadScene("Arena_Offline");
        }
    }
}
