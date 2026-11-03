using System;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

public sealed record ReplayEnvironment(string MatchContentHash, MatchContentCatalog Catalog,
 IReadOnlyDictionary<string, ArenaDefinition> Arenas, IReadOnlyDictionary<string, string>? ArenaDigests = null);
public enum ReplayDelivery
{
    Warmup,
    Live
}
public interface IReplaySink
{
    void Reset(uint targetTick);
    void Present(ReplayDisplay display, ReplayDelivery delivery);
}
/// <summary>ExactSimulation is borrowed read-only until the next player call. Never Tick it.</summary>
public sealed record ReplayDisplay(ReplayViewTick View, ServerSimulation? ExactSimulation, uint TicksBeforeTarget);
public sealed class ReplayPlayer
{
    readonly ReplayReader reader;
    readonly ServerSimulation? shell;
    ReplayFailure? failed;
    public uint? CurrentTick { get; private set; }
    internal ServerSimulation? Shell => shell;
    internal ReplayPlayer(ReplayReader reader, ServerSimulation? shell)
    {
        this.reader = reader;
        this.shell = shell;
    }
    ReplayFailure? Present(uint tick, IReplaySink sink, ReplayDelivery delivery, uint age)
    {
        var v = reader.ReadView(tick);
        if (v.Failure != null) return v.Failure;
        try
        {
            if (shell != null) shell.RestoreSnapshot(reader.ReadExact(tick).Snapshot);
        }
        catch (Exception e) when (ReplayStorage.ParseFailure(e))
        {
            return ReplayStorage.Failure(ReplayFailureCode.InvalidExact, e);
        }
        sink.Present(new(v.Tick!, shell, age), delivery);
        CurrentTick = tick;
        return null;
    }
    public ReplayFailure? Seek(uint tick, IReplaySink sink)
    {
        var v = reader.ReadView(tick);
        if (v.Failure != null)
        {
            if (v.Failure.Code != ReplayFailureCode.TickOutOfRange) failed = v.Failure;
            return v.Failure;
        }
        // Admit the target Exact snapshot before clearing presentation history.
        try
        {
            if (shell != null) shell.RestoreSnapshot(reader.ReadExact(tick).Snapshot);
        }
        catch (Exception e) when (ReplayStorage.ParseFailure(e))
        {
            return failed = ReplayStorage.Failure(ReplayFailureCode.InvalidExact, e);
        }
        sink.Reset(tick);
        CurrentTick = null;
        failed = null;
        uint first = Math.Max(reader.Range!.Value.First, tick >= 30 ? tick - 30 : 0);
        for (uint t = first; ; t++)
        {
            failed = Present(t, sink, ReplayDelivery.Warmup, tick - t);
            if (failed != null || t == tick) return failed;
        }
    }
    public ReplayStepResult Step(IReplaySink sink)
    {
        if (failed != null) return new(ReplayStep.Failed, failed);
        var range = reader.Range!.Value;
        if (CurrentTick == range.Last) return new(ReplayStep.End, null);
        uint next = CurrentTick.HasValue ? checked(CurrentTick.Value + 1) : range.First;
        if (!CurrentTick.HasValue) sink.Reset(next);
        failed = Present(next, sink, ReplayDelivery.Live, 0);
        return new(failed == null ? ReplayStep.Presented : ReplayStep.Failed, failed);
    }
}
internal static class ReplayExact
{
    internal static ReplayFailure? Check(ReplayHeader h, ReplayEnvironment e)
    {
        ReplayFailure? Compare(uint actual, uint expected, ReplayFailureCode code) => actual == expected ? null : new(code, "build", expected.ToString(), actual.ToString());
        var f = Compare(h.Build.Protocol, SimulationProtocol.Version, ReplayFailureCode.ProtocolMismatch) ?? Compare(h.Build.FrameFormat, WorldFramePacket.FormatVersion, ReplayFailureCode.FrameFormatMismatch) ?? Compare(h.Build.SnapshotFormat, ServerSimulation.SnapshotFormatVersion, ReplayFailureCode.SnapshotFormatMismatch);
        if (f != null) return f;
        if (h.Build.ContentHash != e.MatchContentHash) return new(ReplayFailureCode.ContentHashMismatch, "content", e.MatchContentHash, h.Build.ContentHash);
        if (!e.Arenas.TryGetValue(h.ArenaId, out var arena) || arena.Name != h.ArenaId) return new(ReplayFailureCode.ArenaUnavailable, h.ArenaId, "available", "");
        if (h.ArenaDigest != "" && (e.ArenaDigests == null || !e.ArenaDigests.TryGetValue(h.ArenaId, out var digest) || digest != h.ArenaDigest)) return new(ReplayFailureCode.ArenaIdentityMismatch, h.ArenaId, h.ArenaDigest, "");
        foreach (var p in h.Roster)
        {
            var c = e.Catalog.ResolvePackage(p.Package.PackageId);
            if (c == null) return new(ReplayFailureCode.PackageUnavailable, p.Package.PackageId, "available", "");
            if (c.Identity != p.Package) return new(ReplayFailureCode.PackageIdentityMismatch, p.Package.PackageId, p.Package.Version, c.Identity.Version);
        }
        try
        {
            _ = Rule(h, arena);
        }
        catch (Exception ex) when (ReplayStorage.ParseFailure(ex))
        {
            return ReplayStorage.Failure(ReplayFailureCode.InvalidRules, ex);
        }
        return null;
    }
    internal static IMatchRule Rule(ReplayHeader h, ArenaDefinition arena)
    {
        if (!MatchFormats.TryParseWire(h.Rules.FormatId, out var format)) throw new ArgumentException("Format");
        // MatchFormats.Admits currently omits Scramble, whose arena contract is the course contract.
        bool admitted = format switch
        {
            MatchFormat.Stock => arena.Purpose == ArenaPurpose.PvP,
            MatchFormat.SlopballCoop or MatchFormat.SlopballVersus => arena.Arcade?.Slopball != null,
            MatchFormat.CourseRace or MatchFormat.CourseCoop or MatchFormat.CourseScramble => arena.Arcade?.Course != null,
            MatchFormat.MankiMayhem => arena.Arcade?.Mayhem != null,
            _ => false
        };
        if (!admitted) throw new ArgumentException("Arena format");
        var ff = h.Rules.FriendlyFireId switch
        {
            "off" => FriendlyFireMode.Off,
            "shoves" => FriendlyFireMode.Shoves,
            "on" => FriendlyFireMode.On,
            _ => throw new ArgumentException("Friendly fire")
        };
        if (!format.AdmitsFriendlyFire(ff) || (h.Rules.BallOnly && format != MatchFormat.SlopballVersus)) throw new ArgumentException("Rules");
        var camps = new Dictionary<ulong,
      SlopballCamp>();
        foreach (var p in h.Roster)
        {
            if (!SlopballCamps.TryParse(p.CampId, out var c)) throw new ArgumentException("Camp");
            camps.Add(p.EntityId, c);
        }
        return format.CreateRule(h.Rules.MaxStocks, ff, camps, h.Rules.BallOnly);
    }
    internal static ServerSimulation CreateShell(ReplayHeader h, ReplayEnvironment e)
    {
        var arena = e.Arenas[h.ArenaId];
        var s = new ServerSimulation(arena, Rule(h, arena)) { ReplayDisplay = true };
        foreach (var p in h.Roster)
        {
            var c = e.Catalog.ResolvePackage(p.Package.PackageId)!;
            s.RegisterEntity(p.EntityId, c.Definition, new CharacterState
            {
                EntityId = p.EntityId,
                Form = new FormState
                {
                    Active = (byte)(p.FormId == "alternate" ? 1 : 0)
                }
            }
            , c.BakedAnimation);
        }
        return s;
    }
}
