using System;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>Presentation-only skirt motion. Added leaf chains never write mapped Humanoid bones.</summary>
    [DefaultExecutionOrder(1010)]
    [DisallowMultipleComponent]
    public sealed class SkirtBoneSpring : MonoBehaviour
    {
        [Serializable]
        public sealed class Chain
        {
            public Transform[] Bones = Array.Empty<Transform>();
        }

        [Serializable]
        public sealed class LegCapsule
        {
            public Transform Start;
            public Transform End;
            public float Radius = .08f;
        }

        [SerializeField] private Transform _hips;
        [SerializeField] private Transform _modelRoot;
        [SerializeField] private Chain[] _chains = Array.Empty<Chain>();
        [SerializeField] private LegCapsule[] _capsules = Array.Empty<LegCapsule>();
        [SerializeField, Range(0f, 1f)] private float _stiffness = .22f;
        [SerializeField, Range(0f, 1f)] private float _damping = .3f;
        [SerializeField, Range(0f, 1f)] private float _neighbourBlend = .35f;
        [SerializeField] private float _maxHemTravel = .2f;
        [SerializeField] private float _fabricClearance = .04f;
        [SerializeField] private float _teleportDistance = 1.5f;

        private Quaternion[][] _restRotation;
        private Quaternion[][] _restHipsRotation;
        private Quaternion[][] _poseRotation;
        private Vector3[][] _tipLocal;
        private Vector3[][] _restTipHips;
        private Vector3[][] _position;
        private Vector3[][] _previous;
        private Vector3[][] _next;
        private Vector3[][] _target;
        private Vector3[] _rootHips;
        private Vector3 _lastRoot;
        private Vector3 _restUpHips;
        private bool _reset = true;
        private float[] _lastLift;
        private SkinnedMeshRenderer _skin;
        private Vector3[] _vertices;
        private BoneWeight[] _weights;
        private Matrix4x4[] _bindposes;
        private Matrix4x4[] _skinMatrices;
        private int[] _skinChain;
        private int[] _skinSegment;
        private Transform[] _skinBones;
        private Vector3[] _capsuleStart, _capsuleAxis, _capsuleMin, _capsuleMax;
        private float[] _capsuleRadius;
        private Vector3[,] _contactPoint;
        private Vector3[,] _contactPush;
        private float[,] _contactDepth;

        private void OnEnable() => _reset = true;

        private void OnDisable()
        {
            if (_restRotation == null) return;
            for (int c = 0; c < _chains.Length; c++)
                for (int j = 0; j < _chains[c].Bones.Length; j++)
                    if (_chains[c].Bones[j] != null) _chains[c].Bones[j].localRotation = _restRotation[c][j];
        }

        public void ResetSimulation() => _reset = true;

        private bool Initialize()
        {
            if (_restRotation != null) return true;
            if (_hips == null || _modelRoot == null || _chains.Length < 3) return false;
            foreach (var chain in _chains)
                if (chain.Bones.Length < 2 || chain.Bones.Length != _chains[0].Bones.Length || Array.Exists(chain.Bones, b => b == null)) return false;
            int count = _chains.Length;
            _restRotation = new Quaternion[count][];
            _restHipsRotation = new Quaternion[count][];
            _poseRotation = new Quaternion[count][];
            _tipLocal = new Vector3[count][];
            _restTipHips = new Vector3[count][];
            _position = new Vector3[count][];
            _previous = new Vector3[count][];
            _next = new Vector3[count][];
            _target = new Vector3[count][];
            _rootHips = new Vector3[count];
            for (int c = 0; c < count; c++)
            {
                var bones = _chains[c].Bones;
                int n = bones.Length;
                _restRotation[c] = new Quaternion[n];
                _restHipsRotation[c] = new Quaternion[n];
                _poseRotation[c] = new Quaternion[n];
                _tipLocal[c] = new Vector3[n];
                _restTipHips[c] = new Vector3[n];
                _position[c] = new Vector3[n];
                _previous[c] = new Vector3[n];
                _next[c] = new Vector3[n];
                _target[c] = new Vector3[n];
                _rootHips[c] = _hips.InverseTransformPoint(bones[0].position);
                for (int j = 0; j < n; j++)
                {
                    var bone = bones[j];
                    _restRotation[c][j] = bone.localRotation;
                    _restHipsRotation[c][j] = Quaternion.Inverse(_hips.rotation) * bone.rotation;
                    Vector3 tip = j + 1 < n ? bones[j + 1].position : bone.position + bone.position - bones[j - 1].position;
                    _tipLocal[c][j] = bone.InverseTransformPoint(tip);
                    _restTipHips[c][j] = _hips.InverseTransformPoint(tip);
                }
            }
            _lastLift = new float[count];
            _skin = GetComponent<SkinnedMeshRenderer>();
            _vertices = _skin.sharedMesh.vertices;
            _weights = _skin.sharedMesh.boneWeights;
            _bindposes = _skin.sharedMesh.bindposes;
            _skinBones = _skin.bones;
            _skinMatrices = new Matrix4x4[_skinBones.Length];
            _capsuleStart = new Vector3[_capsules.Length]; _capsuleAxis = new Vector3[_capsules.Length];
            _capsuleMin = new Vector3[_capsules.Length]; _capsuleMax = new Vector3[_capsules.Length];
            _capsuleRadius = new float[_capsules.Length];
            _skinChain = new int[_skinBones.Length];
            _skinSegment = new int[_skinBones.Length];
            for (int b = 0; b < _skinBones.Length; b++)
            {
                _skinChain[b] = -1;
                for (int c = 0; c < count; c++)
                    for (int j = 0; j < _chains[c].Bones.Length; j++)
                        if (_skinBones[b] == _chains[c].Bones[j]) { _skinChain[b] = c; _skinSegment[b] = j; }
            }
            _contactPoint = new Vector3[count, _chains[0].Bones.Length];
            _contactPush = new Vector3[count, _chains[0].Bones.Length];
            _contactDepth = new float[count, _chains[0].Bones.Length];
            _restUpHips = _hips.InverseTransformDirection(_modelRoot.up);
            _reset = true;
            return true;
        }

        private void LateUpdate()
        {
            if (Application.isPlaying) Step(Time.deltaTime);
        }

        public void Step(float deltaTime)
        {
            if (!Initialize() || deltaTime <= 0f) return;
            float dt = Mathf.Min(deltaTime, 1f / 30f);
            float frames = dt * 60f;
            float pull = 1f - Mathf.Pow(1f - _stiffness, frames);
            float keep = Mathf.Pow(1f - _damping, frames);
            float scale = Mathf.Abs(_modelRoot.lossyScale.x);
            if (Vector3.Distance(_lastRoot, _hips.position) > _teleportDistance) _reset = true;
            _lastRoot = _hips.position;
            Vector3 up = _hips.TransformDirection(_restUpHips);
            bool crouching = _capsules.Length >= 4;
            for (int leg = 0; leg < 2 && crouching; leg++)
            {
                var thigh = _capsules[leg]; var shin = _capsules[leg + 2];
                crouching = thigh.Start != null && thigh.End != null && shin.End != null
                    && Vector3.Angle(thigh.End.position - thigh.Start.position, shin.End.position - thigh.End.position) > 85f;
            }
            for (int c = 0; c < _chains.Length; c++)
            {
                var bones = _chains[c].Bones;
                Vector3 pivot = _hips.TransformPoint(_rootHips[c]);
                Vector3 radial = Vector3.ProjectOnPlane(pivot - _hips.position, up).normalized;
                float lift = 0f;
                for (int leg = 0; leg < Mathf.Min(2, _capsules.Length); leg++)
                {
                    var thigh = _capsules[leg];
                    if (thigh.Start == null || thigh.End == null) continue;
                    Vector3 direction = (thigh.End.position - thigh.Start.position).normalized;
                    float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, up), -1f, 1f)) * Mathf.Rad2Deg;
                    float alignment = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.2f, .7f, Vector3.Dot(radial, Vector3.ProjectOnPlane(direction, up).normalized)));
                    float legLift = Mathf.InverseLerp(-20f, 15f, elevation) * 80f;
                    if (crouching) legLift = Mathf.Max(legLift, 50f);
                    lift = Mathf.Max(lift, legLift * alignment * alignment);
                    if (_capsules.Length <= leg + 2 || _capsules[leg + 2].End == null) continue;
                    Vector3 foot = _capsules[leg + 2].End.position;
                    Vector3 hem = _hips.TransformPoint(_restTipHips[c][bones.Length - 1]);
                    if (Vector3.Dot(foot - hem, up) <= .05f * scale) continue;
                    Vector3 toFoot = foot - pivot;
                    Vector3 footRadial = Vector3.ProjectOnPlane(foot - _hips.position, up);
                    float footAlignment = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.2f, .7f, Vector3.Dot(radial, footRadial.normalized)));
                    Vector3 restHem = hem - pivot;
                    float restAngle = Mathf.Atan2(Vector3.ProjectOnPlane(restHem, up).magnitude, -Vector3.Dot(restHem, up)) * Mathf.Rad2Deg;
                    float reach = Mathf.Max(0f, footRadial.magnitude - Vector3.ProjectOnPlane(pivot - _hips.position, up).magnitude) + .07f * scale;
                    float drop = Mathf.Max(.04f * scale, -Vector3.Dot(toFoot, up) - .04f * scale);
                    float footLift = Mathf.Clamp(Mathf.Atan2(reach, drop) * Mathf.Rad2Deg - restAngle + 4f, 0f, 85f);
                    lift = Mathf.Max(lift, footLift * footAlignment * footAlignment);
                }
                Quaternion poseLift = Quaternion.AngleAxis(-lift, Vector3.Cross(up, radial));
                Quaternion previousLift = Quaternion.AngleAxis(-_lastLift[c], Vector3.Cross(up, radial));
                for (int j = 0; j < bones.Length; j++)
                {
                    bones[j].localRotation = _restRotation[c][j];
                    Vector3 target = pivot + poseLift * (_hips.TransformPoint(_restTipHips[c][j]) - pivot);
                    _target[c][j] = target;
                    _poseRotation[c][j] = poseLift * _hips.rotation * _restHipsRotation[c][j];
                    if (_reset) _position[c][j] = _previous[c][j] = target;
                    else
                    {
                        Vector3 restOffset = _hips.TransformPoint(_restTipHips[c][j]) - pivot;
                        Vector3 contactMotion = poseLift * restOffset - previousLift * restOffset;
                        _position[c][j] += contactMotion;
                        _previous[c][j] += contactMotion;
                    }
                    Vector3 next = _position[c][j] + (_position[c][j] - _previous[c][j]) * keep;
                    next = Vector3.Lerp(next, target, pull);
                    float fraction = (j + 1f) / bones.Length;
                    _next[c][j] = target + Vector3.ClampMagnitude(next - target, _maxHemTravel * fraction * scale);
                }
                _lastLift[c] = lift;
            }
            for (int c = 0; c < _chains.Length; c++)
            {
                var bones = _chains[c].Bones;
                int left = (c + _chains.Length - 1) % _chains.Length, right = (c + 1) % _chains.Length;
                for (int j = 0; j < bones.Length; j++)
                {
                    var bone = bones[j];
                    Vector3 target = _target[c][j];
                    Vector3 neighbours = (_next[left][j] - _target[left][j]
                        + _next[right][j] - _target[right][j]) * .5f;
                    Vector3 next = Vector3.Lerp(_next[c][j], target + neighbours, _neighbourBlend);
                    Vector3 head = bone.position;
                    Vector3 rest = bone.TransformPoint(_tipLocal[c][j]);
                    float length = Vector3.Distance(head, rest);
                    // Pose contact may exceed the motion envelope: a high kick must lift the fabric.
                    for (int pass = 0; pass < 5; pass++)
                    {
                        next = head + (next - head).normalized * length;
                        foreach (var capsule in _capsules)
                        {
                            if (capsule.Start == null || capsule.End == null) continue;
                            Vector3 a = capsule.Start.position, b = capsule.End.position;
                            Vector3 axis = b - a;
                            float radius = (capsule.Radius + _fabricClearance + (crouching ? .035f : 0f)) * scale;
                            for (int sample = 1; sample <= 4; sample++)
                            {
                                float t = sample * .25f;
                                Vector3 point = Vector3.Lerp(head, next, t);
                                Vector3 closest = a + axis * Mathf.Clamp01(Vector3.Dot(point - a, axis) / Mathf.Max(axis.sqrMagnitude, 1e-8f));
                                Vector3 away = point - closest;
                                float distance = away.magnitude;
                                if (distance >= radius) continue;
                                Vector3 outward = Vector3.ProjectOnPlane(target - _hips.position, up).normalized;
                                Vector3 normal = distance > 1e-5f ? away / distance : outward;
                                if (Vector3.Dot(normal, outward) < .1f) normal = (normal + outward * 1.5f).normalized;
                                next += normal * ((radius - distance) / t);
                            }
                        }
                    }
                    next = head + (next - head).normalized * length;
                    _previous[c][j] = _reset ? next : _position[c][j];
                    _position[c][j] = next;
                    if ((rest - head).sqrMagnitude > 1e-10f && (next - head).sqrMagnitude > 1e-10f)
                        bone.rotation = Quaternion.RotateTowards(_poseRotation[c][j], Quaternion.FromToRotation(rest - head, next - head) * bone.rotation, 20f);
                }
            }
            // The spring continues from the tips the rotation limit allowed. The skin-contact correction poses
            // this frame only: fed back, the spring pulled the fabric into the leg and the next correction
            // overshot, a two-frame flicker even in idle.
            for (int c = 0; c < _chains.Length; c++)
                for (int j = 0; j < _chains[c].Bones.Length; j++)
                {
                    Vector3 tip = _chains[c].Bones[j].TransformPoint(_tipLocal[c][j]);
                    _previous[c][j] += tip - _position[c][j];
                    _position[c][j] = tip;
                }
            ResolveSkinContacts(scale, up);
            _reset = false;
        }

        private void ResolveSkinContacts(float scale, Vector3 up)
        {
            // Tip-only collision misses the fabric between radial chains. Probe the weighted surface too.
            const int stride = 1;
            for (int i = 0; i < _capsules.Length; i++)
            {
                var capsule = _capsules[i];
                _capsuleRadius[i] = 0f;
                if (capsule.Start == null || capsule.End == null) continue;
                Vector3 a = capsule.Start.position, b = capsule.End.position;
                float radius = (capsule.Radius + _fabricClearance * .5f) * scale;
                _capsuleStart[i] = a; _capsuleAxis[i] = b - a; _capsuleRadius[i] = radius;
                _capsuleMin[i] = Vector3.Min(a, b) - Vector3.one * radius;
                _capsuleMax[i] = Vector3.Max(a, b) + Vector3.one * radius;
            }
            for (int pass = 0; pass < 6; pass++)
            {
                Array.Clear(_contactDepth, 0, _contactDepth.Length);
                Array.Clear(_contactPoint, 0, _contactPoint.Length);
                Array.Clear(_contactPush, 0, _contactPush.Length);
                for (int b = 0; b < _skinMatrices.Length; b++)
                    _skinMatrices[b] = _skinBones[b].localToWorldMatrix * _bindposes[b];
                bool contact = false;
                for (int i = 0; i < _vertices.Length; i += stride)
                {
                    BoneWeight w = _weights[i]; Vector3 v = _vertices[i];
                    Vector3 point = _skinMatrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                        + _skinMatrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                        + _skinMatrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                        + _skinMatrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                    int owner = -1; float weight = 0f;
                    void Consider(int bone, float value) { if (_skinChain[bone] >= 0 && value > weight) { owner = bone; weight = value; } }
                    Consider(w.boneIndex0, w.weight0); Consider(w.boneIndex1, w.weight1);
                    Consider(w.boneIndex2, w.weight2); Consider(w.boneIndex3, w.weight3);
                    if (owner < 0 || weight < .05f) continue;
                    int c = _skinChain[owner], j = _skinSegment[owner];
                    for (int cap = 0; cap < _capsules.Length; cap++)
                    {
                        float radius = _capsuleRadius[cap];
                        Vector3 min = _capsuleMin[cap], max = _capsuleMax[cap];
                        if (radius <= 0f || point.x < min.x || point.y < min.y || point.z < min.z
                            || point.x > max.x || point.y > max.y || point.z > max.z) continue;
                        Vector3 a = _capsuleStart[cap], axis = _capsuleAxis[cap];
                        Vector3 closest = a + axis * Mathf.Clamp01(Vector3.Dot(point - a, axis) / Mathf.Max(axis.sqrMagnitude, 1e-8f));
                        Vector3 away = point - closest;
                        float distance = away.magnitude, depth = radius - distance;
                        if (depth <= .001f * scale) continue;
                        Vector3 outward = Vector3.ProjectOnPlane(_hips.TransformPoint(_rootHips[c]) - _hips.position, up).normalized;
                        Vector3 normal = distance > 1e-5f ? away / distance : outward;
                        if (Vector3.Dot(normal, outward) < 0f) normal = (normal + outward * 2f).normalized;
                        // Depth-weighted sums, not the deepest vertex: which vertex was deepest flipped between
                        // frames while the legs strode through the fabric.
                        _contactPoint[c,j] += point * depth;
                        _contactPush[c,j] += normal * (depth * (depth + .006f * scale));
                        _contactDepth[c,j] += depth;
                        contact = true;
                    }
                }
                if (!contact) break;
                for (int c = 0; c < _chains.Length; c++)
                    for (int j = 0; j < _chains[c].Bones.Length; j++)
                    {
                        if (_contactDepth[c,j] <= 0f) continue;
                        var bone = _chains[c].Bones[j];
                        Vector3 before = _contactPoint[c,j] / _contactDepth[c,j] - bone.position;
                        Vector3 after = before + _contactPush[c,j] / _contactDepth[c,j];
                        if (before.sqrMagnitude < 1e-8f || after.sqrMagnitude < 1e-8f) continue;
                        Quaternion correction = Quaternion.FromToRotation(before, after);
                        bone.rotation = Quaternion.RotateTowards(_poseRotation[c][j], Quaternion.RotateTowards(Quaternion.identity, correction, 12f) * bone.rotation, 20f);
                    }
            }
        }
    }
}
