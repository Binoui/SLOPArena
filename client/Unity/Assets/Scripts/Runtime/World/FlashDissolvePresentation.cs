#nullable enable
using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    [DefaultExecutionOrder(1100)]
    public sealed class FlashDissolvePresentation : MonoBehaviour, IPresentationOwnerReceiver
    {
        public ParticleSystem? Pixels;
        public ParticleSystem? Glyphs;

        private enum Look { Stance, Leg, Visible }
        private PlayerRenderer? _owner;
        private CookedFlashCutCapabilityParameters? _flash;
        private readonly List<(Renderer renderer, bool hidden)> _renderers = new();
        private MaterialPropertyBlock? _block;
        private byte _sequence;
        private bool _hadLeg;
        private Look _look;
        private int _lastTick = -1;
        private byte _lastClip = byte.MaxValue;
        private Vector3 _lastPosition;
        private float _cutHeight = -10000f;
        private static readonly int CutHeight = Shader.PropertyToID("_CodeCutHeight");

        public void BindOwner(PlayerRenderer owner)
        {
            _block ??= new MaterialPropertyBlock();
            _owner = owner;
            _sequence = owner.CurrentAttackSequence;
            if (owner.CurrentMove?.Timeline.Stages is { Count: > 0 } stages)
                foreach (var operation in stages[0].Operations)
                    if (operation is CookedStartCapabilityOperation { Parameters: CookedFlashCutCapabilityParameters flash })
                        _flash = flash;
            foreach (var renderer in owner.GetComponentsInChildren<Renderer>(true))
                if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer
                    && renderer.sharedMaterial != null && renderer.sharedMaterial.renderQueue < 2500)
                    _renderers.Add((renderer, renderer.forceRenderingOff));
            _lastPosition = owner.transform.position;
        }

        private void LateUpdate()
        {
            if (_owner == null || _flash == null || _block == null) return;
            if (!PresentationLifetime.IsCurrentAttack(_owner.CurrentActionState, _owner.CurrentAttackSequence, _sequence))
            {
                Restore();
                if (Pixels != null) Pixels.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                if (Glyphs != null) Glyphs.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                enabled = false;
                return;
            }
            int tick = _owner.CurrentAttackElapsedTicks;
            byte clip = _owner.CurrentClipIndex;
            bool leg = _owner.CurrentMove!.Clip(clip).AnimationId == _flash.LegAnimationId;
            if (leg) _hadLeg = true;
            Look previous = _look;
            _look = leg && tick < _flash.LegTicks - 1 ? Look.Leg
                : !_hadLeg && tick < _flash.StanceTicks ? Look.Stance : Look.Visible;
            float height = _owner.CapsuleHeight;
            float feet = _owner.transform.position.y - _owner.ModelYOffset - height * 0.5f;
            _cutHeight = _look == Look.Stance ? feet + Mathf.Pow(Mathf.Clamp01(tick / (float)_flash.StanceTicks), 2.2f) * (height + 0.25f) : -10000f;
            foreach (var (renderer, hidden) in _renderers)
            {
                if (renderer == null) continue;
                renderer.forceRenderingOff = hidden || _look == Look.Leg
                    || _look == Look.Stance && renderer is MeshRenderer && renderer.bounds.center.y < _cutHeight;
                renderer.GetPropertyBlock(_block);
                _block.SetFloat(CutHeight, _cutHeight);
                renderer.SetPropertyBlock(_block);
            }
            if (tick == _lastTick && clip == _lastClip) return;
            bool reappear = _look == Look.Visible && previous == Look.Leg;
            Vector3 center = _owner.transform.position + Vector3.up * -_owner.ModelYOffset;
            int count = _look == Look.Stance ? 15 : _look == Look.Leg ? 45 : reappear ? 70 : 0;
            for (int i = 0; i < count; i++)
            {
                float u = Hash(tick * 397 + i * 31);
                float v = Hash(tick * 113 + i * 73);
                float w = Hash(tick * 251 + i * 17);
                Vector3 position;
                if (_look == Look.Stance)
                    position = new Vector3(center.x + (u - 0.5f) * 0.7f, _cutHeight + (v - 0.5f) * 0.24f, center.z + (w - 0.5f) * 0.5f);
                else
                    position = Vector3.Lerp(_lastPosition, center, u) + new Vector3((v - 0.5f) * 0.45f, (w - 0.5f) * height * (_look == Look.Leg ? 0.28f : 1f), (v - 0.5f) * 0.45f);
                Emit(Pixels, position);
                if (_look == Look.Stance && i % 4 == 0) Emit(Glyphs, position + Vector3.up * 0.1f);
            }
            _lastPosition = center;
            _lastTick = tick;
            _lastClip = clip;
        }

        private static float Hash(int value) => Mathf.Repeat(Mathf.Sin(value * 12.9898f) * 43758.5453f, 1f);

        private static void Emit(ParticleSystem? system, Vector3 position)
        {
            if (system != null) system.Emit(new ParticleSystem.EmitParams { position = position }, 1);
        }

        private void Restore()
        {
            if (_block == null) return;
            foreach (var (renderer, hidden) in _renderers)
            {
                if (renderer == null) continue;
                renderer.forceRenderingOff = hidden;
                renderer.GetPropertyBlock(_block);
                _block.SetFloat(CutHeight, -10000f);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
