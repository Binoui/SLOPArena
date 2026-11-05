using System;
using System.Collections.Generic;
using System.Diagnostics;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.StageHazards;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Cosmetic only: After Hours' Bass Drop. Called after every simulation tick, including ticks
    /// between rendered frames; everything it shows is a function of the hazard view and absolute
    /// ticks, never the wall clock, and it decides no hit, pad or timing.
    ///
    /// One drop, from the cue (beat = (ImpactTick - WarningTick) / 4, 30 ticks at 120 BPM):
    /// the lit pads snap on at WarningTick with the DJ's "4" and pulse on "3", "2" and "1" while the
    /// dark pads go black; the pulse builds toward the drop with eighths, then sixteenths. At
    /// ImpactTick every lit pad flashes white and blasts a square column off the floor (walls on the
    /// lit outline, rings the size of a pad), then the floor settles back over the recovery.
    /// The countdown is a floating seven-segment LED sign (<see cref="Placement"/>).
    /// Sounds fire once per (ordinal, edge) for every edge crossed since the last presented tick, and
    /// a rewind never clears what already fired, so a rollback cannot double them.
    ///
    /// Art: the shaders and materials in Resources/<see cref="ResourceFolder"/> and the clips that
    /// tools/sfx/bass_drop_clips.py writes to Resources/Audio/SFX/BassDrop. Missing art draws nothing.
    /// </summary>
    public sealed class BassDropPresentation : IStageHazardPresenter
    {
        public const string ResourceFolder = "StageHazards/BassDrop";
        public const string SoundFolder = "Audio/SFX/BassDrop";
        public const string PresentationId = "bass_drop.drop";
        // BassDropHazard's recovery is internal to Shared; the cue retires this long after EndTick.
        private const int RecoveryTicks = 45;
        private const float FloorLift = 0.012f;
        // The blast: walls on the lit outline as tall as the danger (0.5 m) and a bit more, and rings.
        private const int BlastTicks = 30, RingTicks = 16;
        private const float WallHeight = 1.05f, RingHeight = 1.2f;
        private static readonly int[] RingDelays = { 0, 3, 6 };
        // A sound more than this late (a client that skipped ticks) is marked fired but stays silent:
        // it would be off the beat.
        private const int StaleSoundTicks = 6;

        public enum CountdownPlacement { Floor, Booth }

        /// <summary>Where the countdown floats. Floor: over the dance floor's centre; Booth: over the DJ riser.</summary>
        public static CountdownPlacement Placement = CountdownPlacement.Floor;

        public Transform Root { get; }
        /// <summary>Diagnostics for captures and smoke checks.</summary>
        public string Phase { get; private set; } = "idle";
        public string ActiveVariant { get; private set; } = "";
        public string CountdownText { get; private set; } = "";
        public int LitPads { get; private set; }
        public int Presents { get; private set; }
        public double MaxPresentMilliseconds { get; private set; }
        public double MeanPresentMilliseconds => Presents == 0 ? 0 : _presentTotalMs / Presents;
        public IReadOnlyList<string> FiredEdges => _firedLog;
        public Transform Countdown => _sign != null ? _sign.transform : null;

        private static readonly string[] EdgeNames = { "beat4", "beat3", "beat2", "beat1", "drop" };
        private readonly HashSet<(uint, byte)> _fired = new();
        private readonly List<string> _firedLog = new();
        private uint _lastTick;
        private bool _started;
        private double _presentTotalMs;

        private readonly Material _padsMaterial, _blastMaterial, _signMaterial;
        private readonly GameObject _pads, _blast, _sign;
        private readonly Mesh _padMesh, _blastMesh, _signMesh;
        private readonly Light _light;
        private readonly AudioSource _audio;
        private readonly AudioClip[] _kicks = new AudioClip[4];
        private readonly AudioClip _riser, _hit, _voice;
        private Color _hot = new(1f, 0.07f, 0.04f);

        // Pad geometry, from the first cue's anchor (the anchor never changes within a match).
        private Vector3[] _centres = Array.Empty<Vector3>();
        private float _half = 1f;
        private readonly Color32[] _padState;
        private readonly bool[] _lit;
        private uint _litOrdinal = uint.MaxValue;

        private readonly List<Vector3> _bv = new();
        private readonly List<Vector2> _buv = new(), _bkind = new();
        private readonly List<Color32> _bc = new();
        private readonly List<int> _bt = new();
        private string _signText;

        public BassDropPresentation(Transform parent)
        {
            Root = StageHazardPresenters.CreateRoot(parent);
            _padsMaterial = Load<Material>(ResourceFolder + "/BassDropPads");
            _blastMaterial = Load<Material>(ResourceFolder + "/BassDropBlast");
            _signMaterial = Load<Material>(ResourceFolder + "/BassDropSign");
            _padState = new Color32[20 * 4];
            _lit = new bool[20];
            if (_padsMaterial == null || _blastMaterial == null || _signMaterial == null)
            {
                Debug.LogError($"[StageHazard] Bass Drop materials missing under Resources/{ResourceFolder}; showing nothing.");
                return;
            }
            _padsMaterial = new Material(_padsMaterial);
            _blastMaterial = new Material(_blastMaterial);
            _signMaterial = new Material(_signMaterial);
            if (_padsMaterial.HasProperty("_HotColor")) _hot = _padsMaterial.GetColor("_HotColor");

            _pads = Part("Lit pads", _padsMaterial, out _padMesh);
            _blast = Part("Blast", _blastMaterial, out _blastMesh);
            _sign = Part("DJ countdown", _signMaterial, out _signMesh);

            var light = new GameObject("Drop light");
            light.transform.SetParent(Root, false);
            light.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            _light = light.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 9f;
            _light.shadows = LightShadows.None;
            _light.enabled = false;

            for (int i = 0; i < 4; i++) _kicks[i] = Load<AudioClip>($"{SoundFolder}/bass_drop_kick_{4 - i}", true);
            _riser = Load<AudioClip>(SoundFolder + "/bass_drop_riser", true);
            _hit = Load<AudioClip>(SoundFolder + "/bass_drop_hit", true);
            // The DJ's "DROP!" shout belongs here once recorded; until then the slot stays empty.
            _voice = Load<AudioClip>(SoundFolder + "/bass_drop_voice", false);
            _audio = Root.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.outputAudioMixerGroup = ClientSettingsService.Instance != null ? ClientSettingsService.Instance.FindBus("SFX") : null;
        }

        private static T Load<T>(string path, bool required = true) where T : Object
        {
            var asset = Resources.Load<T>(path);
            if (asset == null && required) Debug.LogWarning($"[StageHazard] Bass Drop asset missing: Resources/{path}");
            return asset;
        }

        private GameObject Part(string name, Material material, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            mesh = new Mesh { name = "Bass Drop " + name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            return go;
        }

        /// <summary>Moves the countdown sign; captures use it to compare placements on one frame.</summary>
        public void SetPlacement(CountdownPlacement placement)
        {
            Placement = placement;
            if (_sign == null) return;
            // Floor: high over the centre, so it sits above the fighters in the frame from any yaw.
            // Booth: over the DJ riser (x 7.5..13.5, top 1.5), clear of anyone standing on it.
            bool floor = placement == CountdownPlacement.Floor;
            _sign.transform.localPosition = floor ? new Vector3(0f, 4.3f, 0f) : new Vector3(10.5f, 4.4f, 0f);
            _sign.transform.localScale = Vector3.one * SignScale;
        }

        public void Present(StageHazardView view)
        {
            if (view == null) return;
            long started = Stopwatch.GetTimestamp();
            HazardCue lead = null;
            foreach (var cue in view.Cues)
                if (cue.PresentationId == PresentationId && cue.Cells != null && (lead == null || cue.Operation < lead.Operation))
                    lead = cue;
            // A rewind keeps what already fired: replaying the same ticks must not repeat a sound.
            uint since = _started ? _lastTick : (view.Tick == 0 ? 0 : view.Tick - 1);
            if (lead != null && view.Tick > since) FireEdges(lead, since, view.Tick);
            if (_pads != null) Show(view.Tick, lead);
            _lastTick = view.Tick; _started = true;

            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            MaxPresentMilliseconds = Math.Max(MaxPresentMilliseconds, ms);
            _presentTotalMs += ms;
            Presents++;
        }

        // ---------------------------------------------------------------- edges and sound

        private static long EdgeTick(HazardCue cue, int edge)
        {
            long beat = Beat(cue);
            return edge < 4 ? cue.WarningTick + edge * beat : cue.ImpactTick;
        }

        private static long Beat(HazardCue cue) => Math.Max(1, ((long)cue.ImpactTick - cue.WarningTick) / 4);

        private void FireEdges(HazardCue cue, uint since, uint now)
        {
            for (byte edge = 0; edge < 5; edge++)
            {
                long at = EdgeTick(cue, edge);
                if (at <= since || at > now || !_fired.Add((cue.Ordinal, edge))) continue;
                bool late = now - at > StaleSoundTicks;
                string line = $"tick={now} at={at} ordinal={cue.Ordinal} edge={EdgeNames[edge]} variant={cue.Variant}{(late ? " late" : "")}";
                _firedLog.Add(line);
                if (_firedLog.Count > 64) _firedLog.RemoveAt(0);
                Debug.Log("[StageHazard] " + line);
                if (!late) Sound(edge);
            }
            // Forget edges of drops long gone; ordinals only grow.
            if (_fired.Count > 40) _fired.RemoveWhere(key => key.Item1 + 4 < cue.Ordinal);
        }

        private void Sound(byte edge)
        {
            if (_audio == null || !PresentationOutput.TryPlay()) return;
            if (edge < 4)
            {
                if (_kicks[edge] != null) _audio.PlayOneShot(_kicks[edge], 0.42f + 0.05f * edge);
                if (edge == 3 && _riser != null) _audio.PlayOneShot(_riser, 0.32f);
                return;
            }
            if (_hit != null) _audio.PlayOneShot(_hit, 0.62f);
            if (_voice != null) _audio.PlayOneShot(_voice, 0.7f);
        }

        // ---------------------------------------------------------------- the look

        private void Show(uint tick, HazardCue cue)
        {
            if (cue == null)
            {
                Phase = "idle"; ActiveVariant = ""; CountdownText = ""; LitPads = 0;
                Hide();
                return;
            }
            Pads(cue);
            ActiveVariant = cue.Variant;
            long now = tick, warn = cue.WarningTick, impact = cue.ImpactTick, beat = Beat(cue);
            float hot, dark, flash, build, light;
            string text;
            float pop, signBright;
            if (now < impact)
            {
                long k = Math.Max(0, now - warn);
                int beatIndex = (int)Math.Min(3, k / beat);
                long age = k - beatIndex * beat;
                build = Mathf.Clamp01(k / (4f * beat));
                float pulse = Mathf.Exp(-age / 7f);
                // The build: eighths through "2", sixteenths through "1", a strobe in the last eighth.
                if (beatIndex >= 2) pulse = Mathf.Max(pulse, 0.55f * Mathf.Exp(-(age % (beat / 2)) / 4f));
                if (beatIndex == 3) pulse = Mathf.Max(pulse, 0.45f * Mathf.Exp(-(age % Math.Max(1, beat / 4)) / 2.5f));
                hot = 0.62f + 0.3f * build + 0.08f * pulse;
                flash = pulse * (0.22f + 0.26f * build);   // below 0.5: toward the hot core, never white
                dark = Mathf.Min(1f, (k + 1) / 4f) * (0.72f + 0.28f * build);
                text = (4 - beatIndex).ToString();
                pop = 1f + 0.28f * Mathf.Exp(-age / 5f);
                signBright = 0.7f + 0.3f * pulse;
                light = 0.5f + 1.2f * pulse * (0.5f + build);
                Phase = "beat" + (4 - beatIndex);
            }
            else
            {
                long a = now - impact;
                build = 1f;
                flash = a < 3 ? 1f : Mathf.Exp(-(a - 3) / 6f);
                hot = Mathf.Exp(-a / 16f) * (1f - Smooth(Mathf.InverseLerp(26f, 40f, a)));
                dark = 1f - Smooth(Mathf.Clamp01(a / 12f));
                text = a < 36 ? "drOP" : "";
                pop = 1f + 0.35f * Mathf.Exp(-a / 6f);
                signBright = Mathf.Clamp01(1f - (a - 24) / 12f);
                light = 4.5f * flash;
                Phase = a < BlastTicks ? "drop" : "settle";
            }
            CountdownText = text;
            SetPads(hot, dark, flash, build);
            _padsMaterial.SetFloat("_Phase", (now % 60) / 60f);
            Blast(now - impact);
            Sign(text, pop, signBright);
            _light.enabled = light > 0.02f;
            _light.color = Color.Lerp(_hot, Color.white, Mathf.Clamp01(flash));
            _light.intensity = light;
        }

        private void Hide()
        {
            if (_pads.activeSelf) _pads.SetActive(false);
            if (_blast.activeSelf) _blast.SetActive(false);
            if (_sign.activeSelf) _sign.SetActive(false);
            _light.enabled = false;
            _signText = null;
        }

        /// <summary>Builds the 20-pad mesh once and marks this drop's lit pads.</summary>
        private void Pads(HazardCue cue)
        {
            var cells = cue.Cells;
            if (_centres.Length != cells.Anchor.Points.Count)
            {
                int n = cells.Anchor.Points.Count;
                _centres = new Vector3[n];
                _half = cells.Anchor.Radius;
                var vertices = new Vector3[n * 4];
                var uv = new Vector2[n * 4];
                var triangles = new int[n * 6];
                for (int i = 0; i < n; i++)
                {
                    var p = cells.Anchor.Points[i];
                    _centres[i] = new Vector3(p.X, p.Y, p.Z);
                    float y = p.Y + FloorLift;
                    vertices[4 * i] = new Vector3(p.X - _half, y, p.Z - _half);
                    vertices[4 * i + 1] = new Vector3(p.X + _half, y, p.Z - _half);
                    vertices[4 * i + 2] = new Vector3(p.X + _half, y, p.Z + _half);
                    vertices[4 * i + 3] = new Vector3(p.X - _half, y, p.Z + _half);
                    uv[4 * i] = new Vector2(-1, -1); uv[4 * i + 1] = new Vector2(1, -1);
                    uv[4 * i + 2] = new Vector2(1, 1); uv[4 * i + 3] = new Vector2(-1, 1);
                    int v = 4 * i, t = 6 * i;
                    triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
                }
                _padMesh.Clear();
                _padMesh.vertices = vertices;
                _padMesh.uv = uv;
                _padMesh.colors32 = new Color32[n * 4];
                _padMesh.triangles = triangles;
                _padMesh.RecalculateBounds();
                if (_padState.Length < n * 4) throw new InvalidOperationException("Bass Drop expects at most 20 pads.");
            }
            if (_litOrdinal == cue.Ordinal) return;
            _litOrdinal = cue.Ordinal;
            Array.Clear(_lit, 0, _lit.Length);
            foreach (int index in cells.Indices) if (index >= 0 && index < _lit.Length) _lit[index] = true;
            LitPads = cells.Indices.Count;
        }

        private void SetPads(float hot, float dark, float flash, float build)
        {
            if (!_pads.activeSelf) _pads.SetActive(true);
            byte H(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x * 255f), 0, 255);
            int n = _centres.Length;
            for (int i = 0; i < n; i++)
            {
                var c = _lit[i] ? new Color32(H(hot), 0, H(flash), H(build)) : new Color32(0, H(dark), 0, H(build));
                for (int k = 0; k < 4; k++) _padState[4 * i + k] = c;
            }
            _padMesh.SetColors(_padState, 0, n * 4, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
        }

        // ---------------------------------------------------------------- the blast

        private void Blast(long age)
        {
            if (age < 0 || age >= BlastTicks)
            {
                if (_blast.activeSelf) _blast.SetActive(false);
                return;
            }
            _bv.Clear(); _buv.Clear(); _bkind.Clear(); _bc.Clear(); _bt.Clear();
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 7f), 3f);
            float height = 0.25f + (WallHeight - 0.25f) * rise;
            float strength = age < 2 ? 1f : Mathf.Exp(-(age - 2) / 7f);
            var wall = Tint(Color.Lerp(new Color(1f, 0.2f, 0.1f), new Color(1f, 0.9f, 0.7f), Mathf.Exp(-age / 5f)), strength);
            int n = _centres.Length;
            for (int i = 0; i < n; i++)
            {
                if (!_lit[i]) continue;
                var c = _centres[i];
                // Walls only where a lit pad meets a dark pad or the floor's edge: the blast's outline.
                for (int side = 0; side < 4; side++)
                {
                    if (LitNeighbour(i, side)) continue;
                    Vector3 a, b;
                    switch (side)
                    {
                        case 0: a = new Vector3(c.x - _half, c.y, c.z - _half); b = new Vector3(c.x + _half, c.y, c.z - _half); break;
                        case 1: a = new Vector3(c.x + _half, c.y, c.z - _half); b = new Vector3(c.x + _half, c.y, c.z + _half); break;
                        case 2: a = new Vector3(c.x + _half, c.y, c.z + _half); b = new Vector3(c.x - _half, c.y, c.z + _half); break;
                        default: a = new Vector3(c.x - _half, c.y, c.z + _half); b = new Vector3(c.x - _half, c.y, c.z - _half); break;
                    }
                    Quad(a, b, b + Vector3.up * height, a + Vector3.up * height,
                        new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), 0f, wall);
                }
                foreach (int delay in RingDelays)
                {
                    long r = age - delay;
                    if (r < 0 || r >= RingTicks) continue;
                    float t = r / (float)RingTicks;
                    float y = c.y + 0.03f + RingHeight * (1f - (1f - t) * (1f - t));
                    var ring = Tint(Color.Lerp(_hot, Color.white, 0.6f * (1f - t)), Mathf.Pow(1f - t, 1.5f) * (delay == 0 ? 1f : 0.7f));
                    Quad(new Vector3(c.x - _half, y, c.z - _half), new Vector3(c.x + _half, y, c.z - _half),
                        new Vector3(c.x + _half, y, c.z + _half), new Vector3(c.x - _half, y, c.z + _half),
                        new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1), 1f, ring);
                }
            }
            _blastMesh.Clear();
            _blastMesh.SetVertices(_bv);
            _blastMesh.SetUVs(0, _buv);
            _blastMesh.SetUVs(1, _bkind);
            _blastMesh.SetColors(_bc);
            _blastMesh.SetTriangles(_bt, 0, false);
            _blastMesh.bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(12f, 3f, 10f));
            if (!_blast.activeSelf) _blast.SetActive(true);
        }

        private bool LitNeighbour(int index, int side)
        {
            int col = index % 5, row = index / 5;
            switch (side)
            {
                case 0: row--; break;   // south (-z)
                case 1: col++; break;   // east (+x)
                case 2: row++; break;   // north (+z)
                default: col--; break;  // west (-x)
            }
            if (col < 0 || col > 4 || row < 0 || row * 5 + col >= _centres.Length) return false;
            return _lit[row * 5 + col];
        }

        private static Color32 Tint(Color colour, float strength)
        {
            var linear = colour.linear;
            return new Color32((byte)(Mathf.Clamp01(linear.r) * 255), (byte)(Mathf.Clamp01(linear.g) * 255),
                (byte)(Mathf.Clamp01(linear.b) * 255), (byte)(Mathf.Clamp01(strength) * 255));
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, float kind, Color32 colour)
        {
            int v = _bv.Count;
            _bv.Add(a); _bv.Add(b); _bv.Add(c); _bv.Add(d);
            _buv.Add(ua); _buv.Add(ub); _buv.Add(uc); _buv.Add(ud);
            var k = new Vector2(kind, 0f);
            for (int i = 0; i < 4; i++) { _bkind.Add(k); _bc.Add(colour); }
            _bt.Add(v); _bt.Add(v + 2); _bt.Add(v + 1);
            _bt.Add(v); _bt.Add(v + 3); _bt.Add(v + 2);
        }

        // ---------------------------------------------------------------- the countdown sign

        private static float SignScale => Placement == CountdownPlacement.Floor ? 1.8f : 2f;
        private const float GlyphWidth = 0.62f, GlyphGap = 0.14f, PanelPad = 0.22f;

        private static int Segments(char c) => c switch
        {
            '0' => 63, '1' => 6, '2' => 91, '3' => 79, '4' => 102, '5' => 109, '6' => 125, '7' => 7, '8' => 127, '9' => 111,
            'd' => 94, 'r' => 80, 'O' => 63, 'P' => 115, '-' => 64, _ => 0,
        };

        private void Sign(string text, float pop, float bright)
        {
            if (string.IsNullOrEmpty(text) || bright <= 0.01f)
            {
                if (_sign.activeSelf) _sign.SetActive(false);
                _signText = null;
                return;
            }
            if (!_sign.activeSelf)
            {
                _sign.SetActive(true);
                SetPlacement(Placement);
            }
            var tint = Color.Lerp(_hot, new Color(1f, 0.35f, 0.1f), 0.5f).linear;
            var colour = new Color32((byte)(tint.r * 255), (byte)(tint.g * 255), (byte)(tint.b * 255), (byte)(Mathf.Clamp01(bright) * 255));
            if (_signText != text)
            {
                _signText = text;
                int n = text.Length;
                float width = n * GlyphWidth + (n - 1) * GlyphGap;
                var vertices = new List<Vector3>();
                var uv = new List<Vector2>();
                var data = new List<Vector2>();
                var triangles = new List<int>();
                void Add(float x0, float y0, float x1, float y1, Vector2 u0, Vector2 u1, Vector2 d)
                {
                    int v = vertices.Count;
                    vertices.Add(new Vector3(x0, y0)); vertices.Add(new Vector3(x1, y0));
                    vertices.Add(new Vector3(x1, y1)); vertices.Add(new Vector3(x0, y1));
                    uv.Add(new Vector2(u0.x, u0.y)); uv.Add(new Vector2(u1.x, u0.y));
                    uv.Add(new Vector2(u1.x, u1.y)); uv.Add(new Vector2(u0.x, u1.y));
                    for (int i = 0; i < 4; i++) data.Add(d);
                    triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                    triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
                }
                Add(-width / 2 - PanelPad, -0.5f - PanelPad, width / 2 + PanelPad, 0.5f + PanelPad, Vector2.zero, Vector2.one, new Vector2(0f, 1f));
                for (int i = 0; i < n; i++)
                {
                    float x0 = -width / 2 + i * (GlyphWidth + GlyphGap);
                    Add(x0, -0.5f, x0 + GlyphWidth, 0.5f, Vector2.zero, new Vector2(GlyphWidth, 1f), new Vector2(Segments(text[i]), 0f));
                }
                _signMesh.Clear();
                _signMesh.SetVertices(vertices);
                _signMesh.SetUVs(0, uv);
                _signMesh.SetUVs(1, data);
                _signMesh.SetColors(new Color32[vertices.Count]);
                _signMesh.SetTriangles(triangles, 0);
                _signMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 8f);
            }
            var colours = new Color32[_signMesh.vertexCount];
            for (int i = 0; i < colours.Length; i++) colours[i] = colour;
            _signMesh.SetColors(colours);
            _sign.transform.localScale = Vector3.one * (SignScale * pop);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        public void Dispose()
        {
            Root.gameObject.SetActive(false);
            Object.Destroy(Root.gameObject);
            foreach (var o in new Object[] { _padsMaterial, _blastMaterial, _signMaterial, _padMesh, _blastMesh, _signMesh })
                if (o != null) Object.Destroy(o);
        }
    }
}
