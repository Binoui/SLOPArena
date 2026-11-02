using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

internal static class ReplayStorage
{
    internal const long MaxFileBytes = 64_000_000;
    internal const int TrailerReserve = 16400;
    internal const int MaxExact = 65552;
    internal const int MaxLane = 60 * ReplayViewCodec.MaxRecord;
    internal const uint ChunkMagic = 0x4b4e4843;
    internal const uint CommitMagic = 0x54494d43;
    internal const uint DoneMagic = 0x454e4f44;
    internal const uint EndMagic = 0x444e4552;
    internal static byte[] Preamble(ReplayHeader h)
    {
        Validate(h);
        var meta = ReplayMetadata.Write(h);
        if (meta.Length > 65536) throw new InvalidDataException("Header bound");
        var b = ReplayBinary.Bytes(w =>
        {
            w.Write(new byte[] { 83, 76, 79, 80, 82, 69, 80, 0 });
            w.Write((ushort)1);
            w.Write((ushort)1);
            w.Write(meta.Length);
            w.Write(0u);
            w.Write(0u);
            w.Write(meta);
        });
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), ReplayBinary.Crc(b));
        return b;
    }
    internal static void Validate(ReplayHeader h)
    {
        if (h.Build == null || h.Rules == null || h.Build.Protocol == 0 || h.Build.FrameFormat == 0 || h.Build.SnapshotFormat == 0 || string.IsNullOrEmpty(h.MatchId) || string.IsNullOrEmpty(h.ArenaId) || string.IsNullOrEmpty(h.Build.ContentHash) || h.Roster.Count < 2 || h.Roster.Count > 4) throw new InvalidDataException("Required header");
        ulong last = 0;
        foreach (var p in h.Roster)
        {
            if (p.EntityId <= last || p.Package == null || string.IsNullOrEmpty(p.Package.Version) || string.IsNullOrEmpty(p.Package.SourceHash) || string.IsNullOrEmpty(p.Package.CookedContentHash) || string.IsNullOrEmpty(p.Package.PackageHash) || string.IsNullOrEmpty(p.Package.PackageId) || p.FormId is not ("base" or "alternate")) throw new InvalidDataException("Roster");
            last = p.EntityId;
        }
    }
    internal static byte[] Pack(byte[] b, out byte encoding)
    {
        using var s = new MemoryStream();
        using (var d = new DeflateStream(s, CompressionLevel.Optimal, true)) d.Write(b, 0, b.Length);
        var c = s.ToArray();
        encoding = (byte)(c.Length < b.Length ? 1 : 0);
        return encoding == 1 ? c : b;
    }
    internal static byte[] Inflate(byte[] b, byte encoding, int length)
    {
        if (length < 0 || length > MaxLane || encoding > 1) throw new InvalidDataException("Lane bound");
        if (encoding == 0)
        {
            if (b.Length != length) throw new InvalidDataException("Raw lane");
            return b;
        }
        using var d = new DeflateStream(new MemoryStream(b, false), CompressionMode.Decompress);
        var a = new byte[length];
        int n = 0;
        while (n < length)
        {
            int k = d.Read(a, n, length - n);
            if (k == 0) throw new InvalidDataException("Short inflate");
            n += k;
        }
        if (d.ReadByte() != -1) throw new InvalidDataException("Long inflate");
        return a;
    }
    internal static bool ParseFailure(Exception e) => e is InvalidDataException or IOException or ArgumentException or OverflowException or InvalidOperationException;
    internal static ReplayFailure Failure(ReplayFailureCode code, Exception e) => new(code, e.Message, "", "");
}
public sealed class ReplayWriter : IDisposable
{
    readonly Stream output;
    readonly ReplayHeader header;
    readonly byte[] binding;
    readonly List<(uint Tick, WorldFramePacket Packet, byte[] ViewBytes)> pending = new();
    long bytes;
    uint? last,
    received;
    bool limit,
    disposed;
    ReplayWriteResult? completed;
    bool stopped;
    ReplayConclusion? requested;
    public long ExactBytesWritten { get; private set; }
    public long ViewBytesWritten { get; private set; }
    public ReplayWriter(Stream output, ReplayHeader header)
    {
        if (!output.CanWrite || (output.CanSeek && output.Position != 0)) throw new ArgumentException("Output");
        this.output = output;
        this.header = header with
        {
            Roster = Array.AsReadOnly(header.Roster.ToArray())
        };
        binding = ReplayStorage.Preamble(this.header);
        Write(binding);
    }
    void Write(byte[] b)
    {
        output.Write(b, 0, b.Length);
        bytes += b.Length;
    }
    public ReplayAppend Append(ReplayTickCapture tick)
    {
        if (stopped) return ReplayAppend.Stopped;
        if (disposed) throw new ObjectDisposedException(nameof(ReplayWriter));
        if (completed != null || limit) return ReplayAppend.Stopped;
        try
        {
            if (!binding.SequenceEqual(tick.Binding) || tick.Tick != (received.HasValue ? checked(received.Value + 1) : header.StartTick)) throw new ArgumentException("Capture binding or sequence");
            received = tick.Tick;
            pending.Add((tick.Tick, tick.Packet, ReplayViewCodec.Encode(tick.View)));
            if (pending.Count >= 60) Flush();
            return limit ? ReplayAppend.LimitReached : ReplayAppend.BufferedOrCommitted;
        }
        catch (Exception e) { Stop(e); return ReplayAppend.Stopped; }
    }
    void Flush()
    {
        if (pending.Count == 0) return;
        using var ex = new MemoryStream();
        using var vi = new MemoryStream();
        var index = ReplayBinary.Bytes(w =>
        {
            foreach (var t in pending)
            {
                var e = t.Packet.SerializeUncompressedForReplay();
                var v = t.ViewBytes;
                if (v.Length > ReplayViewCodec.MaxRecord) throw new InvalidDataException("View bound");
                w.Write((uint)ex.Length);
                w.Write(e.Length);
                w.Write((uint)vi.Length);
                w.Write(v.Length);
                ex.Write(e, 0, e.Length);
                vi.Write(v, 0, v.Length);
            }
        });
        var ep = ReplayStorage.Pack(ex.ToArray(), out byte ee);
        var vp = ReplayStorage.Pack(vi.ToArray(), out byte ve);
        int total = checked(56 + index.Length + ep.Length + vp.Length);
        uint first = pending[0].Tick;
        if (bytes + total + ReplayStorage.TrailerReserve > ReplayStorage.MaxFileBytes)
        {
            limit = true;
            pending.Clear();
            return;
        }
        var h = ReplayBinary.Bytes(w =>
        {
            w.Write(ReplayStorage.ChunkMagic);
            w.Write(total);
            w.Write(first);
            w.Write((ushort)pending.Count);
            w.Write(ee);
            w.Write(ve);
            w.Write((int)ex.Length);
            w.Write(ep.Length);
            w.Write((int)vi.Length);
            w.Write(vp.Length);
            w.Write(ReplayBinary.Crc(ep));
            w.Write(ReplayBinary.Crc(vp));
        });
        Write(h);
        Write(ReplayBinary.Bytes(w => w.Write(ReplayBinary.Crc(h.Concat(index).ToArray()))));
        Write(index);
        Write(ep);
        Write(vp);
        Write(ReplayBinary.Bytes(w =>
        {
            w.Write(ReplayStorage.CommitMagic);
            w.Write(first);
            w.Write(total);
        }));
        last = pending[pending.Count - 1].Tick;
        ExactBytesWritten += ep.Length;
        ViewBytesWritten += vp.Length;
        pending.Clear();
    }
    void Stop(Exception e)
    {
        stopped = true;
        pending.Clear();
        completed = new(last, new(ReplayEndReason.Cancelled, "writer-failed: " + e.Message, null), bytes, Stopped: true);
    }
    public ReplayWriteResult Complete(ReplayConclusion conclusion)
    {
        if (stopped) return completed!;
        if (disposed) throw new ObjectDisposedException(nameof(ReplayWriter));
        if (completed != null)
        {
            if (requested != conclusion) throw new InvalidOperationException("Conflicting conclusion");
            return completed;
        }
        try
        {
            requested = conclusion;
            Flush();
            if (limit) conclusion = new(ReplayEndReason.Limit, "byte-limit", null);
            var payload = ReplayBinary.Bytes(w =>
            {
                w.Write(last.HasValue);
                w.Write(last ?? 0);
                w.Write(ReplayMetadata.Write(conclusion));
            });
            if (payload.Length > 16384) throw new InvalidDataException("Trailer bound");
            var b = ReplayBinary.Bytes(w =>
            {
                w.Write(ReplayStorage.DoneMagic);
                w.Write(payload.Length);
                w.Write(payload);
            });
            Write(b);
            Write(ReplayBinary.Bytes(w =>
            {
                w.Write(ReplayBinary.Crc(b));
                w.Write(ReplayStorage.EndMagic);
            }));
            output.Flush();
            return completed = new(last, conclusion, bytes);
        }
        catch (Exception e) { Stop(e); return completed!; }
    }
    public void Dispose()
    {
        disposed = true;
        pending.Clear();
    }
}
public sealed class ReplayReader : IDisposable
{
    sealed record Chunk(uint First, ushort Count, long Position, int Total, byte ExactEncoding, byte ViewEncoding, int ExactRaw, int ExactStored, int ViewRaw, int ViewStored, int[] Index);
    readonly Stream input;
    readonly List<Chunk> chunks = new();
    readonly Dictionary<(long, bool),
    byte[]> cache = new();
    bool disposed;
    public ReplayHeader Header { get; private set; } = null!;
    public ReplayRange? Range => chunks.Count == 0 ? null : new(chunks[0].First, checked(chunks[chunks.Count - 1].First + (uint)chunks[chunks.Count - 1].Count - 1));
    public ReplayConclusion? Conclusion { get; private set; }
    public ReplayPrefixReason PrefixReason { get; private set; }
    ReplayReader(Stream input)
    {
        this.input = input;
    }
    public static ReplayOpenResult Open(Stream input) => Open(input, summary: false);

    /// <summary>A summary hops each chunk's lanes instead of reading them, so lane corruption is found only by a full Open.</summary>
    internal static ReplayOpenResult Open(Stream input, bool summary)
    {
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("Seekable input required");
        if (input.Length > ReplayStorage.MaxFileBytes) return new(null, new(ReplayFailureCode.FileTooLarge, "file", "64000000", input.Length.ToString()));
        var self = new ReplayReader(input);
        try
        {
                input.Position = 0;
                using var r = new BinaryReader(input, ReplayBinary.Utf8, true);
                var pre = ReplayBinary.Read(r, 24, 24);
                if (!pre.Take(8).SequenceEqual(new byte[] { 83, 76, 79, 80, 82, 69, 80, 0 })) throw new InvalidDataException("Magic");
                ushort cv = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(pre.AsSpan(8)),
     vv = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(pre.AsSpan(10));
                if (cv > 1 || vv > 1) return new(null, new(ReplayFailureCode.PleaseUpdate, "version", "1", $"{cv}/{vv}"));
                if (cv != 1 || vv != 1) throw new InvalidDataException("Version zero");
                int n = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(pre.AsSpan(12));
                uint crc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(pre.AsSpan(16));
                Array.Clear(pre, 16, 4);
                if (pre.Skip(20).Any(x => x != 0)) throw new InvalidDataException("Reserved");
                var meta = ReplayBinary.Read(r, n, 65536);
                if (ReplayBinary.Crc(pre.Concat(meta).ToArray()) != crc) throw new InvalidDataException("Header CRC");
                self.Header = ReplayMetadata.ReadReplayHeader(meta);
                ReplayStorage.Validate(self.Header);
                self.Scan(r, summary);
                return new(self, null);
        }
        catch (Exception e) when (ReplayStorage.ParseFailure(e))
        {
            return new(null, ReplayStorage.Failure(e is IOException && e is not InvalidDataException && e is not EndOfStreamException ? ReplayFailureCode.IoFailure : ReplayFailureCode.InvalidHeader, e));
        }
    }
    void Scan(BinaryReader r, bool summary)
    {
        bool shortChunk = false;
        while (input.Position < input.Length)
        {
            long pos = input.Position;
            try
            {
                uint magic = r.ReadUInt32();
                if (magic == ReplayStorage.DoneMagic)
                {
                    int n = r.ReadInt32();
                    if (n >= 0 && n <= 16384 && n > input.Length - input.Position) throw new EndOfStreamException();
                    var p = ReplayBinary.Read(r, n, 16384);
                    uint crc = r.ReadUInt32();
                    if (r.ReadUInt32() != ReplayStorage.EndMagic || input.Position != input.Length || crc != ReplayBinary.Crc(ReplayBinary.Bytes(w =>
                    {
                        w.Write(magic);
                        w.Write(n);
                        w.Write(p);
                    }))) throw new InvalidDataException("Trailer");
                    using var pr = ReplayBinary.Reader(p);
                    byte has = pr.ReadByte();
                    uint last = pr.ReadUInt32();
                    if (has > 1 || (has == 1) != (Range != null) || (has == 1 && last != Range!.Value.Last)) throw new InvalidDataException("Trailer range");
                    Conclusion = ReplayMetadata.ReadReplayConclusion(pr.ReadBytes(p.Length - 5));
                    PrefixReason = ReplayPrefixReason.None;
                    return;
                }
                if (magic != ReplayStorage.ChunkMagic || shortChunk || chunks.Count >= 65536) throw new InvalidDataException("Chunk magic/order");
                int total = r.ReadInt32();
                uint first = r.ReadUInt32();
                ushort count = r.ReadUInt16();
                byte ee = r.ReadByte(),
                ve = r.ReadByte();
                int er = r.ReadInt32(),
                es = r.ReadInt32(),
                vr = r.ReadInt32(),
                vs = r.ReadInt32();
                uint ec = r.ReadUInt32(),
                vc = r.ReadUInt32(),
                hc = r.ReadUInt32();
                if (count < 1 || count > 60 || first != (Range.HasValue ? checked(Range.Value.Last + 1) : Header.StartTick) || ee > 1 || ve > 1 || er < 0 || er > count * ReplayStorage.MaxExact || vr < 0 || vr > count * ReplayViewCodec.MaxRecord || es < 0 || es > er || vs < 0 || vs > vr || (ee == 0 && es != er) || (ve == 0 && vs != vr) || total != checked(56 + 16 * count + es + vs)) throw new InvalidDataException("Chunk bounds");
                _ = checked(first + count - 1u);
                if (total > input.Length - pos) throw new EndOfStreamException();
                var ib = ReplayBinary.Read(r, 16 * count, 960);
                long payload = input.Position;
                input.Position = pos;
                var h = ReplayBinary.Read(r, 40, 40);
                input.Position = payload;
                if (ReplayBinary.Crc(h.Concat(ib).ToArray()) != hc) throw new InvalidDataException("Index CRC");
                var idx = new int[count * 4];
                using (var ir = ReplayBinary.Reader(ib)) for (int i = 0; i < idx.Length; i++) idx[i] = ir.ReadInt32();
                int eo = 0,
                vo = 0;
                for (int i = 0; i < count; i++)
                {
                    int j = i * 4;
                    if (idx[j] != eo || idx[j + 2] != vo || idx[j + 1] < 16 || idx[j + 1] > ReplayStorage.MaxExact || idx[j + 3] < 4 || idx[j + 3] > ReplayViewCodec.MaxRecord) throw new InvalidDataException("Index");
                    eo = checked(eo + idx[j + 1]);
                    vo = checked(vo + idx[j + 3]);
                }
                if (eo != er || vo != vr) throw new InvalidDataException("Partition");
                if (summary) input.Position += es + vs;
                else
                {
                    var ep = ReplayBinary.Read(r, es, ReplayStorage.MaxLane);
                    var vp = ReplayBinary.Read(r, vs, ReplayStorage.MaxLane);
                    if (ReplayBinary.Crc(ep) != ec || ReplayBinary.Crc(vp) != vc) throw new InvalidDataException("Lane CRC");
                }
                if (r.ReadUInt32() != ReplayStorage.CommitMagic || r.ReadUInt32() != first || r.ReadInt32() != total) throw new InvalidDataException("Commit");
                chunks.Add(new(first, count, payload, total, ee, ve, er, es, vr, vs, idx));
                shortChunk = count < 60;
            }
            catch (Exception e) when (ReplayStorage.ParseFailure(e))
            {
                PrefixReason = e is EndOfStreamException ? ReplayPrefixReason.TruncatedTail : ReplayPrefixReason.CorruptTail;
                if (e is IOException && e is not EndOfStreamException && e is not InvalidDataException) throw;
                if (chunks.Count == 0 && e is not EndOfStreamException) throw new InvalidDataException("No committed prefix", e);
                return;
            }
        }
        PrefixReason = ReplayPrefixReason.CleanEofWithoutTrailer;
    }
    Chunk Find(uint tick)
    {
        int low = 0,
        high = chunks.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            var c = chunks[mid];
            if (tick < c.First) high = mid - 1;
            else if (tick - c.First >= c.Count) low = mid + 1;
            else return c;
        }
        throw new ArgumentOutOfRangeException(nameof(tick));
    }
    byte[] Record(uint tick, bool exact)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayReader));
        var c = Find(tick);
        var key = (c.Position, exact);
        if (!cache.TryGetValue(key, out var lane))
        {
            input.Position = c.Position + (exact ? 0 : c.ExactStored);
            using var r = new BinaryReader(input, ReplayBinary.Utf8, true);
            lane = ReplayStorage.Inflate(ReplayBinary.Read(r, exact ? c.ExactStored : c.ViewStored, ReplayStorage.MaxLane), exact ? c.ExactEncoding : c.ViewEncoding, exact ? c.ExactRaw : c.ViewRaw);
            if (cache.Count >= 2) cache.Clear();
            cache.Add(key, lane);
        }
        int i = checked((int)(tick - c.First) * 4) + (exact ? 0 : 2);
        var b = new byte[c.Index[i + 1]];
        Array.Copy(lane, c.Index[i], b, 0, b.Length);
        return b;
    }
    public ReplayViewReadResult ReadView(uint tick)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayReader));
        if (!Range.HasValue || tick < Range.Value.First || tick > Range.Value.Last) return new(null, new(ReplayFailureCode.TickOutOfRange, "tick", "", tick.ToString()));
        try
        {
                var view = ReplayViewCodec.Decode(Record(tick, false), tick);
                if (!view.Fighters.Select(f => f.EntityId).SequenceEqual(Header.Roster.Select(p => p.EntityId))) throw new InvalidDataException("View roster");
                return new(view, null);
        }
        catch (Exception e) when (ReplayStorage.ParseFailure(e))
        {
            return new(null, ReplayStorage.Failure(e is IOException && e is not EndOfStreamException ? ReplayFailureCode.IoFailure : ReplayFailureCode.InvalidView, e));
        }
    }
    internal WorldFramePacket ReadExact(uint tick)
    {
        var frame = WorldFramePacket.Deserialize(Record(tick, true));
        if (frame.Tick != tick || frame.MatchState != MatchState.Playing) throw new InvalidDataException("Exact indexed tick/phase");
        return frame;
    }
    public ReplayPlayerOpenResult CreatePlayer(ReplayEnvironment? exactEnvironment = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayReader));
        if (!Range.HasValue) return new(null, new(ReplayFailureCode.EmptyReplay, "range", "", ""));
        if (exactEnvironment == null) return new(new(this, null), null);
        var f = ReplayExact.Check(Header, exactEnvironment);
        if (f != null) return new(null, f);
        try
        {
                return new(new(this, ReplayExact.CreateShell(Header, exactEnvironment)), null);
        }
        catch (Exception e) when (ReplayStorage.ParseFailure(e))
        {
            return new(null, ReplayStorage.Failure(ReplayFailureCode.InvalidRules, e));
        }
    }
    public void Dispose()
    {
        disposed = true;
        cache.Clear();
    }
}
