using System;
using System.Linq;

namespace SlopArena.Shared;

public enum DashStrikeContact : byte { None, Light, Tip }

/// <summary>Standing-body spacing for the fixed line, shared by the CPU and the ready bracket.</summary>
public static class DashStrikeReach
{
    public static CookedDashStrikeCapabilityParameters? Parameters(CookedSlotDefinition? slot)
        => slot?.Timeline.Stages.SelectMany(x => x.Operations).OfType<CookedStartCapabilityOperation>()
            .Select(x => x.Parameters).OfType<CookedDashStrikeCapabilityParameters>().FirstOrDefault();

    public static DashStrikeContact Classify(CookedDashStrikeCapabilityParameters p,
        in CharacterState owner, in CharacterState target, CharacterDefinition targetBody)
    {
        float dx = target.PX - owner.PX, dz = target.PZ - owner.PZ;
        float yaw = dx * dx + dz * dz > .000001f ? MathF.Atan2(dx, dz) : owner.FacingYaw;
        float sx = MathF.Sin(yaw), sz = MathF.Cos(yaw);
        var pose = owner with { FacingYaw = yaw, AttackPosePitch = 0f };
        float halfSegment = MathF.Max(0f, targetBody.CapsuleHeight * .5f - targetBody.CapsuleRadius);
        var body = new SpellResolver.EntityData { Shape = HitboxShape.Capsule, Radius = targetBody.CapsuleRadius,
            PosX = target.PX, PosY = target.PY - halfSegment, PosZ = target.PZ,
            EndX = target.PX, EndY = target.PY + halfSegment, EndZ = target.PZ };
        for (int tick = 1; tick <= p.TravelTicks; tick++)
        {
            float distance = MathF.Min(p.Distance, p.Speed * Simulation.TickDt * tick);
            pose.PX = owner.PX + sx * distance; pose.PZ = owner.PZ + sz * distance;
            if (tick == p.TravelTicks && Touches(p.TipHitbox, in pose, in body)) return DashStrikeContact.Tip;
            if (Touches(p.LightHitbox, in pose, in body)) return DashStrikeContact.Light;
        }
        return DashStrikeContact.None;
    }

    public static bool CorridorClear(in CharacterState owner, CharacterDefinition ownerBody,
        in CharacterState target, CookedDashStrikeCapabilityParameters p, in ArenaDefinition arena)
    {
        float dx = target.PX - owner.PX, dz = target.PZ - owner.PZ;
        float yaw = dx * dx + dz * dz > .000001f ? MathF.Atan2(dx, dz) : owner.FacingYaw;
        float x = owner.PX + MathF.Sin(yaw) * p.Distance, z = owner.PZ + MathF.Cos(yaw) * p.Distance;
        var triangles = new int[arena.CollisionTriangles?.Length ?? 0];
        int count = ArenaCollision.GetCandidateTrianglesForSweep(owner.PX, owner.PY, owner.PZ, x, owner.PY, z,
            ownerBody.CapsuleRadius, ownerBody.CapsuleHeight, in arena, triangles);
        return !ArenaCollision.SweepCapsule(owner.PX, owner.PY, owner.PZ, x, owner.PY, z,
            ownerBody.CapsuleRadius, ownerBody.CapsuleHeight, in arena, triangles, count, out var contact)
            || contact.Time >= .999f || MathF.Abs(contact.NormalY) >= .6f;
    }

    private static bool Touches(CookedHitbox cut, in CharacterState pose, in SpellResolver.EntityData body)
    {
        var evt = CookedCharacterRuntimeAdapter.ToHitboxEvent(cut);
        HitboxGeometry.ResolvePositions(in pose, in evt, null, null, null, 0,
            out float x, out float y, out float z, out float ex, out float ey, out float ez);
        var hitbox = new Hitbox { Shape = (HitboxShape)cut.Shape, Radius = cut.Radius,
            X = x, Y = y, Z = z, EndX = ex, EndY = ey, EndZ = ez };
        return SpellResolver.SurfaceGap(in hitbox, in body, out _, out _, out _) <= 0f;
    }
}
