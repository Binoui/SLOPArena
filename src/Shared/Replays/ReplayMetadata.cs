using System;
using System.IO;
using System.Collections.Generic;
using static SlopArena.Shared.Replays.ReplayFields;
namespace SlopArena.Shared.Replays;

internal static class ReplayMetadata
{
    internal static readonly Table<ReplayBuildIdentity> ReplayBuildIdentityTable = new(() => new("", default!, default!, default!, ""),
        Field<ReplayBuildIdentity, string>(1, Text, x => x.Build, (x,v) => x with { Build = v }, "Build"),
        Field<ReplayBuildIdentity, uint>(2, U32, x => x.Protocol, (x,v) => x with { Protocol = v }, "Protocol"),
        Field<ReplayBuildIdentity, uint>(3, U32, x => x.FrameFormat, (x,v) => x with { FrameFormat = v }, "FrameFormat"),
        Field<ReplayBuildIdentity, uint>(4, U32, x => x.SnapshotFormat, (x,v) => x with { SnapshotFormat = v }, "SnapshotFormat"),
        Field<ReplayBuildIdentity, string>(5, Text, x => x.ContentHash, (x,v) => x with { ContentHash = v }, "ContentHash"));
    internal static byte[] Write(ReplayBuildIdentity x) => ReplayBuildIdentityTable.Write(x);
    internal static ReplayBuildIdentity ReadReplayBuildIdentity(byte[] bytes)
    {
        return ReplayBuildIdentityTable.Read(bytes);
    }
    internal static readonly Table<ReplayParticipant> ReplayParticipantTable = new(() => new(default!, "", "", default!, "", "", ""),
        Field<ReplayParticipant, ulong>(1, U64, x => x.EntityId, (x,v) => x with { EntityId = v }, "EntityId"),
        Field<ReplayParticipant, string>(2, Text, x => x.ParticipantProvider, (x,v) => x with { ParticipantProvider = v }, "ParticipantProvider"),
        Field<ReplayParticipant, string>(3, Text, x => x.ParticipantSubject, (x,v) => x with { ParticipantSubject = v }, "ParticipantSubject"),
        Field<ReplayParticipant, MatchContentIdentity>(4, Obj((w, x) => MatchContentIdentityTable.WriteTo(w, x), ReadMatchContentIdentity), x => x.Package, (x,v) => x with { Package = v }, "Package"),
        Field<ReplayParticipant, string>(5, Text, x => x.FormId, (x,v) => x with { FormId = v }, "FormId"),
        Field<ReplayParticipant, string>(6, Text, x => x.CampId, (x,v) => x with { CampId = v }, "CampId"),
        Field<ReplayParticipant, string>(7, Text, x => x.ColourId, (x,v) => x with { ColourId = v }, "ColourId"),
        Field<ReplayParticipant, string>(8, Text, x => x.DisplayName, (x,v) => x with { DisplayName = v }, "DisplayName"));
    internal static byte[] Write(ReplayParticipant x) => ReplayParticipantTable.Write(x);
    internal static ReplayParticipant ReadReplayParticipant(byte[] bytes)
    {
        Require(bytes, 0xfe);
        return ReplayParticipantTable.Read(bytes);
    }
    internal static readonly Table<ReplayRules> ReplayRulesTable = new(() => new("", "", default!, default!),
        Field<ReplayRules, string>(1, Text, x => x.FormatId, (x,v) => x with { FormatId = v }, "FormatId"),
        Field<ReplayRules, string>(2, Text, x => x.FriendlyFireId, (x,v) => x with { FriendlyFireId = v }, "FriendlyFireId"),
        Field<ReplayRules, byte>(3, U8, x => x.MaxStocks, (x,v) => x with { MaxStocks = v }, "MaxStocks"),
        Field<ReplayRules, bool>(4, Bool, x => x.BallOnly, (x,v) => x with { BallOnly = v }, "BallOnly"));
    internal static byte[] Write(ReplayRules x) => ReplayRulesTable.Write(x);
    internal static ReplayRules ReadReplayRules(byte[] bytes)
    {
        Require(bytes, 0x1e);
        return ReplayRulesTable.Read(bytes);
    }
    internal static readonly Table<ReplayHeader> ReplayHeaderTable = new(() => new("", "", default!, default!, Array.Empty<ReplayParticipant>(), default!, ""),
        Field<ReplayHeader, string>(1, Text, x => x.MatchId, (x,v) => x with { MatchId = v }, "MatchId"),
        Field<ReplayHeader, string>(2, Text, x => x.ArenaId, (x,v) => x with { ArenaId = v }, "ArenaId"),
        Field<ReplayHeader, ReplayRules>(3, Obj((w, x) => ReplayRulesTable.WriteTo(w, x), ReadReplayRules), x => x.Rules, (x,v) => x with { Rules = v }, "Rules"),
        Field<ReplayHeader, uint>(4, U32, x => x.StartTick, (x,v) => x with { StartTick = v }, "StartTick"),
        Field<ReplayHeader, IReadOnlyList<ReplayParticipant>>(5, List(Obj((w, x) => ReplayParticipantTable.WriteTo(w, x), ReadReplayParticipant), 4), x => x.Roster, (x,v) => x with { Roster = v }, "Roster"),
        Field<ReplayHeader, ReplayBuildIdentity>(6, Obj((w, x) => ReplayBuildIdentityTable.WriteTo(w, x), ReadReplayBuildIdentity), x => x.Build, (x,v) => x with { Build = v }, "Build"),
        Field<ReplayHeader, string>(7, Text, x => x.ArenaDigest, (x,v) => x with { ArenaDigest = v }, "ArenaDigest"),
        Field<ReplayHeader, ushort>(8, U16, x => ReplayHeader.TickRate, (x,v) => v == 60 ? x : throw new InvalidDataException("Tick rate"), "TickRate"),
        Field<ReplayHeader, MatchContentHandleMap?>(9, ContentMap, x => x.ContentMap, (x,v) => x with { ContentMap = v }, "ContentMap"));
    internal static byte[] Write(ReplayHeader x) => ReplayHeaderTable.Write(x, x.ContentMap == null ? (ushort)9 : null);
    internal static ReplayHeader ReadReplayHeader(byte[] bytes)
    {
        Require(bytes, 0x17e);
        Require(bytes, 1u << 8);
        return ReplayHeaderTable.Read(bytes);
    }
    internal static readonly Table<ReplayStanding> ReplayStandingTable = new(() => new(default!, default!, default!, default!, default!, default!, default!),
        Field<ReplayStanding, ulong>(1, U64, x => x.EntityId, (x,v) => x with { EntityId = v }, "EntityId"),
        Field<ReplayStanding, byte>(2, U8, x => x.Deaths, (x,v) => x with { Deaths = v }, "Deaths"),
        Field<ReplayStanding, byte>(3, U8, x => x.KOs, (x,v) => x with { KOs = v }, "KOs"),
        Field<ReplayStanding, ushort>(4, U16, x => x.Score, (x,v) => x with { Score = v }, "Score"),
        Field<ReplayStanding, byte>(5, U8, x => x.Place, (x,v) => x with { Place = v }, "Place"),
        Field<ReplayStanding, bool>(6, Bool, x => x.Withdrawn, (x,v) => x with { Withdrawn = v }, "Withdrawn"),
        Field<ReplayStanding, ulong>(7, U64, x => x.CompletedTicks, (x,v) => x with { CompletedTicks = v }, "CompletedTicks"));
    internal static byte[] Write(ReplayStanding x) => ReplayStandingTable.Write(x);
    internal static ReplayStanding ReadReplayStanding(byte[] bytes)
    {
        return ReplayStandingTable.Read(bytes);
    }
    internal static readonly Table<ReplayMatchResult> ReplayMatchResultTable = new(() => new(Array.Empty<ulong>(), "", default!, Array.Empty<ReplayStanding>(), default!, default!, default!, ""),
        Field<ReplayMatchResult, IReadOnlyList<ulong>>(1, List(U64, 4), x => x.WinnerIds, (x,v) => x with { WinnerIds = v }, "WinnerIds"),
        Field<ReplayMatchResult, string>(2, Text, x => x.WinningCampId, (x,v) => x with { WinningCampId = v }, "WinningCampId"),
        Field<ReplayMatchResult, bool>(3, Bool, x => x.Draw, (x,v) => x with { Draw = v }, "Draw"),
        Field<ReplayMatchResult, IReadOnlyList<ReplayStanding>>(4, List(Obj((w, x) => ReplayStandingTable.WriteTo(w, x), ReadReplayStanding), 4), x => x.Standings, (x,v) => x with { Standings = v }, "Standings"),
        Field<ReplayMatchResult, ulong>(5, U64, x => x.TeamReturns, (x,v) => x with { TeamReturns = v }, "TeamReturns"),
        Field<ReplayMatchResult, ushort>(6, U16, x => x.TealScore, (x,v) => x with { TealScore = v }, "TealScore"),
        Field<ReplayMatchResult, ushort>(7, U16, x => x.ClayScore, (x,v) => x with { ClayScore = v }, "ClayScore"),
        Field<ReplayMatchResult, string>(8, Text, x => x.TerminalModePhaseId, (x,v) => x with { TerminalModePhaseId = v }, "TerminalModePhaseId"));
    internal static byte[] Write(ReplayMatchResult x) => ReplayMatchResultTable.Write(x);
    internal static ReplayMatchResult ReadReplayMatchResult(byte[] bytes)
    {
        return ReplayMatchResultTable.Read(bytes);
    }
    internal static readonly Table<ReplayConclusion> ReplayConclusionTable = new(() => new((ReplayEndReason)(-1), "", default!),
        Field<ReplayConclusion, ReplayEndReason>(1, Reason, x => x.Reason, (x,v) => x with { Reason = v }, "Reason"),
        Field<ReplayConclusion, string>(2, Text, x => x.Detail, (x,v) => x with { Detail = v }, "Detail"),
        Field<ReplayConclusion, ReplayMatchResult?>(3, OptionalResult, x => x.Result, (x,v) => x with { Result = v }, "Result"));
    internal static byte[] Write(ReplayConclusion x) => ReplayConclusionTable.Write(x);
    internal static ReplayConclusion ReadReplayConclusion(byte[] bytes)
    {
        Require(bytes, 1u << 1);
        return ReplayConclusionTable.Read(bytes);
    }
    internal static readonly Table<MatchContentIdentity> MatchContentIdentityTable = new(() => new("", "", "", "", ""),
        Field<MatchContentIdentity, string>(1, Text, x => x.PackageId, (x,v) => x with { PackageId = v }, "PackageId"),
        Field<MatchContentIdentity, string>(2, Text, x => x.Version, (x,v) => x with { Version = v }, "Version"),
        Field<MatchContentIdentity, string>(3, Text, x => x.SourceHash, (x,v) => x with { SourceHash = v }, "SourceHash"),
        Field<MatchContentIdentity, string>(4, Text, x => x.CookedContentHash, (x,v) => x with { CookedContentHash = v }, "CookedContentHash"),
        Field<MatchContentIdentity, string>(5, Text, x => x.PackageHash, (x,v) => x with { PackageHash = v }, "PackageHash"));
    internal static byte[] Write(MatchContentIdentity x) => MatchContentIdentityTable.Write(x);
    internal static MatchContentIdentity ReadMatchContentIdentity(byte[] bytes)
    {
        return MatchContentIdentityTable.Read(bytes);
    }
    static void Require(byte[] bytes, uint mask)
    {
        uint present = 0;
        foreach (var f in ReplayBinary.Fields(bytes)) if (f.Key < 32) present |= 1u << f.Key;
        if ((present & mask) != mask) throw new InvalidDataException("Missing metadata field");
    }
    static Codec<MatchContentHandleMap?> ContentMap => new((w, map) =>
    {
        if (map != null) w.Write(ReplayBinary.Utf8.GetBytes(MatchContentHandleMapCodec.Serialize(map)));
    }, bytes => bytes.Length == 0 ? null : MatchContentHandleMapCodec.TryParse(ReplayBinary.Utf8.GetString(bytes), out var map)
        && map?.SchemaVersion == MatchContentHandleMap.LocalSchemaVersion ? map : throw new InvalidDataException("Content map"));

    static Codec<ReplayEndReason> Reason => new((w, v) => Text.Write(w, v switch { ReplayEndReason.Finished => "finished", ReplayEndReason.Cancelled => "cancelled", ReplayEndReason.Limit => "limit", _ => throw new InvalidDataException("Reason") }), b => ReplayBinary.Text(b) switch { "finished" => ReplayEndReason.Finished, "cancelled" => ReplayEndReason.Cancelled, "limit" => ReplayEndReason.Limit, _ => throw new InvalidDataException("Reason") });
    static Codec<ReplayMatchResult?> OptionalResult => new((w, v) => { if (v != null) ReplayMatchResultTable.WriteTo(w, v); }, b => b.Length == 0 ? null : ReadReplayMatchResult(b));
}
