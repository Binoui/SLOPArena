#nullable enable
using System;
using System.Collections.Generic;
using Animancer;
using SlopArena.Shared;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// One GLB clip as the Shared sampler reads it at every tick: local transforms of the fighter's written nodes, in the
    /// same order for every clip of the fighter so two GLB states blend node by node. Shared by every renderer instance.
    /// </summary>
    public sealed class GlbMotionClip
    {
        public int FrameCount { get; }
        public float DurationSeconds { get; }
        public bool Loop { get; }
        /// <summary>frame * nodes * 10 + node * 10: position xyz, rotation xyzw, scale xyz.</summary>
        internal readonly float[] Frames;

        internal GlbMotionClip(GlbMotionRig rig, GlbClip clip, IReadOnlyList<int> written)
        {
            FrameCount = clip.FrameCount;
            DurationSeconds = clip.DurationSeconds;
            Loop = clip.Loop;
            Frames = new float[clip.FrameCount * written.Count * 10];
            var poses = new GlbNodePose[rig.NodeCount];
            for (int tick = 0, at = 0; tick < clip.FrameCount; tick++)
            {
                rig.SampleLocal(clip, tick, poses);
                foreach (int node in written)
                {
                    var p = poses[node];
                    Frames[at++] = p.TX; Frames[at++] = p.TY; Frames[at++] = p.TZ;
                    Frames[at++] = p.QX; Frames[at++] = p.QY; Frames[at++] = p.QZ; Frames[at++] = p.QW;
                    Frames[at++] = p.SX; Frames[at++] = p.SY; Frames[at++] = p.SZ;
                }
            }
        }
    }

    /// <summary>Writes a <see cref="GlbMotionClip"/> into the animation stream at the state's time.</summary>
    public struct GlbMotionJob : IAnimancerStateJob
    {
        public NativeArray<TransformStreamHandle> Handles;
        /// <summary>Per handle: 1 position, 2 rotation, 4 scale; the paths any clip of the fighter animates.</summary>
        public NativeArray<byte> Paths;
        public NativeArray<float> Frames;
        /// <summary>The state's time for this evaluation; see <see cref="GlbMotionState.Update"/>.</summary>
        public NativeArray<double> Clock;
        public int FrameCount;
        public float Duration;
        public bool Loop;

        public readonly float Length => Duration;
        public readonly bool IsLooping => Loop;

        public readonly void ProcessRootMotion(AnimationStream stream, double time) { }

        // Ticks hold the sampled keys exactly; between ticks the display blends neighbours (looping clips wrap to frame 0).
        public readonly void ProcessAnimation(AnimationStream stream, double time)
        {
            double frame = Clock[0] * 60.0;
            int f0, f1;
            if (Loop)
            {
                frame %= FrameCount;
                if (frame < 0) frame += FrameCount;
                f0 = Math.Min((int)frame, FrameCount - 1);
                f1 = (f0 + 1) % FrameCount;
            }
            else
            {
                frame = Math.Max(0, Math.Min(frame, FrameCount - 1));
                f0 = (int)frame;
                f1 = Math.Min(f0 + 1, FrameCount - 1);
            }
            float u = (float)(frame - f0);
            int stride = Handles.Length * 10;
            for (int i = 0; i < Handles.Length; i++)
            {
                int a = f0 * stride + i * 10, b = f1 * stride + i * 10;
                byte paths = Paths[i];
                var handle = Handles[i];
                if ((paths & 1) != 0)
                    handle.SetLocalPosition(stream, Vector3.LerpUnclamped(V(a), V(b), u));
                if ((paths & 2) != 0)
                {
                    var q0 = Q(a + 3);
                    var q1 = Q(b + 3);
                    if (Quaternion.Dot(q0, q1) < 0) q1 = new Quaternion(-q1.x, -q1.y, -q1.z, -q1.w);
                    handle.SetLocalRotation(stream, Quaternion.LerpUnclamped(q0, q1, u).normalized);
                }
                if ((paths & 4) != 0)
                    handle.SetLocalScale(stream, Vector3.LerpUnclamped(V(a + 7), V(b + 7), u));
            }
        }

        private readonly Vector3 V(int at) => new(Frames[at], Frames[at + 1], Frames[at + 2]);
        private readonly Quaternion Q(int at) => new(Frames[at], Frames[at + 1], Frames[at + 2], Frames[at + 3]);

        internal readonly GlbMotionJob Copy() => new()
        {
            Handles = new NativeArray<TransformStreamHandle>(Handles, Allocator.Persistent),
            Paths = new NativeArray<byte>(Paths, Allocator.Persistent),
            Frames = new NativeArray<float>(Frames, Allocator.Persistent),
            Clock = new NativeArray<double>(Clock, Allocator.Persistent),
            FrameCount = FrameCount, Duration = Duration, Loop = Loop,
        };

        public void Dispose()
        {
            if (Handles.IsCreated) Handles.Dispose();
            if (Paths.IsCreated) Paths.Dispose();
            if (Frames.IsCreated) Frames.Dispose();
            if (Clock.IsCreated) Clock.Dispose();
        }
    }

    /// <summary>
    /// A GLB clip as a first-class Animancer state. Its key is a placeholder clip carrying the clip's length and loop
    /// flag, so the renderer's <c>Play(clip, fade)</c> calls fade between GLB clips, and between GLB and other states,
    /// in Animancer's own mixer.
    /// </summary>
    public sealed class GlbMotionState : AnimationJobState<GlbMotionJob>
    {
        private readonly AnimationClip _key;
        private bool _timeSet;

        public GlbMotionState(AnimationClip key, GlbMotionJob job) : base(job) { _key = key; }

        public override AnimationClip Clip { get => _key; set => base.Clip = value; }
        public override Object MainObject { get => _key; set => base.MainObject = value; }

        // Animancer runs pre-updates before playable time advances, so the base job time lags a clip state by one
        // frame. The clock holds the time this evaluation reaches, which is where a clip state samples. Unity does not
        // advance a playable in the first evaluation after its time is set (a seek, Stop, a new state) and advances it
        // by the frame's step otherwise, a resume included.
        public override void Update()
        {
            base.Update();
            var clock = Job.Clock;
            clock[0] = _timeSet ? RawTime : RawTime + AnimancerGraph.DeltaTime * EffectiveSpeed;
            _timeSet = false;
        }

        public override double RawTime
        {
            get => base.RawTime;
            set
            {
                base.RawTime = value;
                var clock = Job.Clock;
                clock[0] = value;
                _timeSet = true;
            }
        }

        public override AnimancerState Clone(CloneContext context)
        {
            var clone = new GlbMotionState(_key, Job.Copy());
            clone.CopyFrom(this, context);
            return clone;
        }
    }

    /// <summary>
    /// Sits on a portable fighter's model. Each instance the renderer creates binds its own transforms and registers one
    /// <see cref="GlbMotionState"/> per bound animation, keyed by that animation's placeholder clip.
    /// </summary>
    public sealed class GlbMotionBinder : MonoBehaviour
    {
        [SerializeField] private Transform[] _nodes = Array.Empty<Transform>();
        [SerializeField] private byte[] _paths = Array.Empty<byte>();
        [SerializeField] private AnimationClip[] _keys = Array.Empty<AnimationClip>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AnimationClip, GlbMotionClip> Motion = new();
        private AnimancerGraph? _bound;

        internal void Configure(Transform[] nodes, byte[] paths, IReadOnlyList<(AnimationClip Key, GlbMotionClip Motion)> clips)
        {
            _nodes = nodes;
            _paths = paths;
            _keys = new AnimationClip[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                _keys[i] = clips[i].Key;
                Motion.Remove(clips[i].Key);
                Motion.Add(clips[i].Key, clips[i].Motion);
            }
        }

        /// <summary>Register this model's GLB states on <paramref name="animancer"/>; safe to call again.</summary>
        public void Bind(AnimancerComponent animancer)
        {
            var graph = animancer.Graph;
            if (_bound == graph) return;
            var animator = animancer.Animator;
            var layer = animancer.Layers[0];
            foreach (var key in _keys)
            {
                if (!Motion.TryGetValue(key, out var motion) || graph.States.TryGet(key, out _)) continue;
                var handles = new NativeArray<TransformStreamHandle>(_nodes.Length, Allocator.Persistent);
                for (int i = 0; i < _nodes.Length; i++) handles[i] = animator.BindStreamTransform(_nodes[i]);
                var state = new GlbMotionState(key, new GlbMotionJob
                {
                    Handles = handles,
                    Paths = new NativeArray<byte>(_paths, Allocator.Persistent),
                    Frames = new NativeArray<float>(motion.Frames, Allocator.Persistent),
                    Clock = new NativeArray<double>(1, Allocator.Persistent),
                    FrameCount = motion.FrameCount, Duration = motion.DurationSeconds, Loop = motion.Loop,
                });
                state.SetParent(layer);
                state.Key = key;
            }
            _bound = graph;
        }

        /// <summary>The placeholder Animancer key for a bound clip: legacy, never played, with the clip's length and wrap.</summary>
        internal static AnimationClip Key(string name, float duration, bool loop)
        {
            var clip = new AnimationClip { name = name, legacy = true, frameRate = 60f, wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever };
            clip.SetCurve("", typeof(Transform), "localScale.x", AnimationCurve.Constant(0f, duration, 1f));
            return clip;
        }
    }
}
