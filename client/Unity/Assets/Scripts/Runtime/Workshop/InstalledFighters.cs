#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Client.Network;
using SlopArena.Client.UI;
using SlopArena.Shared;
using Steamworks;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    public sealed class InstalledFighters : MonoBehaviour
    {
        sealed class Revision : IDisposable
        {
            internal string Stamp = "", Caption = "", Diagnostic = "";
            internal LocalFighterItemResult? Result;
            internal MadeFighterPresentation.Lease? Lease;
            public void Dispose() { if (Lease != null) FighterPresentationReleases.AfterRenderers(Lease); Lease = null; }
        }
        sealed class Source
        {
            internal string Key = "", Caption = "", Folder = "", HostStamp = "", Diagnostic = "";
            internal SteamWorkshopClient.FighterInstallRecord? Install;
            internal Revision? Retained;
        }
        static InstalledFighters? active;
        readonly FighterRevisionSet<Revision> revisions = new();
        readonly WorkshopDownloadRetry downloads = new();
        readonly CancellationTokenSource cancel = new();
        IReadOnlyList<string> rows = Array.Empty<string>();
        AdmittedFighter[] visible = Array.Empty<AdmittedFighter>();
        public static IReadOnlyList<string> StatusRows => active?.rows ?? Array.Empty<string>();
        bool busy;
        float next;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        { active?.Stop(); active = null; LocalFighterLibrary.ResetMadeSources(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartSession()
        {
            var owner = new GameObject("Installed fighters");
            DontDestroyOnLoad(owner); active = owner.AddComponent<InstalledFighters>();
        }
        void Stop() { cancel.Cancel(); revisions.Dispose(); }
        void OnDestroy() { Stop(); if (active == this) active = null; }
        async void Update()
        {
            if (busy || cancel.IsCancellationRequested || !Application.isPlaying || Time.unscaledTime < next) return;
            busy = true; next = Time.unscaledTime + 3;
            try { await RefreshSources(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning("[InstalledFighters] " + e.Message); }
            finally { busy = false; }
        }
        void RequireSession()
        { cancel.Token.ThrowIfCancellationRequested(); if (!Application.isPlaying || active != this) throw new OperationCanceledException(); }
        async Task RefreshSources()
        {
            var token = cancel.Token;
            string root = Path.Combine(Application.persistentDataPath, "Workshop", "FighterCooks");
            var sources = new List<Source>();
            try
            {
                var local = await Task.Run(() => FighterWorkspaceSources.Read(root), token);
                RequireSession();
                sources.AddRange(local.Select(s => new Source { Key = s.Key, Caption = s.SourceFolder ?? "Older workspace cache " + Path.GetFileName(s.Key), Folder = s.ItemFolder ?? "", Diagnostic = s.Diagnostic ?? "" }));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { sources.Add(new Source { Key = "Workspaces", Caption = root, Diagnostic = e.Message }); }
            if (ChatSession.Instance?.SteamAvailable == true && SteamWorkshopClient.Shared.Available)
            {
                try
                {
                    var installed = await SteamWorkshopClient.Shared.SubscribedFighters();
                    RequireSession();
                    foreach (var record in installed)
                    {
                        string diagnostic = record.Diagnostic;
                        if (diagnostic.Length == 0 && !record.Current)
                        {
                            if (downloads.Request(record.Id, record.Change, Time.realtimeSinceStartupAsDouble, record.Downloading)
                                && !SteamUGC.DownloadItem(new PublishedFileId_t(record.Id), false)) diagnostic = "Steam refused the download. Retrying with back-off.";
                            if (diagnostic.Length == 0) diagnostic = SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(record.Id), out ulong done, out ulong total)
                                ? $"Downloading {done} / {total} bytes" : "Waiting for Steam download. Retrying with back-off.";
                        }
                        else if (record.Current) downloads.Complete(record.Id);
                        sources.Add(new Source { Key = "Steam " + record.Id, Caption = "Steam " + record.Id, Folder = record.Current ? record.Folder : "", HostStamp = record.Change + ":" + (uint)record.State, Diagnostic = diagnostic, Install = record });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    sources.Add(new Source { Key = "Steam", Caption = "Steam subscriptions", Diagnostic = e.Message });
                    foreach (var old in revisions.Current.Where(p => p.Key.StartsWith("Steam ", StringComparison.Ordinal)))
                        sources.Add(new Source { Key = old.Key, Caption = old.Value.Caption, Retained = old.Value });
                }
            }
            await revisions.Replace(async (previous, staged) =>
            {
                foreach (var source in sources)
                {
                    RequireSession();
                    if (source.Retained != null) { staged[source.Key] = source.Retained; continue; }
                    var revision = new Revision { Caption = source.Caption, Diagnostic = source.Diagnostic };
                    try
                    {
                        revision.Stamp = source.HostStamp + (source.Folder.Length == 0 ? ":" + source.Diagnostic : await Task.Run(() => Stamp(source.Folder), token));
                        RequireSession();
                        if (previous.TryGetValue(source.Key, out var old) && old.Stamp == revision.Stamp)
                        { staged[source.Key] = old; continue; }
                        staged[source.Key] = revision;
                        if (revision.Diagnostic.Length > 0) continue;
                        if (source.Folder.Length == 0) throw new IOException("No installed fighter folder.");
                        var result = await Task.Run(() => source.Install == null ? LocalFighterItems.Read(source.Folder) : source.Install.Admit(), token);
                        RequireSession(); revision.Result = result;
                        if (result.Fighter == null || result.Item == null) throw new InvalidDataException(string.Join("; ", result.Diagnostics.Select(LocalFighterWorkspace.DiagnosticText)));
                        var library = LocalFighterLibrary.Current.WithLocalItem(result.Fighter);
                        library.TryGetCatalog(out var catalog, out _);
                        revision.Lease = await MadeFighterPresentation.PrepareAsync(catalog!.ResolvePackage(result.Fighter.Choose().Revision.PackageId)!, result.Item, token);
                        RequireSession();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { revision.Diagnostic = e.Message; staged[source.Key] = revision; }
                }
            }, staged =>
            {
                RequireSession();
                var ready = staged.Values.Where(r => r.Lease != null).ToArray();
                var duplicates = ready.GroupBy(r => r.Result!.Fighter!.Choose().Revision.PackageId).Where(g => g.Count() > 1)
                    .ToDictionary(g => g.Key, g => FighterWorkspaceSources.DuplicateDiagnostic(g.Key, g.Select(r => r.Caption)));
                var nextVisible = ready.Where(r => !duplicates.ContainsKey(r.Result!.Fighter!.Choose().Revision.PackageId)).Select(r => r.Result!.Fighter!).ToArray();
                if (!visible.SequenceEqual(nextVisible)) { LocalFighterLibrary.SetMadeFighters(nextVisible); visible = nextVisible; }
                rows = staged.Values.Select(r => r.Caption + " / " + (r.Lease != null && duplicates.TryGetValue(r.Result!.Fighter!.Choose().Revision.PackageId, out string duplicate) ? duplicate : r.Diagnostic.Length > 0 ? r.Diagnostic : "Ready")).ToArray();
            }, token);
        }
        static string Stamp(string folder) => folder + string.Join("|", Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Select(p =>
        { var f = new FileInfo(p); return f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks + ":" + f.CreationTimeUtc.Ticks; }));
    }
}
