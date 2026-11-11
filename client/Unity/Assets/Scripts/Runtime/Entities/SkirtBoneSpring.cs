using System;
using System.Collections.Generic;
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

        /// <summary>How raised limbs swing the chains outward before the spring runs.</summary>
        public enum LiftModel
        {
            /// <summary>A long skirt from the waist: capsules 0-1 are thighs and 2-3 shins; raised thighs, a crouch and
            /// raised feet lift the chains facing them.</summary>
            LegPose,
            /// <summary>Any capsules (legs, arms, tail): each chain swings just far enough to clear the limb parts under
            /// it (below its top third, above its hem, inside its rest line, crossing it rather than hanging along it),
            /// straight out or 40 degrees to either side, whichever needs the smallest swing. The swing rises within a
            /// few frames and settles back slowly. A limb no swing within the lift limit clears is left to the collision passes,
            /// which push fabric out from a limb behind it and fade out for a limb in front. For fabric that hangs
            /// above the legs, such as a poncho.</summary>
            Clearance,
        }

        [Tooltip("The bone the chains hang from: their parent, whose frame carries their rest pose.")]
        [SerializeField] private Transform _hips;
        [SerializeField] private Transform _modelRoot;
        [SerializeField] private Chain[] _chains = Array.Empty<Chain>();
        [SerializeField] private LegCapsule[] _capsules = Array.Empty<LegCapsule>();
        [SerializeField] private LiftModel _liftModel = LiftModel.LegPose;
        [Tooltip("Clearance only: the largest outward swing, in degrees.")]
        [SerializeField, Range(0f, 85f)] private float _maxLift = 70f;
        [Tooltip("Clearance only: extra radius around each capsule, in rig metres, for the fabric between neighbouring chains.")]
        [SerializeField] private float _liftReach = .03f;
        [SerializeField, Range(0f, 1f)] private float _stiffness = .22f;
        [SerializeField, Range(0f, 1f)] private float _damping = .3f;
        [SerializeField, Range(0f, 1f)] private float _neighbourBlend = .35f;
        [SerializeField] private float _maxHemTravel = .2f;
        [SerializeField] private float _fabricClearance = .04f;
        [SerializeField] private float _teleportDistance = 1.5f;
        [Tooltip("LegPose only: the thigh elevation, in degrees above level, where chains facing it start to lift.")]
        [SerializeField] private float _legLiftFrom = -20f;
        [Tooltip("LegPose only: the thigh elevation where that lift is full. A long skirt (the defaults) lets a thigh move inside it before the hem lifts; a short coat lies on the thigh and starts near -75.")]
        [SerializeField] private float _legLiftFull = 15f;
        [Tooltip("LegPose only: the full lift, in degrees. Past 90 the fabric folds up against the body, as a coat front does over a knee drawn to the chest.")]
        [SerializeField, Range(0f, 150f)] private float _legLift = 80f;
        [Tooltip("LegPose only: 0 lets a raised thigh lift every chain facing it, as one closed skirt does. Above 0, in rig metres, it lifts only chains whose pivot is within this distance to either side of its hip joint, fading to nothing at twice that: each panel of an open coat lies on its own thigh.")]
        [SerializeField] private float _legLiftWidth;
        [Tooltip("How far fabric swings back toward straight down when the body leans: 0 keeps the rest shape square to the hips, 1 hangs plumb. Only fabric the lean swings away from the body hangs; the rest stays on the body.")]
        [SerializeField, Range(0f, 1f)] private float _hang;
        [Tooltip("The largest hang swing, in degrees, so a tumbling body does not wrap its coat around itself.")]
        [SerializeField, Range(0f, 90f)] private float _maxHang = 40f;

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
        private bool _hanging;
        private Quaternion _hangSwing = Quaternion.identity;
        private Quaternion[] _lastLiftRotation;
        private Quaternion[][] _skinFrom, _skinCorrection;
        private static readonly float[] SwingTurns = { 0f, -40f, 40f };
        private const int LimbSamples = 4;
        private readonly List<Vector4> _under = new List<Vector4>();
        private Vector3[] _restLine = Array.Empty<Vector3>();
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

        // Clearance and hanging fabric read their rest frame before any clip poses the body: a lean in the first
        // pose would become the fabric's idea of upright. LegPose without hang keeps reading it at the first step.
        private void Awake()
        {
            if (_liftModel == LiftModel.Clearance || _hang > 0f) Initialize();
        }

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
            _lastLiftRotation = new Quaternion[count];
            _skinFrom = new Quaternion[count][];
            _skinCorrection = new Quaternion[count][];
            for (int c = 0; c < count; c++)
            {
                _lastLiftRotation[c] = Quaternion.identity;
                _skinFrom[c] = new Quaternion[_chains[c].Bones.Length];
                _skinCorrection[c] = new Quaternion[_chains[c].Bones.Length];
                for (int j = 0; j < _skinCorrection[c].Length; j++) _skinCorrection[c][j] = Quaternion.identity;
            }
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
            bool crouching = _liftModel == LiftModel.LegPose && _capsules.Length >= 4;
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
                Vector3 liftAxis = Vector3.Cross(up, radial);
                float lift = 0f;
                // Clearance measures its lift from the hanging line, so the hang comes first and both add up.
                _hanging = _hang > 0f && _liftModel == LiftModel.Clearance && HangSwing(up, radial, 0f, out _hangSwing);
                if (_liftModel == LiftModel.Clearance) lift = ClearanceLift(c, pivot, up, radial, scale, out liftAxis);
                for (int leg = 0; _liftModel == LiftModel.LegPose && leg < Mathf.Min(2, _capsules.Length); leg++)
                {
                    var thigh = _capsules[leg];
                    if (thigh.Start == null || thigh.End == null) continue;
                    Vector3 direction = (thigh.End.position - thigh.Start.position).normalized;
                    float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, up), -1f, 1f)) * Mathf.Rad2Deg;
                    float alignment = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.2f, .7f, Vector3.Dot(radial, Vector3.ProjectOnPlane(direction, up).normalized)));
                    if (_legLiftWidth > 0f)
                    {
                        Vector3 across = Vector3.Cross(up, Vector3.ProjectOnPlane(direction, up)).normalized;
                        float apart = Mathf.Abs(Vector3.Dot(pivot - thigh.Start.position, across)) / scale;
                        alignment *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_legLiftWidth, 2f * _legLiftWidth, apart));
                    }
                    float legLift = Mathf.InverseLerp(_legLiftFrom, _legLiftFull, elevation) * _legLift;
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
                // LegPose lifts from the rest line: a chain a raised thigh already carries forward hangs only the rest
                // of the way, or a sprint's knee drive and its lean would add up and throw the front panel over his chest.
                if (_hang > 0f && _liftModel == LiftModel.LegPose) _hanging = HangSwing(up, radial, lift, out _hangSwing);
                Quaternion poseLift = Quaternion.AngleAxis(-lift, liftAxis);
                Quaternion previousLift = Quaternion.AngleAxis(-_lastLift[c], Vector3.Cross(up, radial));
                if (_liftModel == LiftModel.Clearance)
                {
                    // The swing rises with the limb almost at once and settles back slowly; snapping between swing
                    // directions or dropping when a limb left popped the fabric.
                    previousLift = _reset ? poseLift : _hips.rotation * _lastLiftRotation[c] * Quaternion.Inverse(_hips.rotation);
                    float applied = Quaternion.Angle(Quaternion.identity, previousLift);
                    poseLift = Quaternion.RotateTowards(previousLift, poseLift, (lift > applied ? 600f : 240f) * dt);
                    _lastLiftRotation[c] = Quaternion.Inverse(_hips.rotation) * poseLift * _hips.rotation;
                }
                for (int j = 0; j < bones.Length; j++)
                {
                    bones[j].localRotation = _restRotation[c][j];
                    Vector3 target = pivot + poseLift * (_hips.TransformPoint(_restTipHips[c][j]) - pivot);
                    _poseRotation[c][j] = poseLift * _hips.rotation * _restHipsRotation[c][j];
                    if (_hanging)
                    {
                        target = pivot + poseLift * (_hangSwing * (_hips.TransformPoint(_restTipHips[c][j]) - pivot));
                        _poseRotation[c][j] = poseLift * _hangSwing * _hips.rotation * _restHipsRotation[c][j];
                    }
                    _target[c][j] = target;
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
                                float push = radius - distance;
                                if (_liftModel == LiftModel.Clearance) push *= LimbBehind(away, distance, outward);
                                if (push <= 0f) continue;
                                Vector3 normal = distance > 1e-5f ? away / distance : outward;
                                if (Vector3.Dot(normal, outward) < .1f) normal = (normal + outward * 1.5f).normalized;
                                next += normal * (push / t);
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
            if (_liftModel == LiftModel.Clearance)
                for (int c = 0; c < _chains.Length; c++)
                    for (int j = 0; j < _chains[c].Bones.Length; j++)
                        _skinFrom[c][j] = _chains[c].Bones[j].rotation;
            ResolveSkinContacts(scale, up);
            if (_liftModel == LiftModel.Clearance) SettleSkinContacts(dt);
            _reset = false;
        }

        /// <summary>The swing that lets a chain hang when the body leans: its rest line turns from the hips' own
        /// down toward straight down, by <see cref="_hang"/> of the lean and at most <see cref="_maxHang"/>. A real
        /// coat's front falls away from a leaning chest while its back lies on the body, so only a chain the swing
        /// carries outward gets it; one it would press into the body keeps its rest line. <paramref name="taken"/> is
        /// the part of the swing, in degrees, a lift already made. False when there is no swing left.</summary>
        private bool HangSwing(Vector3 up, Vector3 radial, float taken, out Quaternion swing)
        {
            swing = Quaternion.identity;
            float lean = Vector3.Angle(up, Vector3.up);
            if (lean < .5f) return false;
            Vector3 axis = Vector3.Cross(up, Vector3.up).normalized;
            float angle = Mathf.Min(lean * _hang, _maxHang);
            Vector3 carried = Quaternion.AngleAxis(angle, axis) * -up + up;
            float outward = Vector3.Dot(carried.normalized, radial);
            float share = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.2f, .5f, outward));
            float left = angle * share - taken;
            if (left <= 0f) return false;
            swing = Quaternion.AngleAxis(left, axis);
            return true;
        }

        /// <summary>The smallest swing, in degrees, that lifts chain <paramref name="c"/>'s rest line clear of every
        /// limb under it, and the axis it turns about; no swing when nothing within the limit clears them.</summary>
        private float ClearanceLift(int c, Vector3 pivot, Vector3 up, Vector3 radial, float scale, out Vector3 axis)
        {
            axis = Vector3.Cross(up, radial);
            GatherLimbsUnder(c, pivot, up, radial, scale);
            if (_under.Count == 0 || Penetration(pivot, axis, 0f) <= 0f) return 0f;
            // Swinging only straight out cleared an arm hanging just behind a corner chain by flaring it wide;
            // a small swing forward clears it.
            float best = float.MaxValue;
            foreach (float turn in SwingTurns)
            {
                Vector3 candidate = Vector3.Cross(up, Quaternion.AngleAxis(turn, up) * radial);
                float lift = SmallestClearSwing(pivot, candidate);
                if (lift >= 0f && lift < best) { best = lift; axis = candidate; }
            }
            // Snapping to the limit for a limb no swing clears (one hanging along the chain) spread wings in idle.
            return best == float.MaxValue ? 0f : best;
        }

        /// <summary>Samples of every capsule under chain <paramref name="c"/>: below the top third of its rest line,
        /// above its hem and inside it. Fabric lifts over a limb beneath it; swinging out over a forearm already in
        /// front of it flipped the front panel up like a table.</summary>
        private void GatherLimbsUnder(int c, Vector3 pivot, Vector3 up, Vector3 radial, float scale)
        {
            _under.Clear();
            int n = _chains[c].Bones.Length;
            if (_restLine.Length != n) _restLine = new Vector3[n];
            for (int j = 0; j < n; j++) _restLine[j] = _hips.TransformPoint(_restTipHips[c][j]);
            if (_hanging) for (int j = 0; j < n; j++) _restLine[j] = pivot + _hangSwing * (_restLine[j] - pivot);
            float hem = Vector3.Dot(pivot - _restLine[n - 1], up);
            Vector3 hang = (_restLine[n - 1] - pivot).normalized;
            foreach (var capsule in _capsules)
            {
                if (capsule.Start == null || capsule.End == null) continue;
                // An arm hanging beside a chain is nudged by the collision passes; swinging clear of a limb that
                // parallel took a wide flare and spread wings in idle. Raised arms and thighs cross the chain.
                if (Mathf.Abs(Vector3.Dot((capsule.End.position - capsule.Start.position).normalized, hang)) > .7f) continue;
                float radius = (capsule.Radius + _liftReach) * scale;
                for (int sample = 0; sample <= LimbSamples; sample++)
                {
                    Vector3 point = Vector3.Lerp(capsule.Start.position, capsule.End.position, sample / (float)LimbSamples);
                    // The top third of the fabric rides the body: swinging it off an upper arm beside the pivot
                    // took near the full limit and spread wings. The collision passes handle limbs there.
                    float depth = Vector3.Dot(pivot - point, up);
                    if (depth < hem / 3f || depth > hem) continue;
                    Vector3 closest = pivot, head = pivot;
                    foreach (Vector3 tip in _restLine)
                    {
                        Vector3 candidate = ClosestOnSegment(point, head, tip);
                        if ((candidate - point).sqrMagnitude < (closest - point).sqrMagnitude) closest = candidate;
                        head = tip;
                    }
                    if (Vector3.Dot(point - closest, radial) > 0f) continue;
                    _under.Add(new Vector4(point.x, point.y, point.z, radius));
                }
            }
        }

        /// <summary>The smallest swing about <paramref name="axis"/> that clears every limb under the chain, or -1.</summary>
        private float SmallestClearSwing(Vector3 pivot, Vector3 axis)
        {
            const int Steps = 7;
            float below = 0f;
            for (int step = 1; step <= Steps; step++)
            {
                float lift = _maxLift * step / Steps;
                if (Penetration(pivot, axis, lift) <= 0f)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float mid = (below + lift) * .5f;
                        if (Penetration(pivot, axis, mid) <= 0f) lift = mid;
                        else below = mid;
                    }
                    return lift;
                }
                below = lift;
            }
            return -1f;
        }

        /// <summary>How deep the gathered rest line, swung by <paramref name="lift"/> degrees about
        /// <paramref name="axis"/>, reaches into the deepest limb under it (negative when clear).</summary>
        private float Penetration(Vector3 pivot, Vector3 axis, float lift)
        {
            Quaternion swing = Quaternion.AngleAxis(-lift, axis);
            float worst = float.NegativeInfinity;
            foreach (var limb in _under)
            {
                Vector3 point = limb;
                // Fabric beside the pivot rides the body; no swing moves it off a limb there.
                Vector3 head = pivot + swing * (_restLine[0] - pivot) * .5f;
                foreach (Vector3 rest in _restLine)
                {
                    Vector3 tip = pivot + swing * (rest - pivot);
                    worst = Mathf.Max(worst, limb.w - Vector3.Distance(point, ClosestOnSegment(point, head, tip)));
                    head = tip;
                }
            }
            return worst;
        }

        /// <summary>How fully a limb at <paramref name="away"/> from the fabric pushes it out: fully from behind, not
        /// at all from in front. The fade keeps a limb sliding past the fabric from popping it.</summary>
        private static float LimbBehind(Vector3 away, float distance, Vector3 outward)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.2f, .3f, Vector3.Dot(away, outward) / Mathf.Max(distance, 1e-5f)));

        private static Vector3 ClosestOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 axis = b - a;
            return a + axis * Mathf.Clamp01(Vector3.Dot(point - a, axis) / Mathf.Max(axis.sqrMagnitude, 1e-10f));
        }

        /// <summary>The skin-contact correction poses one frame without feeding the spring, so an arm leaving fabric it
        /// held at the 20-degree limit snapped the fabric back in one frame. Clearance keeps each bone's correction and
        /// moves it toward this frame's at most 10 degrees a frame rising and 4 settling.</summary>
        private void SettleSkinContacts(float dt)
        {
            Quaternion frame = _hips.rotation, toFrame = Quaternion.Inverse(frame);
            for (int c = 0; c < _chains.Length; c++)
                for (int j = 0; j < _chains[c].Bones.Length; j++)
                {
                    var bone = _chains[c].Bones[j];
                    Quaternion wanted = bone.rotation * Quaternion.Inverse(_skinFrom[c][j]);
                    Quaternion held = _reset ? wanted : frame * _skinCorrection[c][j] * toFrame;
                    float rate = Quaternion.Angle(Quaternion.identity, wanted) > Quaternion.Angle(Quaternion.identity, held) ? 600f : 240f;
                    Quaternion applied = Quaternion.RotateTowards(held, wanted, rate * dt);
                    bone.rotation = applied * _skinFrom[c][j];
                    _skinCorrection[c][j] = toFrame * applied * frame;
                }
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
                        if (_liftModel == LiftModel.Clearance) depth *= LimbBehind(away, distance, outward);
                        if (depth <= 0f) continue;
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
