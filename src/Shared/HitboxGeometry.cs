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
    /// <summary>Most poses a swept hitbox tests in one tick, the current pose included.</summary>
    public const int MaxSweepSteps = 24;

    /// <summary>Closest spacing between swept poses, in metres.</summary>
    private const float MinSweepSpacing = 0.05f;

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
    /// Clip position of an attack pose, in baked frames: the elapsed ticks mapped onto the
    /// clip so it spans the stage, clamped to the last frame. Fractional, like the clip
    /// time the renderer plays; poses interpolate between the neighbouring baked frames.
    /// </summary>
    public static float AttackFrame(float elapsedTicks, int frameCount, int durationTicks)
    {
        if (frameCount <= 1) return 0f;
        float frame = durationTicks > 0 ? elapsedTicks * frameCount / durationTicks : elapsedTicks;
        if (frame <= 0f) return 0f;
        return frame >= frameCount - 1 ? frameCount - 1 : frame;
    }

    /// <summary>
    /// Resolve a hitbox's world-space start/end positions at the state's AttackElapsedTicks.
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
    /// <param name="move">Cooked move selected at activation, including its ground/air variant.</param>
    /// <param name="animIndex">Presentation clip index, independent of the timeline stage.</param>
    public static void ResolvePositions(
        in CharacterState s, in HitboxEvent evt,
        BakedAnimationData? baked, CharacterDefinition? def,
        CookedSlotDefinition? move, byte animIndex,
        out float wx, out float wy, out float wz,
        out float wex, out float wey, out float wez)
        => ResolvePositionsAt(in s, in evt, baked, def, move, animIndex,
            s.AttackElapsedTicks, out wx, out wy, out wz, out wex, out wey, out wez);

    /// <summary>
    /// <see cref="ResolvePositions"/> at an explicit, possibly fractional, attack time.
    /// Between baked frames a capsule with two baked ends turns rigidly (start point
    /// interpolated, direction and length blended) so a fast blade keeps its length.
    /// </summary>
    public static void ResolvePositionsAt(
        in CharacterState s, in HitboxEvent evt,
        BakedAnimationData? baked, CharacterDefinition? def,
        CookedSlotDefinition? move, byte animIndex, float elapsedTicks,
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
        float scale = weaponAnchored ? 1f : (def?.HurtboxBoneScale ?? 1f);

        // The bake holds exactly the bones the baker wrote (head/spine2/hips/hands/feet/
        // toes, plus synthetic points like _weapon_tip). HurtboxBoneDefs order is
        // unrelated to bake order, so a bone the bake lacks has no position.
        int animIdx = -1, startPoint = -1, endPoint = -1;
        float frame = 0f;
        if (baked != null && def != null && (evt.BoneName != null || evt.EndBoneName != null)
            && TryResolveFrame(baked, def, move, animIndex, elapsedTicks, out animIdx, out frame))
        {
            if (evt.BoneName != null) startPoint = baked.FindBoneIndex(evt.BoneName);
            if (evt.EndBoneName != null) endPoint = baked.FindBoneIndex(evt.EndBoneName);
        }

        int frame0 = (int)frame;
        PoseAtFrame(in s, in evt, baked, def, animIdx, frame0, startPoint, endPoint, scale, cos, sin,
            out wx, out wy, out wz, out wex, out wey, out wez);
        float blend = frame - frame0;
        if (blend > 0f && (startPoint >= 0 || endPoint >= 0))
        {
            PoseAtFrame(in s, in evt, baked, def, animIdx, frame0 + 1, startPoint, endPoint, scale, cos, sin,
                out float x1, out float y1, out float z1, out float ex1, out float ey1, out float ez1);
            BlendPose(startPoint >= 0 && endPoint >= 0, blend,
                ref wx, ref wy, ref wz, ref wex, ref wey, ref wez, x1, y1, z1, ex1, ey1, ez1);
        }

        if (s.AttackPosePitch != 0f && def != null)
        {
            ApplyAttackPosePitch(in s, def, ref wx, ref wy, ref wz);
            ApplyAttackPosePitch(in s, def, ref wex, ref wey, ref wez);
        }
    }

    /// <summary>
    /// The poses a tracked hitbox passes through between the previous tick's owner sample
    /// and the current owner state, earliest first and excluding both ends: the previous
    /// pose was tested last tick and the current pose is the hitbox itself. The owner's
    /// position, facing and pitch blend between the two samples; the clip follows every
    /// baked frame in between. Spacing stays within half the radius, so the union of the
    /// poses covers the swept volume. Returns null when the hitbox barely moved, after an
    /// attack-clock seek or reset, or when the stage changed.
    /// </summary>
    public static HitboxPose[]? SweepPoses(
        in HitboxEvent evt, BakedAnimationData? baked, CharacterDefinition? def,
        CookedSlotDefinition? move, byte animIndex,
        in HitboxSweepOrigin from, in CharacterState owner, in HitboxPose current)
    {
        int clockStep = owner.AttackElapsedTicks - from.AttackElapsedTicks;
        if (clockStep < 0 || clockStep > 1 || owner.ComboStage != from.ComboStage)
            return null;

        float travel = MathF.Max(
            Distance(from.Pose.X, from.Pose.Y, from.Pose.Z, current.X, current.Y, current.Z),
            Distance(from.Pose.EndX, from.Pose.EndY, from.Pose.EndZ, current.EndX, current.EndY, current.EndZ));
        int steps = (int)MathF.Ceiling(travel / MathF.Max(MinSweepSpacing, evt.Radius * 0.5f));
        if (clockStep > 0 && baked != null && def != null
            && TryResolveFrame(baked, def, move, animIndex, from.AttackElapsedTicks, out _, out float frame0)
            && TryResolveFrame(baked, def, move, animIndex, owner.AttackElapsedTicks, out _, out float frame1))
            steps = Math.Max(steps, (int)MathF.Ceiling((frame1 - frame0) * 2f));
        if (steps > MaxSweepSteps) steps = MaxSweepSteps;
        if (steps <= 1) return null;

        float yawStep = owner.FacingYaw - from.FacingYaw;
        while (yawStep > MathF.PI) yawStep -= 2f * MathF.PI;
        while (yawStep < -MathF.PI) yawStep += 2f * MathF.PI;
        var poses = new HitboxPose[steps - 1];
        var sample = owner;
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            sample.PX = from.PX + (owner.PX - from.PX) * t;
            sample.PY = from.PY + (owner.PY - from.PY) * t;
            sample.PZ = from.PZ + (owner.PZ - from.PZ) * t;
            sample.FacingYaw = from.FacingYaw + yawStep * t;
            sample.AttackPosePitch = from.AttackPosePitch + (owner.AttackPosePitch - from.AttackPosePitch) * t;
            ResolvePositionsAt(in sample, in evt, baked, def, move, animIndex,
                from.AttackElapsedTicks + clockStep * t,
                out float x, out float y, out float z, out float ex, out float ey, out float ez);
            poses[i - 1] = new HitboxPose { X = x, Y = y, Z = z, EndX = ex, EndY = ey, EndZ = ez };
        }
        return poses;
    }

    /// <summary>
    /// Find the pose animation and its fractional baked frame for an attack time. Falls back
    /// to idle when the stage animation is missing; false when the bake has neither.
    /// </summary>
    private static bool TryResolveFrame(
        BakedAnimationData baked, CharacterDefinition def, CookedSlotDefinition? move,
        byte animIndex, float elapsedTicks,
        out int animIdx, out float frame)
    {
        frame = 0f;
        if (move == null) { animIdx = -1; return false; }
        string targetAnim = move.Clip(animIndex).AnimationId;
        animIdx = baked.FindAnimIndex(targetAnim);
        if (animIdx < 0) animIdx = baked.FindAnimIndex("idle");
        if (animIdx < 0) return false;

        int frameCount = baked.Animations[animIdx].FrameCount;
        if (frameCount <= 0) return false;
        frame = move.Clip(animIndex).At(elapsedTicks).FrameAt(frameCount);

        return true;
    }

    /// <summary>World start/end of a hitbox at one whole baked frame, before attack-pose pitch.</summary>
    private static void PoseAtFrame(
        in CharacterState s, in HitboxEvent evt, BakedAnimationData? baked, CharacterDefinition? def,
        int animIdx, int frame, int startPoint, int endPoint, float scale, float cos, float sin,
        out float wx, out float wy, out float wz, out float wex, out float wey, out float wez)
    {
        // Bone anchor plus the anchor-relative offset; entity-relative offset otherwise.
        if (startPoint >= 0
            && BakedPointToWorld(in s, def!, baked!, animIdx, frame, startPoint, scale, out float bx, out float by, out float bz))
        {
            wx = bx + ((evt.OffX * cos) + (evt.OffZ * sin));
            wy = by + evt.OffY;
            wz = bz + ((-evt.OffX * sin) + (evt.OffZ * cos));
        }
        else
        {
            wx = s.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
            wy = s.PY + evt.OffY;
            wz = s.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));
        }

        // Capsule end: second baked point when EndBoneName is set, else the EndOff
        // delta (facing-rotated) from the start point.
        if (endPoint >= 0
            && BakedPointToWorld(in s, def!, baked!, animIdx, frame, endPoint, scale, out float ex, out float ey, out float ez))
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
    }

    /// <summary>
    /// Blend one segment toward another the way swept and between-frame hitbox poses do:
    /// the start point is interpolated and, when <paramref name="rigid"/>, the end follows
    /// the blended direction at the blended length. The renderer's baked blade path uses it
    /// so the drawn trail and the tested blade agree between baked frames.
    /// </summary>
    public static void BlendSegment(
        bool rigid, float blend,
        ref float startX, ref float startY, ref float startZ, ref float endX, ref float endY, ref float endZ,
        float nextStartX, float nextStartY, float nextStartZ, float nextEndX, float nextEndY, float nextEndZ)
        => BlendPose(rigid, blend, ref startX, ref startY, ref startZ, ref endX, ref endY, ref endZ,
            nextStartX, nextStartY, nextStartZ, nextEndX, nextEndY, nextEndZ);

    /// <summary>
    /// Blend a pose toward the next frame's pose. With two baked ends the segment moves
    /// rigidly: the start point is interpolated and the end follows the blended direction
    /// at the blended length, so a turning blade does not shorten mid-frame.
    /// </summary>
    private static void BlendPose(
        bool rigid, float blend,
        ref float sx, ref float sy, ref float sz, ref float ex, ref float ey, ref float ez,
        float sx1, float sy1, float sz1, float ex1, float ey1, float ez1)
    {
        float dx0 = ex - sx, dy0 = ey - sy, dz0 = ez - sz;
        float dx1 = ex1 - sx1, dy1 = ey1 - sy1, dz1 = ez1 - sz1;
        float lerpX = ex + (ex1 - ex) * blend, lerpY = ey + (ey1 - ey) * blend, lerpZ = ez + (ez1 - ez) * blend;
        sx += (sx1 - sx) * blend;
        sy += (sy1 - sy) * blend;
        sz += (sz1 - sz) * blend;
        ex = lerpX; ey = lerpY; ez = lerpZ;
        if (!rigid) return;

        float length0 = MathF.Sqrt((dx0 * dx0) + (dy0 * dy0) + (dz0 * dz0));
        float length1 = MathF.Sqrt((dx1 * dx1) + (dy1 * dy1) + (dz1 * dz1));
        if (length0 < 1e-6f || length1 < 1e-6f) return;
        float ux = (dx0 / length0) + ((dx1 / length1) - (dx0 / length0)) * blend;
        float uy = (dy0 / length0) + ((dy1 / length1) - (dy0 / length0)) * blend;
        float uz = (dz0 / length0) + ((dz1 / length1) - (dz0 / length0)) * blend;
        float directionLength = MathF.Sqrt((ux * ux) + (uy * uy) + (uz * uz));
        if (directionLength < 1e-6f) return; // opposite directions: keep the straight blend
        float length = (length0 + (length1 - length0) * blend) / directionLength;
        ex = sx + ux * length;
        ey = sy + uy * length;
        ez = sz + uz * length;
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

    /// <summary>
    /// <see cref="BakedPointToWorld(in CharacterState, CharacterDefinition, BakedAnimationData, int, int, int, float, out float, out float, out float)"/>
    /// at a fractional frame, interpolated between the two neighbouring baked frames.
    /// </summary>
    public static bool BakedPointToWorld(
        in CharacterState s, CharacterDefinition def, BakedAnimationData baked,
        int animIndex, float frame, int pointIndex, float scale,
        out float x, out float y, out float z)
    {
        int frame0 = frame <= 0f ? 0 : (int)frame;
        if (!BakedPointToWorld(in s, def, baked, animIndex, frame0, pointIndex, scale, out x, out y, out z))
            return false;
        float blend = frame - frame0;
        if (blend <= 0f
            || !BakedPointToWorld(in s, def, baked, animIndex, frame0 + 1, pointIndex, scale, out float x1, out float y1, out float z1))
            return true;
        x += (x1 - x) * blend;
        y += (y1 - y) * blend;
        z += (z1 - z) * blend;
        return true;
    }

    private static float Distance(float x0, float y0, float z0, float x1, float y1, float z1)
        => MathF.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)) + ((z1 - z0) * (z1 - z0)));
}
