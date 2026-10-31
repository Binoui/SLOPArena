using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    public sealed class MankiGrinScatter : MonoBehaviour
    {
        public Transform[] Clods;
        public Transform Face;
        private Vector3[] _positions, _scales;
        private Quaternion[] _rotations;
        private MeshRenderer[] _renderers;
        private MeshRenderer _faceRenderer;
        private Vector3 _facePosition, _faceScale;
        private MaterialPropertyBlock _properties;
        private float _age;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _positions = new Vector3[Clods.Length];
            _scales = new Vector3[Clods.Length];
            _rotations = new Quaternion[Clods.Length];
            _renderers = new MeshRenderer[Clods.Length];
            _properties = new MaterialPropertyBlock();
            for (int i = 0; i < Clods.Length; i++)
            {
                _positions[i] = Clods[i].localPosition;
                _scales[i] = Clods[i].localScale;
                _rotations[i] = Clods[i].localRotation;
                _renderers[i] = Clods[i].GetComponentInChildren<MeshRenderer>();
            }
            _facePosition = Face.localPosition;
            _faceScale = Face.localScale;
            _faceRenderer = Face.GetComponentInChildren<MeshRenderer>();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float tick = _age * 60f;
            float split = Mathf.Clamp01(tick / 6f);
            for (int i = 0; i < Clods.Length; i++)
            {
                var start = _positions[i];
                var outwards = new Vector3(start.x, .16f + i % 3 * .09f, start.z).normalized;
                float travel = split * .64f + Mathf.Max(0f, tick - 6f) * .045f;
                float end = 12f + i % 3 * 2f;
                float fade = 1f - Mathf.Clamp01((tick - 7f) / (end - 7f));
                float size = Mathf.Lerp(.38f, 1f, split) * Mathf.Lerp(.45f, 1f, fade);
                Clods[i].localPosition = start + outwards * travel + Vector3.up * tick * .014f;
                Clods[i].localRotation = _rotations[i] * Quaternion.Euler(tick * (i % 2 == 0 ? 3f : -3f), tick * 2f, tick * 1.4f);
                Clods[i].localScale = _scales[i] * size;
                Clods[i].gameObject.SetActive(tick < end);
                var tint = Color.Lerp(new Color(1f, .84f, .72f), Color.white, i / 5f);
                tint.a = fade;
                _properties.SetColor(BaseColor, tint);
                _renderers[i].SetPropertyBlock(_properties);
            }
            float faceFade = Mathf.Clamp01((tick - 1f) / 2f) * (1f - Mathf.Clamp01((tick - 7f) / 5f));
            Face.localPosition = _facePosition + Vector3.up * tick * .04f;
            Face.localScale = _faceScale * Mathf.Lerp(.65f, 1f, split);
            Face.gameObject.SetActive(tick < 12f);
            _properties.SetColor(BaseColor, new Color(1f, 1f, 1f, faceFade));
            _faceRenderer.SetPropertyBlock(_properties);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;
        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (Face != null && camera != null) Face.rotation = camera.transform.rotation;
        }
    }
}
