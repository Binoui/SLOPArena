using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>Explicit local/online presentation dependencies. All hazard state comes from Shared.</summary>
    public sealed partial class MankiMayhemPresentation : IDisposable
    {
        public const float ShowPitch = 8f;
        public enum CameraOption { Today, Deck, TwoShots }
        public static CameraOption SelectedCamera { get; set; } = CameraOption.Deck;
        private readonly CameraMount _mount;
        private readonly Bounds _todayBounds, _deckBounds;
        private const float DeckPitch = 21f;
        private bool? _boothShot;
        private CameraOption? _appliedCamera;
        // A View-track Replay's landing rings: a marker size that never follows today's blast radius.
        private const float FixedRingRadius = 2.5f;
        public MankiHostPresenter Host { get; }
        private readonly ServerSimulation _sim;
        private readonly ProjectileVFXManager _projectiles;
        private readonly Dictionary<ulong, PlayerRenderer> _fighters;
        private readonly MankiMayhemView _view;
        private readonly Func<UnityEngine.Camera> _camera;
        private readonly Transform _root;
        private readonly Material _ink, _gold, _hot;
        private readonly List<(Transform Root, LineRenderer Fill)> _rings = new();
        private int _round = -2;
        private readonly Dictionary<string, Transform> _props = new();
        private readonly Dictionary<ulong, Transform> _waves = new();
        private readonly Dictionary<ulong, Transform> _walls = new();
        private readonly Dictionary<MayhemEventKey, Transform> _wallEffects = new();
        private readonly HashSet<ulong> _liveWallIds = new();
        private readonly List<ulong> _expiredWallIds = new();
        private readonly List<MayhemEventKey> _expiredWallEffects = new();
        private readonly AudioClip _wallHit = Resources.Load<AudioClip>("Audio/SFX/Bonk/impact_light");
        private readonly AudioClip _wallBreak = Resources.Load<AudioClip>("Audio/SFX/CoursePier/course_car_break");

        /// <summary>
        /// A Replay on its View track records where and when each bomb lands, not the blast radius or call time it was
        /// tuned with: landing rings draw at one fixed size and fill from the recorded launch to the landing.
        /// </summary>
        public bool FixedTelegraphs { get; set; }

        private float RingRadius => FixedTelegraphs ? FixedRingRadius : MayhemPresentationTuning.BlastRadius;

        public MankiMayhemPresentation(Transform parent, ServerSimulation simulation, MankiMayhemDefinition stage, MatchContentEntry manki, MatchContentCatalog content,
            IReadOnlyDictionary<ulong, PlayerRenderer> fighters, IReadOnlyList<HUDManager.HudPlayer> players,
            HUDManager hud, ProjectileVFXManager projectiles, CameraMount camera, Func<UnityEngine.Camera> renderCamera, FriendlyFireMode friendlyFire = FriendlyFireMode.Shoves)
        {
            _sim = simulation; _projectiles = projectiles; _fighters = new Dictionary<ulong, PlayerRenderer>(fighters);
            _camera = renderCamera; _mount = camera;
            _root = new GameObject("Manki Mayhem Presentation").transform;
            _root.SetParent(parent, false);
            Host = new MankiHostPresenter(_root, manki, stage);
            _projectiles.RegisterHazardOwner(ServerSimulation.MayhemHostEntityId, Host.Catalog, Host.Bomb, Host.Hand);
            _view = new MankiMayhemView(hud.Document.rootVisualElement, players, content, friendlyFire);
            _ink = Material(new Color(.13f, .025f, .025f));
            _gold = Material(new Color(1f, .73f, .04f));
            _hot = Material(new Color(1f, .18f, .035f));
            CreateShow(players);
            CreateDeck(stage);
            CreatePropMaterials();
            foreach(string name in new[] { "kit-diamonds", "beeline", "beeline-card" })
            { var element=hud.Document.rootVisualElement.Q<VisualElement>(name); if(element!=null) element.style.display=DisplayStyle.None; }
            // Fit the deck and the host, rather than letting fighters move the show's camera.
            var bounds = new Bounds(new Vector3((stage.MinX + stage.MaxX) * .5f, stage.DeckTop + 1.4f,
                (stage.MinZ + stage.MaxZ) * .5f), new Vector3(stage.MaxX - stage.MinX + 2f, 3f, stage.MaxZ - stage.MinZ + 2f));
            _todayBounds = bounds;
            _deckBounds = new Bounds(new Vector3((stage.MinX + stage.MaxX) * .5f, stage.DeckTop + 1.5f,
                (stage.MinZ + stage.MaxZ) * .5f), new Vector3(stage.MaxX - stage.MinX, 3f, stage.MaxZ - stage.MinZ));
            UpdateCamera();
            var billboard = hud.Document.rootVisualElement.Q<VisualElement>("player-billboard");
            if (billboard != null) billboard.style.display = DisplayStyle.None;
        }

        private void UpdateCamera()
        {
            var phase = _sim.MayhemView?.Cursor.Phase;
            bool booth = SelectedCamera == CameraOption.TwoShots && phase is MayhemPhase.Call or MayhemPhase.SpeedUp;
            if (_appliedCamera == SelectedCamera && _boothShot == booth) return;
            _appliedCamera = SelectedCamera; _boothShot = booth;
            if (SelectedCamera == CameraOption.Today) _mount.SetShowFraming(_todayBounds, ShowPitch);
            else if (booth)
            {
                var bounds = new Bounds(_screens[0].Root.position, Vector3.zero);
                foreach (var screen in _screens)
                {
                    bounds.Encapsulate(screen.Root.position + new Vector3(-5.3f, -3.3f, 0));
                    bounds.Encapsulate(screen.Root.position + new Vector3(5.3f, 3.3f, 0));
                }
                foreach (var renderer in Host.Root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                _mount.SetShowFraming(bounds, 8f);
            }
            else _mount.SetShowFraming(_deckBounds, DeckPitch, .95f);
        }

        private static Material Material(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            return material;
        }

        public void AfterTick(IReadOnlyList<MayhemEvent> events = null, IReadOnlyList<MayhemEvent> retracted = null)
        {
            var state = _sim.MayhemView; if (state == null) return;
            if (_round != state.Cursor.Episode)
            {
                foreach (var ring in _rings) if (ring.Root != null) Object.Destroy(ring.Root.gameObject);
                _rings.Clear();
                foreach (var prop in _props.Values) Object.Destroy(prop.gameObject);
                _props.Clear(); ResetCraftProps(); _round = state.Cursor.Episode;
            }
            UpdateBombTargets(state);
            _view.Events(events ?? _sim.LastTickMayhemEvents, retracted);
            ShowEvents(events ?? _sim.LastTickMayhemEvents, retracted);
            DeckEvents(events ?? _sim.LastTickMayhemEvents, retracted);
            UpdateGroundWaves(state);
            UpdateWalls(events ?? _sim.LastTickMayhemEvents, retracted);
            UpdateCraftProps(state);
            UpdateView();
        }

        private void UpdateWalls(IReadOnlyList<MayhemEvent> events, IReadOnlyList<MayhemEvent> retracted)
        {
            if (retracted != null)
                foreach (var e in retracted)
                    if (_wallEffects.TryGetValue(e.Key, out var effect))
                    { Object.Destroy(effect.gameObject); _wallEffects.Remove(e.Key); }
            _liveWallIds.Clear();
            foreach (var wall in _sim.MayhemView.Scene.Solids)
            {
                _liveWallIds.Add(wall.EntityId);
                if (!_walls.TryGetValue(wall.EntityId, out var root)) { root = CardboardWall(wall); _walls.Add(wall.EntityId, root); }
                CrumpleWall(root, wall);
                float height = wall.HalfExtents.Y * 2;
                // Without a hit budget the wall is a View-track recording: drawn upright while it stands, with no
                // rise, sink or cracks from today's tuning.
                bool neutral = wall.HitBudget == 0;
                float rise = neutral ? 1f
                    : wall.Phase == RuntimeSolidPhase.Rising ? Mathf.Clamp01(wall.PhaseTick / (float)MayhemPresentationTuning.WallRiseTicks)
                    : wall.Phase == RuntimeSolidPhase.Sinking ? 1f - Mathf.Clamp01(wall.PhaseTick / (float)MayhemPresentationTuning.WallSinkTicks) : 1f;
                root.position = new Vector3(wall.Center.X, wall.Center.Y - height * (1 - rise), wall.Center.Z);
                root.rotation = Quaternion.Euler(0, wall.Yaw * Mathf.Rad2Deg, 0);
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    child.gameObject.SetActive(wall.HitsLeft > 0);
                }
            }
            _expiredWallIds.Clear();
            foreach (var id in _walls.Keys) if (!_liveWallIds.Contains(id)) _expiredWallIds.Add(id);
            foreach (var id in _expiredWallIds) { Object.Destroy(_walls[id].gameObject); _walls.Remove(id); }
            foreach (var e in events)
            {
                if (e.Kind is not (MayhemEventKind.SolidHit or MayhemEventKind.SolidBroken) || _wallEffects.ContainsKey(e.Key)) continue;
                var wall = _sim.MayhemView.Scene.Solids.FirstOrDefault(w => w.EntityId == e.Subject);
                if (wall.EntityId == 0) continue;
                var root = new GameObject("Wall impact debris").transform; root.SetParent(_root, false);
                root.position = new Vector3(wall.Center.X, wall.Center.Y + wall.HalfExtents.Y * .6f, wall.Center.Z);
                var audio = root.gameObject.AddComponent<AudioSource>(); audio.spatialBlend = 0f; audio.volume = .18f;
                audio.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
                audio.clip = e.Kind == MayhemEventKind.SolidBroken ? _wallBreak : _wallHit;
                if (audio.clip != null && PresentationOutput.TryPlay()) audio.Play();
                for (int i = 0; i < (e.Kind == MayhemEventKind.SolidBroken ? 18 : 12); i++)
                {
                    var piece = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(piece.GetComponent<Collider>());
                    piece.transform.SetParent(root, false); piece.transform.localScale = new Vector3(.48f, .07f, .32f);
                    piece.GetComponent<Renderer>().sharedMaterial = _cardboard;
                }
                for (int i = 0; i < 6; i++)
                    DeckObject(root, "Cardboard dust", _sphereMesh, _dust, Vector3.zero, Vector3.one * .35f);
                _wallEffects.Add(e.Key, root);
            }
            _expiredWallEffects.Clear();
            foreach (var pair in _wallEffects)
            {
                float age = ((long)_sim.CurrentTick - pair.Key.MatchTick) / 60f;
                if (age < 0 || age > .7f) { Object.Destroy(pair.Value.gameObject); _expiredWallEffects.Add(pair.Key); continue; }
                for (int i = 0; i < pair.Value.childCount; i++)
                {
                    var piece = pair.Value.GetChild(i); float angle = (i * 2.4f + pair.Key.Operation) % (Mathf.PI * 2);
                    piece.localPosition = new Vector3(Mathf.Sin(angle) * age * 3, age * 3 - age * age * 7, Mathf.Cos(angle) * age * 3);
                    piece.localRotation = Quaternion.Euler(i * 27 + age * 160, age * 200, i * 36);
                    if (piece.name == "Cardboard dust") piece.localScale = Vector3.one * (.35f + age * .8f) * Mathf.Clamp01((.7f - age) * 4);
                }
            }
            foreach (var key in _expiredWallEffects) _wallEffects.Remove(key);
        }

        public void UpdateView()
        {
            UpdateCamera();
            UpdateShow();
            if (_sim.MayhemView != null) UpdateDeck(_sim.MayhemView);
            _view.Present(_sim, id => _fighters[id].transform.position, _camera());
        }

        private static Vector3 Point(ArcadePoint p) => new(p.X,p.Y,p.Z);
        private Transform Prop(string key, PrimitiveType type, Material material)
        {
            if (_props.TryGetValue(key, out var found)) return found;
            var go = GameObject.CreatePrimitive(type); go.name = key; Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false); go.GetComponent<Renderer>().sharedMaterial = material;
            _props.Add(key, go.transform); return go.transform;
        }
        private static class MayhemPresentationTuning
        {
            public const float BlastRadius = 2.5f;
            public const int CallTicks = 60, SlamFlashPeriodTicks = 12, SlamFlashOnTicks = 6, WallRiseTicks = 18, WallSinkTicks = RuntimeSolidTuning.WallSinkTicks;
            public const int CrackedWallHits = RuntimeSolidTuning.CrackedWallHits, WallHits = 3;
        }

        private LineRenderer Circle(Transform parent, string name, Material material, float width, float progress,
            float radius = MayhemPresentationTuning.BlastRadius)
        {
            var line = Line(parent, name, material, width); SetCircle(line, progress, radius); return line;
        }
        private static LineRenderer Line(Transform parent, string name, Material material, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false; line.sharedMaterial = material;
            line.startWidth = line.endWidth = width;
            line.numCornerVertices = line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; return line;
        }
        private static void SetCircle(LineRenderer line, float progress, float radius = MayhemPresentationTuning.BlastRadius)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(64 * progress) + 1);
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)(count - 1) * Mathf.PI * 2f * progress;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
        }
        /// <summary>A Replay seek: rings, waves, walls, wall debris, stings, score stamps and host reactions start over.</summary>
        public void Reset()
        {
            static void Remove(Transform t) { if (t == null) return; t.gameObject.SetActive(false); Object.Destroy(t.gameObject); }
            foreach (var ring in _rings) Remove(ring.Root);
            _rings.Clear(); ResetCraftProps(); _round = -2;
            foreach (var prop in _props.Values) Remove(prop); _props.Clear();
            foreach (var wave in _waves.Values) Remove(wave);
            _waves.Clear();
            foreach (var wall in _walls.Values) Remove(wall);
            _walls.Clear();
            foreach (var effect in _wallEffects.Values) Remove(effect);
            _wallEffects.Clear();
            foreach (var sound in _showSounds.Values) if (sound != null) Remove(sound.transform);
            _showSounds.Clear();
            ResetDeck();
            _view.Reset();
            Host.Reset();
        }

        public void Dispose()
        {
            _projectiles.UnregisterOwner(ServerSimulation.MayhemHostEntityId);
            DisposeCraftProps();
            DisposeDeck();
            DisposeShow();
            _view.Dispose(); Host.Dispose();
            Object.Destroy(_root.gameObject);
            Object.Destroy(_ink); Object.Destroy(_gold); Object.Destroy(_hot);
        }
    }
}
