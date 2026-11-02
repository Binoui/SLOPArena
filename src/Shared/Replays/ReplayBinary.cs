using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

internal static class ReplayBinary
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s, Utf8, true);
        write(w);
        return s.ToArray();
    }
    internal static BinaryReader Reader(byte[] b) => new(new MemoryStream(b, false), Utf8);
    internal static byte[] Text(string s)
    {
        var b = Utf8.GetBytes(s);
        if (b.Length > 4096) throw new InvalidDataException("String bound");
        return b;
    }
    internal static string Text(byte[] b)
    {
        if (b.Length > 4096) throw new InvalidDataException("String bound");
        return Utf8.GetString(b);
    }
    internal static float Finite(float x) => float.IsNaN(x) || float.IsInfinity(x) ? throw new InvalidDataException("Nonfinite") : x;
    internal static T Scalar<T>(byte[] b, Func<BinaryReader, T> read)
    {
        using var r = Reader(b);
        var v = read(r);
        if (r.BaseStream.Position != b.Length) throw new InvalidDataException("Scalar width");
        return v;
    }
    internal static bool Boolean(byte[] b) => b.Length == 1 && b[0] <= 1 ? b[0] == 1 : throw new InvalidDataException("Bool");
    internal static void Field(BinaryWriter w, ushort id, byte[] value)
    {
        w.Write(id);
        w.Write(value.Length);
        w.Write(value);
    }
    internal static IEnumerable<KeyValuePair<ushort, byte[]>> Fields(byte[] b)
    {
        if (b.Length > ReplayViewCodec.MaxRecord) throw new InvalidDataException("Object bound");
        using var r = Reader(b);
        ushort last = 0;
        int count = 0;
        while (r.BaseStream.Position < b.Length)
        {
            ushort tag = r.ReadUInt16();
            int n = r.ReadInt32();
            if (tag <= last || ++count > 128 || n < 0 || n > b.Length - r.BaseStream.Position) throw new InvalidDataException("Field framing");
            last = tag;
            yield return new(tag, r.ReadBytes(n));
        }
    }
    internal static string Kind(byte[] b)
    {
        var f = Fields(b).FirstOrDefault();
        if (f.Key != 1) throw new InvalidDataException("Missing kind");
        return Text(f.Value);
    }
    internal static byte[] List<T>(IReadOnlyList<T> list, Func<T, byte[]> encode) => Bytes(w =>
    {
        w.Write(list.Count);
        foreach (var x in list)
        {
            var b = encode(x);
            w.Write(b.Length);
            w.Write(b);
        }
    });
    internal static IReadOnlyList<T> List<T>(byte[] b, Func<byte[], T> decode, int max) => Scalar(b, r =>
    {
        int n = r.ReadInt32();
        if (n < 0 || n > max) throw new InvalidDataException("List bound");
        var a = new T[n];
        for (int i = 0; i < n; i++)
        {
            int len = r.ReadInt32();
            if (len < 0 || len > b.Length - r.BaseStream.Position) throw new InvalidDataException("List framing");
            a[i] = decode(r.ReadBytes(len));
        }
        return (IReadOnlyList<T>)Array.AsReadOnly(a);
    });
    internal static uint Crc(byte[] b)
    {
        uint c = 0xffffffff;
        foreach (byte x in b)
        {
            c ^= x;
            for (int j = 0; j < 8; j++) c = (c >> 1) ^ ((c & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~c;
    }
    internal static byte[] Read(BinaryReader r, int n, int max)
    {
        if (n < 0 || n > max || n > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Length");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return b;
    }
}
