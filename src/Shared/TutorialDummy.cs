using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Scripted tutorial dummy input. The dummy is an ordinary fighter fed ordinary
    /// input, so its attacks, blocks and launches stay simulation-owned.
    /// </summary>
    public static class TutorialDummy
    {
        /// <summary>Capsule-centre distance at which a sparring dummy stops and swings.</summary>
        public const float SparringRange = 1.6f;
        /// <summary>One readable swing per interval, leaving time to raise and drop shield.</summary>
        public const uint SwingIntervalTicks = 80;
        private const float LevelTolerance = 0.3f;

        public static InputState Build(TutorialDummyBehavior behavior, in CharacterState dummy,
            in CharacterState player, uint tick)
        {
            if (behavior != TutorialDummyBehavior.Sparring || !dummy.IsGrounded || !player.IsGrounded
                || Math.Abs(player.PY - dummy.PY) > LevelTolerance)
                return default;
            float dx = player.PX - dummy.PX, dz = player.PZ - dummy.PZ;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < 0.001f) return default;
            var input = new InputState
            {
                AimYaw = (short)Math.Clamp((int)(MathF.Atan2(dx, dz) * (18000f / MathF.PI)), -18000, 18000),
                FaceToCamera = true,
            };
            // Walk only toward a player standing on the same floor, so the dummy never follows anyone offstage.
            if (distance > SparringRange)
            {
                input.MoveX = dx / distance;
                input.MoveY = dz / distance;
            }
            else if (tick % SwingIntervalTicks == 0 && dummy.State == ActionState.Idle && dummy.AnimLockTicks == 0)
                input.ActiveSlot = AbilitySlots.Normal1;
            return input;
        }
    }
}
