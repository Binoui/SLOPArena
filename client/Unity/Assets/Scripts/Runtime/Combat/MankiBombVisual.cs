using UnityEngine;
using UnityEngine.Rendering;
using SlopArena.Shared;

namespace SlopArena.Client.Combat
{
    public sealed class MankiBombVisual : MonoBehaviour
    {
        private AudioSource _warningAudio;
        private AudioClip _warningClip;
        private bool _warned;
        private Transform _bomb, _zone;
        private GameObject _quiet, _armed, _triggered;

        public static MankiBombVisual Create(GameObject prefab)
        {
            var root = new GameObject("Manki Bomb");
            var visual = root.AddComponent<MankiBombVisual>();
            visual._bomb = prefab != null ? Instantiate(prefab, root.transform, false).transform
                : new GameObject("Missing bomb presentation").transform;
            visual._bomb.SetParent(root.transform, false);
            if (prefab == null) Debug.LogError("[MankiBombVisual] Bomb presentation prefab is missing.");
            visual._quiet = visual._bomb.Find("Unarmed")?.gameObject;
            visual._armed = visual._bomb.Find("Armed")?.gameObject;
            visual._triggered = visual._bomb.Find("Triggered")?.gameObject;
            visual._zone = visual._bomb.Find("Ember stitch");
            if (visual._zone != null) visual._zone.SetParent(root.transform, false);
            visual._warningClip = Resources.Load<AudioClip>("Audio/SFX/Slopball/slopball_beep");
            visual._warningAudio = root.AddComponent<AudioSource>();
            visual._warningAudio.playOnAwake = false;
            visual._warningAudio.spatialBlend = 1f;
            visual._warningAudio.maxDistance = 25f;
            visual._warningAudio.volume = .5f;
            visual._warningAudio.outputAudioMixerGroup = SlopArena.Client.UI.ClientSettingsService.Instance.FindBus("SFX");
            return visual;
        }

        public void UpdateBomb(in Hitbox bomb, in ArenaDefinition arena, int[] triangleCandidates)
        {
            _bomb.localScale = Vector3.one * (bomb.Radius / .35f);
            bool triggered = bomb.MinePhase == MinePhase.Triggered;
            if (!_warned && triggered && _warningClip != null) _warningAudio.PlayOneShot(_warningClip);
            _warned = triggered;
            SetVisible(_quiet, bomb.MinePhase is MinePhase.Falling or MinePhase.Arming);
            SetVisible(_armed, bomb.MinePhase == MinePhase.Armed);
            SetVisible(_triggered, triggered);
            // Every face and fuse is a steady phase variant, including ReducedFlashing.
            // Re-read the phase each tick so a rollback restores the quiet/armed look too.
            if (_triggered != null)
            {
                float warning = 1f - bomb.MineTicksLeft / (float)Mathf.Max(1, bomb.MineWarningTicks);
                var fuse = _triggered.transform.Find("Hot fuse");
                if (fuse != null) fuse.localScale = new Vector3(1f, Mathf.Lerp(1f, .65f, Mathf.Clamp01(warning)), 1f);
            }
            if (_zone == null) return;
            bool floor = SpellResolver.TryProjectStraightDrop(arena, bomb.X, bomb.Y, bomb.Z,
                bomb.Radius, triangleCandidates, out float landingY);
            _zone.gameObject.SetActive(floor && bomb.Explosion.HasValue);
            if (!_zone.gameObject.activeSelf) return;
            _zone.SetPositionAndRotation(new Vector3(bomb.X, landingY + .025f, bomb.Z), Quaternion.identity);
            _zone.localScale = Vector3.one * bomb.Explosion.Value.Radius;
        }

        private static void SetVisible(GameObject part, bool visible)
        {
            if (part != null && part.activeSelf != visible) part.SetActive(visible);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;
        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_bomb != null && camera != null) _bomb.rotation = camera.transform.rotation;
        }
    }
}
