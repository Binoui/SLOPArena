using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

// Explicit delegates and closed generic tables: no runtime reflection or generated code required by AOT.
internal static class ReplayFields
{
    internal sealed record Codec<T>(Action<BinaryWriter, T> Write, Func<byte[], T> Read);
    static Codec<T> Scalar<T>(Action<BinaryWriter, T> write, Func<BinaryReader, T> read) => new(write, b => ReplayBinary.Scalar(b, read));
    internal static readonly Codec<byte> U8 = Scalar<byte>((w,v) => w.Write(v), r => r.ReadByte());
    internal static readonly Codec<ushort> U16 = Scalar<ushort>((w,v) => w.Write(v), r => r.ReadUInt16());
    internal static readonly Codec<uint> U32 = Scalar<uint>((w,v) => w.Write(v), r => r.ReadUInt32());
    internal static readonly Codec<ulong> U64 = Scalar<ulong>((w,v) => w.Write(v), r => r.ReadUInt64());
    internal static readonly Codec<int> I32 = Scalar<int>((w,v) => w.Write(v), r => r.ReadInt32());
    internal static readonly Codec<float> F32 = Scalar<float>((w,v) => w.Write(ReplayBinary.Finite(v)), r => ReplayBinary.Finite(r.ReadSingle()));
    internal static readonly Codec<bool> Bool = new((w, v) => w.Write(v ? (byte)1 : (byte)0), ReplayBinary.Boolean);
    internal static readonly Codec<string> Text = new((w, v) => WriteText(w, v), ReplayBinary.Text);
    internal static readonly Codec<ReplayVector> Vec = Scalar<ReplayVector>((w,v) => { w.Write(ReplayBinary.Finite(v.X)); w.Write(ReplayBinary.Finite(v.Y)); w.Write(ReplayBinary.Finite(v.Z)); }, r => new(ReplayBinary.Finite(r.ReadSingle()), ReplayBinary.Finite(r.ReadSingle()), ReplayBinary.Finite(r.ReadSingle())));
    internal static Codec<T> Obj<T>(Action<BinaryWriter, T> write, Func<byte[], T> read) => new(write, read);
    internal static Codec<IReadOnlyList<T>> List<T>(Codec<T> codec, int max) => new((w, xs) =>
    {
        if (xs.Count > max) throw new InvalidDataException("List bound");
        w.Write(xs.Count);
        foreach (var x in xs)
        {
            long start = BeginSize(w);
            codec.Write(w, x);
            EndSize(w, start);
        }
    }, b => ReplayBinary.List(b, codec.Read, max));
    internal sealed record FieldEntry<T>(ushort Tag, string Name, Action<BinaryWriter, T> Write, Func<T, byte[], T> Read);
    internal static FieldEntry<T> Field<T,V>(ushort tag, Codec<V> codec, Func<T,V> get, Func<T,V,T> set, string name) => new(tag, name, (w, x) => codec.Write(w, get(x)), (x,b) => set(x, codec.Read(b)));
    internal sealed class Table<T>
    {
        internal readonly FieldEntry<T>[] Fields;
        readonly Func<T> factory;
        internal Table(Func<T> factory, params FieldEntry<T>[] fields) { this.factory = factory; Fields = fields; }
        internal byte[] Write(T x)
        {
            return ReplayBinary.Bytes(w => WriteTo(w, x));
        }
        internal void WriteTo(BinaryWriter w, T x)
        {
            long start = w.BaseStream.Position;
            foreach (var f in Fields)
            {
                w.Write(f.Tag);
                long fieldStart = BeginSize(w);
                f.Write(w, x);
                EndSize(w, fieldStart);
            }
            if (w.BaseStream.Position - start > ReplayViewCodec.MaxRecord) throw new InvalidDataException("Object bound");
        }
        internal T Read(byte[] bytes)
        {
            var x = factory();
            foreach (var f in ReplayBinary.Fields(bytes))
            {
                var field = Fields.FirstOrDefault(v => v.Tag == f.Key);
                if (field != null) x = field.Read(x, f.Value);
            }
            return x;
        }
    }
    static void WriteText(BinaryWriter w, string value)
    {
        int length = ReplayBinary.Utf8.GetByteCount(value);
        if (length > 4096) throw new InvalidDataException("String bound");
        Span<byte> bytes = stackalloc byte[length];
        ReplayBinary.Utf8.GetBytes(value.AsSpan(), bytes);
        w.Write(bytes);
    }
    static long BeginSize(BinaryWriter w)
    {
        w.Write(0);
        return w.BaseStream.Position;
    }
    static void EndSize(BinaryWriter w, long start)
    {
        long end = w.BaseStream.Position;
        w.BaseStream.Position = start - 4;
        w.Write(checked((int)(end - start)));
        w.BaseStream.Position = end;
    }
    internal sealed record KindEntry<T>(string Kind, Func<T,bool> Accepts, Action<BinaryWriter,T> Write, Func<byte[],T> Read);
    internal static KindEntry<T> Kind<T,V>(string kind, Table<V> table) where V : T => new(kind, x => x is V, (w, x) => table.WriteTo(w, (V)x!), b => table.Read(b));
    internal sealed class KindRegistry<T>
    {
        internal readonly KindEntry<T>[] Kinds;
        internal KindRegistry(params KindEntry<T>[] kinds) { Kinds = kinds; }
        internal byte[] Write(T x) => ReplayBinary.Bytes(w => WriteTo(w, x));
        internal void WriteTo(BinaryWriter w, T x) => (Kinds.FirstOrDefault(k => k.Accepts(x)) ?? throw new InvalidDataException("Kind")).Write(w, x);
        internal T Read(byte[] b) => (Kinds.FirstOrDefault(k => k.Kind == ReplayBinary.Kind(b)) ?? throw new InvalidDataException("Kind")).Read(b);
    }
}
