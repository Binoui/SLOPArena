using System;
using System.Collections.Generic;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    public sealed partial class MankiMayhemPresentation
    {
        private readonly Material[] _panelMaterials = new Material[4], _bulbMaterials = new Material[4];
        private readonly List<Mesh> _deckMeshes = new();
        private readonly Transform[] _patterns = new Transform[4], _rigs = new Transform[3];
        private readonly Light[] _spots = new Light[3];
        private Material _rigInk, _beamMaterial, _signMaterial;
        private Transform _deckRoot, _poolSign, _eye;
        private TextMesh _poolTitle, _poolState;
        private readonly List<Transform> _confetti = new();
        private readonly Material[] _confettiMaterials = new Material[3];
        private readonly Dictionary<MayhemEventKey, uint> _confettiBursts = new();
        private readonly Dictionary<MayhemEventKey, uint> _cancelFlashes = new();
        private uint _burstTick;
        private float _deckTop, _deckMinX, _deckMaxX, _deckMinZ, _deckMaxZ;
        private ChannelEntry _deckChannel;
        private MayhemDeckBeat? _deckBeat;
        private Mesh _cubeMesh, _sphereMesh;

        private Material Glow(Color color, float emission)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetFloat("_Smoothness", .15f);
            material.EnableKeyword("_EMISSION");
            Tint(material, color, emission);
            return material;
        }
        private static void Tint(Material material, Color color, float emission)
        {
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * emission);
        }
        private Transform DeckObject(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            root.localPosition = position; root.localScale = scale;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return root;
        }
        private void CombineDeck(Transform parent, string name, List<CombineInstance> parts, Material material)
        {
            var mesh = new Mesh { name = name }; mesh.CombineMeshes(parts.ToArray(), true, true);
            _deckMeshes.Add(mesh); DeckObject(parent, name, mesh, material, Vector3.zero, Vector3.one);
        }
        private void CreateDeck(MankiMayhemDefinition stage)
        {
            MayhemChannelLooks.Validate();
            MankiMayhemView.ValidateCopy();
            _deckRoot = new GameObject("Show deck world space").transform;
            _deckRoot.SetParent(_root, true);
            _deckTop = stage.DeckTop; _deckMinX = stage.MinX; _deckMaxX = stage.MaxX;
            _deckMinZ = stage.MinZ; _deckMaxZ = stage.MaxZ;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(primitive);
            primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = primitive.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(primitive);
            _rigInk = Glow(new Color(.035f, .035f, .045f), 0);
            for (int i = 0; i < 4; i++) _panelMaterials[i] = Glow(Color.gray, .16f);
            for (int i = 0; i < 4; i++) _bulbMaterials[i] = Glow(Color.yellow, 1);
            float width = _deckMaxX - _deckMinX, depth = _deckMaxZ - _deckMinZ;
            for (int pattern = 0; pattern < 4; pattern++)
            {
                var parent = new GameObject("Light-up deck " + (MayhemFloorPattern)pattern).transform;
                parent.SetParent(_deckRoot, false); _patterns[pattern] = parent;
                var groups = new[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
                for (int z = 0; z < 10; z++) for (int x = 0; x < 12; x++)
                {
                    int ring = Mathf.Max(Mathf.Abs(x * 2 - 11) / 2, Mathf.Abs(z * 2 - 9) / 2);
                    int colour = (MayhemFloorPattern)pattern switch
                    {
                        MayhemFloorPattern.Checker => (x + z) % 2,
                        MayhemFloorPattern.Stripes => x % 3,
                        MayhemFloorPattern.Rings => ring % 3,
                        _ => (ring + (x < 6 ? 0 : 1)) % 3,
                    };
                    if (x == 0 || x == 11 || z == 0 || z == 9) colour = 3;
                    groups[colour].Add(new CombineInstance { mesh = _cubeMesh, transform = Matrix4x4.TRS(
                        new Vector3(_deckMinX + (x + .5f) * width / 12, _deckTop + .025f, _deckMinZ + (z + .5f) * depth / 10),
                        Quaternion.identity, new Vector3(width / 12 - .035f, .035f, depth / 10 - .035f)) });
                }
                for (int i = 0; i < 4; i++) if (groups[i].Count > 0) CombineDeck(parent, "Panel colour " + i, groups[i], _panelMaterials[i]);
                parent.gameObject.SetActive(false);
            }
            var bulbs = new[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
            int ordinal = 0;
            for (int side = 0; side < 4; side++)
            {
                int count = side % 2 == 0 ? 28 : 24;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)count;
                    var p = side switch
                    {
                        0 => new Vector3(Mathf.Lerp(_deckMinX, _deckMaxX, t), _deckTop + .18f, _deckMinZ),
                        1 => new Vector3(_deckMaxX, _deckTop + .18f, Mathf.Lerp(_deckMinZ, _deckMaxZ, t)),
                        2 => new Vector3(Mathf.Lerp(_deckMaxX, _deckMinX, t), _deckTop + .18f, _deckMaxZ),
                        _ => new Vector3(_deckMinX, _deckTop + .18f, Mathf.Lerp(_deckMaxZ, _deckMinZ, t)),
                    };
                    bulbs[ordinal++ % 4].Add(new CombineInstance { mesh = _sphereMesh, transform = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * .22f) });
                }
            }
            for (int i = 0; i < 4; i++) CombineDeck(_deckRoot, "Marquee chase " + i, bulbs[i], _bulbMaterials[i]);
            _beamMaterial = new Material(Shader.Find("SlopArena/Mayhem Beam"));
            _beamMaterial.SetColor("_BaseColor", new Color(1f, .7f, .2f, .035f));
            var cone = BeamMesh(); _deckMeshes.Add(cone);
            for (int i = 0; i < 3; i++)
            {
                var rig = new GameObject("Overhead show spot " + i).transform; rig.SetParent(_deckRoot, false);
                rig.position = new Vector3(Mathf.Lerp(_deckMinX + 1, _deckMaxX - 1, i / 2f), _deckTop + 5, _deckMaxZ + 1);
                _rigs[i] = rig;
                DeckObject(rig, "Lamp housing", _cubeMesh, _rigInk, Vector3.zero, new Vector3(.8f, .65f, 1));
                DeckObject(rig, "Lamp lens", _sphereMesh, _bulbMaterials[i], new Vector3(0, 0, .6f), new Vector3(.6f, .6f, .15f));
                DeckObject(rig, "Visible light beam", cone, _beamMaterial, new Vector3(0, 0, .7f), new Vector3(3.3f, 3.3f, 11));
                var light = rig.gameObject.AddComponent<Light>(); light.type = LightType.Spot;
                light.range = 24; light.spotAngle = 42; light.innerSpotAngle = 24; light.intensity = 2;
                light.shadows = LightShadows.None; _spots[i] = light;
            }
            _poolSign = new GameObject("Pool rules light").transform; _poolSign.SetParent(_deckRoot, false);
            _poolSign.position = new Vector3(0, _deckTop + 3, _deckMaxZ + 1);
            _signMaterial = Glow(Color.cyan, .6f);
            DeckObject(_poolSign, "Pool sign ink rim", _cubeMesh, _rigInk, Vector3.zero, new Vector3(17, 2.6f, .25f));
            DeckObject(_poolSign, "Pool sign lit face", _cubeMesh, _signMaterial, new Vector3(0, 0, -.15f), new Vector3(16.7f, 2.3f, .1f));
            _poolTitle = Lettering(_poolSign, "Pool rules declaration", new Vector3(.8f, .45f, -.24f), 1.8f, Color.white);
            _poolState = Lettering(_poolSign, "Pool rules state", new Vector3(.8f, -.65f, -.24f), .65f, Color.white);
            _eye = new GameObject("Bashie watch eye").transform; _eye.SetParent(_poolSign, false);
            _eye.localPosition = new Vector3(-6.8f, 0, -.3f);
            var eyeLine = Line(_eye, "Eyelid", _ink, .12f); eyeLine.positionCount = 33;
            for (int i = 0; i <= 32; i++) { float a = i * Mathf.PI / 16; eyeLine.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .65f, 0)); }
            DeckObject(_eye, "Pupil", _sphereMesh, _rigInk, Vector3.zero, new Vector3(.6f, .9f, .1f));
            for (int i = 0; i < 3; i++) _confettiMaterials[i] = Material(i == 0 ? new Color(1, .75f, .1f) : i == 1 ? new Color(.75f, .4f, .95f) : new Color(1, .3f, .12f));
            CreateDeckDressing();
            for (int i = 0; i < 72; i++)
            {
                var bit = DeckObject(_deckRoot, "ON AIR confetti", _cubeMesh, _confettiMaterials[i % 3], Vector3.zero, new Vector3(.12f, .035f, .24f));
                bit.gameObject.SetActive(false); _confetti.Add(bit);
            }
        }
        private static Mesh BeamMesh()
        {
            var vertices = new Vector3[17]; var triangles = new int[16 * 3];
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8; vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % 16 + 1;
            }
            var mesh = new Mesh { name = "Show light cone", vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); return mesh;
        }
        private void DeckEvents(IReadOnlyList<MayhemEvent> events, IReadOnlyList<MayhemEvent> retracted)
        {
            if (retracted != null) for (int i = 0; i < retracted.Count; i++) { _confettiBursts.Remove(retracted[i].Key); _cancelFlashes.Remove(retracted[i].Key); }
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Kind == MayhemEventKind.Pass) _confettiBursts[e.Key] = e.MatchTick;
                if (e.Kind == MayhemEventKind.Fail) _cancelFlashes[e.Key] = e.MatchTick;
            }
        }
        private void UpdateDeck(MayhemShowView view)
        {
            var look = MayhemChannelLooks.For(view.Channel); var beat = MayhemChannelLooks.Beat(view);
            bool reduced = ClientSettingsService.Instance.ReducedFlashing;
            bool flash = false;
            foreach (var tick in _cancelFlashes.Values) if (view.Tick >= tick && view.Tick - tick < 12) flash = true;
            // The whole deck only goes red when nobody is celebrating: a buzzer fail must not drown out others' ON AIR.
            foreach (var tick in _confettiBursts.Values) if (view.Tick >= tick && view.Tick - tick < 12) flash = false;
            if (reduced) flash = false;
            var effective = flash && beat != MayhemDeckBeat.Watching && beat != MayhemDeckBeat.Warning ? MayhemDeckBeat.Fail : beat;
            if (_deckChannel != view.Channel || _deckBeat != effective)
            {
                _deckChannel = view.Channel; _deckBeat = effective;
                for (int i = 0; i < _patterns.Length; i++) _patterns[i].gameObject.SetActive(i == (int)look.Pattern);
                float glow = effective == MayhemDeckBeat.Call ? .32f : effective == MayhemDeckBeat.Pass ? .42f : effective == MayhemDeckBeat.SpeedUp ? .55f : effective == MayhemDeckBeat.Ended ? .4f : .16f;
                bool red = effective == MayhemDeckBeat.Watching || effective == MayhemDeckBeat.Fail;
                Tint(_panelMaterials[0], red ? new Color(.35f, .025f, .02f) : look.A, red ? .15f : glow);
                Tint(_panelMaterials[1], red ? new Color(.42f, .035f, .025f) : look.B, red ? .15f : glow);
                Tint(_panelMaterials[2], red ? new Color(.28f, .02f, .025f) : look.C, red ? .15f : glow);
                Color perimeter = effective == MayhemDeckBeat.Warning ? new Color(1, .46f, .015f)
                    : red ? new Color(.65f, .025f, .015f) : effective == MayhemDeckBeat.Safe ? look.A : look.C;
                Tint(_panelMaterials[3], perimeter, effective == MayhemDeckBeat.Warning ? .65f : .22f);
                UpdateDeckDressing(look, view.Cursor.Phase == MayhemPhase.Play);
                _poolSign.gameObject.SetActive(view.Channel?.Id == MayhemChannels.NoRunning);
                Color sign = beat == MayhemDeckBeat.Watching ? new Color(1, .05f, .02f) : beat == MayhemDeckBeat.Warning ? new Color(1, .56f, .03f) : new Color(.15f, .65f, .7f);
                Tint(_signMaterial, sign, .5f);
                _poolTitle.characterSize = beat == MayhemDeckBeat.Warning ? .14f : .18f;
                _poolTitle.text = beat == MayhemDeckBeat.Warning ? "TURNING! STOP!" : "NO RUNNING!";
                _poolState.text = beat == MayhemDeckBeat.Watching ? "POOL RULES / BASHIE IS WATCHING" : beat == MayhemDeckBeat.Warning ? "POOL RULES / EYES OPENING" : "POOL RULES / NOT LOOKING";
                _poolSign.localRotation = Quaternion.Euler(0, MankiMayhemView.FakeTurn(view) * 40, 0);
                _eye.localScale = new Vector3(1, beat == MayhemDeckBeat.Warning ? .45f : beat == MayhemDeckBeat.Watching ? 1 : .12f, 1);
                foreach (var spot in _spots) spot.color = red ? Color.red : beat == MayhemDeckBeat.Warning ? new Color(1, .45f, .02f) : look.Spot;
                Color beam = red ? Color.red : beat == MayhemDeckBeat.Warning ? sign : look.Spot;
                _beamMaterial.SetColor("_BaseColor", new Color(beam.r, beam.g, beam.b, .17f));
            }
            Color edge = beat == MayhemDeckBeat.Safe ? new Color(.12f, .8f, .75f) : beat == MayhemDeckBeat.Watching ? new Color(1, .015f, .005f) : beat == MayhemDeckBeat.Warning ? new Color(1, .45f, .01f) : effective == MayhemDeckBeat.Fail ? Color.red : beat == MayhemDeckBeat.Pass ? new Color(.4f, 1, .7f) : beat == MayhemDeckBeat.Ended ? look.Accent : new Color(1, .66f, .16f);
            int chase = (int)(view.Tick / (uint)Math.Max(3, 12 - view.Cursor.Act * 3));
            for (int i = 0; i < 4; i++)
            {
                bool on = reduced || (beat == MayhemDeckBeat.Warning ? view.Tick % 30 < 15 : look.Chase switch
                {
                    MayhemBulbChase.Alternating => (i + chase) % 2 == 0,
                    MayhemBulbChase.Outward => (Math.Abs(i - 2) + chase) % 3 == 0,
                    _ => (i + chase) % 4 == 0,
                });
                Tint(_bulbMaterials[i], edge, on ? 2 : .55f);
            }
            for (int i = 0; i < 3; i++)
            {
                float sweep = !reduced && beat == MayhemDeckBeat.Call ? Mathf.Sin(view.Tick * .025f + i) * 3 : 0;
                var target = new Vector3((i - 1) * 7 + sweep, _deckTop, -1);
                _rigs[i].rotation = Quaternion.LookRotation(target - _rigs[i].position);
            }
            bool burst = false; _burstTick = 0;
            foreach (var tick in _confettiBursts.Values)
                if (view.Tick >= tick && view.Tick - tick < 90 && (!burst || tick > _burstTick)) { burst = true; _burstTick = tick; }
            float age = burst ? (view.Tick - _burstTick) / 60f : 0;
            for (int i = 0; i < _confetti.Count; i++)
            {
                var bit = _confetti[i]; bit.gameObject.SetActive(burst);
                if (!burst) continue;
                float a = i * 2.39996f;
                bit.position = new Vector3((i % 2 == 0 ? -8 : 8) + Mathf.Cos(a) * age * 4, _deckTop + 2 + age * 7 - age * age * 5, _deckMaxZ - 1 + Mathf.Sin(a) * age * 4);
                bit.rotation = Quaternion.Euler(i * 41 + age * 180, i * 17, age * 240);
            }
        }
        private void ResetDeck()
        { _deckBeat = null; _confettiBursts.Clear(); _cancelFlashes.Clear(); foreach (var bit in _confetti) bit.gameObject.SetActive(false); }
        private void DisposeDeck()
        {
            foreach (var mesh in _deckMeshes) Object.Destroy(mesh);
            foreach (var material in _panelMaterials) Object.Destroy(material);
            foreach (var material in _bulbMaterials) Object.Destroy(material);
            foreach (var material in _confettiMaterials) Object.Destroy(material);
            DisposeDeckDressing();
            Object.Destroy(_rigInk); Object.Destroy(_beamMaterial); Object.Destroy(_signMaterial);
        }
    }
}
