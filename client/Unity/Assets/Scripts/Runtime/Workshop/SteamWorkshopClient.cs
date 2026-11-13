#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;

namespace SlopArena.Client.Workshop
{
    public sealed class WorkshopSteamItem
    {
        public ulong Id { get; }
        public string Title { get; }
        public string Description { get; }
        public int Visibility { get; }
        public ulong Owner { get; }
        public EItemState State => (EItemState)SteamUGC.GetItemState(new PublishedFileId_t(Id));
        public WorkshopSteamItem(SteamUGCDetails_t detail)
        {
            Id = detail.m_nPublishedFileId.m_PublishedFileId; Title = detail.m_rgchTitle;
            Description = detail.m_rgchDescription; Visibility = (int)detail.m_eVisibility; Owner = detail.m_ulSteamIDOwner;
        }
    }

    // ChatSession keeps pumping callbacks after the screen closes. These operations retain their terminal results.
    public sealed partial class SteamWorkshopClient
    {
        public static SteamWorkshopClient Shared { get; } = new();
        public const uint AppId = 5324910;
        public const string Unavailable = "Steam Workshop is not available in this build. Import, preview and wear local items here.";
        private static readonly SemaphoreSlim UploadGate = new(1, 1);
        private static readonly WorkshopOperations<ulong> Uploads = new();
        private static readonly WorkshopOperations<WorkshopLibraryItem> Downloads = new();
        public UGCUpdateHandle_t UploadHandle { get; private set; } = UGCUpdateHandle_t.Invalid;
        public string LastOperation { get; private set; } = "";
        public bool Available => SteamUtils.GetAppID().m_AppId == AppId && SteamUser.BLoggedOn();
        private void RequireAvailable() { if (!Available) throw new InvalidOperationException(Unavailable); }
        private static async Task<T> Call<T>(SteamAPICall_t handle) where T : struct
        {
            if (handle == SteamAPICall_t.Invalid) throw new InvalidOperationException("Steam refused the request.");
            var completion = new TaskCompletionSource<T>();
            using var callback = CallResult<T>.Create((result, failed) =>
            {
                if (failed) completion.TrySetException(new InvalidOperationException("Steam request failed with an I/O error."));
                else completion.TrySetResult(result);
            });
            callback.Set(handle);
            return await completion.Task;
        }
        private static void Require(bool ok, string operation) { if (!ok) throw new InvalidOperationException("Steam refused " + operation + "."); }
        private static void Result(EResult result)
        {
            if (result == EResult.k_EResultFileNotFound || result == EResult.k_EResultAccessDenied) throw new WorkshopItemUnavailableException();
            if (result != EResult.k_EResultOK) throw new InvalidOperationException("Steam returned " + result + ".");
        }
        public async Task<WorkshopSteamItem> GetItem(ulong id)
        {
            RequireAvailable();
            var query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(id) }, 1);
            try
            {
                Require(SteamUGC.SetReturnLongDescription(query, true), "description query");
                Require(SteamUGC.SetAllowCachedResponse(query, 0), "fresh item details");
                var result = await Call<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query));
                Result(result.m_eResult);
                if (result.m_unNumResultsReturned == 0) throw new WorkshopItemUnavailableException();
                Require(SteamUGC.GetQueryUGCResult(query, 0, out var detail), "item details");
                Result(detail.m_eResult);
                if (detail.m_nConsumerAppID.m_AppId != AppId) throw new WorkshopItemUnavailableException();
                return new WorkshopSteamItem(detail);
            }
            finally { SteamUGC.ReleaseQueryUGCRequest(query); }
        }
        public bool IsPublishing(string folder) => Uploads.IsRunning("upload:" + WorkshopLibrary.CanonicalFolder(folder));
        public Task<ulong> Publish(ulong id, string folder, string preview, WorkshopPublishEdits edits, Action<ulong> rememberId, Action<ulong> legal)
            => Uploads.Start("upload:" + WorkshopLibrary.CanonicalFolder(folder), () => PublishCore(id, folder, preview, edits, rememberId, legal));
        private async Task<ulong> PublishCore(ulong id, string folder, string preview, WorkshopPublishEdits edits, Action<ulong> rememberId, Action<ulong> legal)
        {
            await UploadGate.WaitAsync();
            string? stage = null;
            try
            {
                RequireAvailable(); LastOperation = "Preparing upload";
                var item = await Task.Run(() => WorkshopLibrary.Verify(folder));
                if (id != 0) WorkshopPublishEdits.RequireOwner((await GetItem(id)).Owner, SteamUser.GetSteamID().m_SteamID);
                // Steam reads content asynchronously. Upload a private snapshot, not the mutable library folder.
                stage = await Task.Run(() =>
                {
                    string path = Path.Combine(Path.GetTempPath(), "slop-workshop-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(path);
                    item.WriteSnapshot(path);
                    return path;
                });
                if (id == 0)
                {
                    var created = await Call<CreateItemResult_t>(SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity));
                    Result(created.m_eResult); id = created.m_nPublishedFileId.m_PublishedFileId;
                    await Task.Run(() => rememberId(id));
                    if (created.m_bUserNeedsToAcceptWorkshopLegalAgreement) legal(id);
                }
                if (!SlopArena.Shared.WorkshopItemManifestCodec.TryWriteSteamMetadata(item.Manifest, out string metadata, out var diagnostic)) throw new InvalidOperationException(diagnostic!.Message);
                UploadHandle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
                if (UploadHandle == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Steam refused StartItemUpdate.");
                if (edits.Title != null) Require(SteamUGC.SetItemTitle(UploadHandle, edits.Title), "title");
                if (edits.Description != null) Require(SteamUGC.SetItemDescription(UploadHandle, edits.Description), "description");
                if (edits.Visibility.HasValue) Require(SteamUGC.SetItemVisibility(UploadHandle, (ERemoteStoragePublishedFileVisibility)edits.Visibility.Value), "visibility");
                Require(SteamUGC.SetItemContent(UploadHandle, stage), "content");
                Require(SteamUGC.SetItemPreview(UploadHandle, preview), "preview image");
                Require(SteamUGC.SetItemMetadata(UploadHandle, metadata), "hash metadata");
                var tags = new List<string> { "model-swap", item.Manifest.BaseFighterPackageId };
                if (item.Manifest.AiAssisted) tags.Add("AI assisted");
                Require(SteamUGC.SetItemTags(UploadHandle, tags), "tags");
                var submitted = await Call<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(UploadHandle, "Model swap update"));
                Result(submitted.m_eResult);
                if (submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement) legal(id);
                LastOperation = "Published item " + id;
                return id;
            }
            catch (Exception e) { LastOperation = e.Message; throw; }
            finally
            {
                UploadHandle = UGCUpdateHandle_t.Invalid;
                try
                {
                    if (stage != null)
                    {
                        string cleanup = stage;
                        try { await Task.Run(() => Directory.Delete(cleanup, true)); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    }
                }
                finally { UploadGate.Release(); }
            }
        }
        public string Progress()
        {
            if (UploadHandle == UGCUpdateHandle_t.Invalid) return LastOperation;
            var state = SteamUGC.GetItemUpdateProgress(UploadHandle, out ulong done, out ulong total);
            return $"{state}: {done} / {total} bytes";
        }
        public async Task<IReadOnlyList<WorkshopSteamItem>> List(bool published)
        {
            RequireAvailable();
            var items = new List<WorkshopSteamItem>();
            for (uint page = 1; ; page++)
            {
                var query = SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(), published ? EUserUGCList.k_EUserUGCList_Published : EUserUGCList.k_EUserUGCList_Subscribed,
                    EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items, EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc, new AppId_t(AppId), new AppId_t(AppId), page);
                try
                {
                    var result = await Call<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query)); Result(result.m_eResult);
                    for (uint i = 0; i < result.m_unNumResultsReturned; i++)
                        if (SteamUGC.GetQueryUGCResult(query, i, out var detail) && detail.m_eResult == EResult.k_EResultOK) items.Add(new WorkshopSteamItem(detail));
                    if (items.Count >= result.m_unTotalMatchingResults || result.m_unNumResultsReturned == 0) break;
                }
                finally { SteamUGC.ReleaseQueryUGCRequest(query); }
            }
            return items;
        }
        public Task<WorkshopLibraryItem> Download(ulong id) => Downloads.Start("download:" + id, () => DownloadCore(id));
        public static bool InstalledCurrent(ulong id, out string folder)
        {
            var file = new PublishedFileId_t(id);
            var state = (EItemState)SteamUGC.GetItemState(file);
            folder = "";
            return (state & EItemState.k_EItemStateInstalled) != 0 && (state & (EItemState.k_EItemStateNeedsUpdate | EItemState.k_EItemStateDownloading | EItemState.k_EItemStateDownloadPending)) == 0
                && SteamUGC.GetItemInstallInfo(file, out _, out folder, 32768, out _);
        }
        private async Task<WorkshopLibraryItem> DownloadCore(ulong id)
        {
            string folder = await DownloadFolder(id);
            return await Task.Run(() => WorkshopLibrary.Verify(folder));
        }
        public async Task<string> DownloadFolder(ulong id)
        {
            RequireAvailable();
            if (InstalledCurrent(id, out string folder)) return folder;
            var completion = new TaskCompletionSource<EResult>();
            using var callback = Callback<DownloadItemResult_t>.Create(result =>
            {
                if (result.m_unAppID.m_AppId == AppId && result.m_nPublishedFileId.m_PublishedFileId == id) completion.TrySetResult(result.m_eResult);
            });
            Require(SteamUGC.DownloadItem(new PublishedFileId_t(id), true), "download");
            while (!completion.Task.IsCompleted)
            {
                await Task.WhenAny(completion.Task, Task.Delay(500));
                if (!completion.Task.IsCompleted && InstalledCurrent(id, out folder)) return folder;
            }
            Result(await completion.Task);
            Require(SteamUGC.GetItemInstallInfo(new PublishedFileId_t(id), out _, out folder, 32768, out _), "install folder");
            return folder;
        }
        public static void OpenPage(ulong id = 0) => SteamFriends.ActivateGameOverlayToWebPage(id == 0
            ? $"https://steamcommunity.com/app/{AppId}/workshop/" : $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
    }
}
