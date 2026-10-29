using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    public enum FacingAimShapeKind : byte { Lane, Cone, Stream, Laser }

    /// <summary>
    /// The floor footprint of a facing-aim move, read once from its cooked capability.
    /// Distances are metres along the aim from the fighter's ground point.
    /// </summary>
    public readonly struct FacingAimShape
    {
        public static readonly FacingAimShape Lane = new(FacingAimShapeKind.Lane, 4f, 0.5f);

        public readonly FacingAimShapeKind Kind;
        /// <summary>Cone: far end at reach scale 1. Stream, Laser: how far the shot travels. Lane: far end.</summary>
        public readonly float Reach;
        /// <summary>Cone: far radius at reach scale 1. Stream: shot radius at the far end. Lane: half width.</summary>
        public readonly float EndRadius;
        /// <summary>Cone: reach scale of every charge tier, ascending.</summary>
        public readonly IReadOnlyList<float> TierReachScales;
        private readonly float[]? _reachScaleByChargeTick;
        private readonly ushort _fullChargeTicks;

        private FacingAimShape(FacingAimShapeKind kind, float reach, float endRadius,
            float[]? reachScaleByChargeTick = null, float[]? tierReachScales = null, ushort fullChargeTicks = 0)
        {
            Kind = kind;
            Reach = reach;
            EndRadius = endRadius;
            _reachScaleByChargeTick = reachScaleByChargeTick;
            TierReachScales = tierReachScales ?? Array.Empty<float>();
            _fullChargeTicks = fullChargeTicks;
        }

        public float ReachScaleAt(ushort chargeTicks) => _reachScaleByChargeTick is { Length: > 0 } byTick
            ? byTick[Math.Min(chargeTicks, byTick.Length - 1)] : 1f;

        public float FullReachScale => _reachScaleByChargeTick is { Length: > 0 } byTick ? byTick[byTick.Length - 1] : 1f;

        /// <summary>Stream: how full the pump is, 0 after a tap and 1 at full charge.</summary>
        public float ChargeAt(ushort chargeTicks)
            => _fullChargeTicks == 0 ? 1f : Math.Min(chargeTicks, _fullChargeTicks) / (float)_fullChargeTicks;

        private static readonly Dictionary<Type, Func<CookedCapabilityParameters, FacingAimShape>> ByCapability = new()
        {
            [typeof(CookedChargedStreamCapabilityParameters)] = parameters =>
            {
                var stream = (CookedChargedStreamCapabilityParameters)parameters;
                return new FacingAimShape(FacingAimShapeKind.Stream,
                    stream.Projectile.LaunchOffsetZ + Travel(stream.Projectile), stream.Projectile.Radius,
                    fullChargeTicks: stream.MaxChargeTicks);
            },
            [typeof(CookedFingerGunsCapabilityParameters)] = parameters =>
            {
                var guns = (CookedFingerGunsCapabilityParameters)parameters;
                return new FacingAimShape(FacingAimShapeKind.Laser, Travel(guns.Projectile), guns.Projectile.Radius);
            },
        };

        public static FacingAimShape For(CookedSlotDefinition slot)
        {
            foreach (var stage in slot.Timeline.Stages)
            foreach (var operation in stage.Operations)
                if (operation is CookedStartCapabilityOperation start
                    && ByCapability.TryGetValue(start.Parameters.GetType(), out var make))
                    return make(start.Parameters);
            return Lane;
        }

        private static float Travel(CookedProjectile projectile)
            => projectile.Speed * projectile.MaxFlightTicks * SlopArena.Shared.Simulation.TickDt;


    }

    /// <summary>
    /// Floor marker for facing-aim moves: the move's real hit area in its own colours, toon style
    /// (ink rim under a coloured outline, translucent fill). One mesh in local space (+Z = aim),
    /// rebuilt on every <see cref="Show"/>; within it later layers draw over earlier ones.
    /// </summary>
    public sealed class FacingAimMarker : MonoBehaviour
    {
        // The drawn floor sits a few centimetres above the sampled height (0.03 m was buried);
        // the fighter ring uses 0.06 m for the same reason.
        private const float FloorLift = 0.07f;
        private const float InkWidth = 0.09f;
        private const float OutlineWidth = 0.045f;

        private const float ConeNear = 0.45f;
        private const float ConeNearRadius = 0.12f;
        private const float StreamNear = 0.5f;
        private const float StreamNearRadius = 0.18f;
        private const float StreamWaveRadiansPerMetre = 9f;
        private const float StreamWaveRadiansPerSecond = 14f;
        private const float LaserNear = 0.6f;
        private const float LaserReticleDistance = 4.5f;
        private const float LaneNear = 0.55f;

        private static readonly Color32 Ink = new(27, 24, 34, 255);
        private static readonly Color32 White = new(255, 255, 255, 255);
        private static readonly Color32 Orange = new(255, 106, 26, 255);
        private static readonly Color32 Hot = new(255, 226, 120, 255);
        private static readonly Color32 Cyan = new(62, 200, 255, 255);
        private static readonly Color32 Red = new(255, 59, 48, 255);
        private static readonly Color32 Yellow = new(255, 207, 51, 255);

        private readonly List<Vector3> _vertices = new();
        private readonly List<Color32> _colors = new();
        private readonly List<int> _triangles = new();
        private readonly List<Vector2> _shape = new();
        private readonly List<Vector2> _scratch = new();
        private Mesh _mesh;
        private Material _material;
        private MeshRenderer _renderer;

        private void Awake()
        {
            _mesh = new Mesh { name = nameof(FacingAimMarker) };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(Shader.Find("SLOPArena/Facing Aim Marker"));
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }

        public void Show(Vector3 origin, float yawRad, in FacingAimShape shape, ushort chargeTicks)
        {
            float floorY = PresentationFloor.TrySample(origin.x, origin.z, out float floor) ? floor : origin.y;
            transform.SetPositionAndRotation(new Vector3(origin.x, floorY + FloorLift, origin.z),
                Quaternion.Euler(0f, yawRad * Mathf.Rad2Deg, 0f));
            // AimHandler can sit on a scaled fighter root, and the footprint is in world metres.
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
            switch (shape.Kind)
            {
                case FacingAimShapeKind.Cone: BuildCone(shape, chargeTicks); break;
                case FacingAimShapeKind.Stream: BuildStream(shape, chargeTicks); break;
                case FacingAimShapeKind.Laser: BuildLaser(shape); break;
                default: BuildLane(shape); break;
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _renderer.enabled = true;
        }

        public void Clear()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        private void BuildCone(in FacingAimShape shape, ushort chargeTicks)
        {
            float scale = shape.ReachScaleAt(chargeTicks);
            float fullScale = shape.FullReachScale;
            float end = Mathf.Max(shape.Reach * scale, ConeNear + 0.1f);
            float radius = Mathf.Max(shape.EndRadius * scale, ConeNearRadius);

            if (scale < fullScale - 0.001f)
            {
                Capsule(_scratch, ConeNear, Mathf.Max(shape.Reach * fullScale, ConeNear + 0.1f),
                    ConeNearRadius, Mathf.Max(shape.EndRadius * fullScale, ConeNearRadius));
                AddStroke(_scratch, true, 0.03f, Alpha(Orange, 70));
            }
            Capsule(_shape, ConeNear, end, ConeNearRadius, radius);
            AddFill(_shape, Alpha(Orange, 120));
            Capsule(_scratch, ConeNear + 0.05f, end * 0.92f, 0.05f, radius * 0.55f);
            AddFill(_scratch, Alpha(Hot, 150));

            for (int i = 0; i < shape.TierReachScales.Count; i++)
            {
                float tierScale = shape.TierReachScales[i];
                if (tierScale >= scale - 0.001f) continue;
                float bandDistance = shape.Reach * tierScale;
                float halfWidth = 0.92f * Mathf.Lerp(ConeNearRadius, radius,
                    Mathf.InverseLerp(ConeNear, end, bandDistance));
                Segment(_scratch, new Vector2(-halfWidth, bandDistance), new Vector2(halfWidth, bandDistance));
                AddStroke(_scratch, false, 0.047f, Orange);
                AddStroke(_scratch, false, 0.02f, Hot);
            }

            AddStroke(_shape, true, InkWidth, Alpha(Ink, 210));
            AddStroke(_shape, true, OutlineWidth, Orange);
        }

        private void BuildStream(in FacingAimShape shape, ushort chargeTicks)
        {
            float end = Mathf.Max(shape.Reach, StreamNear + 0.1f);
            float radius = Mathf.Max(shape.EndRadius, 0.05f);
            Capsule(_shape, StreamNear, end, StreamNearRadius, radius);
            AddFill(_shape, Fade(Cyan, Mathf.Lerp(0.3f, 0.5f, shape.ChargeAt(chargeTicks))));
            AddStroke(_shape, true, InkWidth, Alpha(Ink, 200));
            AddStroke(_shape, true, OutlineWidth, Cyan);

            // Subtracting time moves the crests away from the fighter, like the water.
            _scratch.Clear();
            float phase = Time.time * StreamWaveRadiansPerSecond;
            for (float f = StreamNear + 0.1f; f <= end - 0.1f; f += 0.1f)
            {
                float amplitude = Mathf.Min(0.12f, 0.6f * Mathf.Lerp(StreamNearRadius, radius, Mathf.InverseLerp(StreamNear, end, f)));
                _scratch.Add(new Vector2(amplitude * Mathf.Sin(f * StreamWaveRadiansPerMetre - phase), f));
            }
            AddStroke(_scratch, false, 0.036f, Alpha(White, 230));

            for (int i = 0; i < 4; i++)
            {
                float f = end * (0.27f + 0.167f * i);
                float side = 0.7f * Mathf.Lerp(StreamNearRadius, radius, Mathf.InverseLerp(StreamNear, end, f));
                Circle(_scratch, new Vector2(i % 2 == 0 ? side : -side, f), 0.07f, 12);
                AddFill(_scratch, Alpha(White, 230));
            }

            Circle(_scratch, new Vector2(0f, end + 0.2f), 0.45f, 40);
            AddStroke(_scratch, true, OutlineWidth, Alpha(Cyan, 200));
        }

        private void BuildLaser(in FacingAimShape shape)
        {
            float end = Mathf.Max(shape.Reach, LaserNear + 0.1f);
            AddFadingLine(LaserNear, end, InkWidth, Ink, 0.8f);
            AddFadingLine(LaserNear, end, 0.05f, Red, 1f);

            var reticle = new Vector2(0f, Mathf.Min(LaserReticleDistance, end));
            Circle(_scratch, reticle, 0.22f, 24);
            AddStroke(_scratch, true, InkWidth, Alpha(Ink, 220));
            AddStroke(_scratch, true, OutlineWidth, Red);
            Circle(_scratch, reticle, 0.06f, 12);
            AddFill(_scratch, Red);
        }

        private void BuildLane(in FacingAimShape shape)
        {
            Capsule(_shape, LaneNear, shape.Reach, shape.EndRadius, shape.EndRadius);
            AddFill(_shape, Alpha(White, 95));
            AddStroke(_shape, true, InkWidth, Alpha(Ink, 200));
            AddStroke(_shape, true, OutlineWidth, Yellow);

            float f = shape.Reach + shape.EndRadius * 0.05f;
            AddArrowhead(new Vector2(0f, f + 0.5f), new Vector2(0f, f + 0.18f), new Vector2(0.55f, f), Alpha(Ink, 220));
            AddArrowhead(new Vector2(0f, f + 0.4f), new Vector2(0f, f + 0.18f), new Vector2(0.38f, f + 0.06f), Yellow);
        }

        /// <summary>A tapered capsule from <paramref name="near"/> to a round cap centred at <paramref name="far"/>.</summary>
        private static void Capsule(List<Vector2> into, float near, float far, float nearRadius, float farRadius)
        {
            const int capSteps = 24;
            into.Clear();
            into.Add(new Vector2(-nearRadius, near));
            into.Add(new Vector2(-farRadius, far));
            for (int i = 1; i < capSteps; i++)
            {
                float angle = Mathf.PI * (i / (float)capSteps - 0.5f);
                into.Add(new Vector2(farRadius * Mathf.Sin(angle), far + farRadius * Mathf.Cos(angle)));
            }
            into.Add(new Vector2(farRadius, far));
            into.Add(new Vector2(nearRadius, near));
        }

        private static void Circle(List<Vector2> into, Vector2 centre, float radius, int steps)
        {
            into.Clear();
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                into.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        private static void Segment(List<Vector2> into, Vector2 from, Vector2 to)
        {
            into.Clear();
            into.Add(from);
            into.Add(to);
        }

        /// <summary>Fan from the centroid: every shape filled here is convex.</summary>
        private void AddFill(List<Vector2> points, Color32 color)
        {
            Vector2 centre = Vector2.zero;
            for (int i = 0; i < points.Count; i++) centre += points[i];
            centre /= points.Count;
            int hub = _vertices.Count;
            AddVertex(centre, color);
            for (int i = 0; i < points.Count; i++) AddVertex(points[i], color);
            for (int i = 0; i < points.Count; i++)
                AddTriangle(hub, hub + 1 + i, hub + 1 + (i + 1) % points.Count);
        }

        /// <summary>A ribbon with mitred joins, so overlapping translucent quads never double up at corners.</summary>
        private void AddStroke(List<Vector2> points, bool closed, float width, Color32 color)
        {
            int count = points.Count;
            if (count < 2) return;
            int first = _vertices.Count;
            float half = width * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector2 incoming = i > 0 || closed ? Direction(points[(i - 1 + count) % count], points[i]) : Vector2.zero;
                Vector2 outgoing = i < count - 1 || closed ? Direction(points[i], points[(i + 1) % count]) : Vector2.zero;
                if (incoming == Vector2.zero) incoming = outgoing;
                if (outgoing == Vector2.zero) outgoing = incoming;
                Vector2 inNormal = new(-incoming.y, incoming.x);
                Vector2 normal = new Vector2(-(incoming.y + outgoing.y), incoming.x + outgoing.x);
                normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : inNormal;
                float miter = 1f / Mathf.Max(Vector2.Dot(normal, inNormal), 0.5f);
                Vector2 offset = normal * (half * miter);
                AddVertex(points[i] - offset, color);
                AddVertex(points[i] + offset, color);
            }
            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 2;
                int b = first + (i + 1) % count * 2;
                AddTriangle(a, a + 1, b + 1);
                AddTriangle(a, b + 1, b);
            }
        }

        /// <summary>A straight line along the aim whose alpha fades to nothing at the far end.</summary>
        private void AddFadingLine(float near, float far, float width, Color32 color, float alphaScale)
        {
            const int steps = 30;
            int first = _vertices.Count;
            float half = width * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float f = Mathf.Lerp(near, far, t);
                var faded = Fade(color, alphaScale * Mathf.Pow(1f - t, 0.6f));
                AddVertex(new Vector2(-half, f), faded);
                AddVertex(new Vector2(half, f), faded);
            }
            for (int i = 0; i < steps; i++)
            {
                int a = first + i * 2;
                AddTriangle(a, a + 1, a + 3);
                AddTriangle(a, a + 3, a + 2);
            }
        }

        /// <summary>A chevron mirrored across the aim line: tip, inner notch and the right wing.</summary>
        private void AddArrowhead(Vector2 tip, Vector2 notch, Vector2 rightWing, Color32 color)
        {
            int first = _vertices.Count;
            AddVertex(tip, color);
            AddVertex(notch, color);
            AddVertex(rightWing, color);
            AddVertex(new Vector2(-rightWing.x, rightWing.y), color);
            AddTriangle(first, first + 2, first + 1);
            AddTriangle(first, first + 1, first + 3);
        }

        private static Vector2 Direction(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            return length > 1e-5f ? delta / length : Vector2.zero;
        }

        private void AddVertex(Vector2 point, Color32 color)
        {
            _vertices.Add(new Vector3(point.x, 0f, point.y));
            _colors.Add(color);
        }

        private void AddTriangle(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        private static Color32 Alpha(Color32 color, byte alpha) => new(color.r, color.g, color.b, alpha);

        private static Color32 Fade(Color32 color, float alpha)
            => new(color.r, color.g, color.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
    }
}
