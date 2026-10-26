using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    [DefaultExecutionOrder(810)]
    public sealed class WhistlePuffRig : MonoBehaviour
    {
        // Measured in the Whistle bone's frame on Bashie's imported mesh.
        private static readonly Vector3 Mouthpiece = new(0.0418f, 0.0714f, 0.0547f);
        private static readonly Quaternion HeldRotation = Quaternion.LookRotation(Vector3.back, Vector3.up)
            * Quaternion.Inverse(Quaternion.LookRotation(Mouthpiece - new Vector3(0.01f, 0.035f, 0.035f),
                new Vector3(0.01f, 0.035f, 0.035f)));

        private Transform _head = null!, _whistle = null!, _shell = null!;
        private SkinnedMeshRenderer? _body;
        private int _puffShape = -1;
        private Vector3 _shellScale, _beakInHead;
        private Quaternion _heldInHead;
        private Vector3 _whistlePosition, _whistleScale;
        private Quaternion _whistleRotation;
        private bool _requested, _applied;
        private float _puff, _hold;

        public static WhistlePuffRig? For(PlayerRenderer owner)
        {
            var animator = owner.Animator;
            if (animator == null || !animator.isHuman) return null;
            var rig = animator.GetComponent<WhistlePuffRig>();
            if (rig != null) return rig;
            rig = animator.gameObject.AddComponent<WhistlePuffRig>();
            if (rig.Bind(animator)) return rig;
            Destroy(rig);
            return null;
        }

        public void Request(float puff, float hold)
        {
            _puff = Mathf.Max(0f, puff);
            _hold = Mathf.Clamp01(hold);
            _requested = true;
        }

        private bool Bind(Animator animator)
        {
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Whistle") _whistle = t;
                if (t.name == "Shell") _shell = t;
            }
            if (_head == null || _whistle == null) return false;
            var rest = new Dictionary<Transform, Matrix4x4>();
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var binds = skin.sharedMesh.bindposes;
                var bones = skin.bones;
                var onModel = transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                for (int i = 0; i < bones.Length && i < binds.Length; i++)
                    if (bones[i] != null && !rest.ContainsKey(bones[i])) rest[bones[i]] = onModel * binds[i].inverse;
            }
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.name == "Bashie_Body") _body = skin;
            if (_body != null && _body.sharedMesh != null)
                _puffShape = _body.sharedMesh.GetBlendShapeIndex("Puff");
            if (_puffShape < 0)
                Debug.LogWarning("[WhistlePuffRig] Bashie_Body has no Puff blend shape. Rerun Bashie's presentation builder.", this);
            if (_shell != null) _shellScale = LocalRest(rest, _shell).lossyScale;
            if (!rest.TryGetValue(_head, out var headRest)) return false;
            _beakInHead = headRest.inverse.MultiplyPoint3x4(headRest.GetPosition() + new Vector3(0.065f, 0.10f, 0.203f));
            _heldInHead = Quaternion.Inverse(headRest.rotation) * HeldRotation;
            var whistle = LocalRest(rest, _whistle);
            _whistlePosition = whistle.GetPosition();
            _whistleRotation = whistle.rotation;
            _whistleScale = whistle.lossyScale;
            return true;
        }

        private static Matrix4x4 LocalRest(Dictionary<Transform, Matrix4x4> rest, Transform bone)
            => rest.TryGetValue(bone, out var own) && rest.TryGetValue(bone.parent, out var parent)
                ? parent.inverse * own : Matrix4x4.TRS(bone.localPosition, bone.localRotation, bone.localScale);

        private void Update()
        {
            // Humanoid animation does not reset scales or this non-Humanoid prop.
            if (_applied)
            {
                Restore();
                _applied = false;
            }
        }

        private void LateUpdate()
        {
            if (_requested)
            {
                if (_puffShape >= 0 && _body != null)
                {
                    _body.SetBlendShapeWeight(_puffShape, 100f * _puff);
                    if (_shell != null) _shell.localScale = _shellScale * (1f + 0.2f * _puff);
                }
                if (_hold > 0f)
                {
                    Vector3 beak = _head.TransformPoint(_beakInHead);
                    Quaternion rotation = _head.rotation * _heldInHead;
                    _whistle.rotation = Quaternion.Slerp(_whistle.rotation, rotation, _hold);
                    Vector3 mouthpiece = _whistle.TransformVector(Mouthpiece);
                    _whistle.position = Vector3.Lerp(_whistle.position, beak - mouthpiece, _hold);
                }
                _applied = true;
            }
            else if (_applied)
            {
                Restore();
                _applied = false;
            }
            _requested = false;
        }

        private void Restore()
        {
            if (_puffShape >= 0 && _body != null) _body.SetBlendShapeWeight(_puffShape, 0f);
            if (_shell != null) _shell.localScale = _shellScale;
            if (_whistle != null)
            {
                _whistle.SetLocalPositionAndRotation(_whistlePosition, _whistleRotation);
                _whistle.localScale = _whistleScale;
            }
        }

        private void OnDisable()
        {
            if (_applied) Restore();
            _requested = _applied = false;
        }
    }
}
