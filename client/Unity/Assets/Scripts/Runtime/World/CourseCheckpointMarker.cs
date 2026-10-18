using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// A Character Course checkpoint (or finish) frame on the floor around its Shared sensor
    /// volume. Hidden until it matters: jade once every target it needs is broken (crossing it
    /// will save), red after the fighter reached it with targets missing. A save flashes it away.
    /// Presentation only; Shared decides every save.
    /// </summary>
    public sealed class CourseCheckpointMarker : MonoBehaviour
    {
        public enum Look : byte { Hidden, Armed, Locked }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public MeshFilter Frame;
        public MeshRenderer FrameRenderer;
        public Material ArmedMaterial;
        public Material LockedMaterial;
        public ParticleSystem ArmedMotes;
        public ParticleSystem LockedMotes;
        [Tooltip("Width of the frame band, centred on the volume's edge (metres).")]
        public float Width = .55f;
        public float PulseHz = .8f;
        public float FlashSeconds = .6f;
        public int FlashMotes = 24;
        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private Look _look;
        private float _flash = -1f, _age;

        public Look Current => _look;

        private void Awake() => _block = new MaterialPropertyBlock();

        /// <summary>Builds the frame around a volume of <paramref name="halfSize"/> (x, z) metres.</summary>
        public void Setup(Vector2 halfSize)
        {
            Vector2 inner = new(Mathf.Max(.05f, halfSize.x - Width * .5f), Mathf.Max(.05f, halfSize.y - Width * .5f));
            Vector2 outer = new(halfSize.x + Width * .5f, halfSize.y + Width * .5f);
            Vector2[] signs = { new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f) };
            var vertices = new Vector3[8];
            var uvs = new Vector2[8];
            var colors = new Color[8];
            for (int i = 0; i < 4; i++)
            {
                vertices[i] = new Vector3(signs[i].x * inner.x, 0f, signs[i].y * inner.y);
                vertices[i + 4] = new Vector3(signs[i].x * outer.x, 0f, signs[i].y * outer.y);
                uvs[i] = new Vector2(.5f, 0f);
                uvs[i + 4] = new Vector2(.5f, 1f);
                colors[i] = colors[i + 4] = Color.white;
            }
            var triangles = new int[24];
            for (int i = 0; i < 4; i++)
            {
                int a = i, b = (i + 1) % 4, c = i + 4, d = (i + 1) % 4 + 4, t = i * 6;
                triangles[t] = a; triangles[t + 1] = c; triangles[t + 2] = d;
                triangles[t + 3] = a; triangles[t + 4] = d; triangles[t + 5] = b;
            }
            if (_mesh == null) _mesh = new Mesh { name = "Course Checkpoint Frame" };
            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.colors = colors;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            if (Frame != null) Frame.sharedMesh = _mesh;
            foreach (var motes in new[] { ArmedMotes, LockedMotes })
            {
                if (motes == null) continue;
                var shape = motes.shape;
                shape.scale = new Vector3(halfSize.x * 2f, .02f, halfSize.y * 2f);
            }
            _look = Look.Hidden;
            _flash = -1f;
            Apply();
        }

        public void Show(Look look)
        {
            if (look == _look) return;
            _look = look;
            if (_flash < 0f) Apply();
        }

        /// <summary>The fighter saved here: the frame flares and fades, and a burst of motes rises.</summary>
        public void Saved()
        {
            _look = Look.Hidden;
            _flash = 0f;
            if (FrameRenderer != null)
            {
                FrameRenderer.enabled = true;
                FrameRenderer.sharedMaterial = ArmedMaterial;
            }
            if (ArmedMotes != null)
            {
                ArmedMotes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                ArmedMotes.Emit(FlashMotes);
            }
            if (LockedMotes != null) LockedMotes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Apply()
        {
            if (FrameRenderer != null)
            {
                FrameRenderer.enabled = _look != Look.Hidden;
                FrameRenderer.sharedMaterial = _look == Look.Locked ? LockedMaterial : ArmedMaterial;
            }
            Emit(ArmedMotes, _look == Look.Armed);
            Emit(LockedMotes, _look == Look.Locked);
            transform.localScale = Vector3.one;
        }

        private static void Emit(ParticleSystem motes, bool on)
        {
            if (motes == null) return;
            if (on && !motes.isEmitting) motes.Play(true);
            else if (!on && motes.isEmitting) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (FrameRenderer == null || !FrameRenderer.enabled) return;
            float alpha;
            if (_flash >= 0f)
            {
                _flash += Time.deltaTime / FlashSeconds;
                if (_flash >= 1f)
                {
                    _flash = -1f;
                    Apply();
                    return;
                }
                float t = _flash;
                transform.localScale = new Vector3(1f + .14f * t, 1f, 1f + .14f * t);
                alpha = 1f - t * t;
            }
            else alpha = .78f + .22f * Mathf.Sin(_age * Mathf.PI * 2f * PulseHz);
            var material = FrameRenderer.sharedMaterial;
            if (material == null) return;
            Color tint = material.GetColor(BaseColor);
            tint.a *= alpha;
            FrameRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, tint);
            FrameRenderer.SetPropertyBlock(_block);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
