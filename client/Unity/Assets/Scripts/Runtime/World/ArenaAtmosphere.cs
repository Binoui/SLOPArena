using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Presentation-only atmosphere owned by a stage prefab. Start runs after MatchVisualStyle
    /// so a stage may replace the shared neutral background without affecting simulation.
    /// A stage that owns its lighting keeps its lights under a "Stage Lights" root instead;
    /// MatchBase then switches the match scene's directional lights off.
    /// </summary>
    public sealed class ArenaAtmosphere : MonoBehaviour
    {
        [SerializeField] private Color _background = new(0.45f, 0.70f, 0.90f);
        [SerializeField] private Color _ambientSky = new(0.55f, 0.68f, 0.80f);
        [SerializeField] private Color _ambientGround = new(0.18f, 0.23f, 0.30f);
        [SerializeField] private float _ambientIntensity = 1f;
        [SerializeField] private bool _fog;
        [SerializeField] private Color _fogColor = new(0.15f, 0.18f, 0.28f);
        [SerializeField] private float _fogStart = 35f;
        [SerializeField] private float _fogEnd = 100f;

        public void Configure(
            Color background,
            Color ambientSky,
            Color ambientGround,
            float ambientIntensity,
            bool fog = false,
            Color fogColor = default,
            float fogStart = 35f,
            float fogEnd = 100f)
        {
            _background = background;
            _ambientSky = ambientSky;
            _ambientGround = ambientGround;
            _ambientIntensity = ambientIntensity;
            _fog = fog;
            _fogColor = fogColor == default ? background : fogColor;
            _fogStart = fogStart;
            _fogEnd = fogEnd;
        }

        private void Start()
        {
            var camera = Object.FindFirstObjectByType<UnityEngine.Camera>();
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = _background;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _ambientSky;
            RenderSettings.ambientEquatorColor = Color.Lerp(_ambientSky, _ambientGround, 0.5f);
            RenderSettings.ambientGroundColor = _ambientGround;
            RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.fog = _fog;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
        }
    }
}
