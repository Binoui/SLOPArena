#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.Client.Workshop
{
    public sealed class WorkshopLibraryItem
    {
        private readonly IReadOnlyDictionary<string, byte[]> _payloads;
        public string Folder { get; }
        public WorkshopItemManifest Manifest { get; }
        public string GlbPath => Path.Combine(Folder, Manifest.Payloads.Keys.Single(x => x.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)));
        internal WorkshopLibraryItem(string folder, WorkshopItemManifest manifest, IReadOnlyDictionary<string, byte[]> payloads)
        { Folder = folder; Manifest = manifest; _payloads = payloads; }
        public void WriteSnapshot(string folder)
        {
            foreach (var payload in _payloads) File.WriteAllBytes(Path.Combine(folder, payload.Key), payload.Value);
            File.WriteAllBytes(Path.Combine(folder, "item.json"), WorkshopItemManifestCodec.Write(Manifest));
        }
        public void RequireBase(string? expectedBase)
        {
            if (expectedBase != null && Manifest.BaseFighterPackageId != expectedBase)
                throw new InvalidDataException($"item.base.mismatch: Expected {expectedBase}; item names {Manifest.BaseFighterPackageId}.");
        }
        public (byte[] Glb, string? BoneMap) Payload(string? expectedBase = null)
        {
            RequireBase(expectedBase);
            var glb = _payloads.Single(x => x.Key.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).Value;
            return ((byte[])glb.Clone(), _payloads.TryGetValue("humanoid.json", out var map) ? Encoding.UTF8.GetString(map) : null);
        }
    }

    public sealed class WorkshopPublishSettings
    {
        public ulong Id { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int Visibility { get; set; } = 2;
        public static WorkshopPublishSettings Read(string path, string fallback)
        {
            try
            {
                var settings = File.Exists(path) ? JsonSerializer.Deserialize<WorkshopPublishSettings>(File.ReadAllText(path)) : null;
                if (settings != null && settings.Title != null && settings.Description != null && settings.Visibility >= 0 && settings.Visibility <= 3) return settings;
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
            return new WorkshopPublishSettings { Title = fallback };
        }
    }

    // All methods that touch disk run on a worker. Cached entries own the bytes that passed verification.
    public sealed class WorkshopLibrary
    {
        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, (string Stamp, WorkshopLibraryItem Item)> Cache = new();
        private static readonly HashSet<string> Droppings = new(StringComparer.OrdinalIgnoreCase) { ".directory", "Thumbs.db", "desktop.ini" };
        public string Root { get; }
        public string ImportFolder => Path.Combine(Root, "Import");
        public string ItemsFolder => Path.Combine(Root, "Items");
        public WorkshopLibrary(string root) => Root = Path.GetFullPath(root);
        public void EnsureDirectories() { Directory.CreateDirectory(ImportFolder); Directory.CreateDirectory(ItemsFolder); }
        public static string CanonicalFolder(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        public static string ItemKey(string folder) => WorkshopItemManifestCodec.HashPayload(Encoding.UTF8.GetBytes(CanonicalFolder(folder).Replace('\\', '/')));
        public bool IsCreatorItem(WorkshopLibraryItem item) => string.Equals(CanonicalFolder(Path.GetDirectoryName(item.Folder)!), CanonicalFolder(ItemsFolder), StringComparison.OrdinalIgnoreCase);
        public static byte[] ReadCapped(string path, long limit)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > limit) throw new InvalidDataException($"{Path.GetFileName(path)} exceeds {limit} bytes.");
            var bytes = new byte[stream.Length];
            for (int offset = 0; offset < bytes.Length;)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException(path);
                offset += count;
            }
            return bytes;
        }
        private static string Stamp(string folder)
        {
            if (Directory.EnumerateDirectories(folder).Any()) throw new InvalidDataException("item.payload.extra: subdirectory");
            return string.Join("|", Directory.GetFiles(folder).Where(p => !Droppings.Contains(Path.GetFileName(p))).OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => { var f = new FileInfo(p); return f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks + ":" + f.CreationTimeUtc.Ticks; }));
        }
        public static WorkshopLibraryItem Verify(string folder)
        {
            lock (CacheLock) return VerifyCore(folder);
        }
        private static WorkshopLibraryItem VerifyCore(string folder)
        {
            folder = CanonicalFolder(folder);
            string stamp = Stamp(folder);
            if (Cache.TryGetValue(folder, out var cached) && cached.Stamp == stamp) return cached.Item;
            var read = WorkshopItemManifestCodec.Read(ReadCapped(Path.Combine(folder, "item.json"), WorkshopItemManifestCodec.MaxManifestBytes));
            if (!read.IsAccepted) throw new InvalidDataException(string.Join("; ", read.Diagnostics.Select(x => x.Code + ": " + x.Message)));
            var manifest = read.Manifest!;
            if (!ModelSwapBases.Accepts(manifest.BaseFighterPackageId, out string refusal)) throw new InvalidDataException(refusal);
            var files = new Dictionary<string, byte[]>();
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name == "item.json" || Droppings.Contains(name)) continue;
                if (!manifest.Payloads.ContainsKey(name)) throw new InvalidDataException("item.payload.extra: " + name);
                files[name] = ReadCapped(file, name == "humanoid.json" ? 65536 : WorkshopGlbLimits.Default.MaxFileBytes);
            }
            var diagnostics = WorkshopItemManifestCodec.Verify(manifest, files);
            if (diagnostics.Count > 0) throw new InvalidDataException(string.Join("; ", diagnostics.Select(x => x.Code + ": " + x.Message)));
            var check = WorkshopGlbPrecheck.Check(files.Single(x => x.Key.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).Value, WorkshopGlbLimits.Default);
            if (!check.IsAccepted) throw new InvalidDataException(string.Join("; ", check.Diagnostics.Select(x => x.Code + ": " + x.Message)));
            if (Stamp(folder) != stamp) throw new InvalidDataException("item.changed: Item files changed during verification. Retry.");
            var item = new WorkshopLibraryItem(folder, manifest, files);
            if (Cache.Count >= 8) Cache.Clear();
            Cache[folder] = (stamp, item);
            return item;
        }
        public WorkshopLibraryItem Save(byte[] glb, string? boneMap, string baseId, string creator, string license, string attribution, bool ai)
        {
            EnsureDirectories();
            var payloads = new Dictionary<string, string> { ["model.glb"] = WorkshopItemManifestCodec.HashPayload(glb) };
            byte[]? map = boneMap == null ? null : Encoding.UTF8.GetBytes(boneMap);
            if (map != null) payloads["humanoid.json"] = WorkshopItemManifestCodec.HashPayload(map);
            var json = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, itemType = "model-swap", itemVersion = "1.0.0", payloads, creator, license, attribution, aiAssisted = ai, baseFighterPackageId = baseId });
            var read = WorkshopItemManifestCodec.Read(json);
            if (!read.IsAccepted) throw new InvalidDataException(string.Join("; ", read.Diagnostics.Select(x => x.Message)));
            string folder = Path.Combine(ItemsFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "model.glb"), glb);
            if (map != null) File.WriteAllBytes(Path.Combine(folder, "humanoid.json"), map);
            File.WriteAllBytes(Path.Combine(folder, "item.json"), WorkshopItemManifestCodec.Write(read.Manifest!));
            return Verify(folder);
        }
        public void Wear(int player, WorkshopLibraryItem? item)
        {
            if (player < 1 || player > 4) throw new ArgumentOutOfRangeException(nameof(player));
            EnsureDirectories();
            string path = Path.Combine(Root, $"wear-{player}.txt");
            if (item == null) { if (File.Exists(path)) File.Delete(path); }
            else { Verify(item.Folder).RequireBase(item.Manifest.BaseFighterPackageId); File.WriteAllText(path, JsonSerializer.Serialize(new SavedChoice { Folder = item.Folder, BaseId = item.Manifest.BaseFighterPackageId })); }
        }
        public WorkshopLibraryItem? Worn(int player)
        {
            try
            {
                string path = Path.Combine(Root, $"wear-{player}.txt");
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                if (!text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    var legacy = Verify(text); Wear(player, legacy); return legacy;
                }
                var saved = JsonSerializer.Deserialize<SavedChoice>(text);
                if (saved == null || string.IsNullOrEmpty(saved.BaseId)) return null;
                var item = Verify(saved.Folder); item.RequireBase(saved.BaseId); return item;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException) { return null; }
        }
        public sealed class SavedChoice
        {
            public string Folder { get; set; } = "";
            public string BaseId { get; set; } = "";
        }
        public void RememberInstalled(WorkshopLibraryItem item)
        {
            EnsureDirectories();
            string index = Path.Combine(Root, "installed.txt");
            var folders = File.Exists(index) ? File.ReadAllLines(index).ToList() : new List<string>();
            if (!folders.Contains(item.Folder)) { folders.Add(item.Folder); File.WriteAllLines(index, folders); }
        }
        public IReadOnlyList<WorkshopLibraryItem> WearableItems()
        {
            EnsureDirectories();
            var result = new List<WorkshopLibraryItem>();
            string index = Path.Combine(Root, "installed.txt");
            foreach (string folder in Directory.GetDirectories(ItemsFolder).Concat(File.Exists(index) ? File.ReadAllLines(index) : Array.Empty<string>()).Distinct())
            {
                try { result.Add(Verify(folder)); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
            }
            return result;
        }
        public string StatePath(WorkshopLibraryItem item)
        {
            string legacy = Path.Combine(Root, Path.GetFileName(CanonicalFolder(item.Folder)) + ".publish.json");
            return File.Exists(legacy) ? legacy : Path.Combine(Root, ItemKey(item.Folder) + ".publish.json");
        }
        public static string? ImportBoneMap(string glb)
        {
            string named = Path.ChangeExtension(glb, ".humanoid.json");
            if (File.Exists(named)) return Encoding.UTF8.GetString(ReadCapped(named, 65536));
            // A shared import folder may contain many rigs. An unnamed map is only unambiguous in an item folder.
            string folder = Path.GetDirectoryName(glb)!;
            string generic = Path.Combine(folder, "humanoid.json");
            return File.Exists(Path.Combine(folder, "item.json")) && File.Exists(generic) ? Encoding.UTF8.GetString(ReadCapped(generic, 65536)) : null;
        }
    }
}
