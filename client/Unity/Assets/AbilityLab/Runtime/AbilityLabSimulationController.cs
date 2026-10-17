using System;
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Tools;

/// <summary>Runs one isolated package move through the Shared authoritative simulation.</summary>
public sealed class AbilityLabSimulationController
{
    private const ulong PreviewEntityId = 1;
    private ServerSimulation _simulation;
    private readonly List<TimelinePresentationEvent> _events = new();
    private readonly List<CharacterState> _stateHistory = new();
    private readonly List<Hitbox> _testedHitboxes = new();
    private readonly List<SpellResolver.EntityData> _hurtboxes = new();

    public IReadOnlyList<TimelinePresentationEvent> PresentationEvents => _events;
    public IReadOnlyList<CharacterState> StateHistory => _stateHistory;
    /// <summary>The actor's hitboxes Shared tested on the last simulated tick, the pose the model shows.</summary>
    public IReadOnlyList<Hitbox> TestedHitboxes => _testedHitboxes;
    /// <summary>The actor's hurtboxes Shared tested on the last simulated tick.</summary>
    public IReadOnlyList<SpellResolver.EntityData> Hurtboxes => _hurtboxes;
    public bool IsReady => _simulation != null;
    public float ActiveSwordHitboxSeconds
    {
        get
        {
            if (_simulation == null) return -1f;
            foreach (var hitbox in _simulation.Resolver.GetActiveHitboxes())
                if (hitbox.Active && hitbox.TracksBone
                    && hitbox.SourceEvent.BoneName == "_weapon_hilt"
                    && hitbox.SourceEvent.EndBoneName == "_weapon_tip")
                    return hitbox.AgeTicks * SlopArena.Shared.Simulation.TickDt;
            return -1f;
        }
    }

    public void Simulate(
        CharacterDefinition definition,
        BakedAnimationData? baked,
        byte wireSlot,
        bool airborne,
        Vector3 origin,
        float facingYaw,
        int targetTick)
    {
        Clear();
        if (definition == null || targetTick < 0) return;

        float floorY = origin.y - definition.CapsuleHeight * 0.5f;
        // An airborne preview must not start touching the floor and cancel its
        // move before the sword window. Keep the authoring arena below it.
        if (airborne) floorY -= 100f;
        var simulation = new ServerSimulation(CreateArena(floorY))
        {
            NoCooldownsEntityId = PreviewEntityId,
            NoGravityEntityId = airborne ? PreviewEntityId : (ulong?)null,
        };
        simulation.SetTick(0);
        var state = new CharacterState
        {
            EntityId = PreviewEntityId,
            PX = origin.x,
            PY = origin.y,
            PZ = origin.z,
            FacingYaw = facingYaw,
            AimYaw = facingYaw,
            IsGrounded = !airborne,
            State = ActionState.Idle,
        };
        simulation.RegisterEntity(PreviewEntityId, definition, state, baked);
        var inputs = new Dictionary<ulong, InputState>(1);
        for (int tick = 0; tick <= targetTick; tick++)
        {
            inputs[PreviewEntityId] = tick == 0
                ? new InputState
                {
                    ActiveSlot = wireSlot,
                    FacingYaw = ToDegreesHundredths(facingYaw),
                    AimYaw = ToDegreesHundredths(facingYaw),
                }
                : default;
            simulation.Tick(inputs);
            _events.AddRange(simulation.GetPresentationEvents(clear: true));
            _stateHistory.Add(simulation.GetState(PreviewEntityId));
        }
        foreach (var hitbox in simulation.Resolver.LastTickCheckedHitboxes)
            if (hitbox.OwnerId == PreviewEntityId) _testedHitboxes.Add(hitbox);
        foreach (var shape in simulation.LastTickAttackEntities ?? Array.Empty<SpellResolver.EntityData>())
            if (shape.Id == PreviewEntityId && !shape.ShieldSurface && shape.Active) _hurtboxes.Add(shape);
        _simulation = simulation;
    }

    /// <summary>Record one real two-fighter run. Seeking never resimulates or forces an outcome.</summary>
    public AbilityLabScenarioResult RunScenario(CharacterDefinition definition, BakedAnimationData? baked,
        AbilityLabScenarioOptions options, Vector3 origin, float facingYaw)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (options == null) throw new ArgumentNullException(nameof(options));
        options.Validate();
        const ulong opponentId = 2;
        bool grab = options.Action == "grab";
        SlotAddress address = default;
        byte wireSlot = 0;
        if (grab && definition.CaptureGeometry == null)
            throw new ArgumentException("This package has no cooked capture geometry.");
        if (!grab)
        {
            CanonicalSlotProjection.TryGet(options.Action, out address);
            int index = Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
            wireSlot = (byte)(AbilityLab.SlotIndices[index] + 1);
            var spec = definition.GetSlotAbility((byte)(wireSlot - 1), address.IsAirborne);
            if (spec?.Stages is not { Length: > 0 })
                throw new ArgumentException($"Action '{options.Action}' is unavailable in this package.");
        }

        float floorY = origin.y - definition.CapsuleHeight * 0.5f;
        var simulation = new ServerSimulation(CreateArena(floorY))
        {
            NoCooldownsEntityId = PreviewEntityId,
        };
        simulation.SetTick(0);
        var actor = new CharacterState
        {
            EntityId = PreviewEntityId,
            PX = origin.x, PY = origin.y + (address.IsAirborne ? definition.CapsuleHeight : 0f), PZ = origin.z,
            FacingYaw = facingYaw, AimYaw = facingYaw,
            IsGrounded = !address.IsAirborne, State = ActionState.Idle,
        };
        float relativeYaw = Mathf.Repeat(options.RelativeFacingDegrees + 180f, 360f) - 180f;
        float opponentYaw = facingYaw + relativeYaw * Mathf.Deg2Rad;
        var opponent = new CharacterState
        {
            EntityId = opponentId,
            PX = origin.x + Mathf.Sin(facingYaw) * options.Distance,
            PY = origin.y,
            PZ = origin.z + Mathf.Cos(facingYaw) * options.Distance,
            FacingYaw = opponentYaw, AimYaw = opponentYaw,
            DamagePercent = options.OpponentDamage,
            IsGrounded = true, State = ActionState.Idle,
        };
        simulation.RegisterEntity(PreviewEntityId, definition, actor, baked);
        simulation.RegisterEntity(opponentId, definition, opponent, baked);
        var frames = new List<AbilityLabScenarioFrame>(options.LastFrame + 1);
        var contacts = new List<AbilityLabScenarioContact>();
        var interactions = new List<AbilityLabScenarioInteraction>();
        var events = new List<TimelinePresentationEvent>();
        var deaths = new List<ServerSimulation.DeathEvent>();
        var inputs = new Dictionary<ulong, InputState>(2);
        int actorPoseTicks = 0, opponentPoseTicks = 0;
        for (int frame = 0; frame <= options.LastFrame; frame++)
        {
            inputs[PreviewEntityId] = new InputState
            {
                ActiveSlot = frame == 0 ? wireSlot : (byte)0,
                GrabPressed = frame == 0 && grab,
                FacingYaw = ToDegreesHundredths(facingYaw),
                AimYaw = ToDegreesHundredths(facingYaw),
                // ChargeTicks counts held input frames including activation; release on the next frame.
                IsAiming = frame < options.ChargeTicks,
                AimDistance = (ushort)Mathf.RoundToInt(Mathf.Min(options.Distance, 65f) * 100f),
            };
            inputs[opponentId] = new InputState
            {
                FacingYaw = ToDegreesHundredths(opponentYaw),
                AimYaw = ToDegreesHundredths(opponentYaw),
                ShieldHeld = options.OpponentBehavior == AbilityLabOpponentBehavior.Shield,
                ShieldPressed = frame == 0 && options.OpponentBehavior == AbilityLabOpponentBehavior.Shield,
            };
            var previousActor = actor;
            var previousOpponent = opponent;
            simulation.Tick(inputs);
            actor = simulation.GetState(PreviewEntityId);
            opponent = simulation.GetState(opponentId);
            actorPoseTicks = PoseTicks(in previousActor, in actor, actorPoseTicks);
            opponentPoseTicks = PoseTicks(in previousOpponent, in opponent, opponentPoseTicks);
            uint matchTick = (uint)(frame + 1);
            foreach (var hit in simulation.LastTickHits)
                contacts.Add(new AbilityLabScenarioContact(frame, hit));
            if (previousActor.InteractionId == 0 && actor.InteractionId != 0
                && opponent.InteractionId == actor.InteractionId)
                interactions.Add(new AbilityLabScenarioInteraction(frame, matchTick, "capture", actor.InteractionId));
            if (previousActor.InteractionId != 0 && actor.InteractionId == 0)
            {
                // This is an observation of the resolved paired states, not a
                // predicted release tick or an invented hitbox contact.
                string kind = opponent.DamagePercent > previousOpponent.DamagePercent
                    && opponent.State is (ActionState.Hitstun or ActionState.Thrown) ? "release" : "terminal";
                interactions.Add(new AbilityLabScenarioInteraction(frame, matchTick, kind, previousActor.InteractionId));
            }
            events.AddRange(simulation.GetPresentationEvents(clear: true));
            deaths.AddRange(simulation.LastTickDeaths);
            var activeHitboxes = simulation.Resolver.GetActiveHitboxes();
            float swordSeconds = -1f;
            foreach (var hitbox in activeHitboxes)
                if (hitbox.Active && hitbox.OwnerId == PreviewEntityId && hitbox.TracksBone
                    && hitbox.SourceEvent.BoneName == "_weapon_hilt"
                    && hitbox.SourceEvent.EndBoneName == "_weapon_tip")
                {
                    swordSeconds = hitbox.AgeTicks * SlopArena.Shared.Simulation.TickDt;
                    break;
                }
            // Shared reuses its entity list each tick; a frame owns a snapshot
            // so backward scrubbing/capture cannot see later collision data.
            var hurtboxes = new List<SpellResolver.EntityData>(simulation.LastTickAttackEntities);
            // The resolver drops a hitbox on its last tested tick; overlays need every tested one.
            var testedHitboxes = new List<Hitbox>(simulation.Resolver.LastTickCheckedHitboxes);
            frames.Add(new AbilityLabScenarioFrame(frame, matchTick, actor, opponent,
                swordSeconds, hurtboxes, testedHitboxes, actorPoseTicks, opponentPoseTicks));
        }
        return new AbilityLabScenarioResult(options, frames, contacts, interactions, events, deaths);
    }

    private static int PoseTicks(in CharacterState previous, in CharacterState current, int elapsed)
    {
        if (previous.State != current.State || previous.Deaths != current.Deaths
            || current.DamagePercent > previous.DamagePercent)
            return 0;
        return previous.HitstopTicks > 0 || current.HitstopTicks > 0 ? elapsed : elapsed + 1;
    }

    public void Clear()
    {
        _events.Clear();
        _stateHistory.Clear();
        _testedHitboxes.Clear();
        _hurtboxes.Clear();
        _simulation = null;
    }

    private static short ToDegreesHundredths(float radians)
    {
        return (short)Mathf.RoundToInt(Mathf.DeltaAngle(0f, radians * Mathf.Rad2Deg) * 100f);
    }

    private static ArenaDefinition CreateArena(float floorY)
    {
        const int width = 100;
        const int height = 100;
        var heightmap = new float[width * height];
        for (int i = 0; i < heightmap.Length; i++) heightmap[i] = floorY;
        return new ArenaDefinition
        {
            Name = "ability-lab",
            DisplayName = "Ability Lab",
            KillHeight = floorY - 20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0f, Y = floorY, Z = 0f, Yaw = 0f } },
            Heightmap = new ArenaHeightmap
            {
                Data = heightmap,
                Width = width,
                Height = height,
                CellSize = 1f,
                OriginX = 0f,
                OriginZ = 0f,
            },
        };
    }
}
