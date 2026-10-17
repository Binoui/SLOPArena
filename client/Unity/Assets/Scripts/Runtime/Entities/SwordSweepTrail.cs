using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Entities
{
    /// <summary>Cosmetic, world-fixed blade ribbons and particle accents. Never feeds simulation.</summary>
    internal sealed class SwordSweepTrail
    {
        private const int MaxSamples = 64;
        private const int SegmentsPerPose = 4;
        private const int MaxRenderSamples = MaxSamples * SegmentsPerPose;
        private const float MinMovementSq = 0.025f * 0.025f;
        private const int DotsPerSample = 5;
        private const float BackgroundOffset = 0.003f;

        private readonly struct Sample
        {
            public readonly Vector3 Hilt;
            public readonly Vector3 Tip;
            public readonly float Time;
            public readonly float U;
            public readonly bool BreakBefore;

            public readonly float WindowTime;
            public Sample(float time, Vector3 hilt, Vector3 tip, float u, bool breakBefore, float windowTime)
            {
                Time = time;
                Hilt = hilt;
                Tip = tip;
                U = u;
                BreakBefore = breakBefore;
                WindowTime = windowTime;
            }
        }

        private sealed class Ribbon
        {
            public readonly Mesh Mesh;
            public readonly Color StartTint;
            public readonly ParticleSystem.MinMaxGradient ColorOverLifetime;
            public readonly ParticleSystem.MinMaxCurve Dissolve;
            public readonly ParticleSystem.MinMaxCurve DissolveWidth;
            public readonly ParticleSystem.MinMaxCurve DissolveNoise;
            public readonly float DissolveStart;
            public readonly float DissolveEnd;
            public float InnerBladeFraction;
            public readonly float StartDelay;
            public readonly List<Vector3> Vertices = new(MaxRenderSamples * 2);
            public readonly List<Color32> Colors = new(MaxRenderSamples * 2);
            public readonly List<Vector2> Uvs = new(MaxRenderSamples * 2);
            public readonly List<Vector4> CustomData = new(MaxRenderSamples * 2);
            public readonly List<int> Triangles = new((MaxRenderSamples - 1) * 6);

            public Ribbon(ParticleSystem source, Material material, Transform parent,
                string name, float bladeWidth, float startDelay = 0f)
            {
                var main = source.main;
                StartDelay = startDelay;
                StartTint = main.startColor.Evaluate(0.5f);
                var color = source.colorOverLifetime;
                ColorOverLifetime = color.enabled
                    ? color.color
                    : new ParticleSystem.MinMaxGradient(Color.white);
                ReadCustomData(source, out var dissolve, out var width, out var noise);
                Dissolve = dissolve;
                DissolveWidth = width;
                DissolveNoise = noise;
                DissolveStart = dissolve.Evaluate(0f);
                DissolveEnd = dissolve.Evaluate(1f);
                InnerBladeFraction = 1f - Mathf.Clamp(bladeWidth, 0.01f, 1f);
                Mesh = new Mesh { name = name };
                Mesh.MarkDynamic();
                // Edit-mode previews (Ability Lab) must survive a scene switch's unused-asset unload.
                if (!Application.isPlaying) Mesh.hideFlags = HideFlags.DontSave;
                var child = new GameObject(name);
                child.transform.SetParent(parent, false);
                child.AddComponent<MeshFilter>().sharedMesh = Mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            private static void ReadCustomData(ParticleSystem source,
                out ParticleSystem.MinMaxCurve dissolve,
                out ParticleSystem.MinMaxCurve width,
                out ParticleSystem.MinMaxCurve noise)
            {
                var custom = source.customData;
                bool valid = custom.enabled
                    && custom.GetMode(ParticleSystemCustomData.Custom1) == ParticleSystemCustomDataMode.Vector
                    && custom.GetVectorComponentCount(ParticleSystemCustomData.Custom1) >= 3;
                dissolve = valid ? custom.GetVector(ParticleSystemCustomData.Custom1, 0)
                    : new ParticleSystem.MinMaxCurve(0f);
                width = valid ? custom.GetVector(ParticleSystemCustomData.Custom1, 1)
                    : new ParticleSystem.MinMaxCurve(0f);
                noise = valid ? custom.GetVector(ParticleSystemCustomData.Custom1, 2)
                    : new ParticleSystem.MinMaxCurve(0f);
            }
        }

        private sealed class Accent
        {
            public readonly ParticleSystem System;
            public readonly bool IsLine;
            public readonly float StartDelay;
            public readonly float Duration;
            public readonly float Speed;
            public readonly ParticleSystem.MinMaxCurve Rate;
            public float EmissionRemainder;

            public Accent(ParticleSystem source, Transform parent, bool isLine)
            {
                System = Object.Instantiate(source.gameObject, parent, false).GetComponent<ParticleSystem>();
                System.name = isLine ? source.name : "TECH blade sweep dots";
                if (!Application.isPlaying) System.gameObject.hideFlags = HideFlags.DontSave;
                IsLine = isLine;
                var main = System.main;
                StartDelay = main.startDelay.constant;
                Duration = Mathf.Max(0.02f, main.duration);
                Rate = System.emission.rateOverTime;
                var shape = System.shape;
                var velocity = System.velocityOverLifetime;
                Speed = Mathf.Abs(main.startSpeed.Evaluate(0.5f))
                    + Mathf.Abs(velocity.orbitalZ.Evaluate(0.5f)) * shape.radius;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Local;
                main.maxParticles = Mathf.Min(main.maxParticles, 256);
                main.startDelay = 0f;
                shape.enabled = false;
                var emission = System.emission;
                emission.enabled = false;
                if (isLine)
                {
                    // Replace the vendor's circular orbit with blade-following world velocity.
                    velocity.orbitalX = 0f;
                    velocity.orbitalY = 0f;
                    velocity.orbitalZ = 0f;
                }
                Reset();
            }

            public void Reset()
            {
                EmissionRemainder = 0f;
                System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                System.Play(true);
                System.Pause(true);
            }
        }

        private readonly Transform _visualTransform;
        private readonly GameObject _visual;
        private readonly Ribbon _root;
        private readonly Ribbon _background;
        private readonly List<Accent> _accents = new(3);
        private readonly float _lifetime;
        private readonly List<Sample> _samples = new(MaxSamples);
        private readonly List<Sample> _renderSamples = new(MaxRenderSamples);
        private bool _geometryDirty;
        private bool _active;
        private bool _breakNextSample;
        private bool _hasDrawTime;
        private float _segmentOrigin;
        private float _lastDrawTime;
        private readonly float _uvDuration;
        public float VisibleLifetime { get; }
        public GameObject StylePrefab { get; }

        public SwordSweepTrail(Transform owner, GameObject stylePrefab, float lifetime, float bladeWidth)
        {
            StylePrefab = stylePrefab;
            _lifetime = Mathf.Max(0.02f, lifetime);
            VisibleLifetime = _lifetime;
            _visual = new GameObject("TECH blade sweep");
            // A hidden attack-only weapon must not hide its still-fading tail.
            _visual.transform.SetParent(owner, false);
            if (!Application.isPlaying) _visual.hideFlags = HideFlags.DontSave;
            _visualTransform = _visual.transform;

            var root = stylePrefab.GetComponent<ParticleSystem>();
            if (root == null) root = stylePrefab.GetComponentInChildren<ParticleSystem>(true);
            if (root == null) throw new System.ArgumentException("Trail style has no ParticleSystem.", nameof(stylePrefab));
            var rootRenderer = root.GetComponent<ParticleSystemRenderer>();
            if (rootRenderer == null || rootRenderer.sharedMaterial == null)
                throw new System.ArgumentException("Trail style has no particle material.", nameof(stylePrefab));
            _root = new Ribbon(root, rootRenderer.sharedMaterial, _visualTransform,
                "TECH blade sweep root", bladeWidth);
            // A short sweep should show more than a sliver of the authored artwork.
            // Keep this scale fixed; never refit UVs to the remaining history.
            _uvDuration = Mathf.Max(_lifetime, root.main.duration * 0.5f);

            var backgroundTransform = stylePrefab.transform.Find("Background slash")
                ?? stylePrefab.transform.Find("Background trail");
            var background = backgroundTransform != null
                ? backgroundTransform.GetComponent<ParticleSystem>()
                : null;
            var backgroundRenderer = background != null
                ? background.GetComponent<ParticleSystemRenderer>()
                : null;
            _background = backgroundRenderer != null && backgroundRenderer.sharedMaterial != null
                ? new Ribbon(background, backgroundRenderer.sharedMaterial, _visualTransform,
                    "TECH blade sweep background", bladeWidth,
                    backgroundTransform.name == "Background trail" ? background.main.startDelay.constant : 0f)
                : null;

            AddAccent(stylePrefab, "Dots", false);
            AddAccent(stylePrefab, "Lines", true);
            AddAccent(stylePrefab, "Lines black", true);
            foreach (var accent in _accents)
                VisibleLifetime = Mathf.Max(VisibleLifetime, accent.System.main.startLifetime.constantMax);
        }

        private void AddAccent(GameObject stylePrefab, string name, bool isLine)
        {
            var child = stylePrefab.transform.Find(name);
            var source = child != null ? child.GetComponent<ParticleSystem>() : null;
            if (source != null) _accents.Add(new Accent(source, _visualTransform, isLine));
        }

        public void SetBladeWidth(float width)
        {
            float inner = 1f - Mathf.Clamp(width, 0.01f, 1f);
            _root.InnerBladeFraction = inner;
            if (_background != null) _background.InnerBladeFraction = inner;
        }

        public void SetActive(bool active)
        {
            if (active && !_active && _samples.Count > 0)
                _breakNextSample = true;
            _active = active;
        }

        public void Clear()
        {
            _samples.Clear();
            _renderSamples.Clear();
            _geometryDirty = false;
            _root.Mesh.Clear();
            _background?.Mesh.Clear();
            foreach (var accent in _accents) accent.Reset();
            _breakNextSample = false;
            _hasDrawTime = false;
        }

        public void SampleAt(float time, Vector3 hilt, Vector3 tip)
        {
            if (!_active) return;
            if (_samples.Count > 0 && time < _samples[_samples.Count - 1].Time)
                Clear();
            ExpireSamples(time);
            if (_samples.Count > 0 && time <= _samples[_samples.Count - 1].Time) return;
            bool hasMotion = _samples.Count > 0 && !_breakNextSample;
            if (hasMotion
                && (hilt - _samples[_samples.Count - 1].Hilt).sqrMagnitude < MinMovementSq
                && (tip - _samples[_samples.Count - 1].Tip).sqrMagnitude < MinMovementSq)
                return;
            if (_samples.Count == 0 || _breakNextSample)
            {
                _segmentOrigin = time;
                foreach (var accent in _accents) accent.EmissionRemainder = 0f;
            }
            bool breakBefore = _breakNextSample;
            _breakNextSample = false;
            if (_samples.Count == MaxSamples) _samples.RemoveAt(0);
            // Keep the mask's black U borders outside the visible sweep. Coordinates
            // belong to each sample, never to its changing index in the history.
            float u = Mathf.Lerp(0.1f, 0.9f, Mathf.Clamp01((time - _segmentOrigin) / _uvDuration));
            _samples.Add(new Sample(time, hilt, tip, u, breakBefore, time - _segmentOrigin));
            _geometryDirty = true;

            if (hasMotion && _accents.Count > 0)
            {
                AdvanceParticles(time);
                var prior = _samples[_samples.Count - 2];
                float windowTime = time - _segmentOrigin;
                foreach (var accent in _accents)
                {
                    int count = DotsPerSample;
                    if (accent.IsLine)
                    {
                        float elapsed = windowTime - accent.StartDelay;
                        if (elapsed <= 0f || elapsed > accent.Duration) continue;
                        float delta = Mathf.Min(time - prior.Time, elapsed);
                        accent.EmissionRemainder += Mathf.Max(0f, accent.Rate.Evaluate(
                            Mathf.Clamp01((elapsed - delta * 0.5f) / accent.Duration))) * delta;
                        count = Mathf.Min(256, Mathf.FloorToInt(accent.EmissionRemainder));
                        accent.EmissionRemainder -= count;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        float t = Mathf.Lerp(_root.InnerBladeFraction, 1f, (i + 0.5f) / count);
                        if (!accent.IsLine) t = Mathf.Lerp(_root.InnerBladeFraction, 1f, (float)i / (count - 1));
                        var emit = new ParticleSystem.EmitParams { position = Vector3.Lerp(hilt, tip, t) };
                        if (accent.IsLine)
                            emit.velocity = -(tip - prior.Tip).normalized * accent.Speed;
                        accent.System.Emit(emit, 1);
                    }
                }
            }
        }

        public void DrawAt(float time)
        {
            ExpireSamples(time);
            BuildRenderSamples();
            Draw(_root, time, 0f);
            if (_background != null) Draw(_background, time, BackgroundOffset);

            AdvanceParticles(time);
        }

        private void AdvanceParticles(float time)
        {
            if (_accents.Count == 0) return;
            if (_hasDrawTime && time > _lastDrawTime)
                foreach (var accent in _accents)
                {
                    accent.System.Simulate(time - _lastDrawTime, true, false, false);
                    accent.System.Pause(true);
                }
            if (!_hasDrawTime || time > _lastDrawTime)
                _lastDrawTime = time;
            _hasDrawTime = true;
        }

        private void ExpireSamples(float time)
        {
            if (_samples.Count == 0) return;
            if (time - _samples[_samples.Count - 1].Time >= _lifetime)
            {
                _samples.Clear();
                _geometryDirty = true;
                return;
            }
            // Retain one old control pose so still-live interpolated sections
            // keep their world positions and UVs when the oldest pose expires.
            while (_samples.Count > 1 && time - _samples[1].Time >= _lifetime)
            {
                _samples.RemoveAt(0);
                _geometryDirty = true;
            }
        }

        private void BuildRenderSamples()
        {
            if (!_geometryDirty) return;
            _geometryDirty = false;
            _renderSamples.Clear();
            for (int i = 0; i < _samples.Count; i++)
            {
                var sample = _samples[i];
                if (i == 0 || sample.BreakBefore)
                {
                    _renderSamples.Add(sample);
                    continue;
                }
                var prior = _samples[i - 1];
                for (int step = 1; step < SegmentsPerPose; step++)
                {
                    float t = (float)step / SegmentsPerPose;
                    Vector3 hilt = Vector3.Lerp(prior.Hilt, sample.Hilt, t);
                    // A blade rotates around its base: interpolating orientation,
                    // rather than tip position, produces a curved, rigid sweep.
                    Vector3 blade = Vector3.Slerp(prior.Tip - prior.Hilt,
                        sample.Tip - sample.Hilt, t);
                    _renderSamples.Add(new Sample(Mathf.Lerp(prior.Time, sample.Time, t),
                        hilt, hilt + blade, Mathf.Lerp(prior.U, sample.U, t), false,
                        Mathf.Lerp(prior.WindowTime, sample.WindowTime, t)));
                }
                _renderSamples.Add(sample);
            }
        }

        private void Draw(Ribbon ribbon, float now, float normalOffset)
        {
            if (_renderSamples.Count < 2)
            {
                if (ribbon.Mesh.vertexCount != 0) ribbon.Mesh.Clear();
                return;
            }

            var vertices = ribbon.Vertices;
            var colors = ribbon.Colors;
            var uvs = ribbon.Uvs;
            var customData = ribbon.CustomData;
            var triangles = ribbon.Triangles;
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            customData.Clear();
            triangles.Clear();
            Matrix4x4 worldToLocal = _visualTransform.worldToLocalMatrix;

            for (int i = 0; i < _renderSamples.Count; i++)
            {
                var sample = _renderSamples[i];
                Vector3 offset = Vector3.zero;
                if (normalOffset != 0f)
                {
                    Vector3 sweep = i + 1 < _renderSamples.Count && !_renderSamples[i + 1].BreakBefore
                        ? _renderSamples[i + 1].Tip - sample.Tip
                        : i > 0 && !sample.BreakBefore
                            ? sample.Tip - _renderSamples[i - 1].Tip
                            : sample.Tip - sample.Hilt;
                    Vector3 normal = Vector3.Cross(sample.Tip - sample.Hilt, sweep).normalized;
                    if (normal.sqrMagnitude < 0.5f)
                        normal = Vector3.Cross(sample.Tip - sample.Hilt, Vector3.right).normalized;
                    offset = normal * normalOffset;
                }
                Vector3 inner = Vector3.Lerp(sample.Hilt, sample.Tip, ribbon.InnerBladeFraction);
                vertices.Add(worldToLocal.MultiplyPoint3x4(inner + offset));
                vertices.Add(worldToLocal.MultiplyPoint3x4(sample.Tip + offset));

                float age = Mathf.Clamp01((now - sample.Time) / _lifetime);
                Color tint = ribbon.StartTint * ribbon.ColorOverLifetime.Evaluate(age);
                if (sample.WindowTime < ribbon.StartDelay) tint.a = 0f;
                colors.Add(tint);
                colors.Add(tint);
                // Stable per-sample U stays finite; source V is its 0..0.5 radial band.
                uvs.Add(new Vector2(sample.U, 0f));
                uvs.Add(new Vector2(sample.U, 0.5f));
                // The prefab reveals an entire authored arc along U. A sweep instead
                // ages each section: compensate 1-U and reuse the source dissolve's
                // progression so fresh sections remain visible anywhere on the arc.
                float width = ribbon.DissolveWidth.Evaluate(age);
                float progress = Mathf.InverseLerp(ribbon.DissolveEnd,
                    ribbon.DissolveStart, ribbon.Dissolve.Evaluate(age));
                var dissolve = new Vector4(1f - sample.U + Mathf.Lerp(-0.2f, width * 0.5f, progress),
                    width, ribbon.DissolveNoise.Evaluate(age), 0f);
                customData.Add(dissolve);
                customData.Add(dissolve);
                if (i == 0 || sample.BreakBefore) continue;
                int prior = (i - 1) * 2;
                int current = i * 2;
                // The source TECH material is Cull Off; one face pair avoids alpha doubling.
                triangles.Add(prior); triangles.Add(prior + 1); triangles.Add(current);
                triangles.Add(prior + 1); triangles.Add(current + 1); triangles.Add(current);
            }

            ribbon.Mesh.Clear();
            ribbon.Mesh.SetVertices(vertices);
            ribbon.Mesh.SetColors(colors);
            ribbon.Mesh.SetUVs(0, uvs);
            ribbon.Mesh.SetUVs(1, customData);
            ribbon.Mesh.SetTriangles(triangles, 0, false);
            ribbon.Mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            if (Application.isPlaying)
            {
                Object.Destroy(_visual);
                Object.Destroy(_root.Mesh);
                if (_background != null) Object.Destroy(_background.Mesh);
            }
            else
            {
                Object.DestroyImmediate(_visual);
                Object.DestroyImmediate(_root.Mesh);
                if (_background != null) Object.DestroyImmediate(_background.Mesh);
            }
        }
    }
}
