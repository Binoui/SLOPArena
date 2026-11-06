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
public enum FailCause : byte { Hazard, Fell, StillCaged, OutsideSpot, Caught, BoardsRemain, OutOfRing, OutOfFrame, Glitched, NoBonk, HeGotUp, Misfire, TooSlow }

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
        FailCause.OutOfRing => SplatStyle.Fallen,
        FailCause.OutOfFrame => SplatStyle.Dizzy,
        FailCause.Glitched => SplatStyle.Fallen,
        FailCause.NoBonk => SplatStyle.Dizzy,
        FailCause.HeGotUp or FailCause.Misfire or FailCause.TooSlow => SplatStyle.Dizzy,
    };
}

public enum MayhemZoneKind : byte { Spotlight, SumoRing, SelfieFrame, GlitchPanel }
public enum MayhemTargetKind : byte { BoardStack, CardboardHead, DecoyHead, DizzyHost }

public enum MayhemBombKind : byte { Aimed, Barrage }

public enum BombPhase : byte { Pending, InFlight, Exploded }

/// <summary>A bomb's scheduled landing, replaced by the actual position and tick once it explodes.</summary>
public readonly record struct BombCue(byte Lane, ushort Ordinal, bool Launched, ArcadePoint Landing,
    uint LaunchTick, uint ImpactTick, float BlastRadius, bool Exploded = false, MayhemBombKind Kind = MayhemBombKind.Aimed)
{
    public BombPhase Phase => Exploded ? BombPhase.Exploded : Launched ? BombPhase.InFlight : BombPhase.Pending;
}

/// <summary>An expanding shockwave ring from a slam. Its radius at a tick is (tick - SlamTick) × Speed / 60.</summary>
public readonly record struct RingCue(byte Lane, ushort Ordinal, ArcadePoint Center, uint SlamTick,
    float Speed, float HalfWidth, float HalfHeight, ushort LifetimeTicks);

public readonly record struct ZoneCue(byte Lane, ushort Ordinal, MayhemZoneKind Kind, ArcadePoint Center, float Radius,
    uint ActiveFrom, uint ActiveUntil, ArcadePoint EndCenter, float EndRadius, uint ArmedFrom, float HalfDepth = 0, float EndHalfDepth = 0)
{
    public ZoneCue(byte lane, ushort ordinal, MayhemZoneKind kind, ArcadePoint center, float radius, uint from, uint until)
        : this(lane, ordinal, kind, center, radius, from, until, center, radius, from) { }
    public bool ActiveAt(uint tick) => tick >= ActiveFrom && tick < ActiveUntil;
    public bool ArmedAt(uint tick) => tick >= ArmedFrom && ActiveAt(tick);
    private float Fraction(uint tick) => tick <= ActiveFrom ? 0 : tick >= ActiveUntil ? 1
        : (float)(tick - ActiveFrom) / (ActiveUntil - ActiveFrom);
    public ArcadePoint CenterAt(uint tick)
    {
        float t = Fraction(tick);
        return new(Center.X + (EndCenter.X - Center.X) * t, Center.Y + (EndCenter.Y - Center.Y) * t,
            Center.Z + (EndCenter.Z - Center.Z) * t);
    }
    public float RadiusAt(uint tick)
    {
        float t = Fraction(tick);
        // Slow the final SUMO! squeeze so survivors can enter the space a cancelled rival frees.
        if (Kind == MayhemZoneKind.SumoRing) t *= 2 - t;
        return Radius + (EndRadius - Radius) * t;
    }
    public float HalfDepthAt(uint tick) => HalfDepth + (EndHalfDepth - HalfDepth) * Fraction(tick);
    public bool Contains(float x, float z, uint tick)
    {
        var center = CenterAt(tick);
        float radius = RadiusAt(tick);
        return Kind switch
        {
            MayhemZoneKind.Spotlight or MayhemZoneKind.SumoRing => MayhemMath.DistanceSquared(x, z, center.X, center.Z) <= radius * radius,
            MayhemZoneKind.SelfieFrame => MathF.Abs(x - center.X) <= radius && MathF.Abs(z - center.Z) <= HalfDepthAt(tick),
            MayhemZoneKind.GlitchPanel => x >= center.X - radius && x < center.X + radius
                && z >= center.Z - HalfDepthAt(tick) && z < center.Z + HalfDepthAt(tick),
        };
    }
}

public enum MayhemWatchKind : byte { Look, FakeLook, Draw, FakeDraw }

/// <summary>A guest's watch: the warning (whistle and turn) starts at WarningTick; moving between StartTick and EndTick fails.</summary>
public readonly record struct WatchCue(byte Lane, ushort Ordinal, uint WarningTick, uint StartTick, uint EndTick,
    MayhemWatchKind Kind = MayhemWatchKind.Look, string CopyKey = "");

/// <summary>A hit target with an exclusive active window. A null owner marks a shared target.</summary>
public readonly record struct TargetCue(byte Lane, ushort Ordinal, MayhemTargetKind Kind, ulong EntityId, byte? OwnerSeat,
    ArcadePoint Center, float Radius, float HalfHeight, byte HitsLeft, byte Hits, uint ActiveFrom = 0, uint ActiveUntil = uint.MaxValue)
{
    public bool ActiveAt(uint tick) => tick >= ActiveFrom && tick < ActiveUntil
        && (Kind == MayhemTargetKind.BoardStack || HitsLeft > 0);
}

public readonly record struct CarryCue(byte Lane, ushort Ordinal, byte HolderSeat, uint FuseEnd, byte? LastGiver);
public readonly record struct HitProgressCue(byte Seat, byte Hits, byte Quota);

/// <summary>Everything a channel shows and everything the engine collides with, as closed records.</summary>
public sealed record ChannelScene(IReadOnlyList<BombCue> Bombs, IReadOnlyList<RingCue> Rings,
    IReadOnlyList<ZoneCue> Zones, IReadOnlyList<WatchCue> Watches, IReadOnlyList<RuntimeSolid> Solids,
    IReadOnlyList<TargetCue> Targets)
{
    public IReadOnlyList<CarryCue> Carries { get; init; } = Array.Empty<CarryCue>();
    public IReadOnlyList<HitProgressCue> HitProgress { get; init; } = Array.Empty<HitProgressCue>();

    public static readonly ChannelScene Empty = new(Array.Empty<BombCue>(), Array.Empty<RingCue>(),
        Array.Empty<ZoneCue>(), Array.Empty<WatchCue>(), Array.Empty<RuntimeSolid>(), Array.Empty<TargetCue>());

    internal static ChannelScene Concat(ChannelScene a, ChannelScene b) => new(
        Join(a.Bombs, b.Bombs), Join(a.Rings, b.Rings), Join(a.Zones, b.Zones), Join(a.Watches, b.Watches),
        Join(a.Solids, b.Solids), Join(a.Targets, b.Targets))
        { Carries = Join(a.Carries, b.Carries), HitProgress = Join(a.HitProgress, b.HitProgress) };

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
public readonly record struct MayhemCpuIntent(CpuAction Action, float X, float Z, byte Slot = 0, bool Face = false, ushort PressToken = 0)
{
    public static MayhemCpuIntent Wait => new(CpuAction.Wait, 0, 0);
}

internal enum ChannelMoment : byte { BeforeActions, AfterCombat }

internal enum MayhemContactKind : byte { HazardBody, SolidHit, HostBlast, BombImpact, FighterHit }

/// <summary>A collision fact routed to the channel. Host hazards carry the lane and ordinal decoded from the activation.</summary>
internal readonly record struct MayhemContact(MayhemContactKind Kind, byte Lane, ushort Ordinal, ulong Attacker,
    ulong Target, int TargetSeat, ulong ActivationId, ArcadePoint Position = default, byte AttackSlot = AbilitySlots.None);

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
internal sealed record MayhemFrame(uint Tick, MankiMayhemDefinition Stage, MayhemFighterFact[] Fighters,
    FriendlyFireMode FriendlyFire = FriendlyFireMode.Shoves);

/// <summary>An on-air contestant fixed at the call, with the start the reset placed them at.</summary>
internal readonly record struct ChannelSeat(byte Seat, ulong EntityId, MayhemPose Start, float CapsuleRadius, float CapsuleHeight);

/// <summary>What a channel starts from. A regular channel or a remix child gets a solo airing; PRIME TIME! gets the remix.</summary>
internal sealed record ChannelSetup(MayhemAiring Airing, int Episode, byte Lane, SpeedTier Tier, uint CallStart, uint PlayStart,
    uint Deadline, MankiMayhemDefinition Stage, IReadOnlyList<ChannelSeat> OnAir)
{
    public ChannelDraw Draw => ((SoloAiring)Airing).Draw;
    public uint Seed => Draw.Seed;
}

internal sealed record ChannelRestore(MayhemAiring Airing, int Episode, byte Lane, SpeedTier Tier, MankiMayhemDefinition Stage,
    IReadOnlyList<ulong> Roster, uint PlayStart, uint Deadline, byte OnAir)
{
    public ChannelDraw Draw => ((SoloAiring)Airing).Draw;
    public ChannelId Id => Airing.Channel;
    public uint Seed => Draw.Seed;
}

internal readonly record struct ChannelTick(ChannelMoment Moment, MayhemPhase Phase, uint Tick, uint PlayStart,
    uint Deadline, MayhemFrame Frame);

internal readonly record struct ContestantProbe(MayhemFighterFact Fighter, MayhemFrame Frame, bool AtDeadline);

/// <summary>
/// The CPU's view: its own facts and a look-ahead equal to its reaction delay. <see cref="Planned"/> is the intent this CPU
/// planned on the tick before, so a policy can keep a choice instead of remaking it every tick.
/// </summary>
internal readonly record struct CpuProbe(MayhemFighterFact Self, MayhemFrame Frame, MayhemControls Controls,
    uint PlayStart, uint Deadline, int LookaheadTicks, bool Slip = false, MayhemCpuIntent Planned = default)
{
    public uint Tick => Frame.Tick;
    public uint Seen => Frame.Tick + (uint)Math.Max(0, LookaheadTicks);
}
