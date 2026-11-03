using System;
using System.Collections.Generic;
using System.IO;

namespace SlopArena.Shared.Replays
{
    public enum ReplayTrack { Exact, View }

    /// <summary>Best plays Exact when the gate passes, else View. ViewOnly forces View, to compare the tracks by eye.</summary>
    public enum ReplayTrackChoice { Best, ViewOnly }

    /// <summary>Today's baked arena with the SHA-256 digest of its baked bytes, as the Exact gate compares it.</summary>
    public sealed record ReplayArena(ArenaDefinition Definition, string Digest);

    /// <summary>
    /// Today's installed content, supplied by the host app. Never historical content. LoadArena returns null for an
    /// unknown arena. StandInPackageId names the fighter drawn for a recorded package this build lacks.
    /// </summary>
    public sealed record ReplayContent(MatchContentCatalog Catalog, Func<string, ReplayArena?> LoadArena, string StandInPackageId);

    /// <summary>Recorded: identity equals the file. Today: same package ID, other version. StandIn: package missing.</summary>
    public enum ReplayCastKind { Recorded, Today, StandIn }

    /// <summary>One fighter as this build draws it. Content is what the display world registered for EntityId.</summary>
    public sealed record ReplayCastMember(ulong EntityId, ReplayParticipant Recorded, MatchContentEntry Content,
        ReplayCastKind Kind);

    /// <summary>None: no active move. ByName/BySlot: today's move found. Missing: neutral pose, recorded name shown.</summary>
    public enum ReplayMoveMatch { None, ByName, BySlot, Missing }

    public readonly record struct ReplayMoveLabel(ulong EntityId, string RecordedName, ReplayMoveMatch Match);

    /// <summary>
    /// What a presenter receives for one tick. World is the session's display world restored to View.Tick, with that
    /// tick's hits, deaths and presentation events. Read it as a live simulation; never tick it (Tick throws).
    /// </summary>
    public readonly struct ReplayFrame
    {
        public ReplayFrame(ServerSimulation world, ReplayViewTick view, ReplayDelivery delivery,
            uint ticksBeforeTarget, IReadOnlyList<ReplayMoveLabel> moves)
        {
            World = world;
            View = view;
            Delivery = delivery;
            TicksBeforeTarget = ticksBeforeTarget;
            Moves = moves;
        }

        public ServerSimulation World { get; }
        public ReplayViewTick View { get; }
        public ReplayDelivery Delivery { get; }
        public uint TicksBeforeTarget { get; }
        public IReadOnlyList<ReplayMoveLabel> Moves { get; }
    }

    /// <summary>
    /// Seek calls Reset once, then Present for max(first, target-30)..target as Warmup, the target last with
    /// TicksBeforeTarget 0. Step calls Present once as Live. A refused seek calls neither.
    /// </summary>
    public interface IReplayPresenter
    {
        void Reset(uint targetTick);
        void Present(in ReplayFrame frame);
    }

    public sealed record ReplayWorldOpenResult(ReplayWorld? World, ReplayFailure? Failure);

    /// <summary>
    /// A Replay session for viewers and the only public way to get a display world. It owns the file stream, the
    /// reader, the cursor, the cast and one ServerSimulation restored from either track for each presented tick.
    /// File, content and decode problems come back as ReplayFailure. One caller at a time.
    /// </summary>
    public sealed class ReplayWorld : IDisposable
    {
        readonly Stream file;
        readonly ReplayReader reader;
        readonly ReplayPlayer player;
        readonly ReplayCastIndex cast;
        readonly ServerSimulation? viewShell;
        readonly Sink sink;
        IReplayPresenter? presenting;
        ReplayFailure? decodeFailure;

        ReplayWorld(Stream file, ReplayReader reader, ReplayPlayer player, ReplayCastIndex cast, ServerSimulation world,
            ServerSimulation? viewShell, ReplayTrack track, ReplayFailure? exactRefusal, ArenaDefinition arena)
        {
            this.file = file;
            this.reader = reader;
            this.player = player;
            this.cast = cast;
            this.viewShell = viewShell;
            World = world;
            Track = track;
            ExactRefusal = exactRefusal;
            Arena = arena;
            sink = new Sink(this);
        }

        /// <summary>
        /// Takes ownership of <paramref name="file"/>, which must be readable and seekable. Exact needs the gate to
        /// pass and <paramref name="choice"/> Best; View needs today's arena for the recorded arena ID.
        /// </summary>
        public static ReplayWorldOpenResult Open(Stream file, ReplayContent content, ReplayTrackChoice choice = ReplayTrackChoice.Best)
        {
            ReplayReader? reader = null;
            try
            {
                var opened = ReplayReader.Open(file);
                if (opened.Failure != null) return Fail(opened.Failure);
                reader = opened.Reader!;
                var header = reader.Header;
                if (!reader.Range.HasValue) return Fail(new(ReplayFailureCode.EmptyReplay, "range", "", ""));
                var arena = content.LoadArena(header.ArenaId);
                if (arena == null) return Fail(new(ReplayFailureCode.ArenaUnavailable, header.ArenaId, "available", ""));
                var cast = ReplayCastIndex.Build(header, content);
                if (cast == null) return Fail(new(ReplayFailureCode.PackageUnavailable, content.StandInPackageId, "stand-in", ""));
                ReplayFailure? refusal = null;
                if (choice == ReplayTrackChoice.Best)
                {
                    var exact = reader.CreatePlayer(ReplayExactEnvironment.For(content, arena));
                    if (exact.Player is { Shell: { } shell } exactPlayer)
                        return new(new ReplayWorld(file, reader, exactPlayer, cast, shell, null, ReplayTrack.Exact, null, arena.Definition), null);
                    refusal = exact.Failure;
                }
                var view = reader.CreatePlayer();
                if (view.Failure != null) return Fail(view.Failure);
                var world = ReplayShells.Create(header, arena.Definition, cast);
                return new(new ReplayWorld(file, reader, view.Player!, cast, world, world, ReplayTrack.View, refusal, arena.Definition), null);
            }
            catch (Exception e) when (ReplayStorage.ParseFailure(e))
            {
                return Fail(ReplayStorage.Failure(ReplayFailureCode.InvalidRules, e));
            }

            ReplayWorldOpenResult Fail(ReplayFailure failure)
            {
                reader?.Dispose();
                file.Dispose();
                return new(null, failure);
            }
        }

        public ReplayHeader Header => reader.Header;
        public ReplayRange Range => reader.Range!.Value;
        /// <summary>Null means a Partial Replay with unknown outcome (no trailer).</summary>
        public ReplayConclusion? Conclusion => reader.Conclusion;
        public ReplayPrefixReason PrefixReason => reader.PrefixReason;
        public ReplayTrack Track { get; }
        /// <summary>Why this file plays View on this build. Null on Exact, or when the caller chose ViewOnly.</summary>
        public ReplayFailure? ExactRefusal { get; }
        public IReadOnlyList<ReplayCastMember> Cast => cast.Members;
        public ArenaDefinition Arena { get; }
        /// <summary>Stable for the whole session, so presenters may keep it.</summary>
        public ServerSimulation World { get; }
        /// <summary>Recorded duration for the slot resolved by the same cast policy as View restoration.</summary>
        public ushort? CooldownDuration(ReplayFighter fighter, byte wireSlot)
        {
            ushort? duration = null;
            foreach (var cooldown in fighter.Cooldowns)
                if (cast.Move(fighter.EntityId, cooldown.PackageId, cooldown.FormId, cooldown.MoveName,
                    cooldown.SlotId, false) is { Slot: not null } resolved && resolved.WireSlot == wireSlot)
                    duration = cooldown.DurationTicks > 0 ? cooldown.DurationTicks : null;
            return duration;
        }

        public uint? CurrentTick => player.CurrentTick;

        public ReplayFailure? Seek(uint tick, IReplayPresenter presenter)
        {
            presenting = presenter;
            decodeFailure = null;
            try
            {
                return player.Seek(tick, sink) ?? decodeFailure;
            }
            finally { presenting = null; }
        }

        public ReplayStepResult Step(IReplayPresenter presenter)
        {
            if (decodeFailure != null) return new(ReplayStep.Failed, decodeFailure);
            presenting = presenter;
            try
            {
                var result = player.Step(sink);
                return decodeFailure != null ? new(ReplayStep.Failed, decodeFailure) : result;
            }
            finally { presenting = null; }
        }

        public void Dispose()
        {
            reader.Dispose();
            file.Dispose();
        }

        /// <summary>Private so no caller can drive presentation around the decoder.</summary>
        sealed class Sink : IReplaySink
        {
            readonly ReplayWorld owner;
            internal Sink(ReplayWorld owner) => this.owner = owner;

            public void Reset(uint targetTick) => owner.presenting!.Reset(targetTick);

            public void Present(ReplayDisplay display, ReplayDelivery delivery)
            {
                if (owner.decodeFailure != null) return;
                try
                {
                    // Decoding builds every value before the display world changes, so a failure leaves the last tick shown.
                    if (display.ExactSimulation == null)
                        owner.viewShell!.ApplySnapshot(ReplayViewDecoder.Prepare(owner.viewShell, display.View, owner.cast, owner.Arena));
                    owner.World.ApplyTickOutputs(ReplayViewDecoder.Outputs(display.View, owner.cast, owner.Arena));
                }
                catch (Exception e) when (ReplayStorage.ParseFailure(e) || e is KeyNotFoundException or IndexOutOfRangeException)
                {
                    owner.decodeFailure = ReplayStorage.Failure(ReplayFailureCode.InvalidView, e);
                    return;
                }
                owner.presenting!.Present(new ReplayFrame(owner.World, display.View, delivery, display.TicksBeforeTarget,
                    owner.cast.Labels(display.View)));
            }
        }
    }
}
