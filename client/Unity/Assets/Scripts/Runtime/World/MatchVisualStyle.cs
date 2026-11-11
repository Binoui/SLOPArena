using System.Collections.Generic;

using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Applies the shared graphic-stylized presentation after a match has spawned its stage
    /// and character models: the fighter shader, and the stage's look (<see cref="StageLook"/>) as the grade of
    /// the picture, the light and ink on fighters and the stage's own lamps. This is deliberately
    /// presentation-only: stage collision and simulation state remain untouched.
    /// </summary>
    public sealed class MatchVisualStyle : MonoBehaviour
    {
        // The ink line keeps between these two widths on the fighter, and its hull sits this far behind the body.
        private const float InkMinMetres = 0.012f, InkMaxMetres = 0.024f, InkDepthPush = 0.14f;

        private static readonly int FighterFillId = Shader.PropertyToID("_FighterFill");
        private static readonly int KickColorAId = Shader.PropertyToID("_FighterKickColorA");
        private static readonly int KickDirAId = Shader.PropertyToID("_FighterKickDirA");
        private static readonly int KickColorBId = Shader.PropertyToID("_FighterKickColorB");
        private static readonly int KickDirBId = Shader.PropertyToID("_FighterKickDirB");
        private static readonly int KickPowerId = Shader.PropertyToID("_FighterKickPower");
        private static readonly int LiftId = Shader.PropertyToID("_FighterLift");
        private static readonly int LampCapId = Shader.PropertyToID("_FighterLampCap");
        private static readonly int InkColorId = Shader.PropertyToID("_FighterInkColor");
        private static readonly int InkId = Shader.PropertyToID("_FighterInk");

        private readonly List<Material> _runtimeMaterials = new();
        private readonly List<(UniversalAdditionalCameraData data, bool postProcessing, bool dithering)> _cameras = new();
        private VolumeProfile? _runtimeProfile;
        private StageLookSettings _look = StageLookSettings.Default;
        private Bloom? _bloom;
        private DepthOfField? _farBlur;
        private int _qualityLevel = -1;
        private int _cameraCount = -1;
        private bool _applied;

        /// <summary>The look of the match being played; null outside a match.</summary>
        public static MatchVisualStyle? Current { get; private set; }

        public void Apply()
        {
            Current = this;
            var stage = FindFirstObjectByType<StageLook>();
            _look = stage != null ? stage.Settings : StageLookSettings.Default;
            StageLook.Active = _look;
            ApplyAtmosphere();
            ApplyCameras();
            ApplyCharacterMaterials();
            gameObject.AddComponent<StageLookLights>().SetPools(_look.Pools, stage != null ? stage.transform : null);
            _applied = true;
        }

        /// <summary>Resting silhouette rim applied to every fighter material.</summary>
        public const float RimStrength = 0.72f, RimPower = 3.2f;

        /// <summary>Camera-facing fill on fighters only, so night stages with a weak key keep them readable (owner pick 2026-11-04).</summary>
        public const float FighterFill = 0.6f;

        /// <summary>Fighter silhouette colour follows camp, course or seat identity.</summary>
        public static Color RimColorFor(PlayerRenderer renderer) => SeatColours.ForFighter(renderer.EntityId, renderer.PlayerNumber);

        /// <summary>The fighter shader; null when the build lacks it.</summary>
        public static Shader? FighterShader => Resources.Load<Shader>("Shaders/GraphicCharacter");

        /// <summary>A fighter material with the match's shadow tint and resting rim; callers copy the model's own map, colour and culling.</summary>
        public static Material CreateFighterMaterial(Shader shader, string name, Color rim)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_ShadowColor", new Color(0.16f, 0.18f, 0.28f));
            material.SetColor("_RimColor", rim);
            material.SetFloat("_RimPower", RimPower);
            material.SetFloat("_RimStrength", RimStrength);
            return material;
        }

        private void ApplyAtmosphere()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.39f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.17f, 0.20f, 0.30f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.06f, 0.09f);
            RenderSettings.ambientIntensity = 1.15f;

            var volumeObject = new GameObject("SLOPArena Visual Grade");
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SLOPArena Runtime Visual Grade";
            volume.profile = profile;
            _runtimeProfile = profile;

            // No tonemapping keeps the colours stages and fighters were authored in; a stage built on glow asks for
            // Neutral. ACES, which this volume used to carry without ever drawing, shifted hues and bleached emission.
            var grade = _look.Grade;
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(grade.NeutralTonemap ? TonemappingMode.Neutral : TonemappingMode.None);

            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(grade.Contrast);
            color.saturation.Override(grade.Saturation);
            color.colorFilter.Override(grade.Filter);

            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.Override(grade.BloomThreshold);
            _bloom.intensity.Override(grade.BloomIntensity);
            _bloom.scatter.Override(grade.BloomScatter);

            var vignette = profile.Add<Vignette>(true);
            vignette.color.Override(grade.VignetteColor);
            vignette.intensity.Override(grade.Vignette);
            vignette.smoothness.Override(0.45f);

            if (grade.SplitToning)
            {
                var split = profile.Add<SplitToning>(true);
                split.shadows.Override(grade.SplitShadows);
                split.highlights.Override(grade.SplitHighlights);
                split.balance.Override(grade.SplitBalance);
            }

            if (_look.FarBlur.Enabled)
            {
                _farBlur = profile.Add<DepthOfField>(true);
                _farBlur.mode.Override(DepthOfFieldMode.Gaussian);
                _farBlur.gaussianStart.Override(_look.FarBlur.Start);
                _farBlur.gaussianEnd.Override(_look.FarBlur.End);
                _farBlur.gaussianMaxRadius.Override(_look.FarBlur.Radius);
                _farBlur.highQualitySampling.Override(true);
            }
            ApplyQuality();
        }

        // Low keeps the grade with a plain bloom; the far blur and the finer bloom filter are for the other levels.
        private void ApplyQuality()
        {
            _qualityLevel = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            bool low = _qualityLevel >= 0 && _qualityLevel < names.Length && names[_qualityLevel].Contains("Low");
            if (_bloom != null) _bloom.highQualityFiltering.Override(!low);
            if (_farBlur != null) _farBlur.active = !low;
        }

        // The grade only draws on cameras that render post-processing; scene cameras ship with it off.
        private void ApplyCameras()
        {
            _cameraCount = UnityEngine.Camera.allCamerasCount;
            foreach (var camera in UnityEngine.Camera.allCameras)
            {
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null) continue;
                var data = camera.GetUniversalAdditionalCameraData();
                if (data.renderType != CameraRenderType.Base || (data.renderPostProcessing && data.dithering)) continue;
                _cameras.Add((data, data.renderPostProcessing, data.dithering));
                data.renderPostProcessing = true;
                data.dithering = true;
            }
        }

        private void Update()
        {
            if (!_applied) return;
            if (QualitySettings.GetQualityLevel() != _qualityLevel) ApplyQuality();
            // A camera that joins the match later (a replay or photo camera) takes the grade too.
            if (UnityEngine.Camera.allCamerasCount != _cameraCount) ApplyCameras();
        }

        private void ApplyCharacterMaterials()
        {
            var shader = FighterShader;
            if (shader == null)
            {
                Debug.LogWarning("[MatchVisualStyle] SLOPArena/GraphicCharacter shader not found.");
                return;
            }
            Shader.SetGlobalFloat(FighterFillId, FighterFill);
            ApplyFighterLook();

            foreach (var player in FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None))
                StyleSkins(player.GetComponentsInChildren<SkinnedMeshRenderer>(true), RimColorFor(player), shader);
        }

        /// <summary>
        /// Give a model that replaced a fighter's model mid-match (a model swap taken off) the look every fighter got
        /// at the start: the fighter shader with its seat rim, and the ink line when the stage has one.
        /// </summary>
        public void StyleModel(PlayerRenderer player, GameObject model)
        {
            var shader = FighterShader;
            if (shader != null) StyleSkins(model.GetComponentsInChildren<SkinnedMeshRenderer>(true), RimColorFor(player), shader);
        }

        private void StyleSkins(SkinnedMeshRenderer[] skins, Color rimColor, Shader shader)
        {
            foreach (var skin in skins)
            {
                var originals = skin.sharedMaterials;
                var styled = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    var original = originals[i];
                    if (original == null)
                        continue;

                    var material = CreateFighterMaterial(shader, original.name + "_GraphicRuntime", rimColor);
                    if (original.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                        material.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
                        material.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
                    }
                    if (original.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", original.GetColor("_BaseColor"));
                    if (original.HasProperty("_Cull"))
                        material.SetFloat("_Cull", original.GetFloat("_Cull"));
                    styled[i] = material;
                    _runtimeMaterials.Add(material);
                }
                skin.sharedMaterials = styled;
                if (_look.Fighters.Ink > 0f) FighterInk.Apply(skin);
            }
        }

        // The stage's light on every fighter: two kick lights on the edges, a lift, the lamp cap and the ink line.
        private void ApplyFighterLook()
        {
            var fighters = _look.Fighters;
            Color kickA = fighters.KickA, kickB = fighters.KickB;
            Vector3 directionA = fighters.KickADirection, directionB = fighters.KickBDirection;
            var key = fighters.KicksFromKeyLight ? KeyLight() : null;
            if (key != null)
            {
                // A stage without its own kicks: its key light on the fighter's edge, answered from the far side.
                Color.RGBToHSV(key.color, out float hue, out float saturation, out _);
                kickA = Color.HSVToRGB(hue, saturation, 1f);
                directionA = -key.transform.forward;
                directionB = new Vector3(-directionA.x, Mathf.Max(directionA.y * 0.5f, 0.2f), -directionA.z);
            }
            Shader.SetGlobalColor(KickColorAId, new Color(kickA.r, kickA.g, kickA.b, fighters.KickAStrength));
            Shader.SetGlobalVector(KickDirAId, directionA.normalized);
            Shader.SetGlobalColor(KickColorBId, new Color(kickB.r, kickB.g, kickB.b, fighters.KickBStrength));
            Shader.SetGlobalVector(KickDirBId, directionB.normalized);
            Shader.SetGlobalFloat(KickPowerId, fighters.KickPower);
            Shader.SetGlobalFloat(LiftId, fighters.Lift);
            Shader.SetGlobalFloat(LampCapId, fighters.LampCap);
            Shader.SetGlobalColor(InkColorId, fighters.InkColor);
            Shader.SetGlobalVector(InkId, new Vector4(fighters.Ink, InkMinMetres, InkMaxMetres, InkDepthPush));
        }

        private static Light? KeyLight()
        {
            Light? key = null;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.isActiveAndEnabled && light.type == LightType.Directional && (key == null || light.intensity > key.intensity))
                    key = light;
            return key;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            foreach (var material in _runtimeMaterials)
                if (material != null)
                    Destroy(material);
            if (_runtimeProfile != null)
                Destroy(_runtimeProfile);
            if (!_applied) return;

            foreach (var (data, postProcessing, dithering) in _cameras)
            {
                if (data == null) continue;
                data.renderPostProcessing = postProcessing;
                data.dithering = dithering;
            }
            // Fighters drawn outside a match (none today) fall back to the plain shader look.
            Shader.SetGlobalColor(KickColorAId, Color.clear);
            Shader.SetGlobalColor(KickColorBId, Color.clear);
            Shader.SetGlobalFloat(LiftId, 0f);
            Shader.SetGlobalFloat(LampCapId, 0f);
            Shader.SetGlobalVector(InkId, Vector4.zero);
            if (StageLook.Active == _look) StageLook.Active = StageLookSettings.Default;
        }
    }
}
