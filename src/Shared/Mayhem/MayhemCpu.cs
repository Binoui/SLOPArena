using System;
using System.Collections.Generic;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Mayhem;

/// <summary>
/// The one CPU adapter for every channel: it asks the channel for intent planned one reaction delay ahead, holds it
/// for that delay, turns it into ordinary input and runs the result through the same gate as a human. It keeps no
/// gameplay authority; online CPUs would stay input producers.
/// </summary>
public sealed class MayhemCpu
{
    private const float ArriveDistance = .15f, WalkDistance = .6f, FacingTolerance = .35f;
    private readonly int _delay;
    private readonly int _slipPercent;
    private static readonly int[] SlipPercent = { 45, 25, 10 };

    internal static bool Slips(uint channelSeed, byte seat, int percent)
        => MayhemMath.Sample(channelSeed, 0xC001, seat) % 100 < percent;
    private readonly Queue<MayhemCpuIntent> _pending = new();
    private CpuAction _lastAction = CpuAction.Wait;

    public MayhemCpu(CpuDifficulty difficulty) : this(BotDifficultyProfile.ForDifficulty(difficulty).ReactionDelayTicks)
        => _slipPercent = SlipPercent[(int)difficulty];

    public MayhemCpu(int reactionDelayTicks) => _delay = Math.Max(0, reactionDelayTicks);

    internal MayhemCpu(int reactionDelayTicks, int slipPercent) : this(reactionDelayTicks) => _slipPercent = slipPercent;

    public int ReactionDelayTicks => _delay;

    /// <summary>The input for the next tick. Call once per tick, before <see cref="ServerSimulation.Tick"/>.</summary>
    public InputState Decide(ServerSimulation sim, ulong id)
    {
        if (sim.MayhemControlsFor(id) is not { } gate) return default;
        _pending.Enqueue(sim.MayhemCpuIntentFor(id, _delay, _slipPercent));
        var due = _pending.Count > _delay ? _pending.Dequeue() : MayhemCpuIntent.Wait;
        var input = ToInput(due, sim.GetState(id));
        _lastAction = due.Action;
        return gate.Filter(input);
    }

    private InputState ToInput(in MayhemCpuIntent intent, in CharacterState self)
    {
        var input = default(InputState);
        switch (intent.Action)
        {
            case CpuAction.Wait:
            case CpuAction.Brake:
                break;
            case CpuAction.Move:
                Steer(ref input, self, intent.X, intent.Z);
                break;
            case CpuAction.Jump:
                input.Jump = _lastAction != CpuAction.Jump;
                input.JumpHeld = true;
                Steer(ref input, self, intent.X, intent.Z);
                break;
            case CpuAction.Normal:
            case CpuAction.Special:
                if (Face(ref input, self, intent.X, intent.Z)) input.ActiveSlot = intent.Slot;
                break;
            case CpuAction.Shield:
                input.ShieldHeld = true;
                input.ShieldPressed = _lastAction != CpuAction.Shield;
                break;
            case CpuAction.Grab:
                if (Face(ref input, self, intent.X, intent.Z)) input.GrabPressed = true;
                break;
            case CpuAction.Beeline:
                if (Face(ref input, self, intent.X, intent.Z)) input.DashPressed = true;
                break;
        }
        return input;
    }

    private static short Centidegrees(float yaw) => (short)MathF.Round(yaw * 18000f / MathF.PI);

    private static void Steer(ref InputState input, in CharacterState self, float x, float z)
    {
        float dx = x - self.PX, dz = z - self.PZ;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < ArriveDistance) return;
        input.MoveX = dx / distance;
        input.MoveY = dz / distance;
        input.Walk = distance < WalkDistance;
        input.FacingYaw = input.AimYaw = Centidegrees(MathF.Atan2(dx, dz));
    }

    /// <summary>Snaps facing toward the point; true once the fighter already faces it.</summary>
    private static bool Face(ref InputState input, in CharacterState self, float x, float z)
    {
        float yaw = MathF.Atan2(x - self.PX, z - self.PZ);
        input.FaceToCamera = true;
        input.FacingYaw = input.AimYaw = Centidegrees(yaw);
        float delta = MathF.IEEERemainder(yaw - self.FacingYaw, MathF.PI * 2f);
        return MathF.Abs(delta) <= FacingTolerance;
    }
}
