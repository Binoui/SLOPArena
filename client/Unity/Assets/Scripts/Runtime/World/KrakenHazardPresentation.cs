using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.StageHazards;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Cosmetic only: the Splash Deck kraken (concept B, "the grumpy neighbour"). Called on every
    /// simulation tick, including ticks between rendered frames. Everything it shows is a function of
    /// the hazard view and its tick: clips are sampled by normalised phase time, never the wall clock,
    /// and it decides no hit, lane or timing.
    ///
    /// Timeline of one slam (ticks relative to the cue's ImpactTick and EndTick):
    /// rise (crossfading out of the lurk and swimming onto the variant's anchor), the low warn hold,
    /// the whip (the slam clip up to the moment the arm meets the deck, landing exactly on ImpactTick),
    /// the settle and lie until EndTick, then withdraw until the cue disappears. Between slams it
    /// loops the lurk under the water at the ambient pose pushed out to the kraken's clearance.
    ///
    /// Art comes from tools/blender/splash_deck/kraken_build.py via Resources (kraken.fbx, kraken.json
    /// and the materials in <see cref="ResourceFolder"/>). Missing art leaves an empty root.
    /// </summary>
    public sealed class KrakenHazardPresentation : IStageHazardPresenter
    {
        public const string ResourceFolder = "StageHazards/Kraken";
        // KrakenHazard's tuning is internal to Shared; these mirror it. A cue appears WarningTicks
        // before its impact and disappears RecoveryTicks after its end.
        private const int WarningTicks = 120, RecoveryTicks = 45;
        // How the warning window is shared out. The slam clip's arm meets the deck at WhipLand.
        private const int RiseTicks = 54, WhipTicks = 24, SettleTicks = 5;
        private const float WhipLand = 0.75f;
        private const int CrossfadeTicks = 14, DepartTicks = 150, TicksPerSecond = 60;
        // Lurk: the route's corners are rounded over this many metres so the heading turns gently.
        private const float LurkRound = 6f;
        private const int MaxDrops = 160;

        public Transform Root { get; }
        /// <summary>Diagnostics for the Editor capture: the current phase and Present's own cost.</summary>
        public string Phase { get; private set; } = "lurk";
        public string ActiveVariant { get; private set; } = "";
        public int Presents { get; private set; }
        public double LastPresentMilliseconds { get; private set; }
        public double MaxPresentMilliseconds { get; private set; }
        public double MeanPresentMilliseconds => Presents == 0 ? 0 : _presentTotalMs / Presents;
        public Transform Kraken => _kraken != null ? _kraken.transform : null;

        private readonly Dictionary<(uint, ushort), HazardCue> _known = new();
        private readonly HashSet<(uint, ushort, byte)> _edges = new();
        private uint _lastTick;
        private bool _started;
        private double _presentTotalMs;

        private sealed class Variant
        {
            public Vector3 Anchor;
            public float Yaw, LaneX, Side, HalfWidth;
            public int Rise, Warn, Slam, Lie, Withdraw;
        }

        private sealed class Art
        {
            public GameObject Model;
            public AnimationClip[] Clips;
            public int Lurk;
            public readonly Dictionary<string, Variant> Variants = new();
            public float WaterY, Clearance, DeckTop, SternX, BowX, MaxBeam;
            public Vector2[] Beam;
            public string HeroArm;
            public int BonesPerArm;
            public Material Body, Submerged, Shadow, Spray;
            public AudioClip Surface, Thud;
        }

        private sealed class Action
        {
            public uint Ordinal;
            public Variant Variant;
            public long Retire;
        }

        private static Art _art;
        private readonly Art _a;
        private readonly GameObject _kraken;
        private PlayableGraph _graph;
        private readonly AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _clips;
        private int _weightA = -1, _weightB = -1;
        private readonly Material _body, _submerged, _shadow, _spray;
        private readonly GameObject _shadowObject;
        private readonly Mesh _sprayMesh;
        private readonly Transform[] _tip;
        private readonly AudioSource _audio;
        private readonly Vector3[] _dropCentres = new Vector3[MaxDrops * 4];
        private readonly Vector2[] _dropSizes = new Vector2[MaxDrops * 4];
        private readonly Color32[] _dropColours = new Color32[MaxDrops * 4];
        private int _drops, _shownDrops;
        private Action _current, _previous;

        public KrakenHazardPresentation(Transform parent)
        {
            Root = StageHazardPresenters.CreateRoot(parent);
            _a = Load();
            if (_a == null) return;

            _kraken = Object.Instantiate(_a.Model, Root);
            _kraken.name = "Kraken";
            var animator = _kraken.GetComponent<Animator>();
            if (animator == null) animator = _kraken.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("Kraken hazard");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "Kraken", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, _a.Clips.Length);
            _clips = new AnimationClipPlayable[_a.Clips.Length];
            for (int i = 0; i < _clips.Length; i++)
            {
                _clips[i] = AnimationClipPlayable.Create(_graph, _a.Clips[i]);
                _clips[i].SetApplyFootIK(false);
                _graph.Connect(_clips[i], 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);
            }
            output.SetSourcePlayable(_mixer);

            _body = new Material(_a.Body);
            _body.SetFloat("_DeckY", _a.DeckTop);
            _body.SetFloat("_DeckHalfBeam", _a.MaxBeam);
            _body.SetFloat("_DeckMinX", _a.SternX);
            _body.SetFloat("_DeckMaxX", _a.BowX);
            _submerged = new Material(_a.Submerged);
            _submerged.SetFloat("_WaterY", _a.WaterY);
            foreach (var renderer in _kraken.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.sharedMaterials = new[] { _body, _submerged };
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.updateWhenOffscreen = false;
                // The arms reach ~70 m from the body and the clips move the Root by ~30 m.
                renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 220f);
            }
            var bones = new Dictionary<string, Transform>();
            foreach (var t in _kraken.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            var tip = new List<Transform>();
            for (int k = _a.BonesPerArm - 10; k < _a.BonesPerArm; k++)
                if (bones.TryGetValue($"{_a.HeroArm}_{k:00}", out var bone)) tip.Add(bone);
            _tip = tip.ToArray();

            _shadow = new Material(_a.Shadow);
            _shadowObject = new GameObject("Arm shadow");
            _shadowObject.transform.SetParent(Root, false);
            _shadowObject.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var shadowRenderer = _shadowObject.AddComponent<MeshRenderer>();
            shadowRenderer.sharedMaterial = _shadow;
            shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
            _shadowObject.SetActive(false);

            _spray = new Material(_a.Spray);
            var sprayObject = new GameObject("Spray");
            sprayObject.transform.SetParent(Root, false);
            _sprayMesh = new Mesh { name = "Kraken spray" };
            _sprayMesh.MarkDynamic();
            var corners = new Vector2[MaxDrops * 4];
            var triangles = new int[MaxDrops * 6];
            for (int i = 0; i < MaxDrops; i++)
            {
                corners[4 * i] = new Vector2(-1, -1); corners[4 * i + 1] = new Vector2(1, -1);
                corners[4 * i + 2] = new Vector2(1, 1); corners[4 * i + 3] = new Vector2(-1, 1);
                int v = 4 * i, t = 6 * i;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            _sprayMesh.vertices = _dropCentres;
            _sprayMesh.uv = corners;
            _sprayMesh.SetUVs(1, _dropSizes);
            _sprayMesh.colors32 = _dropColours;
            _sprayMesh.triangles = triangles;
            _sprayMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
            sprayObject.AddComponent<MeshFilter>().sharedMesh = _sprayMesh;
            var sprayRenderer = sprayObject.AddComponent<MeshRenderer>();
            sprayRenderer.sharedMaterial = _spray;
            sprayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sprayRenderer.receiveShadows = false;

            _audio = Root.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        private static Art Load()
        {
            if (_art != null && _art.Model != null) return _art;
            var json = Resources.Load<TextAsset>(ResourceFolder + "/kraken");
            var model = Resources.Load<GameObject>(ResourceFolder + "/kraken");
            if (json == null || model == null)
            {
                Debug.LogError($"[StageHazard] Kraken art missing under Resources/{ResourceFolder}; showing nothing.");
                return null;
            }
            var doc = JObject.Parse(json.text);
            var art = new Art
            {
                Model = model,
                WaterY = (float)doc["waterY"],
                Clearance = (float)doc["lurk"]["clearance"],
                DeckTop = (float)doc["deck"]["top"],
                SternX = (float)doc["deck"]["sternX"],
                BowX = (float)doc["deck"]["bowX"],
                HeroArm = (string)doc["rig"]["heroArm"],
                BonesPerArm = (int)doc["rig"]["bonesPerArm"],
                Body = Resources.Load<Material>(ResourceFolder + "/Kraken"),
                Submerged = Resources.Load<Material>(ResourceFolder + "/KrakenSubmerged"),
                Shadow = Resources.Load<Material>(ResourceFolder + "/KrakenArmShadow"),
                Spray = Resources.Load<Material>(ResourceFolder + "/KrakenSpray"),
                Surface = Resources.Load<AudioClip>("Audio/SFX/Bashie/bashie_splash"),
                Thud = Resources.Load<AudioClip>("Audio/SFX/Bonk/impact_heavy"),
            };
            var beam = new List<Vector2>();
            foreach (var row in doc["deck"]["halfBeam"]) beam.Add(new Vector2((float)row[0], (float)row[1]));
            art.Beam = beam.ToArray();
            foreach (var row in art.Beam) art.MaxBeam = Mathf.Max(art.MaxBeam, row.y);

            var byName = new Dictionary<string, AnimationClip>();
            foreach (var clip in Resources.LoadAll<AnimationClip>(ResourceFolder)) byName[clip.name] = clip;
            var clips = new List<AnimationClip>();
            int Index(string name)
            {
                if (!byName.TryGetValue(name, out var clip))
                    throw new InvalidOperationException($"Kraken clip '{name}' is missing; reimport kraken.fbx with kraken.json.");
                int i = clips.IndexOf(clip);
                if (i < 0) { clips.Add(clip); i = clips.Count - 1; }
                return i;
            }
            art.Lurk = Index((string)doc["lurk"]["clip"]);
            var lanes = (JObject)doc["lanes"];
            foreach (var property in ((JObject)doc["variants"]).Properties())
            {
                var v = property.Value;
                var anchor = v["anchor"]["position"];
                var lane = lanes[(string)v["lane"]];
                var names = v["clips"];
                art.Variants[property.Name] = new Variant
                {
                    Anchor = new Vector3((float)anchor[0], (float)anchor[1], (float)anchor[2]),
                    Yaw = (float)v["anchor"]["yaw"],
                    LaneX = (float)lane["center"][0],
                    HalfWidth = (float)lane["halfExtents"][0],
                    Side = (string)v["side"] == "port" ? 1f : -1f,
                    Rise = Index((string)names["rise"]), Warn = Index((string)names["warn"]), Slam = Index((string)names["slam"]),
                    Lie = Index((string)names["lie"]), Withdraw = Index((string)names["withdraw"]),
                };
            }
            art.Clips = clips.ToArray();
            return _art = art;
        }

        public void Present(StageHazardView view)
        {
            if (view == null) return;
            long started = Stopwatch.GetTimestamp();
            if (_started && view.Tick < _lastTick)
            {
                _known.Clear(); _edges.Clear(); _started = false;
                _current = _previous = null;
            }
            HazardCue lead = null;
            foreach (var cue in view.Cues)
            {
                _known[(cue.Ordinal, cue.Operation)] = cue;
                if (lead == null || cue.Operation < lead.Operation) lead = cue;
            }
            List<(uint, ushort)> stale = null;
            foreach (var pair in _known)
            {
                var cue = pair.Value;
                Edge(cue, 0, view.Tick, !_edges.Contains((cue.Ordinal, cue.Operation, (byte)0)));
                Edge(cue, 1, cue.ImpactTick, view.Tick >= cue.ImpactTick);
                Edge(cue, 2, cue.EndTick, view.Tick >= cue.EndTick);
                if ((long)cue.EndTick + RecoveryTicks + 600 < view.Tick) (stale ??= new()).Add(pair.Key);
            }
            if (stale != null)
                foreach (var key in stale)
                {
                    _known.Remove(key);
                    for (byte e = 0; e < 3; e++) _edges.Remove((key.Item1, key.Item2, e));
                }
            if (_a != null) Show(view, lead);
            _lastTick = view.Tick; _started = true;

            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            LastPresentMilliseconds = ms;
            MaxPresentMilliseconds = Math.Max(MaxPresentMilliseconds, ms);
            _presentTotalMs += ms;
            Presents++;
        }

        private void Show(StageHazardView view, HazardCue lead)
        {
            long now = view.Tick;
            Lurk(view.AmbientPose.Position, out var lurkPosition, out float lurkYaw);
            Variant v = null;
            if (lead != null && _a.Variants.TryGetValue(lead.Variant, out v))
            {
                if (_current == null || _current.Ordinal != lead.Ordinal)
                    _current = new Action { Ordinal = lead.Ordinal, Variant = v, Retire = (long)lead.EndTick + RecoveryTicks };
            }
            else if (_current != null)
            {
                _previous = _current;
                _current = null;
            }

            int a = -1, b;
            float ta = 0f, tb, wb = 1f;
            Vector3 position;
            float yaw, shadowReach = 0f, shadowStrength = 0f;
            _drops = 0;
            if (v != null)
            {
                long impact = lead.ImpactTick, end = lead.EndTick;
                long riseStart = impact - WarningTicks, warnStart = riseStart + RiseTicks, whipStart = impact - WhipTicks;
                position = v.Anchor; yaw = v.Yaw;
                ActiveVariant = lead.Variant;
                if (now < warnStart)
                {
                    Phase = "rise";
                    float t = Mathf.Clamp01((now - riseStart) / (float)RiseTicks);
                    float arrive = Smooth(Mathf.Clamp01((now - riseStart) / (RiseTicks * 0.6f)));
                    position = Vector3.Lerp(lurkPosition, v.Anchor, arrive);
                    yaw = Mathf.LerpAngle(lurkYaw, v.Yaw, arrive);
                    a = _a.Lurk; ta = LurkPhase(now);
                    b = v.Rise; tb = t; wb = Mathf.Clamp01((now - riseStart) / (float)CrossfadeTicks);
                    shadowReach = 0.15f + 0.3f * Smooth(t); shadowStrength = 0.3f * Smooth(t);
                }
                else if (now < whipStart)
                {
                    Phase = "warn";
                    float k = (now - warnStart) / (float)(whipStart - warnStart);
                    b = v.Warn; tb = Loop((now - warnStart) / LoopTicks(v.Warn));
                    shadowReach = 0.45f + 0.2f * k; shadowStrength = 0.3f + 0.15f * k;
                }
                else if (now < impact)
                {
                    Phase = "whip";
                    float k = (now - whipStart) / (float)WhipTicks;
                    b = v.Slam; tb = WhipLand * k;
                    shadowReach = 0.65f + 0.41f * Smooth(k); shadowStrength = 0.45f + 0.33f * k;
                }
                else if (now < end)
                {
                    Phase = "lie";
                    long k = now - impact;
                    if (k < SettleTicks) { b = v.Slam; tb = WhipLand + (1f - WhipLand) * k / SettleTicks; }
                    else { b = v.Lie; tb = Loop((k - SettleTicks) / LoopTicks(v.Lie)); }
                    shadowReach = 1.06f; shadowStrength = 0.78f * (1f - Mathf.Clamp01(k / 6f));
                }
                else
                {
                    Phase = "withdraw";
                    b = v.Withdraw; tb = Mathf.Clamp01((now - end) / (float)RecoveryTicks);
                }
                Spray(lead, v, now, riseStart, warnStart, whipStart, impact, end);
            }
            else
            {
                Phase = "lurk";
                ActiveVariant = "";
                position = lurkPosition; yaw = lurkYaw;
                if (_previous != null && now >= _previous.Retire)
                {
                    float g = Smooth(Mathf.Clamp01((now - _previous.Retire) / (float)DepartTicks));
                    position = Vector3.Lerp(_previous.Variant.Anchor, lurkPosition, g);
                    yaw = Mathf.LerpAngle(_previous.Variant.Yaw, lurkYaw, g);
                }
                b = _a.Lurk; tb = LurkPhase(now);
            }

            _kraken.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Pose(a, ta, b, tb, wb);
            ShowShadow(v, shadowReach, shadowStrength);
            ShowDrops();
        }

        private void Pose(int a, float ta, int b, float tb, float wb)
        {
            if (_weightA >= 0) _mixer.SetInputWeight(_weightA, 0f);
            if (_weightB >= 0) _mixer.SetInputWeight(_weightB, 0f);
            if (a >= 0 && a != b && wb < 1f)
            {
                _mixer.SetInputWeight(a, 1f - wb);
                _clips[a].SetTime(ta * _a.Clips[a].length);
                _weightA = a;
            }
            else
            {
                _weightA = -1;
                wb = 1f;
            }
            _mixer.SetInputWeight(b, wb);
            _clips[b].SetTime(tb * _a.Clips[b].length);
            _weightB = b;
            _graph.Evaluate(0f);
        }

        private void ShowShadow(Variant v, float reach, float strength)
        {
            bool on = v != null && strength > 0.002f;
            if (_shadowObject.activeSelf != on) _shadowObject.SetActive(on);
            if (!on) return;
            float halfBeam = HalfBeamAt(v.LaneX);
            // A box around the lane, from just under the deck to above the raised roofs.
            _shadowObject.transform.SetPositionAndRotation(new Vector3(v.LaneX, _a.DeckTop + 0.7f, 0f), Quaternion.identity);
            _shadowObject.transform.localScale = new Vector3(2f * v.HalfWidth + 3f, 2.8f, 2f * _a.MaxBeam + 4f);
            _shadow.SetFloat("_Strength", strength);
            _shadow.SetFloat("_Reach", reach);
            _shadow.SetFloat("_LaneX", v.LaneX);
            _shadow.SetFloat("_Side", v.Side);
            _shadow.SetFloat("_HalfBeam", halfBeam);
            _shadow.SetFloat("_HalfWidth", v.HalfWidth);
            _shadow.SetFloat("_DeckY", _a.DeckTop);
        }

        // ---------------------------------------------------------------- spray and drips

        private void Spray(HazardCue lead, Variant v, long now, long riseStart, long warnStart, long whipStart, long impact, long end)
        {
            uint seed = lead.Ordinal * 0x9e3779b9u;
            float deck = _a.DeckTop, side = v.Side;
            // Spray off the rail top where the tip comes over, from mid-rise until just after the whip starts.
            Periodic(seed ^ 1, 48, 28, 24, riseStart + RiseTicks / 2, whipStart + 4, now, (h, age) =>
            {
                float x = v.LaneX + (h(0) - 0.5f) * 6.4f;
                var origin = new Vector3(x, deck + 0.35f, side * (HalfBeamAt(x) - 0.25f));
                var velocity = new Vector3((h(1) - 0.5f) * 1.0f, 2.4f + 1.8f * h(2), -side * (0.4f + 1.2f * h(3)));
                return Drop(origin, velocity, age, 24, 0.1f + 0.12f * h(4), new Color32(225, 240, 255, 235));
            });
            // Drips falling off the curled tip during the warning and off the arm as it drags away.
            if (_tip.Length > 0)
            {
                Drips(seed ^ 2, warnStart, impact, now, new Color32(185, 220, 245, 220));
                Drips(seed ^ 3, end, end + RecoveryTicks + 10, now, new Color32(185, 220, 245, 220));
            }
            // The splash where the arm lands, along its whole length.
            if (now >= impact && now < impact + 40)
            {
                float halfBeam = HalfBeamAt(v.LaneX);
                for (int i = 0; i < 70 && _drops < MaxDrops; i++)
                {
                    uint key = Hash(seed ^ 4, (uint)i);
                    float H(int j) => Hash01(key, (uint)j);
                    float u = H(0) * 1.02f;
                    float across = (H(1) * 2f - 1f) * v.HalfWidth * (u < 0.55f ? 0.95f : 0.95f * (1f - (u - 0.55f) / 0.5f));
                    var origin = new Vector3(v.LaneX + across, deck + 0.25f, side * (halfBeam - u * 2f * halfBeam));
                    var velocity = new Vector3(Mathf.Sign(across) * (0.8f + 1.8f * H(2)), 3f + 3.5f * H(3), (H(4) - 0.5f) * 1.2f);
                    int life = 28 + (int)(10 * H(5));
                    Drop(origin, velocity, now - impact, life, 0.08f + 0.12f * H(6), new Color32(235, 245, 255, 235));
                }
            }
        }

        private void Drips(uint seed, long from, long to, long now, Color32 colour)
        {
            Periodic(seed, 14, 20, 18, from, to, now, (h, age) =>
            {
                var bone = _tip[(int)(h(0) * _tip.Length) % _tip.Length];
                var origin = bone.position + new Vector3((h(1) - 0.5f) * 0.8f, -0.3f, (h(2) - 0.5f) * 0.8f);
                if (origin.y < _a.DeckTop + 0.4f) return false;
                return Drop(origin, Vector3.zero, age, 18, 0.06f + 0.06f * h(3), colour);
            });
        }

        /// <summary>count slots, each spawning every `period` ticks (staggered) while from &lt;= spawn &lt; to.</summary>
        private void Periodic(uint seed, int count, int period, int life, long from, long to, long now,
            Func<Func<int, float>, long, bool> emit)
        {
            if (now < from || now >= to + life) return;
            for (int i = 0; i < count && _drops < MaxDrops; i++)
            {
                long offset = from + (long)(Hash01(seed, (uint)i) * period);
                long k = (Math.Min(now, to - 1) - offset);
                if (k < 0) continue;
                k /= period;
                long spawn = offset + k * period;
                long age = now - spawn;
                if (age < 0 || age >= life) continue;
                uint key = Hash(Hash(seed, (uint)i), (uint)k);
                emit(j => Hash01(key, (uint)j), age);
            }
        }

        private bool Drop(Vector3 origin, Vector3 velocity, long age, int life, float radius, Color32 colour)
        {
            if (_drops >= MaxDrops) return false;
            float t = age / (float)TicksPerSecond;
            var p = origin + velocity * t + 0.5f * t * t * new Vector3(0f, -9.81f, 0f);
            if (p.y < _a.DeckTop - 0.05f && Mathf.Abs(p.z) < HalfBeamAt(p.x)) return false;
            float fade = age / (float)life;
            colour.a = (byte)(colour.a * (1f - fade * fade));
            int v = 4 * _drops++;
            for (int c = 0; c < 4; c++)
            {
                _dropCentres[v + c] = p;
                _dropSizes[v + c] = new Vector2(radius, 0f);
                _dropColours[v + c] = colour;
            }
            return true;
        }

        private void ShowDrops()
        {
            if (_drops == 0 && _shownDrops == 0) return;
            for (int i = _drops; i < _shownDrops; i++)
                for (int c = 0; c < 4; c++)
                {
                    _dropSizes[4 * i + c] = Vector2.zero;
                    _dropColours[4 * i + c] = default;
                }
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _sprayMesh.SetVertices(_dropCentres, 0, _dropCentres.Length, flags);
            _sprayMesh.SetUVs(1, _dropSizes, 0, _dropSizes.Length, flags);
            _sprayMesh.SetColors(_dropColours, 0, _dropColours.Length, flags);
            _shownDrops = _drops;
        }

        // ---------------------------------------------------------------- lurk

        /// <summary>
        /// The simulation's lurk route runs about 3 m off the hull; the kraken's head needs its
        /// clearance (the surfacing spots' distance). Push the pose straight out from the deck to that
        /// distance, with rounded corners, and face along the route (it runs anticlockwise seen from above).
        /// </summary>
        private void Lurk(ArcadePoint pose, out Vector3 position, out float yaw)
        {
            var p = new Vector2(pose.X, pose.Z);
            var inner = new Vector2(Mathf.Clamp(p.x, _a.SternX + LurkRound, _a.BowX - LurkRound),
                Mathf.Clamp(p.y, -_a.MaxBeam + LurkRound, _a.MaxBeam - LurkRound));
            var hull = new Vector2(Mathf.Clamp(p.x, _a.SternX, _a.BowX), Mathf.Clamp(p.y, -_a.MaxBeam, _a.MaxBeam));
            var offset = p - inner;
            float length = offset.magnitude;
            var normal = length > 1e-3f ? offset / length : new Vector2(0f, p.y >= 0f ? 1f : -1f);
            float out_ = length - (p - hull).magnitude + _a.Clearance;
            var q = inner + normal * out_;
            position = new Vector3(q.x, _a.WaterY, q.y);
            yaw = Mathf.Atan2(-normal.y, normal.x) * Mathf.Rad2Deg;
        }

        private float LurkPhase(long now) => Loop((now - (_previous?.Retire ?? 0)) / LoopTicks(_a.Lurk));

        private float LoopTicks(int clip) => Mathf.Max(1f, _a.Clips[clip].length * TicksPerSecond);

        private float HalfBeamAt(float x)
        {
            var beam = _a.Beam;
            if (beam.Length == 0 || x < beam[0].x || x > beam[^1].x) return 0f;
            for (int i = 1; i < beam.Length; i++)
                if (x <= beam[i].x)
                    return Mathf.Lerp(beam[i - 1].y, beam[i].y, Mathf.InverseLerp(beam[i - 1].x, beam[i].x, x));
            return beam[^1].y;
        }

        private static float Loop(float x) => x - Mathf.Floor(x);
        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static uint Hash(uint seed, uint value)
        {
            unchecked
            {
                uint v = seed ^ (value * 0x9e3779b9u) ^ 0x4b52414bu;
                v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16);
            }
        }

        private static float Hash01(uint seed, uint value) => (Hash(seed, value) & 0xffffff) / 16777216f;

        // ---------------------------------------------------------------- edges and sound

        private void Edge(HazardCue cue, byte edge, uint tick, bool reached)
        {
            if (!reached || (_started && tick <= _lastTick) || !_edges.Add((cue.Ordinal, cue.Operation, edge))) return;
            // The sections form one action. Only its first section speaks.
            if (cue.Operation != 0) return;
            Debug.Log($"[StageHazard] tick={tick} ordinal={cue.Ordinal} operation={cue.Operation} edge={edge} variant={cue.Variant}");
            if (edge == 0) OnWarning();
            else if (edge == 1) OnImpact();
        }

        /// <summary>Sound hook: the kraken surfaces. A grumpy "HEY!" voice belongs here once recorded.</summary>
        private void OnWarning()
        {
            UISFX.PlayCountdownBeat("HAZARD");
            if (_audio != null && _a?.Surface != null && PresentationOutput.TryPlay()) _audio.PlayOneShot(_a.Surface, 0.55f);
        }

        /// <summary>Sound hook: the arm lands. A placeholder thud; hits on fighters keep their own sounds.</summary>
        private void OnImpact()
        {
            if (_audio != null && _a?.Thud != null && PresentationOutput.TryPlay()) _audio.PlayOneShot(_a.Thud, 0.7f);
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            Root.gameObject.SetActive(false);
            Object.Destroy(Root.gameObject);
            if (_body != null) Object.Destroy(_body);
            if (_submerged != null) Object.Destroy(_submerged);
            if (_shadow != null) Object.Destroy(_shadow);
            if (_spray != null) Object.Destroy(_spray);
            if (_sprayMesh != null) Object.Destroy(_sprayMesh);
        }
    }
}
