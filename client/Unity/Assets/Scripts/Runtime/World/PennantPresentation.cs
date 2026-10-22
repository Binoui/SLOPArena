using SlopArena.Client.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    /// <summary>
    /// A cloth pennant tied near the tip of its owner's weapon: a short chain of points that hangs
    /// under gravity, streams back through the air while the weapon moves and swings over when it
    /// stops. Sir Bonk ties one under his lance for the Joust. It draws as a ribbon turned toward each
    /// camera just before that camera renders, so it reads from behind as well as from the side,
    /// with the texture's top (V = 1) kept up so the emblem on it never flips. Put it on a cue
    /// attached to <c>_weapon_tip</c>; the pennant hangs from <see cref="HoistBack"/> metres toward the
    /// hilt, and its texture runs from the hoist (U = 0) to the fly end. Cosmetic only.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(1000)]
    public sealed class PennantPresentation : MonoBehaviour, IPresentationOwnerReceiver
    {
        public float Length = 0.6f;
        public float Width = 0.26f;
        public int Segments = 6;
        [Tooltip("Metres from the weapon's tip back toward its hilt where the pennant is tied.")]
        public float HoistBack = 0.12f;
        [Tooltip("Air drag per metre, growing with the square of speed as cloth's does: it streams straight back at a gallop " +
                 "without whipping over the top, yet falls freely when the weapon stops.")]
        public float AirDrag = 3.5f;
        [Tooltip("Gentle damping per second that settles the last swings.")]
        public float Damping = 2f;
        [Tooltip("Downward pull in m/s², gentler than real gravity: it is cloth.")]
        public float Gravity = 6f;
        [Tooltip("Flutter at the fly end at full speed, in metres, and its rate.")]
        public float FlutterAmplitude = 0.04f;
        public float FlutterHz = 6f;
        [Tooltip("Hoist speed in m/s at which the flutter is full.")]
        public float FlutterSpeed = 6f;

        private Vector3[] _points = System.Array.Empty<Vector3>();
        private Vector3[] _previous = System.Array.Empty<Vector3>();
        private Vector3[] _drawn = System.Array.Empty<Vector3>();
        private Vector3[] _vertices = System.Array.Empty<Vector3>();
        private Mesh? _mesh;
        private Transform? _hilt;
        private float _speed;

        public void BindOwner(PlayerRenderer owner)
        {
            _hilt = owner != null ? owner.ResolvePresentationBone("_weapon_hilt") : null;
            Hang();
        }

        private void OnEnable()
        {
            BuildMesh();
            Hang();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        private Vector3 Hoist()
        {
            Vector3 tip = transform.position;
            if (_hilt == null) return tip;
            Vector3 back = _hilt.position - tip;
            float length = back.magnitude;
            return length > 1e-4f ? tip + back * (Mathf.Min(HoistBack, length) / length) : tip;
        }

        private void BuildMesh()
        {
            int count = Mathf.Max(1, Segments) + 1;
            if (_mesh != null && _points.Length == count) return;
            _points = new Vector3[count];
            _previous = new Vector3[count];
            _drawn = new Vector3[count];
            _vertices = new Vector3[count * 2];
            var uv = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)(count - 1);
                uv[2 * i] = new Vector2(u, 0f);
                uv[2 * i + 1] = new Vector2(u, 1f);
            }
            for (int s = 0; s < count - 1; s++)
            {
                int k = 2 * s;
                triangles[6 * s] = k;
                triangles[6 * s + 1] = k + 1;
                triangles[6 * s + 2] = k + 2;
                triangles[6 * s + 3] = k + 1;
                triangles[6 * s + 4] = k + 3;
                triangles[6 * s + 5] = k + 2;
            }
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Pennant" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>Starts the pennant hanging straight down from its hoist.</summary>
        private void Hang()
        {
            if (_points.Length == 0) return;
            Vector3 hoist = Hoist();
            int last = _points.Length - 1;
            for (int i = 0; i <= last; i++)
                _points[i] = _previous[i] = hoist + Vector3.down * (Length * i / last);
            _speed = 0f;
            Flutter();
        }

        private void LateUpdate()
        {
            if (_points.Length == 0) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            Vector3 hoist = Hoist();
            if ((hoist - _points[0]).sqrMagnitude > 4f)
            {
                Hang();
                return;
            }
            if (dt > 0f)
            {
                _speed = Mathf.Lerp(_speed, (hoist - _points[0]).magnitude / dt, 1f - Mathf.Exp(-12f * dt));
                _points[0] = _previous[0] = hoist;
                float damping = Mathf.Exp(-Damping * dt);
                Vector3 pull = Sag(hoist) * (Gravity * dt * dt);
                for (int i = 1; i < _points.Length; i++)
                {
                    Vector3 point = _points[i];
                    Vector3 moved = point - _previous[i];
                    // v / (1 + c|v|dt): quadratic drag in a form that stays stable at any speed.
                    Vector3 step = moved * (damping / (1f + AirDrag * moved.magnitude));
                    _previous[i] = point;
                    _points[i] = point + step + pull;
                }
                // Each point keeps its segment's length from the one before it, so the cloth trails its hoist.
                float segment = Length / (_points.Length - 1);
                for (int i = 1; i < _points.Length; i++)
                {
                    Vector3 offset = _points[i] - _points[i - 1];
                    float length = offset.magnitude;
                    _points[i] = _points[i - 1] + (length > 1e-5f ? offset / length : Vector3.down) * segment;
                }
            }
            Flutter();
        }

        /// <summary>
        /// Which way the cloth falls: down, tipped toward the hilt as a flag tied to a shaft rests against it.
        /// Straight down would let a chain left pointing straight up after a swing balance there, because
        /// keeping its links' lengths cancels a pull along the chain.
        /// </summary>
        private Vector3 Sag(Vector3 hoist)
        {
            if (_hilt == null) return Vector3.down;
            Vector3 back = _hilt.position - hoist;
            back.y = 0f;
            return back.sqrMagnitude > 1e-6f ? (Vector3.down + back.normalized * 0.3f).normalized : Vector3.down;
        }

        /// <summary>The drawn centreline: the chain plus a wave that runs to the fly end and grows with speed.</summary>
        private void Flutter()
        {
            int last = _points.Length - 1;
            Vector3 along = (_points[last] - _points[0]).normalized;
            Vector3 wave = Vector3.up - along * Vector3.Dot(Vector3.up, along);
            if (wave.sqrMagnitude < 0.04f) wave = Vector3.Cross(along, Vector3.forward);
            wave = wave.sqrMagnitude > 1e-6f ? wave.normalized : Vector3.right;
            float amount = FlutterAmplitude * Mathf.Clamp01(_speed / Mathf.Max(0.1f, FlutterSpeed));
            float phase = Time.time * FlutterHz * Mathf.PI * 2f;
            for (int i = 0; i <= last; i++)
            {
                float t = last > 0 ? i / (float)last : 0f;
                _drawn[i] = _points[i] + wave * (amount * t * Mathf.Sin(phase - t * 4.5f));
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_mesh == null || _drawn.Length < 2 || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            Transform eye = camera.transform;
            float half = Width * 0.5f;
            int last = _drawn.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                Vector3 tangent = _drawn[Mathf.Min(i + 1, last)] - _drawn[Mathf.Max(i - 1, 0)];
                Vector3 toEye = eye.position - _drawn[i];
                Vector3 side = Side(tangent, toEye, eye);
                _vertices[2 * i] = transform.InverseTransformPoint(_drawn[i] - side * half);
                _vertices[2 * i + 1] = transform.InverseTransformPoint(_drawn[i] + side * half);
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// The ribbon's width direction: facing the camera, eased toward level when the camera looks
        /// along the ribbon (where facing is undefined and would spin), with V = 1 kept up on screen.
        /// </summary>
        private static Vector3 Side(Vector3 tangent, Vector3 toEye, Transform eye)
        {
            Vector3 facing = Vector3.Cross(tangent, toEye);
            Vector3 level = Vector3.Cross(tangent, Vector3.up);
            if (level.sqrMagnitude < 1e-8f) level = eye.right;
            level.Normalize();
            float sine = facing.magnitude / Mathf.Max(1e-6f, tangent.magnitude * toEye.magnitude);
            if (facing.sqrMagnitude > 1e-10f)
            {
                facing.Normalize();
                if (Vector3.Dot(facing, level) < 0f) facing = -facing;
            }
            else facing = level;
            Vector3 side = Vector3.Lerp(level, facing, Mathf.InverseLerp(0.12f, 0.35f, sine)).normalized;
            float up = Vector3.Dot(side, eye.up);
            if (Mathf.Abs(up) < 0.15f) up = Vector3.Dot(side, eye.right);
            return up < 0f ? -side : side;
        }
    }
}
