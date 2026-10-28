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
    /// and character models. This is deliberately presentation-only: stage collision and
    /// simulation state remain untouched.
    /// </summary>
    public sealed class MatchVisualStyle : MonoBehaviour
    {
        private readonly List<Material> _runtimeMaterials = new();
        private VolumeProfile _runtimeProfile;

        public void Apply()
        {
            ApplyAtmosphere();
            ApplyCharacterMaterials();
        }

        /// <summary>Resting silhouette rim applied to every fighter material.</summary>
        public const float RimStrength = 0.72f, RimPower = 3.2f;

        /// <summary>Fighter silhouette colour follows camp, course or seat identity.</summary>
        public static Color RimColorFor(PlayerRenderer renderer) => SeatColours.ForFighter(renderer.EntityId, renderer.PlayerNumber);

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

            _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeProfile.name = "SLOPArena Runtime Visual Grade";
            volume.profile = _runtimeProfile;

            var tonemapping = _runtimeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var color = _runtimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.12f);
            color.contrast.Override(14f);
            color.saturation.Override(8f);
            color.colorFilter.Override(new Color(1f, 0.97f, 0.92f));

            var bloom = _runtimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.18f);
            bloom.scatter.Override(0.55f);

            var vignette = _runtimeProfile.Add<Vignette>(true);
            vignette.color.Override(new Color(0.04f, 0.025f, 0.07f));
            vignette.intensity.Override(0.16f);
            vignette.smoothness.Override(0.48f);
        }

        private void ApplyCharacterMaterials()
        {
            var shader = Shader.Find("SLOPArena/GraphicCharacter");
            if (shader == null)
            {
                Debug.LogWarning("[MatchVisualStyle] SLOPArena/GraphicCharacter shader not found.");
                return;
            }

            foreach (var player in FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None))
            {
                Color rimColor = RimColorFor(player);

                foreach (var modelRenderer in player.GetComponentsInChildren<Renderer>(true))
                {
                    if (modelRenderer is not SkinnedMeshRenderer)
                        continue;

                    var originals = modelRenderer.sharedMaterials;
                    var styled = new Material[originals.Length];
                    for (int i = 0; i < originals.Length; i++)
                    {
                        var original = originals[i];
                        if (original == null)
                            continue;

                        var material = new Material(shader)
                        {
                            name = original.name + "_GraphicRuntime",
                        };
                        if (original.HasProperty("_BaseMap"))
                        {
                            material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                            material.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
                            material.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
                        }
                        if (original.HasProperty("_BaseColor"))
                            material.SetColor("_BaseColor", original.GetColor("_BaseColor"));

                        material.SetColor("_ShadowColor", new Color(0.16f, 0.18f, 0.28f));
                        material.SetColor("_RimColor", rimColor);
                        material.SetFloat("_RimPower", RimPower);
                        material.SetFloat("_RimStrength", RimStrength);
                        styled[i] = material;
                        _runtimeMaterials.Add(material);
                    }
                    modelRenderer.sharedMaterials = styled;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var material in _runtimeMaterials)
                if (material != null)
                    Destroy(material);
            if (_runtimeProfile != null)
                Destroy(_runtimeProfile);
        }
    }
}
