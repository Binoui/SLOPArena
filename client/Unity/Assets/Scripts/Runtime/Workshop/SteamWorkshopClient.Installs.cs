#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SlopArena.Shared;
using Steamworks;

namespace SlopArena.Client.Workshop
{
    public sealed partial class SteamWorkshopClient
    {
        public sealed class FighterInstallRecord
        {
            public string Diagnostic { get; internal set; } = "";
            public ulong Id { get; }
            public string Folder { get; }
            public uint Timestamp { get; }
            public EItemState State { get; }
            public string Change => Timestamp + ":" + (uint)(State & (EItemState.k_EItemStateNeedsUpdate | EItemState.k_EItemStateInstalled));
            public bool Current => Folder.Length > 0 && (State & EItemState.k_EItemStateInstalled) != 0 && (State & (EItemState.k_EItemStateNeedsUpdate | EItemState.k_EItemStateDownloading | EItemState.k_EItemStateDownloadPending)) == 0;
            public bool Downloading => (State & (EItemState.k_EItemStateDownloading | EItemState.k_EItemStateDownloadPending)) != 0;
            private FighterInstallRecord(ulong id)
            {
                Id = id; State = (EItemState)SteamUGC.GetItemState(new PublishedFileId_t(id));
                Folder = SteamUGC.GetItemInstallInfo(new PublishedFileId_t(id), out _, out string folder, 32768, out uint timestamp) ? folder : "";
                Timestamp = timestamp;
            }
            internal LocalFighterItemResult Admit() => LocalFighterItems.ReadDownloaded(Folder, Id);
            internal static FighterInstallRecord Read(ulong id) => new(id);
        }
        readonly Dictionary<ulong, (string Change, string Type)> itemTypes = new();
        public FighterInstallRecord InstallRecord(ulong id) { RequireAvailable(); return FighterInstallRecord.Read(id); }
        public async Task<IReadOnlyList<FighterInstallRecord>> SubscribedFighters()
        {
            RequireAvailable();
            var ids = new PublishedFileId_t[SteamUGC.GetNumSubscribedItems()];
            uint count = SteamUGC.GetSubscribedItems(ids, (uint)ids.Length);
            var result = new List<FighterInstallRecord>();
            for (int i = 0; i < count; i++)
            {
                var record = InstallRecord(ids[i].m_PublishedFileId);
                try
                {
                    if (!itemTypes.TryGetValue(record.Id, out var cached) || cached.Change != record.Change)
                    {
                        var details = await GetItem(record.Id);
                        cached = (record.Change, details.ItemType);
                        if (cached.Type.Length == 0 && record.Current)
                        {
                            string manifest = Path.Combine(record.Folder, "item.json");
                            long total = 0;
                            var read = WorkshopItemManifestCodec.Read(PortableItemFiles.Read(manifest, WorkshopItemManifestCodec.MaxManifestBytes, ref total));
                            if (read.Manifest != null) cached = (record.Change, read.Manifest.ItemType);
                        }
                        itemTypes[record.Id] = cached;
                    }
                    if (cached.Type != "model-swap") result.Add(record);
                }
                catch (Exception e) { record.Diagnostic = e.Message; result.Add(record); }
            }
            return result;
        }
    }
}
