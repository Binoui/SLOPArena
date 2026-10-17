using System;

namespace SlopArena.Shared;

/// <summary>
/// Pure hitbox geometry: resolves a HitboxEvent's world-space capsule endpoints from a
/// character state + pose. Shared by the simulation (ServerAbility.SpawnHitbox), the
/// Ability Lab preview tool, and tests — one implementation so previews cannot drift
/// from what the server resolves (spec #119).
/// </summary>
public static class HitboxGeometry
{
    /// <summary>Rotate a yaw-resolved world point around the simulation hip pivot.</summary>
    /// <remarks>Positive pitch raises points in front of the fighter; facing yaw selects forward.</remarks>
    public static void ApplyAttackPosePitch(
        in CharacterState state, CharacterDefinition def,
        ref float worldX, ref float worldY, ref float worldZ)
    {
        float pitch = state.AttackPosePitch;
        if (pitch == 0f) return;

        float pivotY = def.BoneYToWorldY(state.PY, 0f);
        float dx = worldX - state.PX;
        float dy = worldY - pivotY;
        float dz = worldZ - state.PZ;
        float sinYaw = MathF.Sin(state.FacingYaw);
        float cosYaw = MathF.Cos(state.FacingYaw);
        float forward = dx * sinYaw + dz * cosYaw;
        float right = dx * cosYaw - dz * sinYaw;
        float sinPitch = MathF.Sin(pitch);
        float cosPitch = MathF.Cos(pitch);
        float pitchedForward = forward * cosPitch - dy * sinPitch;
        float pitchedY = forward * sinPitch + dy * cosPitch;
        worldX = state.PX + right * cosYaw + pitchedForward * sinYaw;
        worldY = pivotY + pitchedY;
        worldZ = state.PZ - right * sinYaw + pitchedForward * cosYaw;
    }

    /// <summary>
    /// Resolve a hitbox's world-space start/end positions.
    /// When evt.BoneName is set and baked data is available, positions at the bone's
    /// world position plus the OffX/Y/Z offset — the offset is anchor-relative (bone when
    /// BoneName is set, entity origin otherwise), matching the Ability Lab editor, which
    /// only authors Off*. Mirrors ServerAbility.SpawnHitbox.
    /// Bone names resolve against the baked skeleton's bone set — any baked bone is
    /// attachable (the bake can carry bones the hurtbox defs don't, e.g. toes).
    /// </summary>
    /// <param name="s">Character state: position, facing yaw, and AttackElapsedTicks for the baked-frame projection.</param>
    /// <param name="evt">The hitbox event to position.</param>
    /// <param name="baked">Baked skeleton data (nullable — bone path requires it).</param>
    /// <param name="def">Character definition (bone set + scale + duration lookup).</param>
    /// <param name="animationNames">Ability's AnimationNames[] (AnimIndex selects the pose animation).</param>
    /// <param name="animIndex">Current animation index into animationNames.</param>
    /// <param name="slot">0-based ability slot (for stage duration lookup).</param>
    /// <param name="airborne">True when the ability is an air slot — selects the air spec's stage duration.</param>
    public static void ResolvePositions(
        in CharacterState s, in HitboxEvent evt,
        BakedAnimationData? baked, CharacterDefinition? def,
        string[]? animationNames, byte animIndex, byte slot, bool airborne,
        out float wx, out float wy, out float wz,
        out float wex, out float wey, out float wez)
    {
        float cos = MathF.Cos(s.FacingYaw);
        float sin = MathF.Sin(s.FacingYaw);

        // Weapon-anchored hitboxes (a synthetic `_` baked point is involved — e.g.
        // _weapon_tip, which is baked at VISUAL scale from the actual sword) must not
        // be shrunk by HurtboxBoneScale: the blade capsule must span the exact visual
        // hand→tip, or it starts short of the hand and ends short of the tip. Both
        // endpoints of such a capsule resolve at scale 1.0.
        bool weaponAnchored =
            (evt.BoneName != null && evt.BoneName.StartsWith("_", StringComparison.Ordinal)) ||
            (evt.EndBoneName != null && evt.EndBoneName.StartsWith("_", StringComparison.Ordinal));
        float resolveScale = weaponAnchored ? 1f : (def?.HurtboxBoneScale ?? 1f);

        bool resolved = false;
        wx = wy = wz = 0f;

        // ── Bone-attached hitbox path (requires baked data) ──
        if (evt.BoneName != null && baked != null &&
            TryResolveBone(s, evt.BoneName, baked, def, animationNames, animIndex, slot, airborne, resolveScale,
                out float bx, out float by, out float bz))
        {
            wx = bx + ((evt.OffX * cos) + (evt.OffZ * sin));
            wy = by + evt.OffY;
            wz = bz + ((-evt.OffX * sin) + (evt.OffZ * cos));
            resolved = true;
        }

        // ── Fallback: entity-relative offset (standard path) ──
        if (!resolved)
        {
            wx = s.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
            wy = s.PY + evt.OffY;
            wz = s.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));
        }

        // ── Capsule end: second baked point when EndBoneName is set, else the EndOff
        // delta (facing-rotated) from the start point, exactly as before. ──
        if (evt.EndBoneName != null && baked != null &&
            TryResolveBone(s, evt.EndBoneName, baked, def, animationNames, animIndex, slot, airborne, resolveScale,
                out float ex, out float ey, out float ez))
        {
            wex = ex + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
            wey = ey + evt.EndOffY;
            wez = ez + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));
        }
        else
        {
            wex = wx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
            wey = wy + evt.EndOffY;
            wez = wz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));
        }
        if (s.AttackPosePitch != 0f && def != null)
        {
            ApplyAttackPosePitch(in s, def, ref wx, ref wy, ref wz);
            ApplyAttackPosePitch(in s, def, ref wex, ref wey, ref wez);
        }
        }

    /// <summary>
    /// Resolve a baked point's world position for a bone name: find it in the bake's
    /// BoneNames, project the current AttackElapsedTicks to a baked frame via the
    /// stage duration, and transform the baked Hips-relative position to world space
    /// (scale + facing yaw + Y remap).
    /// <paramref name="scale"/> is HurtboxBoneScale for ordinary bone hitboxes, but 1.0
    /// for weapon-anchored ones (synthetic `_` points are baked at visual scale).
    /// Returns false (caller falls back) when the bone, animation, or def is missing.
    /// </summary>
    private static bool TryResolveBone(
        in CharacterState s, string boneName,
        BakedAnimationData? baked, CharacterDefinition? def,
        string[]? animationNames, byte animIndex, byte slot, bool airborne, float scale,
        out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (baked == null || def == null) return false;

        // The bake holds exactly the bones the baker wrote (SlopArenaBaker's curated
        // mixamorig list: head/spine2/hips/hands/feet/toes, plus synthetic points
        // like _weapon_tip); GetBonePosition indexes BoneNames directly. HurtboxBoneDefs
        // order is unrelated to bake order, so a bone the bake lacks has no position.
        int bi = baked.FindBoneIndex(boneName);
        if (bi < 0) return false;

        string targetAnim = animationNames != null && animationNames.Length > animIndex
            ? animationNames[animIndex] : "idle";
        int animIdx = baked.FindAnimIndex(targetAnim);
        if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
        if (animIdx < 0) return false;

        int fc = baked.Animations[animIdx].FrameCount;
        var spec = def.GetSlotAbility(slot, airborne);
        int durationTicks = (spec != null && animIndex < spec.Stages.Length)
            ? spec.Stages[animIndex].DurationTicks
            : 60;
        int bakedFrame = durationTicks > 0
            ? Math.Min(s.AttackElapsedTicks * fc / durationTicks, fc - 1)
            : Math.Min(s.AttackElapsedTicks, fc - 1);
        if (bakedFrame >= fc) bakedFrame = fc - 1;

        return BakedPointToWorld(s, def, baked, animIdx, bakedFrame, bi, scale, out x, out y, out z);
    }

    /// <summary>
    /// World position of one baked point, before attack-pose pitch. Points are stored
    /// hips-relative; <paramref name="scale"/> pulls them toward the hips
    /// (<c>HurtboxBoneScale</c> for body points, 1 for weapon points). The hips sit where
    /// the animation draws them when the bake carries
    /// <see cref="BakedAnimationData.HipsOffsetPointName"/>, else at the fixed
    /// <c>HipHeight</c> pivot. Shared by hitboxes, hurtboxes and the Ability Lab.
    /// </summary>
    public static bool BakedPointToWorld(
        in CharacterState s, CharacterDefinition def, BakedAnimationData baked,
        int animIndex, int frame, int pointIndex, float scale,
        out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (!baked.TryGetPoint(animIndex, frame, pointIndex, out float bx, out float by, out float bz))
            return false;
        float cos = MathF.Cos(s.FacingYaw);
        float sin = MathF.Sin(s.FacingYaw);
        int hipsIndex = baked.HipsOffsetIndex;
        if (hipsIndex < 0)
        {
            bx *= scale; by *= scale; bz *= scale;
            x = s.PX + ((bx * cos) + (bz * sin));
            y = def.BoneYToWorldY(s.PY, by);
            z = s.PZ + ((-bx * sin) + (bz * cos));
            return true;
        }

        baked.TryGetPoint(animIndex, frame, hipsIndex, out float hx, out float hy, out float hz);
        if (pointIndex == hipsIndex) bx = by = bz = 0f;
        float lx = hx + (bx * scale);
        float ly = hy + (by * scale);
        float lz = hz + (bz * scale);
        x = s.PX + ((lx * cos) + (lz * sin));
        y = s.PY + ly;
        z = s.PZ + ((-lx * sin) + (lz * cos));
        return true;
    }
}
