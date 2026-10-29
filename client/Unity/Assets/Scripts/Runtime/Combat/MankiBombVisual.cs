using UnityEngine;
using UnityEngine.Rendering;
using SlopArena.Shared;
using SharedSimulation = SlopArena.Shared.Simulation;

namespace SlopArena.Client.Combat
{
    public sealed class MankiBombVisual : MonoBehaviour
    {
        private Material _body, _fuse, _zoneMaterial;
        private Transform _bomb, _zone;
        private MeshRenderer _zoneRenderer;
        private MaterialPropertyBlock _zoneProperties;

        public static MankiBombVisual Create()
        {
            var root = new GameObject("Manki Bomb");
            var visual = root.AddComponent<MankiBombVisual>();
            visual._bomb = new GameObject("Body").transform;
            visual._bomb.SetParent(root.transform, false);
            visual._body = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.08f, .07f, .06f) };
            visual._fuse = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(1f, .5f, .03f) };
            visual.Part(PrimitiveType.Sphere, Vector3.zero, Vector3.one * .7f, visual._body);
            visual.Part(PrimitiveType.Cylinder, new Vector3(0f, .38f, 0f), new Vector3(.05f, .12f, .05f), visual._fuse);
            visual.Part(PrimitiveType.Sphere, new Vector3(0f, .5f, 0f), Vector3.one * .12f, visual._fuse);
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
            // The original sphere has a .35 m radius. Scale only the body, not the floor marker.
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

        private void Part(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(_bomb, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Destroy(part.GetComponent<Collider>());
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void OnDestroy()
        {
            Destroy(_body);
            Destroy(_fuse);
            Destroy(_zoneMaterial);
        }
    }
}
