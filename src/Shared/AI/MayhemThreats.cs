using System;
using System.Collections.Generic;

namespace SlopArena.Shared.AI;

/// <summary>Predicted landing rings supplied by the Match to the ordinary CPU input policy.</summary>
public readonly struct MayhemThreats
{
    public readonly IReadOnlyList<MayhemLandingRing> Rings;
    public readonly uint Tick;
    public readonly IReadOnlyList<MayhemSlam>? Slams;
    public readonly IReadOnlyList<RuntimeSolid>? Walls;
    public MayhemThreats(ServerSimulation sim)
    { Rings = sim.MayhemLandingRings; Slams = sim.MayhemSlams; Walls = sim.MayhemWalls; Tick = sim.CurrentTick; }
    public MayhemThreats(IReadOnlyList<MayhemLandingRing> rings, uint tick) { Rings = rings; Tick = tick; Slams = null; Walls = null; }

    public bool TryAvoid(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena, out InputState input)
    {
        input = default;
        if (Rings == null || !self.IsGrounded || self.HitstopTicks > 0 || self.HitstunTicks > 0
            || self.LandingLagTicks > 0 || self.AnimLockTicks > 0 || self.InteractionId != 0
            || !ActionStates.AdmitsActions(self.State)) return false;
        if (Walls != null)
        {
            RuntimeSolid? exit = null;
            foreach (var wall in Walls)
                if (wall.CageEntityId == self.EntityId && wall.HitBudget == MankiMayhemTuning.CrackedWallHits)
                { exit = wall; break; }
            if (exit.HasValue && exit.Value.Phase != RuntimeSolidPhase.Sinking)
            {
                var wall = exit.Value;
                float dx = wall.Center.X - self.PX, dz = wall.Center.Z - self.PZ;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                float yaw = MathF.Atan2(dx, dz);
                short angle = (short)MathF.Round(yaw * 18000f / MathF.PI);
                float away = wall.Center.Z - wall.CageCenter.Z;
                if (!wall.Blocks)
                {
                    float safe = MankiMayhemTuning.CageBlastRadius + def.CapsuleRadius + MankiMayhemTuning.BotClearance;
                    bool clear = MathF.Abs(self.PZ - (wall.CageCenter.Z + MankiMayhemTuning.CageBombOffsetZ)) >= safe;
                    input = new() { MoveY = clear ? 0f : MathF.Sign(away), FacingYaw = angle, AimYaw = angle };
                }
                else
                    input = new() { MoveX = distance > .01f ? dx / distance : 0f,
                        MoveY = distance > .01f ? dz / distance : 0f,
                        FacingYaw = angle, AimYaw = angle,
                        ActiveSlot = distance <= def.CapsuleRadius + MankiMayhemTuning.CageBotAttackReach ? AbilitySlots.Slot1 : (byte)0 };
                return true;
            }
        }
        if (Slams != null && Slams.Count > 0)
        {
            foreach (var slam in Slams)
            {
                float dx = self.PX - slam.Position.X, dz = self.PZ - slam.Position.Z;
                float distance = MathF.Sqrt(dx * dx + dz * dz) - def.CapsuleRadius - MankiMayhemTuning.RingHalfWidth;
                float contact = slam.Tick + MathF.Max(0f, distance) / MankiMayhemTuning.RingSpeed * 60f;
                if (contact >= Tick && contact - Tick <= MankiMayhemTuning.BotJumpLeadTicks)
                { input.Jump = true; return true; }
            }
            return true;
        }
        float clearance = MankiMayhemTuning.BlastRadius + def.CapsuleRadius + MankiMayhemTuning.BotClearance;
        if (DangerDistance(self.PX, self.PZ) >= clearance * clearance) return false;
        float best = float.NegativeInfinity;
        for (int i = 0; i < 16; i++)
        {
            float yaw = i * (MathF.PI / 8f), dx = MathF.Sin(yaw), dz = MathF.Cos(yaw);
            float x = self.PX + dx * clearance, z = self.PZ + dz * clearance;
            if (x < arena.MinX + def.CapsuleRadius || x > arena.MaxX - def.CapsuleRadius
                || z < arena.MinZ + def.CapsuleRadius || z > arena.MaxZ - def.CapsuleRadius) continue;
            float score = DangerDistance(x, z);
            if (score <= best) continue;
            best = score;
            short angle = (short)MathF.Round(yaw * 18000f / MathF.PI);
            input = new InputState { MoveX = dx, MoveY = dz, FacingYaw = angle, AimYaw = angle };
        }
        return best > float.NegativeInfinity;
    }

    private float DangerDistance(float x, float z)
    {
        float nearest = float.PositiveInfinity;
        foreach (var ring in Rings)
        {
            if (Tick > ring.LandingTick + MankiMayhemTuning.BlastTicks) continue;
            float dx = x - ring.Position.X, dz = z - ring.Position.Z;
            nearest = MathF.Min(nearest, dx * dx + dz * dz);
        }
        return nearest;
    }
}
