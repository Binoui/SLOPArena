#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Shared;
using Steamworks;

namespace SlopArena.Client.Workshop
{
    public sealed partial class SteamWorkshopClient
    {
        public Task<ulong> PublishFighter(ulong id, string workspaceKey, FighterItemSnapshot item, string preview,
            WorkshopPublishEdits edits, Action<ulong> rememberId, Action<ulong> legal)
            => Uploads.Start("fighter:" + workspaceKey, () => PublishFighterCore(id, item, preview, edits, rememberId, legal));

        async Task<ulong> PublishFighterCore(ulong id, FighterItemSnapshot item, string preview,
            WorkshopPublishEdits edits, Action<ulong> rememberId, Action<ulong> legal)
        {
            await UploadGate.WaitAsync();
            string? stage = null;
            try
            {
                RequireAvailable(); LastOperation = "Preparing fighter upload";
                if (id != 0) WorkshopPublishEdits.RequireOwner((await GetItem(id)).Owner, SteamUser.GetSteamID().m_SteamID);
                if (id == 0)
                {
                    var created = await Call<CreateItemResult_t>(SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity));
                    Result(created.m_eResult); id = created.m_nPublishedFileId.m_PublishedFileId;
                    await Task.Run(() => rememberId(id));
                    if (created.m_bUserNeedsToAcceptWorkshopLegalAgreement) legal(id);
                }
                stage = Path.Combine(Path.GetTempPath(), "slop-fighter-" + Guid.NewGuid().ToString("N"));
                string folder = stage;
                var manifest = await Task.Run(() =>
                {
                    var cooked = item.RecookForSteam(id);
                    if (cooked.Item == null) throw new InvalidDataException(string.Join("; ", cooked.Diagnostics.Select(LocalFighterWorkspace.DiagnosticText)));
                    Directory.CreateDirectory(folder);
                    foreach (var file in cooked.Item) File.WriteAllBytes(Path.Combine(folder, file.Key), file.Value);
                    var admitted = LocalFighterItems.ReadDownloaded(folder, id);
                    if (admitted.Fighter == null) throw new InvalidDataException(string.Join("; ", admitted.Diagnostics.Select(LocalFighterWorkspace.DiagnosticText)));
                    return WorkshopItemManifestCodec.Read(cooked.Item["item.json"]).Manifest!;
                });
                if (!WorkshopItemManifestCodec.TryWriteSteamMetadata(manifest, out string metadata, out var diagnostic)) throw new InvalidDataException(diagnostic!.Message);
                if (new FileInfo(preview).Length >= 1024 * 1024) throw new InvalidDataException("Preview must be smaller than 1 MiB.");
                UploadHandle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
                if (UploadHandle == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Steam refused StartItemUpdate.");
                if (edits.Title != null) Require(SteamUGC.SetItemTitle(UploadHandle, edits.Title), "title");
                if (edits.Description != null) Require(SteamUGC.SetItemDescription(UploadHandle, edits.Description), "description");
                if (edits.Visibility.HasValue) Require(SteamUGC.SetItemVisibility(UploadHandle, (ERemoteStoragePublishedFileVisibility)edits.Visibility.Value), "visibility");
                Require(SteamUGC.SetItemContent(UploadHandle, stage), "fighter content");
                Require(SteamUGC.SetItemPreview(UploadHandle, preview), "portrait");
                Require(SteamUGC.SetItemMetadata(UploadHandle, metadata), "hash metadata");
                var tags = new List<string> { "fighter" };
                if (manifest.AiAssisted) tags.Add("AI assisted");
                Require(SteamUGC.SetItemTags(UploadHandle, tags), "fighter tags");
                var submitted = await Call<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(UploadHandle, "Fighter update"));
                Result(submitted.m_eResult);
                if (submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement) legal(id);
                LastOperation = "Published fighter " + id;
                return id;
            }
            catch (Exception e) { LastOperation = e.Message; throw; }
            finally
            {
                UploadHandle = UGCUpdateHandle_t.Invalid;
                try { if (stage != null && Directory.Exists(stage)) await Task.Run(() => Directory.Delete(stage, true)); }
                finally { UploadGate.Release(); }
            }
        }
    }
}
