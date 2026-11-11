using System.Buffers.Binary;
using System.Text;
using SlopArena.Client.Workshop;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class WorkshopLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "workshop-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private WorkshopLibrary Library => new(_root);
    private static byte[] Glb()
    {
        var json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"buffers":[{"byteLength":36}],"bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"type":"VEC3","count":3}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0,"skin":0}],"skins":[{"joints":[0]}]}
        """);
        int length = (json.Length + 3) & ~3;
        var bytes = new byte[28 + length + 36];
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        U(0, 0x46546c67); U(4, 2); U(8, (uint)bytes.Length); U(12, (uint)length); U(16, 0x4e4f534a);
        bytes.AsSpan(20, length).Fill(32); json.CopyTo(bytes, 20); U(20 + length, 36); U(24 + length, 0x004e4942);
        return bytes;
    }
    private WorkshopLibraryItem Item() => Library.Save(Glb(), "{\"test\":true}", "fightguy", "Creator", "CC0", "", false);

    [Fact]
    public void VerifiedSnapshotKeepsBothPayloadsWhenDiskChanges()
    {
        var item = Item(); var original = item.Payload("fightguy");
        File.WriteAllBytes(item.GlbPath, new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(item.Folder, "humanoid.json"), "tampered");
        var snapshot = item.Payload("fightguy");
        Assert.Equal(original.Glb, snapshot.Glb); Assert.Equal(original.BoneMap, snapshot.BoneMap);
        Assert.Contains("item.payload.mismatch", Assert.Throws<InvalidDataException>(() => WorkshopLibrary.Verify(item.Folder)).Message);
    }
    [Fact]
    public void PersistedChoicePinsTheBaseAcrossRestarts()
    {
        var item = Item(); Library.Wear(1, item);
        string manifest = Path.Combine(item.Folder, "item.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("fightguy", "bonk"));
        Assert.Equal("bonk", WorkshopLibrary.Verify(item.Folder).Manifest.BaseFighterPackageId);
        Assert.Null(new WorkshopLibrary(_root).Worn(1));
    }
    [Fact]
    public void SnapshotConsumersCannotMutateOtherConsumersBytes()
    {
        var item = Item(); var first = item.Payload(); first.Glb[0] = 0;
        Assert.Equal((byte)'g', item.Payload().Glb[0]);
    }
    [Fact]
    public void StoredBaseMustMatchTheVerifiedManifest()
    { Assert.Contains("item.base.mismatch", Assert.Throws<InvalidDataException>(() => Item().Payload("bonk")).Message); }
    [Fact]
    public void UnchangedFilesReuseVerifiedSnapshotWithoutReadingPayloadAgain()
    {
        var item = Item();
        using var locked = new FileStream(item.GlbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Same(item, WorkshopLibrary.Verify(item.Folder));
    }
    [Theory] [InlineData(".directory")] [InlineData("Thumbs.db")] [InlineData("desktop.ini")]
    public void FileManagerMetadataIsIgnored(string name)
    { var item = Item(); File.WriteAllText(Path.Combine(item.Folder, name), "metadata"); Assert.Equal(item.Manifest.BaseFighterPackageId, WorkshopLibrary.Verify(item.Folder).Manifest.BaseFighterPackageId); }
    [Fact]
    public void UnknownExtraPayloadIsStillRefused()
    { var item = Item(); File.WriteAllText(Path.Combine(item.Folder, "script.dll"), "untrusted"); Assert.Contains("item.payload.extra", Assert.Throws<InvalidDataException>(() => WorkshopLibrary.Verify(item.Folder)).Message); }
    [Theory] [InlineData("null")] [InlineData("{")] [InlineData("{\"Visibility\":9}")] [InlineData("{\"Title\":null}")]
    public void CorruptPublishSettingsFallBackWithoutThrowing(string json)
    { Directory.CreateDirectory(_root); var file = Path.Combine(_root, "state.json"); File.WriteAllText(file, json); Assert.Equal("fallback", WorkshopPublishSettings.Read(file, "fallback").Title); }
    [Fact]
    public void TrailingSeparatorDoesNotChangeItemIdentityAndOtherFoldersDiffer()
    { var item = Item(); Assert.Equal(WorkshopLibrary.ItemKey(item.Folder), WorkshopLibrary.ItemKey(item.Folder + Path.DirectorySeparatorChar)); Assert.NotEqual(WorkshopLibrary.ItemKey(item.Folder), WorkshopLibrary.ItemKey(item.Folder + "-other")); }
    [Fact]
    public void GenericBoneMapInSharedImportFolderIsNotBundled()
    {
        Library.EnsureDirectories(); var path = Path.Combine(Library.ImportFolder, "rig.glb");
        File.WriteAllText(Path.Combine(Library.ImportFolder, "humanoid.json"), "shared"); Assert.Null(WorkshopLibrary.ImportBoneMap(path));
        File.WriteAllText(Path.ChangeExtension(path, ".humanoid.json"), "named"); Assert.Equal("named", WorkshopLibrary.ImportBoneMap(path));
    }
    [Fact]
    public void OpeningUnwritableLibraryDoesNotPerformStartupWrites()
    { string file = Path.Combine(Path.GetTempPath(), "workshop-block-" + Guid.NewGuid().ToString("N")); File.WriteAllText(file, "blocked"); try { var library = new WorkshopLibrary(Path.Combine(file, "child")); Assert.ThrowsAny<IOException>(() => library.EnsureDirectories()); } finally { File.Delete(file); } }
    [Fact]
    public void SnapshotUploadContainsTheOriginalVerifiedBoneMap()
    { var item = Item(); string folder = Path.Combine(_root, "upload"); Directory.CreateDirectory(folder); item.WriteSnapshot(folder); Assert.Empty(WorkshopItemManifestCodec.Verify(item.Manifest, Directory.GetFiles(folder).Where(f => Path.GetFileName(f) != "item.json").ToDictionary(Path.GetFileName, File.ReadAllBytes)!)); }
}
