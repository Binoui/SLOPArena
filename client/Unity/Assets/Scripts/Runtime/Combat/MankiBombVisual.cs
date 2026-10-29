using UnityEngine;
using UnityEngine.Rendering;
using SlopArena.Shared;
using SharedSimulation = SlopArena.Shared.Simulation;

namespace SlopArena.Client.Combat
{
    public sealed class MankiBombVisual : MonoBehaviour
    {
        private Material _zoneMaterial;
        private Transform _bomb, _zone;
        private MeshRenderer _zoneRenderer;
        private MaterialPropertyBlock _zoneProperties;

        public static MankiBombVisual Create(GameObject prefab)
        {
            var root = new GameObject("Manki Bomb");
            var visual = root.AddComponent<MankiBombVisual>();
            visual._bomb = prefab != null ? Instantiate(prefab, root.transform, false).transform
                : new GameObject("Missing bomb presentation").transform;
            visual._bomb.SetParent(root.transform, false);
            if (prefab == null) Debug.LogError("[MankiBombVisual] Bomb presentation prefab is missing.");
            var zone = GameObject.CreatePrimitive(PrimitiveType.Quad);
            zone.name = "Blast ground warning";
            zone.transform.SetParent(root.transform, false);
            Destroy(zone.GetComponent<Collider>());
            visual._zone = zone.transform;
            visual._zoneMaterial = new Material(Resources.Load<Shader>("Shaders/MankiBombZone"));
            visual._zoneRenderer = zone.GetComponent<MeshRenderer>();
            visual._zoneRenderer.sharedMaterial = visual._zoneMaterial;
            visual._zoneRenderer.shadowCastingMode = ShadowCastingMode.Off;
            visual._zoneRenderer.receiveShadows = false;
            visual._zoneProperties = new MaterialPropertyBlock();
            zone.SetActive(false);
            return visual;
        }

        public void UpdateBomb(in Hitbox bomb, in ArenaDefinition arena, int[] triangleCandidates)
        {
            // The generated shell keeps the original .35 m radius. The floor marker stays independent.
            _bomb.localScale = Vector3.one * (bomb.Radius / .35f);
            bool floor = SpellResolver.TryProjectStraightDrop(arena, bomb.X, bomb.Y, bomb.Z,
                bomb.Radius, triangleCandidates, out float landingY);
            _zone.gameObject.SetActive(floor && bomb.Explosion.HasValue);
            if (!_zone.gameObject.activeSelf) return;

            _zone.SetPositionAndRotation(new Vector3(bomb.X, landingY + .025f, bomb.Z), Quaternion.Euler(90f, 0f, 0f));
            _zone.localScale = Vector3.one * (bomb.Explosion.Value.Radius * 2f);
            float progress;
            if (bomb.Gravity > 0f)
            {
                float distance = Mathf.Max(0f, bomb.Y - bomb.Radius - landingY);
                // Same move-then-gravity integration as Shared: d = v*t + g*t*(t-dt)/2.
                float downSpeed = -bomb.VY - bomb.Gravity * SharedSimulation.TickDt * .5f;
                float secondsLeft = (Mathf.Sqrt(downSpeed * downSpeed + 2f * bomb.Gravity * distance) - downSpeed) / bomb.Gravity;
                float age = bomb.AgeTicks * SharedSimulation.TickDt;
                progress = age / Mathf.Max(SharedSimulation.TickDt, age + secondsLeft);
            }
            else progress = bomb.AgeTicks / (float)Mathf.Max(1, bomb.DurationTicks);
            // No time-based pulse or flashing, including with ReducedFlashing enabled.
            // Recompute from the predicted hitbox each tick so rollback corrects the warning too.
            _zoneProperties.SetFloat("_Progress", Mathf.Clamp01(progress));
            _zoneRenderer.SetPropertyBlock(_zoneProperties);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;

        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_bomb != null && camera != null) _bomb.rotation = camera.transform.rotation;
        }

        private void OnDestroy()
        {
            Destroy(_zoneMaterial);
        }
    }
}
