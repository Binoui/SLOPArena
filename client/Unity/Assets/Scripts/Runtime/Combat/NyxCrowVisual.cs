using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Poses the package crow for the phase the resolver reports (ADR-0051 section 9). Rising, it flaps hard. Marking,
    /// it spreads its wings and caws, and its drop line and spot ring show where it will dive. Diving, it tucks its wings
    /// and the line shortens with it. Perched, it folds its wings and dips its head for each peck.
    /// </summary>
    public sealed class NyxCrowVisual : MonoBehaviour
    {
        public Transform LeftWing, RightWing;
        public LineRenderer DropLine;
        public Transform SpotMark;
        public ParticleSystem Caw;

        private PerchPhase _phase = PerchPhase.Rising;
        private int _phaseTicks, _markTicks, _peckInterval;
        private Vector3 _spot, _position;
        private Quaternion _rotation = Quaternion.identity;
        private bool _posed, _cawed;
        private float _age;
        private MaterialPropertyBlock _dots;
        private static readonly int LengthInWidths = Shader.PropertyToID("_LengthInWidths");

        /// <summary>Where Shared puts the crow this tick; the peck dip and the hover bob are added on top.</summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
            _posed = true;
        }

        public void SetLifecycle(PerchPhase phase, int phaseTicks, Vector3 spot, int markTicks, int peckInterval)
        {
            if (phase == PerchPhase.Marking && !_cawed && Caw != null)
            {
                _cawed = true;
                Caw.Play(true);
            }
            _phase = phase;
            _phaseTicks = phaseTicks;
            _spot = spot;
            _markTicks = markTicks;
            _peckInterval = peckInterval;
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            float flap = _phase switch
            {
                PerchPhase.Rising => Mathf.Sin(_age * 36f) * 42f,
                PerchPhase.Marking => -38f + Mathf.Sin(_age * 18f) * 10f,
                PerchPhase.Diving => 74f,
                _ => 68f,
            };
            float fold = _phase switch
            {
                PerchPhase.Marking => -30f,
                PerchPhase.Diving => 70f,
                PerchPhase.Perched => 56f,
                _ => -12f,
            };
            if (LeftWing != null) LeftWing.localRotation = Quaternion.Euler(0f, -flap, fold);
            if (RightWing != null) RightWing.localRotation = Quaternion.Euler(0f, flap, -fold);

            if (_posed)
            {
                var offset = Vector3.zero;
                var tilt = Quaternion.identity;
                if (_phase == PerchPhase.Marking) offset = Vector3.up * (.06f * Mathf.Sin(_age * 9f));
                if (_phase == PerchPhase.Perched && _peckInterval > 0)
                {
                    // The head dips over the eight ticks before each peck and snaps up after it.
                    int toPeck = _peckInterval - _phaseTicks % _peckInterval;
                    float dip = toPeck <= 8 ? 1f - toPeck / 8f : 0f;
                    tilt = Quaternion.Euler(50f * dip, 0f, 0f);
                }
                transform.SetPositionAndRotation(_position + offset, _rotation * tilt);
            }

            bool marked = _phase is PerchPhase.Marking or PerchPhase.Diving;
            if (DropLine != null)
            {
                DropLine.enabled = marked;
                if (marked)
                {
                    DropLine.SetPosition(0, _spot);
                    DropLine.SetPosition(1, transform.position);
                    // The roster's dotted drop line: dots counted from the spot, so they stay put as the crow dives.
                    _dots ??= new MaterialPropertyBlock();
                    DropLine.GetPropertyBlock(_dots);
                    _dots.SetFloat(LengthInWidths, Vector3.Distance(_spot, transform.position) / Mathf.Max(.01f, DropLine.widthMultiplier));
                    DropLine.SetPropertyBlock(_dots);
                }
            }
            if (SpotMark != null)
            {
                SpotMark.gameObject.SetActive(marked);
                if (marked)
                {
                    // Full size at the caw, then a quickening pulse until the dive lands.
                    float urgency = _phase == PerchPhase.Diving ? 1f : Mathf.Clamp01(_phaseTicks / (float)Mathf.Max(1, _markTicks));
                    float pulse = 1f + .08f * Mathf.Sin(_age * Mathf.Lerp(10f, 30f, urgency));
                    SpotMark.SetPositionAndRotation(_spot + Vector3.up * .03f, Quaternion.identity);
                    SpotMark.localScale = Vector3.one * pulse;
                }
            }
        }
    }
}
