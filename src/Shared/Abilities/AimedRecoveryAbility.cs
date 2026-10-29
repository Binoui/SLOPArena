using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// A fixed automatic setup, then one launch aimed by the movement input on the launch tick.
/// Neutral maximizes height; stick magnitude trades height for horizontal reach. The outer
/// timeline owns the short follow-through and completion. No floor, target, or spare jump is needed.
/// With RiseSteerAcceleration the stick keeps steering after launch, toward stick × HorizontalSpeed;
/// a neutral stick keeps the current drift.
/// </summary>
public sealed class AimedRecoveryAbility : ServerAbility, IRecoveryTurnCapability
{
    private readonly CookedAimedRecoveryCapabilityParameters _parameters;
    private ushort _elapsedTicks;
    private bool _launched;
    private float _hopYaw;

    public AimedRecoveryAbility(CookedAimedRecoveryCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override bool OwnsVerticalMotion => true;
    public override float GravityMultiplier => _launched ? 1f : 0f;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _elapsedTicks = 0;
        _launched = false;
        _hopYaw = s.FacingYaw;
        Simulation.ClearMovementInterruptionFlags(ref s);
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.VX = s.VZ = 0f;
        s.VY = _parameters.PreparationRiseSpeed;
        if (s.VY > 0f) s.IsGrounded = false;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_launched)
        {
            Steer(ref s, input);
            return;
        }
        ++_elapsedTicks;
        if (_elapsedTicks < _parameters.StartupTicks)
        {
            if (_parameters.Hop is { } hop)
            {
                // Tick runs after movement. Set the velocity for the NEXT interval, so
                // takeoff/contact samples remain exactly on their authored ticks.
                s.FacingYaw = _hopYaw;
                s.VX = s.VZ = 0f;
                s.VY = 0f;
                if (_elapsedTicks >= hop.StartTicks && _elapsedTicks < hop.ContactTicks)
                {
                    float ticks = hop.ContactTicks - hop.StartTicks;
                    float u = (_elapsedTicks - hop.StartTicks) / ticks;
                    float next = (_elapsedTicks + 1 - hop.StartTicks) / ticks;
                    float height(float t) => hop.LandingHeight * t + 4f * hop.ArcHeight * t * (1f - t);
                    float speed = hop.ForwardDistance * 60f / ticks;
                    s.VX = MathF.Sin(_hopYaw) * speed;
                    s.VZ = MathF.Cos(_hopYaw) * speed;
                    s.VY = (height(next) - height(u)) * 60f;
                    s.IsGrounded = false;
                }
            }
            return;
        }
        _launched = true;

        // MoveX/MoveY already describe the world-plane direction from the input adapter.
        float magnitude = MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY);
        float strength = MathF.Min(1f, magnitude);
        float scale = magnitude > 0.001f ? strength * _parameters.HorizontalSpeed / magnitude : 0f;
        Simulation.ClearMovementInterruptionFlags(ref s);
        s.VX = input.MoveX * scale;
        s.VZ = input.MoveY * scale;
        s.VY = _parameters.VerticalSpeed
            + (_parameters.DirectionalVerticalSpeed - _parameters.VerticalSpeed) * strength;
        s.IsGrounded = false;
        // Ordinary ballistic gravity from launch, matching normal jumps and the old recovery.
        // Activation's recovery-designated FloatWindow reset remains simulation-owned.
        s.AirTimeTicks = def.Movement.FloatWindowTicks;
    }

    public void OnRecoveryTurn(ref CharacterState s, in InputState input)
    {
        if (!_launched) _hopYaw = s.FacingYaw;
    }

    private void Steer(ref CharacterState s, InputState input)
    {
        float accel = _parameters.RiseSteerAcceleration;
        float magnitude = MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY);
        if (accel <= 0f || magnitude <= 0.001f) return;
        float scale = MathF.Min(1f, magnitude) * _parameters.HorizontalSpeed / magnitude;
        float dx = input.MoveX * scale - s.VX, dz = input.MoveY * scale - s.VZ;
        float gap = MathF.Sqrt(dx * dx + dz * dz), step = accel / 60f;
        if (gap <= step) { s.VX += dx; s.VZ += dz; return; }
        s.VX += dx / gap * step;
        s.VZ += dz / gap * step;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_elapsedTicks);
        w.WriteBool(_launched);
        if (_parameters.Hop != null) w.WriteFloat(_hopYaw);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _elapsedTicks = r.ReadUShort();
        _launched = r.ReadBool();
        if (_parameters.Hop != null) _hopYaw = r.ReadFloat();
    }
}
