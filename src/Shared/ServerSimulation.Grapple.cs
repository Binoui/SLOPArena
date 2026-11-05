using System;
namespace SlopArena.Shared;
public partial class ServerSimulation
{
    private int[] _grappleScratch = Array.Empty<int>();
    internal bool FindGrappleAnchor(in CharacterState s, CharacterDefinition def, CookedGrappleCapabilityParameters p,
        float stickX, float stickZ, out GrappleAnchor anchor)
    {
        if (_grappleScratch.Length < (_arena.CollisionTriangles?.Length ?? 0))
            _grappleScratch = new int[_arena.CollisionTriangles!.Length];
        return GrappleAnchors.TryFind(s, def, _arena, p, stickX, stickZ, _grappleScratch, out anchor);
    }
    internal bool MoveGrappleBody(ref CharacterState s, CharacterDefinition def, float x, float y, float z)
    {
        float dx = x - s.PX, dy = y - s.PY, dz = z - s.PZ;
        float commanded = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        float sx = s.PX, sy = s.PY, sz = s.PZ;
        s.VX = dx * 60f; s.VY = dy * 60f; s.VZ = dz * 60f;
        Simulation.MoveDefenseBody(ref s, def, _arena);
        s.VX = s.VY = s.VZ = 0f;
        float progress = commanded > .0001f ? ((s.PX - sx) * dx + (s.PY - sy) * dy + (s.PZ - sz) * dz) / commanded : 0f;
        return commanded < .001f || progress >= commanded * SnagProgressRatio;
    }
    internal TetherStep ZipToPartner(ref CharacterState s, CharacterDefinition def, CookedGrappleCapabilityParameters p,
        out (float X, float Y, float Z) partner)
    {
        partner = default;
        if (s.InteractionId == 0 || !_states.TryGetValue(s.InteractionPartnerId, out var victim)
            || victim.State != ActionState.Reeled || victim.InteractionId != s.InteractionId) return TetherStep.Lost;
        partner = (victim.PX, victim.PY, victim.PZ);
        if (_tick - s.InteractionTick > p.MaxZipTicks) { InterruptInteraction(ref s); return TetherStep.Snapped; }
        float dx = victim.PX - s.PX, dy = victim.PY - s.PY, dz = victim.PZ - s.PZ;
        float horizontal = MathF.Sqrt(dx * dx + dz * dz);
        float gap = def.CapsuleRadius + Def(victim.EntityId).CapsuleRadius + ArrivalGap;
        s.FacingYaw = MathF.Atan2(dx, dz);
        float travel = MathF.Max(0f, horizontal - gap), reach = p.ZipSpeed * Simulation.TickDt;
        float scale = travel > 0 ? MathF.Min(1f, reach / travel) : 1f;
        float mx = horizontal > .001f ? dx / horizontal * travel * scale : 0f;
        float mz = horizontal > .001f ? dz / horizontal * travel * scale : 0f;
        if (!MoveGrappleBody(ref s, def, s.PX + mx, s.PY + Math.Clamp(dy, -reach, reach), s.PZ + mz))
        { InterruptInteraction(ref s); return TetherStep.Snapped; }
        dx = victim.PX - s.PX; dz = victim.PZ - s.PZ;
        if (dx * dx + dz * dz <= (gap + ArrivalTolerance) * (gap + ArrivalTolerance) && MathF.Abs(victim.PY - s.PY) <= ArrivalTolerance)
        {
            ulong interaction = s.InteractionId;
            MarkInteractionTerminal(ref victim, interaction); MarkInteractionTerminal(ref s, interaction);
            victim.StateTicks = 1; _states[victim.EntityId] = victim;
            return TetherStep.Arrived;
        }
        return TetherStep.Holding;
    }
}
