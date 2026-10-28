using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    internal readonly struct ShellTuckMotion
    {
        internal readonly Quaternion? OriginalTuckedRotation;
        internal readonly float ModelLift;

        internal ShellTuckMotion(Quaternion? originalTuckedRotation, float modelLift)
        {
            OriginalTuckedRotation = originalTuckedRotation;
            ModelLift = modelLift;
        }
    }

    /// <summary>
    /// How a fighter folds into its shell (<see cref="ShellTuckRig"/>). Lengths are in the model's own units (metres
    /// at visual scale 1), along its root: +Z forward, +Y up, the floor at 0.
    /// </summary>
    [System.Serializable]
    public struct ShellTuckPose
    {
        [Tooltip("Shell bone (not Humanoid) the fighter folds into; it grows by ShellScale.")]
        public string ShellBone;
        [Tooltip("Bones shrunk to nothing while tucked: Bashie's whistle.")]
        public string[] HiddenBones;
        public float ShellScale, ArmScale, LegScale, HeadScale;
        [Tooltip("Forward pitch of the body when tucked: 90 lays her on her belly, the shell on top.")]
        public float PitchDegrees;
        [Tooltip("Hips position when tucked, on the model root.")]
        public Vector3 Hips;
        [Tooltip("Where the head peeks out: from the shell bone, along the model root.")]
        public Vector3 Head;
        [Tooltip("Rock about the travel direction while tucked: degrees, and cycles per second.")]
        public float WobbleDegrees, WobbleHz;
        [Tooltip("Keep the clip's horizontal turn while fully tucked, rotating the body around the shell centre.")]
        public bool FollowAnimatedHeading;

        /// <summary>
        /// Bashie's look A, picked by the owner on 2026-10-22 from Blender renders of her rig
        /// (tools/blender/bashie_rig/tuck_preview.py): her hat and face peek out of the front, so she still reads as
        /// Bashie from the match camera behind her.
        /// </summary>
        public static ShellTuckPose Bashie => new()
        {
            ShellBone = "Shell",
            HiddenBones = new[] { "Whistle" },
            ShellScale = 1.7f,
            ArmScale = 0.15f,
            LegScale = 0.3f,
            HeadScale = 0.8f,
            PitchDegrees = 90f,
            Hips = new Vector3(0f, 0.141f, -0.11f),
            Head = new Vector3(0f, -0.16f, 0.33f),
            WobbleDegrees = 4f,
            WobbleHz = 1.6f,
        };
    }

    /// <summary>
    /// Folds a Humanoid fighter into its own shell after its animation each frame: Bashie's Shell Slide. At weight 1
    /// the body lies prone with a straight back, so the shell (grown) is on top; the arms and legs shrink into it, the
    /// hidden bones vanish, and the head is held upright where it peeks out of the shell's front. Whatever clip plays
    /// underneath, the tucked shape is the same. Weights past 1 or below 0 overshoot (the pop in and out). Cues ask
    /// for it every frame with <see cref="Request"/> (the last request wins); with none, the bones go back to rest.
    /// Humanoid clips never write bone scale, so this restores every scale it changed. Rest poses come from the skin's
    /// bind poses. Cosmetic only: the simulated hurtbox follows the clip, not this.
    /// </summary>
    [DefaultExecutionOrder(800)] // after the clip and WeaponAttach; before FighterHitFeel (900) and BoneChainSpring (1000)
    public sealed class ShellTuckRig : MonoBehaviour
    {
        private const float MinScale = 0.001f;

        private Transform _root = null!;
        private Transform _hips = null!, _head = null!, _shell = null!;
        private Transform[] _spine = System.Array.Empty<Transform>();
        private Quaternion[] _spineRest = System.Array.Empty<Quaternion>();
        private Transform[] _arms = System.Array.Empty<Transform>(), _legs = System.Array.Empty<Transform>();
        private readonly List<Transform> _hidden = new();
        private readonly Dictionary<Transform, Vector3> _restScale = new();
        private Quaternion _hipsRest, _headRest;
        private Vector3 _headRestLocal;
        private bool _requested, _applied;
        private ShellTuckPose _pose;
        private float _weight, _clock;
        private ShellTuckMotion? _motion;
        private Transform[] _animatedBones = System.Array.Empty<Transform>();
        private Vector3[] _animatedPositions = System.Array.Empty<Vector3>();
        private Quaternion[] _animatedRotations = System.Array.Empty<Quaternion>();
        private bool _restoreAnimationPose;

        /// <summary>The rig on the owner's model, added on first use; null when the model has no Humanoid shell rig.</summary>
        public static ShellTuckRig? For(PlayerRenderer owner, in ShellTuckPose pose)
        {
            Animator? animator = owner != null ? owner.Animator : null;
            if (animator == null || !animator.isHuman) return null;
            var rig = animator.GetComponent<ShellTuckRig>();
            if (rig != null) return rig;
            rig = animator.gameObject.AddComponent<ShellTuckRig>();
            if (rig.Bind(animator, pose)) return rig;
            Destroy(rig);
            return null;
        }

        /// <summary>The tuck weight applied last frame; 0 when the rig is at rest.</summary>
        public float AppliedWeight => _applied ? _weight : 0f;

        /// <summary>Tuck this frame at <paramref name="weight"/> (0 rest, 1 tucked; beyond overshoots).</summary>
        public void Request(in ShellTuckPose pose, float weight)
        {
            _pose = pose;
            _weight = weight;
            _motion = null;
            _requested = true;
        }

        internal void Request(in ShellTuckPose pose, float weight, in ShellTuckMotion motion)
        {
            Request(pose, weight);
            _motion = motion;
        }

        private bool Bind(Animator animator, in ShellTuckPose pose)
        {
            _root = animator.transform;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _shell = FindChild(_root, pose.ShellBone)!;
            if (_hips == null || _head == null || _shell == null) return false;

            var rest = RestPoses();
            var spine = new List<Transform>();
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck })
                if (animator.GetBoneTransform(bone) is { } t) spine.Add(t);
            _spine = spine.ToArray();
            _animatedBones = new Transform[_spine.Length + 2];
            _animatedBones[0] = _hips;
            _animatedBones[1] = _head;
            _spine.CopyTo(_animatedBones, 2);
            _animatedPositions = new Vector3[_animatedBones.Length];
            _animatedRotations = new Quaternion[_animatedBones.Length];
            _spineRest = new Quaternion[_spine.Length];
            for (int i = 0; i < _spine.Length; i++)
                _spineRest[i] = RestLocal(rest, _spine[i]).rotation;
            _hipsRest = rest.TryGetValue(_hips, out var hips) ? hips.rotation : Quaternion.Inverse(_root.rotation) * _hips.rotation;
            _headRest = rest.TryGetValue(_head, out var head) ? head.rotation : Quaternion.Inverse(_root.rotation) * _head.rotation;
            _headRestLocal = RestLocal(rest, _head).GetPosition();

            _arms = Bones(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
            _legs = Bones(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
            foreach (string name in pose.HiddenBones ?? System.Array.Empty<string>())
                if (FindChild(_root, name) is { } t) _hidden.Add(t);
            foreach (var t in Scaled())
                _restScale[t] = t.localScale;
            return true;
        }

        private void Update()
        {
            // Remove the previous flip before animation evaluates, including frozen and culled frames.
            RestoreAnimationPose();
        }

        private void LateUpdate()
        {
            if (_requested)
            {
                if (_motion.HasValue) CaptureAnimationPose();
                Apply(_pose, _weight);
                _applied = true;
            }
            else if (_applied)
            {
                Restore();
                _applied = false;
            }
            _requested = false;
            _motion = null;
        }

        private void OnDisable()
        {
            RestoreAnimationPose();
            if (_applied) Restore();
            _applied = _requested = false;
            _motion = null;
        }

        private void Apply(in ShellTuckPose pose, float w)
        {
            Quaternion nativeLocalRotation = _hips.localRotation;
            Quaternion nativeModelRotation = Quaternion.Inverse(_root.rotation) * _hips.parent.rotation * nativeLocalRotation;
            float held = Mathf.Clamp01(w);
            Vector3 animatedForward = Vector3.ProjectOnPlane(_head.position - _hips.position, _root.up);
            _clock += Time.deltaTime;
            Quaternion facing = _root.rotation;
            float wobble = pose.WobbleDegrees * held * Mathf.Sin(_clock * 2f * Mathf.PI * pose.WobbleHz);
            Quaternion prone = facing * Quaternion.AngleAxis(wobble, Vector3.forward)
                * Quaternion.AngleAxis(pose.PitchDegrees, Vector3.right) * _hipsRest;
            _hips.SetPositionAndRotation(
                Vector3.LerpUnclamped(_hips.position, _root.TransformPoint(pose.Hips), w),
                Quaternion.SlerpUnclamped(_hips.rotation, prone, w));
            for (int i = 0; i < _spine.Length; i++)
                _spine[i].localRotation = Quaternion.SlerpUnclamped(_spine[i].localRotation, _spineRest[i], w);

            Scale(_shell, Mathf.LerpUnclamped(1f, pose.ShellScale, w));
            foreach (var arm in _arms) Scale(arm, Mathf.LerpUnclamped(1f, pose.ArmScale, w));
            foreach (var leg in _legs) Scale(leg, Mathf.LerpUnclamped(1f, pose.LegScale, w));
            foreach (var bone in _hidden) Scale(bone, Mathf.Lerp(1f, 0f, held));

            // The head's local position is the rig's, not the clip's: start from rest so nothing piles up frame on frame.
            Vector3 animated = _head.parent.TransformPoint(_headRestLocal);
            Vector3 peek = _shell.position + facing * Vector3.Scale(pose.Head, _root.lossyScale);
            _head.SetPositionAndRotation(
                Vector3.LerpUnclamped(animated, peek, w),
                Quaternion.Slerp(_head.rotation, facing * _headRest, held));
            Scale(_head, Mathf.LerpUnclamped(1f, pose.HeadScale, w));
            if (pose.FollowAnimatedHeading && held >= 0.999f && animatedForward.sqrMagnitude > 0.000001f)
                _hips.RotateAround(_shell.position, _root.up,
                    Vector3.SignedAngle(_root.forward, animatedForward, _root.up));
            if (_motion is { } motion)
            {
                if (motion.OriginalTuckedRotation is { } original)
                {
                    Quaternion delta = facing * (nativeModelRotation * Quaternion.Inverse(original)) * Quaternion.Inverse(facing);
                    Vector3 pivot = _shell.position;
                    _hips.SetPositionAndRotation(pivot + delta * (_hips.position - pivot), delta * _hips.rotation);
                }
                _hips.position += _root.TransformVector(Vector3.up * motion.ModelLift);
            }
        }

        private void CaptureAnimationPose()
        {
            for (int i = 0; i < _animatedBones.Length; i++)
            {
                _animatedPositions[i] = _animatedBones[i].localPosition;
                _animatedRotations[i] = _animatedBones[i].localRotation;
            }
            _restoreAnimationPose = true;
        }

        private void RestoreAnimationPose()
        {
            if (!_restoreAnimationPose) return;
            for (int i = 0; i < _animatedBones.Length; i++)
            {
                var bone = _animatedBones[i];
                if (bone == null) continue;
                bone.SetLocalPositionAndRotation(_animatedPositions[i], _animatedRotations[i]);
            }
            _restoreAnimationPose = false;
        }

        private void Restore()
        {
            foreach (var (bone, scale) in _restScale)
                if (bone != null) bone.localScale = scale;
            if (_head != null) _head.localPosition = _headRestLocal;
        }

        private void Scale(Transform bone, float factor)
            => bone.localScale = _restScale[bone] * Mathf.Max(MinScale, factor);

        private IEnumerable<Transform> Scaled()
        {
            yield return _shell;
            yield return _head;
            foreach (var t in _arms) yield return t;
            foreach (var t in _legs) yield return t;
            foreach (var t in _hidden) yield return t;
        }

        /// <summary>Each skinned bone's bind pose on the model root.</summary>
        private Dictionary<Transform, Matrix4x4> RestPoses()
        {
            var rest = new Dictionary<Transform, Matrix4x4>();
            foreach (var skin in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var bones = skin.bones;
                var binds = skin.sharedMesh.bindposes;
                Matrix4x4 skinOnRoot = _root.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                for (int i = 0; i < bones.Length && i < binds.Length; i++)
                    if (bones[i] != null && !rest.ContainsKey(bones[i]))
                        rest[bones[i]] = skinOnRoot * binds[i].inverse;
            }
            return rest;
        }

        /// <summary>A bone's rest pose under its parent, or its current one when the skin doesn't know either.</summary>
        private Matrix4x4 RestLocal(Dictionary<Transform, Matrix4x4> rest, Transform bone)
        {
            if (rest.TryGetValue(bone, out var own) && rest.TryGetValue(bone.parent, out var parent))
                return parent.inverse * own;
            return Matrix4x4.TRS(bone.localPosition, bone.localRotation, Vector3.one);
        }

        private static Transform[] Bones(Animator animator, params HumanBodyBones[] bones)
        {
            var found = new List<Transform>();
            foreach (var bone in bones)
                if (animator.GetBoneTransform(bone) is { } t) found.Add(t);
            return found.ToArray();
        }

        private static Transform? FindChild(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
