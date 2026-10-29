using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Shows part of a cue only while its owner's attack shows one of <see cref="AnimationIds"/>, or with
    /// <see cref="WhileRiding"/> while she rides her wave (<see cref="ActionState.Shell"/>, ADR-0050), and, with
    /// <see cref="GroundedOnly"/>, stands on the ground: Bashie's surf wave rides under her on Wave Pool and goes when
    /// the ride ends or she coasts over thin air. <see cref="Grow"/> scales in to <see cref="Scale"/> and out over
    /// <see cref="EaseSeconds"/>; its particle systems stop emitting while hidden (their particles live out) and its
    /// looping audio fades. Cosmetic only.
    /// </summary>
    public sealed class AttackClipShow : MonoBehaviour, IPresentationOwnerReceiver
    {
        public string[] AnimationIds = System.Array.Empty<string>();
        public bool GroundedOnly = true;
        [Tooltip("Show while the owner rides her wave instead of by attack clip.")]
        public bool WhileRiding;
        [Tooltip("Scale of Grow while shown.")]
        public float Scale = 1f;
        [Tooltip("Scaled from 0 to 1 while shown: the mesh and anything that should pop in with it.")]
        public Transform? Grow;
        public float EaseSeconds = 0.08f;

        private PlayerRenderer? _owner;
        private byte _sequence;
        private ParticleSystem[] _systems = System.Array.Empty<ParticleSystem>();
        private AudioSource[] _loops = System.Array.Empty<AudioSource>();
        private float[] _volumes = System.Array.Empty<float>();
        private float _shown;
        private bool _emitting;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            _sequence = owner != null ? owner.CurrentAttackSequence : (byte)0;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _loops = GetComponentsInChildren<AudioSource>(true);
            _volumes = new float[_loops.Length];
            for (int i = 0; i < _loops.Length; i++)
            {
                _volumes[i] = _loops[i].volume;
                _loops[i].volume = 0f;
            }
            foreach (var system in _systems) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _emitting = false;
            Apply();
        }

        private void LateUpdate()
        {
            bool show = _owner != null && _owner.isActiveAndEnabled
                && (WhileRiding ? _owner.CurrentActionState == ActionState.Shell
                    : ShellTuck.ShowsClip(_owner, _sequence, AnimationIds))
                && (!GroundedOnly || _owner.CurrentIsGrounded);
            float step = EaseSeconds > 0f ? Time.deltaTime / EaseSeconds : 1f;
            _shown = Mathf.MoveTowards(_shown, show ? 1f : 0f, step);
            if (show != _emitting)
            {
                _emitting = show;
                foreach (var system in _systems)
                {
                    if (show) system.Play(true);
                    else system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            Apply();
        }

        private void Apply()
        {
            float eased = 1f - (1f - _shown) * (1f - _shown);
            if (Grow != null) Grow.localScale = Vector3.one * Mathf.Max(0.001f, eased * Scale);
            for (int i = 0; i < _loops.Length; i++)
            {
                _loops[i].volume = _volumes[i] * _shown;
                if (_shown > 0f && !_loops[i].isPlaying) _loops[i].Play();
                else if (_shown <= 0f && _loops[i].isPlaying) _loops[i].Stop();
            }
        }
    }
}
