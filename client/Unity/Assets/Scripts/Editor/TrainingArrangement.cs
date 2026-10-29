#if UNITY_EDITOR
using System;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.EditorTools
{
    public readonly struct Arrangement
    {
        public readonly float? FacingRadians;
        public readonly bool FacingFromSpawn;
        public readonly byte? Form;
        public readonly float Distance;
        public readonly NpcAiMode Behavior;
        public readonly int Damage;
        public readonly bool NoCooldowns;
        public Arrangement(float? facing, bool spawn, byte? form, float distance, NpcAiMode behavior, int damage, bool noCooldowns = false)
        { FacingRadians = facing; FacingFromSpawn = spawn; Form = form; Distance = distance; Behavior = behavior; Damage = damage; NoCooldowns = noCooldowns; }
    }
    public readonly struct Arranged
    {
        public readonly float FacingRadians;
        public readonly Vector3 Player, Dummy;
        public readonly byte Form;
        public Arranged(float facing, Vector3 player, Vector3 dummy, byte form)
        { FacingRadians = facing; Player = player; Dummy = dummy; Form = form; }
    }
    public static class TrainingArrangement
    {
        public static Arranged Apply(TrainingMatch match, CameraMount mount, in Arrangement arrangement)
        {
            if (!match.CaptureSequenceReady || match.CaptureInputSequenceOwned || match.CaptureInputOverrideActive || match.CaptureTargetInput != null)
                throw new InvalidOperationException("A ready, unowned Training duel is required.");
            if (!float.IsFinite(arrangement.Distance) || arrangement.Distance < 1 || arrangement.Distance > 25
                || arrangement.Damage < 0 || arrangement.Damage > 300
                || !Enum.IsDefined(typeof(NpcAiMode), arrangement.Behavior)
                || arrangement.FacingRadians.HasValue && !float.IsFinite(arrangement.FacingRadians.Value))
                throw new ArgumentException("Use a finite facing, distance 1–25, damage 0–300 and a supported CPU behavior.");
            var definition = match.CaptureSimulation.GetDefinition(MatchConfig.LocalEntityId);
            byte form = arrangement.Form ?? match.GetCaptureState(MatchConfig.LocalEntityId).Form.Active;
            if (form > 1 || definition.ForForm(form).Form != form) throw new ArgumentException("The fighter does not have the requested form.");
            match.SetNpcMode(arrangement.Behavior);
            match.ResetTrainingPositions(); match.ResetTrainingDamage(); match.ClearCapturePresentation();
            match.SetSelectedNpcDamage(arrangement.Damage); match.SetNoCooldowns(arrangement.NoCooldowns);
            var player = match.GetCaptureState(MatchConfig.LocalEntityId);
            var dummy = match.GetCaptureState(match.SelectedNpcId);
            float yaw = arrangement.FacingFromSpawn ? player.FacingYaw : arrangement.FacingRadians ?? mount.GetCameraYawDeg() * Mathf.Deg2Rad;
            player.Form = new FormState { Active = form };
            player.JumpsLeft = definition.ForForm(form).Movement.MaxJumps;
            player.FacingYaw = player.AimYaw = yaw;
            dummy.PX = player.PX + Mathf.Sin(yaw) * arrangement.Distance;
            dummy.PY = player.PY;
            dummy.PZ = player.PZ + Mathf.Cos(yaw) * arrangement.Distance;
            dummy.FacingYaw = dummy.AimYaw = yaw + Mathf.PI;
            match.SetCaptureState(MatchConfig.LocalEntityId, player);
            match.SetCaptureState(match.SelectedNpcId, dummy);
            return new Arranged(yaw, new Vector3(player.PX, player.PY, player.PZ), new Vector3(dummy.PX, dummy.PY, dummy.PZ), form);
        }
    }
}
#endif
