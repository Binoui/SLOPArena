#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;

namespace SlopArena.Client.Workshop
{
    public sealed class WorkshopSteamItem
    {
        public ulong Id { get; }
        public string Title { get; }
        public EItemState State => (EItemState)SteamUGC.GetItemState(new PublishedFileId_t(Id));
        public WorkshopSteamItem(ulong id, string title) { Id = id; Title = title; }
    }

    // The host owns Steam initialization and callback pumping, including in console tests.
    public sealed class SteamWorkshopClient
    {
        public const uint AppId = 5324910;
        public const string Unavailable = "Steam Workshop is not available in this build. Import, preview and wear local items here.";
        public UGCUpdateHandle_t UploadHandle { get; private set; } = UGCUpdateHandle_t.Invalid;
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
            if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(3))) != completion.Task)
                throw new TimeoutException("Steam request timed out. Retry when Steam is connected.");
            return await completion.Task;
        }
        private static void Require(bool ok, string operation) { if (!ok) throw new InvalidOperationException("Steam refused " + operation + "."); }
        private static void Result(EResult result) { if (result != EResult.k_EResultOK) throw new InvalidOperationException("Steam returned " + result + "."); }

        public async Task<ulong> Publish(ulong id, string folder, string preview, string title, string description,
            ERemoteStoragePublishedFileVisibility visibility, Action<ulong> rememberId, Action<ulong> legal)
        {
            RequireAvailable();
            var item = WorkshopLibrary.Verify(folder);
            if (id == 0)
            {
                var created = await Call<CreateItemResult_t>(SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity));
                Result(created.m_eResult);
                id = created.m_nPublishedFileId.m_PublishedFileId;
                rememberId(id);
                if (created.m_bUserNeedsToAcceptWorkshopLegalAgreement) legal(id);
            }
            if (!SlopArena.Shared.WorkshopItemManifestCodec.TryWriteSteamMetadata(item.Manifest, out string metadata, out var diagnostic))
                throw new InvalidOperationException(diagnostic!.Message);
            UploadHandle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
            if (UploadHandle == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Steam refused StartItemUpdate.");
            try
            {
                Require(SteamUGC.SetItemTitle(UploadHandle, title), "title");
                Require(SteamUGC.SetItemDescription(UploadHandle, description), "description");
                Require(SteamUGC.SetItemVisibility(UploadHandle, visibility), "visibility");
                Require(SteamUGC.SetItemContent(UploadHandle, folder), "content");
                Require(SteamUGC.SetItemPreview(UploadHandle, preview), "preview image");
                Require(SteamUGC.SetItemMetadata(UploadHandle, metadata), "hash metadata");
                var tags = new List<string> { "model-swap", item.Manifest.BaseFighterPackageId };
                if (item.Manifest.AiAssisted) tags.Add("AI assisted");
                Require(SteamUGC.SetItemTags(UploadHandle, tags), "tags");
                var submitted = await Call<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(UploadHandle, "Model swap update"));
                Result(submitted.m_eResult);
                if (submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement) legal(id);
                return id;
            }
            finally { UploadHandle = UGCUpdateHandle_t.Invalid; }
        }

        public string Progress()
        {
            if (UploadHandle == UGCUpdateHandle_t.Invalid) return "Waiting for Steam";
            var state = SteamUGC.GetItemUpdateProgress(UploadHandle, out ulong done, out ulong total);
            return $"{state}: {done} / {total} bytes";
        }

        public async Task<IReadOnlyList<WorkshopSteamItem>> List(bool published)
        {
            RequireAvailable();
            var items = new List<WorkshopSteamItem>();
            for (uint page = 1; ; page++)
            {
                var query = SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(),
                    published ? EUserUGCList.k_EUserUGCList_Published : EUserUGCList.k_EUserUGCList_Subscribed,
                    EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items, EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,
                    new AppId_t(AppId), new AppId_t(AppId), page);
                try
                {
                    var result = await Call<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query));
                    Result(result.m_eResult);
                    for (uint i = 0; i < result.m_unNumResultsReturned; i++)
                        if (SteamUGC.GetQueryUGCResult(query, i, out var detail))
                            items.Add(new WorkshopSteamItem(detail.m_nPublishedFileId.m_PublishedFileId, detail.m_rgchTitle));
                    if (items.Count >= result.m_unTotalMatchingResults || result.m_unNumResultsReturned == 0) break;
                }
                finally { SteamUGC.ReleaseQueryUGCRequest(query); }
            }
            return items;
        }

        public async Task<WorkshopLibraryItem> Download(ulong id)
        {
            RequireAvailable();
            var completion = new TaskCompletionSource<EResult>();
            using var callback = Callback<DownloadItemResult_t>.Create(result =>
            {
                if (result.m_unAppID.m_AppId == AppId && result.m_nPublishedFileId.m_PublishedFileId == id)
                    completion.TrySetResult(result.m_eResult);
            });
            Require(SteamUGC.DownloadItem(new PublishedFileId_t(id), true), "download");
            if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(3))) != completion.Task)
                throw new TimeoutException("Steam download timed out.");
            Result(await completion.Task);
            Require(SteamUGC.GetItemInstallInfo(new PublishedFileId_t(id), out _, out string folder, 32768, out _), "install folder");
            return WorkshopLibrary.Verify(folder);
        }
        public static void OpenPage(ulong id = 0) => SteamFriends.ActivateGameOverlayToWebPage(id == 0
            ? $"https://steamcommunity.com/app/{AppId}/workshop/"
            : $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
    }
}
