using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>Intro, then call, play and verdict per channel, a SPEED UP! beat before acts two and three, then Ended.</summary>
public enum MayhemPhase : byte { Intro, Call, Play, Verdict, SpeedUp, Ended }

public enum SpeedTier : byte { One, Two, Three }

public enum ChannelRole : byte { Regular, Boss }

/// <summary>The one move a channel may switch on beyond move, jump and the three normals.</summary>
public enum ExtraVerb : byte { None, Shield, Grab, Specials, Beeline }

/// <summary>Why a contestant was cancelled. Presentation maps each to its stamp (TOASTED, SHOVED OUT, WRITTEN UP).</summary>
public enum FailCause : byte { Hazard, Fell, StillCaged, OutsideSpot, Caught, BoardsRemain }

public enum SplatStyle : byte { Toasted, Flattened, Dizzy, Fallen }

/// <summary>A channel's stable identity. IDs are never reused.</summary>
public readonly record struct ChannelId(ushort Value)
{
    public static readonly ChannelId None = default;
    public override string ToString() => Value.ToString();
}

/// <summary>A hip position and facing on the deck.</summary>
public readonly record struct MayhemPose(float X, float Y, float Z, float Yaw)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(Yaw);
    internal static MayhemPose Of(in CharacterState state) => new(state.PX, state.PY, state.PZ, state.FacingYaw);
}

/// <summary>Where the show is at one tick. Always derived from the seed; never stored.</summary>
public readonly record struct MayhemCursor(MayhemPhase Phase, int Episode, byte Act, SpeedTier Tier,
    uint PhaseStart, uint PhaseEnd, uint PlayStart, uint Deadline)
{
    public bool HasEpisode => Episode >= 0;
    /// <summary>Call and Play run gameplay; Verdict, SpeedUp, Intro and Ended do not.</summary>
    public bool Gameplay => Phase is MayhemPhase.Call or MayhemPhase.Play;
}

/// <summary>The current episode's failure for one contestant: final for the episode.</summary>
public readonly record struct MayhemFailure(FailCause Cause, SplatStyle Style, uint Tick, MayhemPose Anchor);

public enum JudgementKind : byte { Ready, Incomplete, FailNow }

/// <summary>A channel's answer for one contestant. Ready is provisional until the deadline; FailNow latches at once.</summary>
public readonly record struct MayhemJudgement(JudgementKind Kind, FailCause Cause, SplatStyle Style)
{
    public static MayhemJudgement Ready => default;
    public static MayhemJudgement Incomplete(FailCause cause) => new(JudgementKind.Incomplete, cause, StyleFor(cause));
    public static MayhemJudgement FailNow(FailCause cause, SplatStyle style) => new(JudgementKind.FailNow, cause, style);

    /// <summary>The splat a deadline failure shows, by cause.</summary>
    public static SplatStyle StyleFor(FailCause cause) => cause switch
    {
        FailCause.Hazard => SplatStyle.Toasted,
        FailCause.Fell => SplatStyle.Fallen,
        FailCause.StillCaged => SplatStyle.Toasted,
        FailCause.OutsideSpot => SplatStyle.Dizzy,
        FailCause.Caught => SplatStyle.Dizzy,
        FailCause.BoardsRemain => SplatStyle.Dizzy,
    };
}

public enum MayhemZoneKind : byte { Spotlight }
public enum MayhemTargetKind : byte { BoardStack }

public enum BombPhase : byte { Pending, InFlight, Exploded }

/// <summary>A bomb's scheduled landing, replaced by the actual position and tick once it explodes.</summary>
public readonly record struct BombCue(byte Lane, ushort Ordinal, bool Launched, ArcadePoint Landing,
    uint LaunchTick, uint ImpactTick, float BlastRadius, bool Exploded = false)
{
    public BombPhase Phase => Exploded ? BombPhase.Exploded : Launched ? BombPhase.InFlight : BombPhase.Pending;
}

/// <summary>An expanding shockwave ring from a slam. Its radius at a tick is (tick - SlamTick) × Speed / 60.</summary>
public readonly record struct RingCue(byte Lane, ushort Ordinal, ArcadePoint Center, uint SlamTick,
    float Speed, float HalfWidth, float HalfHeight, ushort LifetimeTicks);

public readonly record struct ZoneCue(byte Lane, ushort Ordinal, MayhemZoneKind Kind, ArcadePoint Center, float Radius,
    uint ActiveFrom, uint ActiveUntil);

/// <summary>A guest's watch: the warning (whistle and turn) starts at WarningTick; moving between StartTick and EndTick fails.</summary>
public readonly record struct WatchCue(byte Lane, ushort Ordinal, uint WarningTick, uint StartTick, uint EndTick);

/// <summary>A focused hit target such as a board stack. Only <see cref="OwnerSeat"/>'s hits progress it.</summary>
public readonly record struct TargetCue(byte Lane, ushort Ordinal, MayhemTargetKind Kind, ulong EntityId, byte OwnerSeat,
    ArcadePoint Center, float Radius, float HalfHeight, byte HitsLeft, byte Hits);

/// <summary>Everything a channel shows and everything the engine collides with, as closed records.</summary>
public sealed record ChannelScene(IReadOnlyList<BombCue> Bombs, IReadOnlyList<RingCue> Rings,
    IReadOnlyList<ZoneCue> Zones, IReadOnlyList<WatchCue> Watches, IReadOnlyList<RuntimeSolid> Solids,
    IReadOnlyList<TargetCue> Targets)
{
    public static readonly ChannelScene Empty = new(Array.Empty<BombCue>(), Array.Empty<RingCue>(),
        Array.Empty<ZoneCue>(), Array.Empty<WatchCue>(), Array.Empty<RuntimeSolid>(), Array.Empty<TargetCue>());

    internal static ChannelScene Concat(ChannelScene a, ChannelScene b) => new(
        Join(a.Bombs, b.Bombs), Join(a.Rings, b.Rings), Join(a.Zones, b.Zones), Join(a.Watches, b.Watches),
        Join(a.Solids, b.Solids), Join(a.Targets, b.Targets));

    private static IReadOnlyList<T> Join<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var all = new T[a.Count + b.Count];
        for (int i = 0; i < a.Count; i++) all[i] = a[i];
        for (int i = 0; i < b.Count; i++) all[a.Count + i] = b[i];
        return all;
    }
}

/// <summary>What a CPU contestant wants this tick. The adapter turns it into ordinary input under the gate.</summary>
public enum CpuAction : byte { Wait, Brake, Move, Jump, Normal, Shield, Grab, Special, Beeline }

/// <summary>
/// A channel's CPU intent: an action and a destination on the deck. <see cref="Slot"/> names the canonical
/// slot for Normal and Special; <see cref="Face"/> asks the adapter to face the destination while acting.
/// </summary>
public readonly record struct MayhemCpuIntent(CpuAction Action, float X, float Z, byte Slot = 0, bool Face = false)
{
    public static MayhemCpuIntent Wait => new(CpuAction.Wait, 0, 0);
}

internal enum ChannelMoment : byte { BeforeActions, AfterCombat }

internal enum MayhemContactKind : byte { HazardBody, SolidHit, HostBlast, BombImpact }

/// <summary>A collision fact routed to the channel. Host hazards carry the lane and ordinal decoded from the activation.</summary>
internal readonly record struct MayhemContact(MayhemContactKind Kind, byte Lane, ushort Ordinal, ulong Attacker,
    ulong Target, int TargetSeat, ulong ActivationId, ArcadePoint Position = default);

internal readonly record struct ChannelContactResult(bool Consumed, int FailSeat, FailCause Cause, SplatStyle Style)
{
    public static ChannelContactResult Ignored => new(false, -1, default, default);
    public static ChannelContactResult Absorbed => new(true, -1, default, default);
    public static ChannelContactResult Fail(int seat, FailCause cause, SplatStyle style) => new(true, seat, cause, style);
}

/// <summary>One roster contestant as a channel sees it this tick. <see cref="Live"/> is on air, pending and not withdrawn.</summary>
internal readonly record struct MayhemFighterFact(byte Seat, ulong EntityId, CharacterState State,
    CharacterDefinition Definition, bool OnAir, bool Live);

/// <summary>Read-only facts for one tick. Seats index the roster sorted by entity ID.</summary>
internal sealed record MayhemFrame(uint Tick, MankiMayhemDefinition Stage, MayhemFighterFact[] Fighters);

/// <summary>An on-air contestant fixed at the call, with the start the reset placed them at.</summary>
internal readonly record struct ChannelSeat(byte Seat, ulong EntityId, MayhemPose Start, float CapsuleRadius, float CapsuleHeight);

internal sealed record ChannelSetup(uint Seed, int Episode, byte Lane, SpeedTier Tier, uint CallStart, uint PlayStart,
    uint Deadline, MankiMayhemDefinition Stage, IReadOnlyList<ChannelSeat> OnAir);

internal sealed record ChannelRestore(ChannelId Id, int Episode, byte Lane, MankiMayhemDefinition Stage,
    IReadOnlyList<ulong> Roster, uint PlayStart, uint Deadline, byte OnAir);

internal readonly record struct ChannelTick(ChannelMoment Moment, MayhemPhase Phase, uint Tick, uint PlayStart,
    uint Deadline, MayhemFrame Frame);

internal readonly record struct ContestantProbe(MayhemFighterFact Fighter, MayhemFrame Frame, bool AtDeadline);

/// <summary>The CPU's view: its own facts and a look-ahead equal to its reaction delay.</summary>
internal readonly record struct CpuProbe(MayhemFighterFact Self, MayhemFrame Frame, MayhemControls Controls,
    uint PlayStart, uint Deadline, int LookaheadTicks, bool Slip = false)
{
    public uint Tick => Frame.Tick;
    public uint Seen => Frame.Tick + (uint)Math.Max(0, LookaheadTicks);
}
