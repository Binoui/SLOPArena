using System.Collections.Generic;

namespace SlopArena.Shared.AI;

/// <summary>One attempted ability input and its resolved move context.</summary>
public sealed class ActionAttempt
{
    public ulong EntityId;
    public int Tick;
    public byte ActiveSlot;
    public bool Air;
}

/// <summary>An ability input accepted by the authoritative simulation.</summary>
public sealed class AcceptedAction
{
    public ulong EntityId;
    public int Tick;
    public byte ActiveSlot;
    public bool Air;
}

/// <summary>One attack swing by one entity: which slot, the active window, and whether it connected.</summary>
public sealed class SwingRecord
{
    public ulong Attacker;
    public ulong Target;
    public byte ActiveSlot;
    /// <summary>True when the attacker was airborne at the swing start (distinguishes g1 vs a1).</summary>
    public bool Air;
    public int StartTick;
    /// <summary>Active window length in ticks (max trigger+duration of the move's first-stage hitboxes).</summary>
    public int WindowTicks;
    /// <summary>True when the simulation accepted this attempted ability input.</summary>
    public bool Accepted;
    public bool Connected;
    /// <summary>Authoritative server-local activation identity used for hit attribution.</summary>
    public ulong ActivationId;
    /// <summary>The opponent's position relative to the attacker, in the attacker's facing frame
    /// (side / forward / height, metres), sampled at swing start. Meaningful when <see cref="Connected"/>
    /// is false (a whiff) — "where did I swing and not connect".</summary>
    public float RelSide, RelForward, RelHeight;

    // ── Contact geometry (closest approach) ──

    /// <summary>Fighter measured against: the selected target at the press, otherwise the
    /// nearest other fighter within <see cref="MatchRecorder.ReferenceSearchRadius"/>; 0 when none.</summary>
    public ulong ReferenceTarget;
    /// <summary>True when <see cref="ReferenceTarget"/> came from proximity because nothing was selected.</summary>
    public bool ReferenceByProximity;
    /// <summary>A shield blocked at least one contact from this activation.</summary>
    public bool Blocked;
    /// <summary>Accepted damaging contacts from this activation (multi-hit moves count each).</summary>
    public int DamagingContacts;
    /// <summary>Ticks on which this activation had a hitbox in the resolver's contact pass.</summary>
    public int HitboxTicks;
    public int FirstHitboxTick = -1, LastHitboxTick = -1;
    /// <summary>True once a hitbox of this activation was measured against the reference target.</summary>
    public bool HasClosestApproach;
    /// <summary>Smallest surface gap, metres, between this activation's tested hitboxes and the
    /// reference target's contact volumes; zero or negative means they overlapped.</summary>
    public float ClosestGap;
    public int ClosestTick;
    /// <summary>Hitbox-surface → target-surface vector at closest approach: attacker facing frame
    /// (positive forward = the target was beyond the hitbox, positive up = above it).</summary>
    public float MissForward, MissSide, MissUp;
    /// <summary>The same vector in world space, for camera-relative analysis.</summary>
    public float MissWorldX, MissWorldY, MissWorldZ;
    /// <summary>Reference target position relative to the attacker at closest approach (facing frame).</summary>
    public float ClosestRelSide, ClosestRelForward, ClosestRelHeight;
    /// <summary>Reference target motion at the press and at closest approach (world m/s).</summary>
    public bool TargetGroundedAtStart;
    public float TargetVXAtStart, TargetVYAtStart, TargetVZAtStart;
    public bool TargetGroundedAtClosest;
    public float TargetVXAtClosest, TargetVYAtClosest, TargetVZAtClosest;
}

/// <summary>A landed hit (from the resolver's LastTickHits).</summary>
public sealed class HitEvent
{
    public ulong Attacker;
    public ulong Target;
    public byte AttackSlot;
    /// <summary>Authoritative server-local ability activation that produced this hit.</summary>
    public ulong ActivationId;
    public bool Air;
    public float Damage;
    public int Tick;
}

/// <summary>A run of same-pair hits classified by authoritative action availability.</summary>
public sealed class ComboLink
{
    public ulong Attacker;
    public ulong Target;
    public int Hits;
    public int StartTick;
    public int EndTick;
    public bool IsTrueCombo;
    public bool IsPressureString;
}

/// <summary>Per-tick entity position sample (provenance for the stats; the spatial maps use swings).</summary>
public sealed class TickSample
{
    public int Tick;
    public ulong EntityId;
    public float PX, PY, PZ;
}
/// <summary>One bot input captured for deterministic public trace comparison.</summary>
public sealed class InputSample
{
    public int Tick;
    public ulong EntityId;
    public InputState Input;
}

/// <summary>Complete telemetry for one self-play match — the lossless JSON source.</summary>
public sealed class MatchRecord
{
    public int Seed;
    public int DurationTicks;
    public ulong WinnerEntityId;
    public bool SharedVictory;
    public bool TimedOut;
    public int Entity1Deaths;
    public int Entity2Deaths;
    public ushort Entity1Damage;
    public ushort Entity2Damage;
    public List<ActionAttempt> ActionAttempts = new();
    public List<AcceptedAction> AcceptedActions = new();
    public List<SwingRecord> Swings = new();
    public List<HitEvent> Hits = new();
    public List<ServerSimulation.DeathEvent> Deaths = new();
    public List<ComboLink> Combos = new();
    public List<TickSample> Samples = new();
    public List<InputSample> Inputs = new();
}
