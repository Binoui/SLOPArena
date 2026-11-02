using System;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

public enum ReplayCaptureStatus
{
    Accepted,
    Ignored,
    Sealed
}
public sealed record ReplayCaptureResult(ReplayCaptureStatus Status, ReplayTickCapture? Capture, uint? LastAcceptedTick, string Detail);
public sealed class ReplayTickCapture
{
    internal ReplayTickCapture(WorldFramePacket packet, ReplayViewTick view, byte[] binding)
    {
        Packet = packet;
        View = view;
        Binding = binding;
    }
    public uint Tick => Packet.Tick;
    public ReplayViewTick View { get; }
    internal WorldFramePacket Packet { get; }
    internal byte[] Binding { get; }
}
/// <summary>Match-thread projection and membership only. No IO, compression, threads or tasks.</summary>
public sealed partial class ReplayCapture
{
    readonly ReplayHeader header;
    readonly Dictionary<ulong, MatchContentEntry> rosterContent;
    readonly ArenaDefinition arena;
    readonly byte[] binding;
    readonly Dictionary<(ulong Target, ulong Owner, uint Tick, byte Slot), SpellResolver.HitResult> lastHits = new();
    readonly Dictionary<(ulong, byte, byte, bool), (ushort Total, ushort Left)> cooldownRuns = new();
    uint? last;
    bool sealedRecording;
    ServerSimulation? owner;
    public ReplayCapture(ReplayHeader header, MatchContentCatalog content, ArenaDefinition arena)
    {
        binding = ReplayStorage.Preamble(header);
        this.header = header with
        {
            Roster = Array.AsReadOnly(Copy(header.Roster))
        };
        rosterContent = new Dictionary<ulong, MatchContentEntry>();
        this.arena = arena;
        foreach (var p in header.Roster)
        {
            var entry = content.ResolvePackage(p.Package.PackageId);
            if (entry?.Identity != p.Package) throw new ArgumentException("Roster package");
            rosterContent.Add(p.EntityId, entry!);
        }
        if (arena.Name != header.ArenaId) throw new ArgumentException("Arena");
    }
    public ReplayCaptureResult Capture(ServerSimulation sim, WorldFramePacket frame)
    {
        ReplayCaptureResult Result(ReplayCaptureStatus status, string detail, ReplayTickCapture? capture = null) => new(status, capture, last, detail);
        try
        {
            if (sealedRecording) return Result(ReplayCaptureStatus.Ignored, "sealed");
            if (frame.MatchState == MatchState.Ended)
            {
                sealedRecording = true;
                return Result(ReplayCaptureStatus.Sealed, "ended");
            }
            if (!last.HasValue && frame.MatchState != MatchState.Playing) return Result(ReplayCaptureStatus.Ignored, "before-playing");
            if (frame.Tick == last) return Result(ReplayCaptureStatus.Ignored, "repeated-tick");
            if ((last.HasValue && (last == uint.MaxValue || frame.Tick != last.Value + 1)) || (!last.HasValue && frame.Tick != header.StartTick))
            {
                sealedRecording = true;
                return Result(ReplayCaptureStatus.Sealed, "noncontiguous-tick");
            }
            if (sim.CurrentTick != frame.Tick || (owner != null && !ReferenceEquals(owner, sim))) throw new ArgumentException("Capture simulation/tick");
            if (owner == null)
            {
                if (sim.GetAllStates().Count != header.Roster.Count) throw new ArgumentException("Simulation roster");
                foreach (var p in header.Roster) if (!sim.GetAllStates().ContainsKey(p.EntityId)) throw new ArgumentException("Simulation roster");
                foreach (var p in header.Roster) if (!ReferenceEquals(sim.GetDefinition(p.EntityId), rosterContent[p.EntityId].Definition.ForForm(sim.GetState(p.EntityId).Form.Active))) throw new ArgumentException("Simulation content");
            }
            owner = sim;
            var packet = new WorldFramePacket(frame.MatchState, new WorldSnapshot((byte[])frame.Snapshot.Data.Clone()), Own(frame.Inputs), Own(frame.Events), Own(frame.Hits), Own(frame.SlopballEvents), Own(frame.CourseEvents), Own(frame.MayhemEvents));
            var view = ProjectView(sim, frame);
            foreach (var death in view.Deaths)
            {
                expiredHits.Clear();
                foreach (var key in lastHits.Keys) if (key.Target == death.EntityId) expiredHits.Add(key);
                foreach (var key in expiredHits) lastHits.Remove(key);
            }
            last = frame.Tick;
            return Result(ReplayCaptureStatus.Accepted, "", new(packet, view, binding));
        }
        catch (Exception e)
        {
            sealedRecording = true;
            return Result(ReplayCaptureStatus.Sealed, "projection-failed: " + e.Message);
        }
    }
    (string Package, string Form, string Slot, string Name) Move(ulong owner, byte form, byte slot, bool air)
    {
        if (!rosterContent.TryGetValue(owner, out var entry)) return ("", ReplaySemanticIds.Form(form), "", "");
        var move = slot == 0 ? null : rosterContent[owner].Definition.ForForm(form).GetCookedSlotAbility(slot, air);
        return (entry.Identity.PackageId, ReplaySemanticIds.Form(form), move?.Id ?? "", move?.Name ?? "");
    }
    string ShapePresentation(Hitbox h)
    {
        if (!rosterContent.TryGetValue(h.OwnerId, out var entry)) return "";
        var key = (entry.Identity.PackageId, h.Form, h.AttackSlot, h.ActivationAirborne, h.VisualOperationIndex);
        if (presentations.TryGetValue(key, out var id)) return id;
        id = "";
        var move = entry.Definition.ForForm(h.Form).GetCookedSlotAbility(h.AttackSlot, h.ActivationAirborne);
        if (move != null)
            foreach (var stage in move.Timeline.Stages)
                foreach (var op in stage.Operations)
                    if (op is CookedEmitPresentationOperation emit && emit.OperationIndex == h.VisualOperationIndex) { id = emit.PresentationId; goto Found; }
        Found:
        presentations.Add(key, id);
        return id;
    }
    readonly Dictionary<(string, byte, byte, bool, int), string> presentations = new();
    readonly List<(ulong Target, ulong Owner, uint Tick, byte Slot)> expiredHits = new();
    // Empty inputs/history are immutable values; nonempty caller-owned buffers may be reused.
    static IReadOnlyList<T> Own<T>(IReadOnlyList<T> source) => source.Count == 0 ? Array.Empty<T>() : Array.AsReadOnly(Copy(source));
    static T[] Copy<T>(IReadOnlyList<T> source)
    {
        var copy = new T[source.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
        return copy;
    }
    static ReplayVector V(float x, float y, float z) => new(x, y, z);
    static ReplayVector V(ArcadePoint p) => new(p.X, p.Y, p.Z);
}
