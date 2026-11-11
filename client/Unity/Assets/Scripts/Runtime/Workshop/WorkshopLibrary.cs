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
        public string Folder { get; }
        public WorkshopItemManifest Manifest { get; }
        public string GlbPath => Path.Combine(Folder, Manifest.Payloads.Keys.Single(x => x.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)));
        public WorkshopLibraryItem(string folder, WorkshopItemManifest manifest) { Folder = folder; Manifest = manifest; }
    }

    public sealed class WorkshopLibrary
    {
        public string Root { get; }
        public string ImportFolder => Path.Combine(Root, "Import");
        public string ItemsFolder => Path.Combine(Root, "Items");
        public WorkshopLibrary(string root)
        {
            Root = root;
            Directory.CreateDirectory(ImportFolder);
            Directory.CreateDirectory(ItemsFolder);
        }

        public static byte[] ReadCapped(string path, long limit)
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > limit) throw new InvalidDataException($"{Path.GetFileName(path)} exceeds {limit} bytes.");
            var bytes = new byte[stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException(path);
                offset += count;
            }
            return bytes;
        }

        public static WorkshopLibraryItem Verify(string folder)
        {
            var read = WorkshopItemManifestCodec.Read(ReadCapped(Path.Combine(folder, "item.json"), WorkshopItemManifestCodec.MaxManifestBytes));
            if (!read.IsAccepted) throw new InvalidDataException(string.Join("; ", read.Diagnostics.Select(x => x.Code + ": " + x.Message)));
            var manifest = read.Manifest!;
            if (!ModelSwapBases.Accepts(manifest.BaseFighterPackageId, out string refusal)) throw new InvalidDataException(refusal);
            var files = new Dictionary<string, byte[]>();
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name == "item.json") continue;
                if (!manifest.Payloads.ContainsKey(name)) throw new InvalidDataException("item.payload.extra: " + name);
                files[name] = ReadCapped(file, name == "humanoid.json" ? 65536 : WorkshopGlbLimits.Default.MaxFileBytes);
            }
            if (Directory.EnumerateDirectories(folder).Any()) throw new InvalidDataException("item.payload.extra: subdirectory");
            var diagnostics = WorkshopItemManifestCodec.Verify(manifest, files);
            if (diagnostics.Count > 0) throw new InvalidDataException(string.Join("; ", diagnostics.Select(x => x.Code + ": " + x.Message)));
            var glb = files.Single(x => x.Key.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).Value;
            var check = WorkshopGlbPrecheck.Check(glb, WorkshopGlbLimits.Default);
            if (!check.IsAccepted) throw new InvalidDataException(string.Join("; ", check.Diagnostics.Select(x => x.Code + ": " + x.Message)));
            return new WorkshopLibraryItem(folder, manifest);
        }

        public WorkshopLibraryItem Save(byte[] glb, string? boneMap, string baseId, string creator, string license, string attribution, bool ai)
        {
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
            string path = Path.Combine(Root, $"wear-{player}.txt");
            if (item == null) { if (File.Exists(path)) File.Delete(path); }
            else { Verify(item.Folder); File.WriteAllText(path, item.Folder); }
        }

        public WorkshopLibraryItem? Worn(int player)
        {
            string path = Path.Combine(Root, $"wear-{player}.txt");
            if (!File.Exists(path)) return null;
            try { return Verify(File.ReadAllText(path)); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return null; }
        }
        public void RememberInstalled(WorkshopLibraryItem item)
        {
            string index = Path.Combine(Root, "installed.txt");
            var folders = File.Exists(index) ? File.ReadAllLines(index).ToList() : new List<string>();
            if (!folders.Contains(item.Folder)) { folders.Add(item.Folder); File.WriteAllLines(index, folders); }
        }

        public IReadOnlyList<WorkshopLibraryItem> WearableItems()
        {
            var result = new List<WorkshopLibraryItem>();
            string index = Path.Combine(Root, "installed.txt");
            var folders = Directory.GetDirectories(ItemsFolder).Concat(File.Exists(index) ? File.ReadAllLines(index) : Array.Empty<string>());
            foreach (string folder in folders.Distinct())
            {
                try { result.Add(Verify(folder)); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
            }
            return result;
        }
        public string StatePath(WorkshopLibraryItem item) => Path.Combine(Root, Path.GetFileName(item.Folder) + ".publish.json");
    }
}
