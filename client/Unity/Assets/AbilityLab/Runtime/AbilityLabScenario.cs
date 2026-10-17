using System;
using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Client.Tools;

public enum AbilityLabOpponentBehavior { Idle, Shield }

/// <summary>Inputs for one isolated, deterministic Shared run; frame zero follows its first input.</summary>
public sealed class AbilityLabScenarioOptions
{
    public const int MaxFrame = 3600;
    public const int MaxChargeTicks = 600;
    public string Action { get; }
    public int LastFrame { get; }
    public float Distance { get; }
    public AbilityLabOpponentBehavior OpponentBehavior { get; }
    public ushort OpponentDamage { get; }
    public float RelativeFacingDegrees { get; }
    public int ChargeTicks { get; }
    public AbilityLabScenarioOptions(string action, int lastFrame = 60, float distance = 2.5f,
        AbilityLabOpponentBehavior opponentBehavior = AbilityLabOpponentBehavior.Idle,
        ushort opponentDamage = 0, float relativeFacingDegrees = 180f, int chargeTicks = 0)
    {
        Action = action;
        LastFrame = lastFrame;
        Distance = distance;
        OpponentBehavior = opponentBehavior;
        OpponentDamage = opponentDamage;
        RelativeFacingDegrees = relativeFacingDegrees;
        ChargeTicks = chargeTicks;
    }

    public void Validate()
    {
        if (Action != "grab" && !CanonicalSlotProjection.TryGet(Action, out _))
            throw new ArgumentException("Action must be a canonical slot ID or 'grab'.");
        if (LastFrame < 0 || LastFrame > MaxFrame)
            throw new ArgumentOutOfRangeException(nameof(LastFrame), $"Scenario frame must be in [0, {MaxFrame}].");
        if (float.IsNaN(Distance) || float.IsInfinity(Distance) || Distance < 0f)
            throw new ArgumentOutOfRangeException(nameof(Distance), "Opponent distance must be finite and non-negative.");
        if (float.IsNaN(RelativeFacingDegrees) || float.IsInfinity(RelativeFacingDegrees))
            throw new ArgumentOutOfRangeException(nameof(RelativeFacingDegrees), "Opponent facing must be finite.");
        if (OpponentBehavior != AbilityLabOpponentBehavior.Idle && OpponentBehavior != AbilityLabOpponentBehavior.Shield)
            throw new ArgumentOutOfRangeException(nameof(OpponentBehavior));
        if (OpponentDamage > 999)
            throw new ArgumentOutOfRangeException(nameof(OpponentDamage), "Opponent damage must be in [0, 999].");
        if (ChargeTicks < 0 || ChargeTicks > MaxChargeTicks)
            throw new ArgumentOutOfRangeException(nameof(ChargeTicks), $"Charge duration must be in [0, {MaxChargeTicks}].");
    }
}

public readonly struct AbilityLabScenarioFrame
{
    public int FrameIndex { get; }
    public uint MatchTick { get; }
    public CharacterState Actor { get; }
    public CharacterState Opponent { get; }
    public int ActorPoseTicks { get; }
    public int OpponentPoseTicks { get; }
    public float ActiveSwordHitboxSeconds { get; }
    public IReadOnlyList<SpellResolver.EntityData> Hurtboxes { get; }
    /// <summary>Every hitbox Shared tested on this frame, including ones that expired or connected.</summary>
    public IReadOnlyList<Hitbox> TestedHitboxes { get; }
    public AbilityLabScenarioFrame(int frameIndex, uint matchTick, CharacterState actor, CharacterState opponent,
        float activeSwordHitboxSeconds, IReadOnlyList<SpellResolver.EntityData> hurtboxes, IReadOnlyList<Hitbox> testedHitboxes,
        int actorPoseTicks = 0, int opponentPoseTicks = 0)
    {
        FrameIndex = frameIndex;
        MatchTick = matchTick;
        Actor = actor;
        Opponent = opponent;
        ActorPoseTicks = actorPoseTicks;
        OpponentPoseTicks = opponentPoseTicks;
        ActiveSwordHitboxSeconds = activeSwordHitboxSeconds;
        Hurtboxes = hurtboxes;
        TestedHitboxes = testedHitboxes;
    }
}

public readonly struct AbilityLabScenarioContact
{
    public int FrameIndex { get; }
    public SpellResolver.HitResult Hit { get; }
    public AbilityLabScenarioContact(int frameIndex, SpellResolver.HitResult hit) { FrameIndex = frameIndex; Hit = hit; }
}

/// <summary>Observed coupled-interaction transitions, separate from accepted hitbox contacts.</summary>
public readonly struct AbilityLabScenarioInteraction
{
    public int FrameIndex { get; }
    public uint MatchTick { get; }
    public string Kind { get; }
    public ulong InteractionId { get; }
    public AbilityLabScenarioInteraction(int frameIndex, uint matchTick, string kind, ulong interactionId)
    { FrameIndex = frameIndex; MatchTick = matchTick; Kind = kind; InteractionId = interactionId; }
}

public sealed class AbilityLabScenarioResult
{
    public AbilityLabScenarioOptions Options { get; }
    public IReadOnlyList<AbilityLabScenarioFrame> Frames { get; }
    public IReadOnlyList<AbilityLabScenarioContact> Contacts { get; }
    public IReadOnlyList<AbilityLabScenarioInteraction> Interactions { get; }
    public IReadOnlyList<TimelinePresentationEvent> PresentationEvents { get; }
    public IReadOnlyList<ServerSimulation.DeathEvent> Deaths { get; }

    public AbilityLabScenarioResult(AbilityLabScenarioOptions options, IReadOnlyList<AbilityLabScenarioFrame> frames,
        IReadOnlyList<AbilityLabScenarioContact> contacts, IReadOnlyList<AbilityLabScenarioInteraction> interactions,
        IReadOnlyList<TimelinePresentationEvent> presentationEvents, IReadOnlyList<ServerSimulation.DeathEvent> deaths)
    {
        Options = options;
        Frames = frames;
        Contacts = contacts;
        Interactions = interactions;
        PresentationEvents = presentationEvents;
        Deaths = deaths;
    }
}
