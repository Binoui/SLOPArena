using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    public sealed class MankiLockReticle : MonoBehaviour
    {
        private Mesh _mesh;
        private Vector3 _target;
        private Material _material;
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color32> _colors = new();
        private readonly List<int> _triangles = new();

        private void Awake()
        {
            _mesh = new Mesh { name = "Manki target lock" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(Resources.Load<Shader>("Shaders/FacingAimMarker"));
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Show(Vector3 target, float progress)
        {
            _target = target;
            transform.position = target;
            _vertices.Clear(); _colors.Clear(); _triangles.Clear();
            float radius = Mathf.Lerp(1.65f, .82f, Mathf.Clamp01(progress));
            for (int layer = 0; layer < 3; layer++)
            {
                float width = layer == 0 ? .13f : layer == 1 ? .075f : .021f;
                Color32 color = layer == 0 ? new Color32(39, 23, 19, 245)
                    : layer == 1 ? new Color32(255, 117, 24, 255) : new Color32(255, 234, 157, 255);
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    var corner = new Vector2(x * radius, y * radius);
                    Stroke(corner - new Vector2(x * .4f, 0f), corner, width, color);
                    Stroke(corner, corner - new Vector2(0f, y * .4f), width, color);
                }
            }
            _mesh.Clear(); _mesh.SetVertices(_vertices); _mesh.SetColors(_colors); _mesh.SetTriangles(_triangles, 0);
        }

        private void Stroke(Vector2 from, Vector2 to, float width, Color32 color)
        {
            Vector2 d = (to - from).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * (width * .5f);
            int first = _vertices.Count;
            foreach (var p in new[] { from - n, from + n, to + n, to - n })
            { _vertices.Add(new Vector3(p.x, p.y, 0)); _colors.Add(color); }
            _triangles.Add(first); _triangles.Add(first + 1); _triangles.Add(first + 2);
            _triangles.Add(first); _triangles.Add(first + 2); _triangles.Add(first + 3);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;
        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera != null)
            {
                transform.rotation = camera.transform.rotation;
                transform.position = _target - camera.transform.forward;
            }
        }
        private void OnDestroy() { Destroy(_mesh); Destroy(_material); }
    }
}
