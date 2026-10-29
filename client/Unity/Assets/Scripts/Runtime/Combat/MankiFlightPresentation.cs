using SlopArena.Client.Entities;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    public sealed class MankiFlightPresentation : MonoBehaviour
    {
        private GameObject _exhaust, _trail;
        private float _fuel;
        private bool _flying;
        private Transform _readout, _fill;
        private TextMesh _label;
        private Material _ink, _amber;

        public void Apply(PlayerRenderer owner, CharacterState state, ushort capacity)
        {
            _fuel = state.Flight.FuelTicksLeft / (float)capacity;
            _flying = state.Flight.Active;
            if (_readout == null) CreateReadout();
            _fill.localScale = new Vector3(.94f * Mathf.Clamp01(_fuel), .065f, .035f);
            _fill.localPosition = new Vector3(-.47f + .47f * Mathf.Clamp01(_fuel), 0f, -.025f);
            _label.text = $"{(_flying ? "FLYING" : "FUEL")} {Mathf.CeilToInt(_fuel * 100f)}%";
            if (_flying)
            {
                if (_exhaust == null) _exhaust = Spawn(owner, "presentation.manki.jetpack-boost.exhaust");
                if (_trail == null) _trail = Spawn(owner, "presentation.manki.jetpack-boost.trail");
            }
            else
            {
                if (_exhaust != null) Destroy(_exhaust);
                if (_trail != null) Destroy(_trail);
            }
        }

        private GameObject Spawn(PlayerRenderer owner, string id)
        {
            foreach (var binding in owner.PresentationCatalog.Presentations)
                if (binding.SemanticId == id)
                    return PresentationPlacementResolver.Instantiate(binding,
                        new PresentationPlacement(AttachmentMode: AuthoringPresentationAttachmentMode.Bone,
                            BoneId: "bone.spine2", LocalPositionY: -.13f, LocalPositionZ: -.15f, DurationTicks: 600),
                        owner, owner.transform, owner.transform.position, owner.transform.rotation);
            return null;
        }

        private void CreateReadout()
        {
            _readout = new GameObject("Jetpack fuel").transform;
            _readout.SetParent(transform, false);
            _readout.localPosition = new Vector3(0f, 2.7f, 0f);
            _ink = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.black };
            _amber = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(1f, .7f, .1f) };
            Bar("Fuel background", _ink).localScale = new Vector3(1f, .12f, .04f);
            _fill = Bar("Fuel remaining", _amber);
            var label = new GameObject("Fuel percent");
            label.transform.SetParent(_readout, false);
            label.transform.localPosition = new Vector3(0f, .2f, -.03f);
            _label = label.AddComponent<TextMesh>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.GetComponent<MeshRenderer>().sharedMaterial = _label.font.material;
            _label.fontSize = 40;
            _label.characterSize = .065f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(1f, .8f, .2f);
        }

        private Transform Bar(string name, Material material)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            bar.transform.SetParent(_readout, false);
            Destroy(bar.GetComponent<Collider>());
            bar.GetComponent<MeshRenderer>().sharedMaterial = material;
            return bar.transform;
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;

        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_readout != null && camera != null) _readout.rotation = camera.transform.rotation;
        }

        private void OnDestroy()
        {
            if (_exhaust != null) Destroy(_exhaust);
            if (_trail != null) Destroy(_trail);
            if (_ink != null) Destroy(_ink);
            if (_amber != null) Destroy(_amber);
        }
    }
}
