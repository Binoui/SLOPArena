using System;
namespace SlopArena.Shared;

/// <summary>A standable lip and the safe capsule centre inside it.</summary>
public readonly record struct GrappleAnchor(float X, float Y, float Z, float PopX, float PopY, float PopZ);

/// <summary>Deterministic, allocation-free search. Callers supply triangle scratch storage.</summary>
public static class GrappleAnchors
{
    public static bool TryFind(in CharacterState s, CharacterDefinition def, in ArenaDefinition arena,
        CookedGrappleCapabilityParameters p, float stickX, float stickZ, int[] scratch, out GrappleAnchor anchor)
    {
        anchor = default;
        float feet = s.PY - def.CapsuleHeight * .5f, best = float.PositiveInfinity;
        bool found = false;
        for (int heading = 0; heading < 16; heading++)
        {
            float yaw = heading * (MathF.PI / 8f), dx = MathF.Sin(yaw), dz = MathF.Cos(yaw);
            if (dx * stickX + dz * stickZ < -1e-5f) continue;
            // Starting above a top is not a lip: require an unsupported -> supported transition.
            bool previous = Drop(s.PX, s.PZ, feet, arena, p, scratch, out _);
            for (int step = 1; step <= 40; step++)
            {
                float distance = p.AnchorReach * step / 40f;
                float x = s.PX + dx * distance, z = s.PZ + dz * distance;
                bool support = Drop(x, z, feet, arena, p, scratch, out float y);
                if (!support || previous) { previous = support; continue; }
                float low = p.AnchorReach * (step - 1) / 40f, high = distance;
                for (int bisect = 0; bisect < 6; bisect++)
                {
                    float mid = (low + high) * .5f;
                    if (Drop(s.PX + dx * mid, s.PZ + dz * mid, feet, arena, p, scratch, out y)) high = mid;
                    else low = mid;
                }
                x = Snap(s.PX + dx * high); z = Snap(s.PZ + dz * high);
                if (!Drop(x + dx * .01f, z + dz * .01f, feet, arena, p, scratch, out y)) break;
                y = Snap(y);
                float px = Snap(x + dx * p.PopInset), pz = Snap(z + dz * p.PopInset);
                float py = y + def.CapsuleHeight * .5f;
                if (ArenaCollision.HasTriangles(arena))
                {
                    if (!ArenaCollision.TryFindSupport(px, py, pz, def.CapsuleRadius, def.CapsuleHeight, arena, scratch, out _)) break;
                    int count = ArenaCollision.GetCandidateTrianglesForSweep(px, py, pz, px, py + .01f, pz,
                        def.CapsuleRadius, def.CapsuleHeight, arena, scratch);
                    if (ArenaCollision.SweepCapsule(px, py + .005f, pz, px, py + .01f, pz,
                        def.CapsuleRadius, def.CapsuleHeight, arena, scratch, count, out _)) break;
                    float hx = s.PX + p.Projectile.LaunchOffsetX * MathF.Cos(s.FacingYaw) + p.Projectile.LaunchOffsetZ * MathF.Sin(s.FacingYaw);
                    float hz = s.PZ - p.Projectile.LaunchOffsetX * MathF.Sin(s.FacingYaw) + p.Projectile.LaunchOffsetZ * MathF.Cos(s.FacingYaw);
                    int line = ArenaCollision.GetCandidateTrianglesForSweep(hx, s.PY + p.Projectile.LaunchOffsetY, hz,
                        x - dx * .03f, y + .03f, z - dz * .03f, .005f, .01f, arena, scratch);
                    if (ArenaCollision.SweepCapsule(hx, s.PY + p.Projectile.LaunchOffsetY, hz,
                        x - dx * .03f, y + .03f, z - dz * .03f, .005f, .01f, arena, scratch, line, out _)) break;
                }
                else if (MathF.Abs(arena.Heightmap.Sample(px, pz) - y) > .02f) break;
                float d = (x - s.PX) * (x - s.PX) + (y - feet) * (y - feet) + (z - s.PZ) * (z - s.PZ);
                if (d <= p.AnchorReach * p.AnchorReach && d < best - .0001f)
                { best = d; found = true; anchor = new(x, y, z, px, py, pz); }
                break;
            }
        }
        return found;
    }
    private static float Snap(float value) => MathF.Round(value * 100f) * .01f;
    private static bool Drop(float x, float z, float feet, in ArenaDefinition arena,
        CookedGrappleCapabilityParameters p, int[] scratch, out float y)
        => SpellResolver.TryProjectStraightDrop(arena, x, feet + p.MaxRise + .01f, z, .005f, scratch, out y)
            && y >= feet + p.MinRise && y <= feet + p.MaxRise;
}
