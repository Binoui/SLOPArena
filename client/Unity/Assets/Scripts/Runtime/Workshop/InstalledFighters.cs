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
            internal string Stamp = "";
            internal LocalFighterItemResult Result = null!;
            internal MadeFighterPresentation.Lease? Lease;
            internal string Diagnostic = "";
            public void Dispose() { if (Lease != null) FighterPresentationReleases.AfterRenderers(Lease); Lease = null; }
        }
        static readonly Dictionary<string, Revision> revisions = new(StringComparer.Ordinal);
        public static IReadOnlyList<string> StatusRows => revisions.Select(p => p.Key + " / " + (p.Value.Diagnostic.Length > 0 ? p.Value.Diagnostic : "Ready")).ToArray();
        bool busy;
        float next;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartSession()
        {
            foreach (var revision in revisions.Values) revision.Dispose();
            revisions.Clear(); LocalFighterLibrary.SetMadeFighters(Array.Empty<AdmittedFighter>());
            var owner = new GameObject("Installed fighters");
            DontDestroyOnLoad(owner); owner.AddComponent<InstalledFighters>();
        }
        async void Update()
        {
            if (busy || Time.unscaledTime < next) return;
            busy = true; next = Time.unscaledTime + 3;
            try { await RefreshSources(); }
            catch (Exception e) { Debug.LogWarning("[InstalledFighters] " + e.Message); }
            finally { busy = false; }
        }
        async Task RefreshSources()
        {
            string root = Path.Combine(Application.persistentDataPath, "Workshop", "FighterCooks");
            var sources = await Task.Run(() =>
            {
                var found = new Dictionary<string, (string Folder, ulong Steam)>();
                if (Directory.Exists(root)) foreach (string workspace in Directory.GetDirectories(root))
                {
                    string pointer = Path.Combine(workspace, "latest.txt");
                    if (!File.Exists(pointer)) continue;
                    string name = File.ReadAllText(pointer);
                    if (!name.StartsWith("revision-", StringComparison.Ordinal) || Path.GetFileName(name) != name) continue;
                    found[workspace] = (Path.Combine(workspace, name), 0);
                }
                return found;
            });
            bool steam = ChatSession.Instance?.SteamAvailable == true && SteamWorkshopClient.Shared.Available;
            if (steam)
            {
                var ids = new PublishedFileId_t[SteamUGC.GetNumSubscribedItems()];
                uint count = SteamUGC.GetSubscribedItems(ids, (uint)ids.Length);
                for (int i = 0; i < count; i++)
                {
                    ulong id = ids[i].m_PublishedFileId;
                    string key = "Steam " + id;
                    if (SteamWorkshopClient.InstalledCurrent(id, out string folder)) sources[key] = (folder, id);
                    else
                    {
                        SteamUGC.DownloadItem(ids[i], false);
                        sources[key] = ("", id);
                        if (revisions.TryGetValue(key, out var pending) && pending.Stamp == "downloading"
                            && SteamUGC.GetItemDownloadInfo(ids[i], out ulong done, out ulong total))
                            pending.Diagnostic = $"Downloading {done} / {total} bytes";
                    }
                }
            }
            bool changed = false;
            foreach (string removed in revisions.Keys.Except(sources.Keys).ToArray())
            { revisions[removed].Dispose(); revisions.Remove(removed); changed = true; }
            foreach (var source in sources)
            {
                var (folder, id) = source.Value;
                string stamp = folder.Length == 0 ? "downloading" : await Task.Run(() => Stamp(folder));
                if (revisions.TryGetValue(source.Key, out var old) && old.Stamp == stamp) continue;
                var revision = new Revision { Stamp = stamp };
                try
                {
                    if (folder.Length == 0) throw new IOException("Downloading from Steam...");
                    var result = await Task.Run(() => id == 0 ? LocalFighterItems.Read(folder) : LocalFighterItems.ReadDownloaded(folder, id));
                    revision.Result = result;
                    if (result.Fighter == null || result.Item == null) throw new InvalidDataException(string.Join("; ", result.Diagnostics.Select(LocalFighterWorkspace.DiagnosticText)));
                    var library = LocalFighterLibrary.Current.WithLocalItem(result.Fighter);
                    library.TryGetCatalog(out var catalog, out _);
                    revision.Lease = await MadeFighterPresentation.PrepareAsync(catalog!.ResolvePackage(result.Fighter.Choose().Revision.PackageId)!, result.Item, CancellationToken.None);
                }
                catch (Exception error) { revision.Diagnostic = error.Message; }
                old?.Dispose(); revisions[source.Key] = revision; changed = true;
            }
            if (changed)
            {
                var admitted = revisions.Values.Where(r => r.Lease != null).Select(r => r.Result.Fighter!).ToArray();
                var duplicate = admitted.GroupBy(f => f.Choose().Revision.PackageId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                foreach (var r in revisions.Values.Where(r => r.Lease != null && duplicate.Contains(r.Result.Fighter!.Choose().Revision.PackageId)))
                    r.Diagnostic = "Duplicate package ID. Give each workspace a unique local ID.";
                LocalFighterLibrary.SetMadeFighters(admitted.Where(f => !duplicate.Contains(f.Choose().Revision.PackageId)).ToArray());
            }
        }
        static string Stamp(string folder)
        {
            if (!Directory.Exists(folder)) return "missing:" + folder;
            return folder + string.Join("|", Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Select(p =>
            { var f = new FileInfo(p); return f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks + ":" + f.CreationTimeUtc.Ticks; }));
        }
    }
}
