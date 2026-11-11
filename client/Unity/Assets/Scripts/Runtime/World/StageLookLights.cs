using System.Collections.Generic;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The lights of a match that are not URP lights: the stage's own pools (<see cref="StageLookSettings.PoolLook"/>)
    /// and the short flash of light at a hit. Stage shaders and GraphicCharacter read them from global arrays
    /// (SlopArenaStageLights.hlsl), so they show on a floor that already carries URP's four lights per renderer
    /// and on every quality level. Presentation only; MatchVisualStyle adds one per match.
    /// </summary>
    public sealed class StageLookLights : MonoBehaviour
    {
        private const int Capacity = 4;     // STAGE_LOOK_LIGHTS in SlopArenaStageLights.hlsl
        private const int MaxPools = 2;
        private const float FlashFadeSeconds = 0.16f;
        private const float FlashMinHoldSeconds = 0.05f;

        // Light, Medium, Heavy, Launch: how much the flash adds at its centre, and how far it reaches (m).
        private static readonly float[] FlashStrength = { 0.6f, 0.9f, 1.2f, 1.5f };
        private static readonly float[] FlashRange = { 2.8f, 3.4f, 4.2f, 5f };

        private static readonly int PositionsId = Shader.PropertyToID("_StageLookLightPositions");
        private static readonly int ColorsId = Shader.PropertyToID("_StageLookLightColors");
        private static readonly int SpotsId = Shader.PropertyToID("_StageLookLightSpots");
        private static readonly int ConesId = Shader.PropertyToID("_StageLookLightCones");
        private static readonly int CountId = Shader.PropertyToID("_StageLookLightCount");

        private static StageLookLights? _instance;

        private struct FlashState
        {
            public Vector3 Position;
            public Color Color;
            public float Range, Hold, Age;
            public bool Active;
        }

        private readonly Vector4[] _positions = new Vector4[Capacity];
        private readonly Vector4[] _colors = new Vector4[Capacity];
        private readonly Vector4[] _spots = new Vector4[Capacity];
        private readonly Vector4[] _cones = new Vector4[Capacity];
        private readonly FlashState[] _flashes = new FlashState[Capacity - MaxPools];
        private int _pools;
        private int _nextFlash;

        /// <summary>Review tools show the flash whatever the player's Reduced Flashing setting.</summary>
        public bool IgnoreReducedFlashing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            _instance = null;
            Shader.SetGlobalFloat(CountId, 0f);
        }

        private void OnEnable() => _instance = this;

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
            Shader.SetGlobalFloat(CountId, 0f);
        }

        /// <summary>The stage's lamps, given in the space of <paramref name="stage"/>. More than two are ignored.</summary>
        public void SetPools(IReadOnlyList<StageLookSettings.PoolLook> pools, Transform? stage)
        {
            _pools = Mathf.Min(pools.Count, MaxPools);
            for (int i = 0; i < _pools; i++)
            {
                var pool = pools[i];
                Vector3 position = stage != null ? stage.TransformPoint(pool.Position) : pool.Position;
                Vector3 direction = (stage != null ? stage.TransformDirection(pool.Direction) : pool.Direction).normalized;
                float range = Mathf.Max(pool.Range, 0.01f);
                float outer = Mathf.Cos(Mathf.Clamp(pool.Angle, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
                float inner = Mathf.Cos(Mathf.Clamp(Mathf.Min(pool.InnerAngle, pool.Angle - 0.5f), 0f, 179f) * 0.5f * Mathf.Deg2Rad);
                _positions[i] = new Vector4(position.x, position.y, position.z, 1f / (range * range));
                _colors[i] = new Vector4(pool.Color.r * pool.Intensity, pool.Color.g * pool.Intensity, pool.Color.b * pool.Intensity, 0f);
                _spots[i] = new Vector4(direction.x, direction.y, direction.z, outer);
                _cones[i] = new Vector4(1f / Mathf.Max(inner - outer, 1e-4f), 0f, 0f, 0f);
            }
            Upload();
        }

        /// <summary>
        /// A short flash of light at an accepted hit, in the colour of its impact graphic. It holds through the
        /// hit's Hitstop, then fades. Reduced Flashing switches it off.
        /// </summary>
        public static void Flash(Vector3 position, ImpactTier tier, int hitstopTicks)
        {
            var lights = _instance;
            if (lights == null || tier == ImpactTier.Block) return;
            if (!lights.IgnoreReducedFlashing && ClientSettingsService.Instance.ReducedFlashing) return;
            int level = Mathf.Clamp((int)tier, 0, FlashStrength.Length - 1);
            // The graphic's own colour, paled a little so it lights a floor of any colour.
            Color color = Color.Lerp(GraphicHitEffect.CoreColor(tier), Color.white, 0.3f) * FlashStrength[level];
            lights._flashes[lights._nextFlash] = new FlashState
            {
                Position = position,
                Color = color,
                Range = FlashRange[level],
                Hold = Mathf.Max(hitstopTicks / 60f, FlashMinHoldSeconds),
                Age = 0f,
                Active = true,
            };
            lights._nextFlash = (lights._nextFlash + 1) % lights._flashes.Length;
        }

        private void LateUpdate()
        {
            bool any = false;
            for (int i = 0; i < _flashes.Length; i++)
            {
                if (!_flashes[i].Active) continue;
                _flashes[i].Age += Time.deltaTime;
                if (_flashes[i].Age >= _flashes[i].Hold + FlashFadeSeconds) _flashes[i].Active = false;
                any = true;
            }
            if (any) Upload();
        }

        private void Upload()
        {
            int count = _pools;
            for (int i = 0; i < _flashes.Length; i++)
            {
                var flash = _flashes[i];
                if (!flash.Active) continue;
                float fade = Mathf.Clamp01(1f - (flash.Age - flash.Hold) / FlashFadeSeconds);
                fade *= fade;
                _positions[count] = new Vector4(flash.Position.x, flash.Position.y, flash.Position.z, 1f / (flash.Range * flash.Range));
                _colors[count] = new Vector4(flash.Color.r * fade, flash.Color.g * fade, flash.Color.b * fade, 1f);
                _spots[count] = new Vector4(0f, -1f, 0f, -2f);     // a point light: every direction is inside the cone
                _cones[count] = new Vector4(1f, 0f, 0f, 0f);
                count++;
            }
            Shader.SetGlobalVectorArray(PositionsId, _positions);
            Shader.SetGlobalVectorArray(ColorsId, _colors);
            Shader.SetGlobalVectorArray(SpotsId, _spots);
            Shader.SetGlobalVectorArray(ConesId, _cones);
            Shader.SetGlobalFloat(CountId, count);
        }
    }
}
