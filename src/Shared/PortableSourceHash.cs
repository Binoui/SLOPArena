using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Buffers.Binary;

namespace SlopArena.Shared;

public static class PortableSourceHash
{
    public static string Compute(IReadOnlyDictionary<string, byte[]> files)
    {
        using var framed = new MemoryStream();
        foreach (string name in new[] { "package.json", "character.json" })
        {
            var path = Encoding.UTF8.GetBytes(name);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, path.Length); framed.Write(length, 0, 4);
            framed.Write(path, 0, path.Length);
            var bytes = files[name];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length); framed.Write(length, 0, 4);
            framed.Write(bytes, 0, bytes.Length);
        }
        return WorkshopItemManifestCodec.HashPayload(framed.ToArray());
    }
}
