using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>Explicit local/online presentation dependencies. All hazard state comes from Shared.</summary>
    public sealed partial class MankiMayhemPresentation : IDisposable
    {
        public const float ShowPitch = 8f;
        public MankiHostPresenter Host { get; }
        private readonly ServerSimulation _sim;
        private readonly ProjectileVFXManager _projectiles;
        private readonly Dictionary<ulong, PlayerRenderer> _fighters;
        private readonly MankiMayhemView _view;
        private readonly Func<UnityEngine.Camera> _camera;
        private readonly Transform _root;
        private readonly Material _ink, _gold, _hot;
        private readonly List<(Transform Root, LineRenderer Fill)> _rings = new();
        private ushort _round = ushort.MaxValue;
        private readonly Dictionary<ulong, Transform> _waves = new();
        private readonly Dictionary<ulong, Transform> _walls = new();
        private readonly Dictionary<MayhemEventKey, Transform> _wallEffects = new();
        private Transform _slamMark;
        private readonly AudioClip _wallHit = Resources.Load<AudioClip>("Audio/SFX/Bonk/impact_light");
        private readonly AudioClip _wallBreak = Resources.Load<AudioClip>("Audio/SFX/CoursePier/course_car_break");

        public MankiMayhemPresentation(Transform parent, ServerSimulation simulation, MankiMayhemDefinition stage, MatchContentEntry manki,
            IReadOnlyDictionary<ulong, PlayerRenderer> fighters, IReadOnlyList<HUDManager.HudPlayer> players,
            HUDManager hud, ProjectileVFXManager projectiles, CameraMount camera, Func<UnityEngine.Camera> renderCamera, FriendlyFireMode friendlyFire = FriendlyFireMode.Shoves)
        {
            _sim = simulation; _projectiles = projectiles; _fighters = new Dictionary<ulong, PlayerRenderer>(fighters);
            _camera = renderCamera;
            _root = new GameObject("Manki Mayhem Presentation").transform;
            _root.SetParent(parent, false);
            Host = new MankiHostPresenter(_root, manki, stage);
            _projectiles.RegisterHazardOwner(ServerSimulation.MayhemHostEntityId, Host.Catalog, Host.Bomb, Host.Hand);
            _view = new MankiMayhemView(hud.Document.rootVisualElement, players, friendlyFire);
            _ink = Material(new Color(.13f, .025f, .025f));
            _gold = Material(new Color(1f, .73f, .04f));
            _hot = Material(new Color(1f, .18f, .035f));
            CreateShow(players);
            // Fit the deck and the host, rather than letting fighters move the show's camera.
            var bounds = new Bounds(new Vector3((stage.MinX + stage.MaxX) * .5f, stage.DeckTop + 1.4f,
                (stage.MinZ + stage.MaxZ) * .5f), new Vector3(stage.MaxX - stage.MinX + 2f, 3f, stage.MaxZ - stage.MinZ + 2f));
            camera.SetShowFraming(bounds, ShowPitch);
            var billboard = hud.Document.rootVisualElement.Q<VisualElement>("player-billboard");
            if (billboard != null) billboard.style.display = DisplayStyle.None;
        }

        private static Material Material(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            return material;
        }

        public void AfterTick(IReadOnlyList<MankiMayhemEvent> events = null, IReadOnlyList<MankiMayhemEvent> retracted = null)
        {
            if (_sim.MankiMayhem is not MankiMayhemState state) return;
            if (_round != state.RoundIndex)
            {
                foreach (var ring in _rings) Object.Destroy(ring.Root.gameObject);
                _rings.Clear(); _round = state.RoundIndex;
            }
            foreach (var ring in _sim.MayhemLandingRings)
            {
                if (ring.Ordinal >= _rings.Count)
                {
                    var root = new GameObject($"Landing ring {ring.Ordinal}").transform;
                    root.SetParent(_root, false);
                    root.position = new Vector3(ring.Position.X, ring.Position.Y + .18f, ring.Position.Z);
                    Circle(root, "Dark rim", _ink, .25f, 1f);
                    var gold = Circle(root, "Gold warning", _gold, .12f, 1f);
                    gold.transform.localPosition = Vector3.up * .015f;
                    var fill = Circle(root, "Impact countdown", _hot, .1f, 0f);
                    fill.transform.localPosition = Vector3.up * .03f;
                    // Raised ribs keep a flat ring legible at the low show pitch.
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4f;
                        var line = Line(root, "Raised warning", _gold, .09f);
                        var point = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * MankiMayhemTuning.BlastRadius;
                        line.positionCount = 2;
                        line.SetPosition(0, point); line.SetPosition(1, point + Vector3.up * .55f);
                    }
                    _rings.Add((root, fill));
                }
                _rings[ring.Ordinal].Root.position = new Vector3(ring.Position.X, ring.Position.Y + .18f, ring.Position.Z);
            }
            _view.Events(events ?? _sim.LastTickMayhemEvents, retracted);
            ShowEvents(events ?? _sim.LastTickMayhemEvents, retracted);
            foreach (var visual in _rings) visual.Root.gameObject.SetActive(false);
            foreach (var ring in _sim.MayhemLandingRings)
            {
                bool attached = _sim.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == ServerSimulation.MayhemHostEntityId
                    && h.VisualOperationIndex == ring.Ordinal && h.AttachedEntityId != 0);
                var visual = _rings[ring.Ordinal];
                visual.Root.gameObject.SetActive(!attached && _sim.CurrentTick <= ring.LandingTick
                    && state.Phase is MankiMayhemPhase.Call or MankiMayhemPhase.Pattern or MankiMayhemPhase.Settle);
                float duration = MankiMayhemTuning.CallTicks + MankiMayhemTuning.FlightTicks - 1;
                float progress = Mathf.Clamp01(1f - (ring.LandingTick - Math.Min(ring.LandingTick, _sim.CurrentTick)) / duration);
                SetCircle(visual.Fill, progress);
            }
            UpdateWaves();
            UpdateWalls(events ?? _sim.LastTickMayhemEvents, retracted);
            UpdateView();
        }

        private void UpdateWaves()
        {
            if (_slamMark == null)
            {
                _slamMark = new GameObject("Slam point warning").transform; _slamMark.SetParent(_root, false);
                Circle(_slamMark, "Slam point dark rim", _ink, .28f, 1f);
                Circle(_slamMark, "Slam point flash", _gold, .15f, 1f);
                _slamMark.localScale = new Vector3(.3f, 1f, .3f);
            }
            var next = _sim.MayhemSlams.FirstOrDefault(s => s.Tick >= _sim.CurrentTick);
            bool warning = next.Tick > 0 && next.Tick - _sim.CurrentTick <= MankiMayhemTuning.CallTicks;
            _slamMark.gameObject.SetActive(warning);
            if (warning)
            {
                _slamMark.position = new Vector3(next.Position.X, next.Position.Y + .15f, next.Position.Z);
                _slamMark.GetChild(1).gameObject.SetActive(ClientSettingsService.Instance.ReducedFlashing
                    || _sim.CurrentTick % MankiMayhemTuning.SlamFlashPeriodTicks < MankiMayhemTuning.SlamFlashOnTicks);
            }
            var live = new HashSet<ulong>();
            foreach (var hitbox in _sim.Resolver.GetActiveHitboxes())
            {
                if (hitbox.Shape != HitboxShape.Ring) continue;
                live.Add(hitbox.EmissionId);
                if (!_waves.TryGetValue(hitbox.EmissionId, out var root))
                {
                    root = new GameObject("JUMP shockwave").transform;
                    root.SetParent(_root, false);
                    Circle(root, "Dark wave edge", _ink, .3f, 1f);
                    var rim = Circle(root, "Gold wave crest", _gold, .15f, 1f);
                    rim.transform.localPosition = Vector3.up * .12f;
                    Circle(root, "Hot wave band", _hot, .12f, 1f);
                    _waves.Add(hitbox.EmissionId, root);
                }
                root.position = new Vector3(hitbox.X, hitbox.Y, hitbox.Z);
                root.localScale = new Vector3(hitbox.Radius / MankiMayhemTuning.BlastRadius, 1f,
                    hitbox.Radius / MankiMayhemTuning.BlastRadius);
            }
            foreach (var id in _waves.Keys.Where(id => !live.Contains(id)).ToArray())
            { Object.Destroy(_waves[id].gameObject); _waves.Remove(id); }
        }

        private Transform Wall(RuntimeSolid wall)
        {
            var root = new GameObject($"Cage wall {wall.EntityId}").transform; root.SetParent(_root, false);
            for (int rail = 0; rail < 5; rail++)
            {
                var plank = GameObject.CreatePrimitive(PrimitiveType.Cube); plank.name = $"Wall rail {rail}";
                Object.Destroy(plank.GetComponent<Collider>()); plank.transform.SetParent(root, false);
                plank.transform.localPosition = new Vector3(0, -wall.HalfExtents.Y + .45f + rail * (wall.HalfExtents.Y * 2 - .8f) / 4f, 0);
                plank.transform.localScale = new Vector3(wall.HalfExtents.X * 2, rail == 0 ? .55f : .14f, wall.HalfExtents.Z * 2);
                plank.GetComponent<Renderer>().sharedMaterial = wall.HitBudget == MankiMayhemTuning.CrackedWallHits ? _gold : _hot;
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube); post.name = "Cage post";
                Object.Destroy(post.GetComponent<Collider>()); post.transform.SetParent(root, false);
                post.transform.localPosition = new Vector3(side * (wall.HalfExtents.X - .1f), 0, 0);
                post.transform.localScale = new Vector3(.15f, wall.HalfExtents.Y * 2, .2f);
                post.GetComponent<Renderer>().sharedMaterial = _gold;
            }
            for (int stage = 0; stage < MankiMayhemTuning.WallHits; stage++)
            {
                var crack = Line(root, $"Crack {stage}", _ink, .12f); crack.positionCount = 4;
                float x = -.6f + stage * .5f;
                crack.SetPositions(new[] { new Vector3(x, -wall.HalfExtents.Y + .7f, -.2f), new Vector3(x + .3f, -wall.HalfExtents.Y + .4f, -.2f),
                    new Vector3(x, -wall.HalfExtents.Y + .1f, -.2f), new Vector3(x + .5f, -wall.HalfExtents.Y + .3f, -.2f) });
            }
            return root;
        }

        private void UpdateWalls(IReadOnlyList<MankiMayhemEvent> events, IReadOnlyList<MankiMayhemEvent> retracted)
        {
            if (retracted != null)
                foreach (var e in retracted)
                    if (_wallEffects.TryGetValue(e.Key, out var effect))
                    { Object.Destroy(effect.gameObject); _wallEffects.Remove(e.Key); }
            var live = new HashSet<ulong>();
            foreach (var wall in _sim.MayhemWalls)
            {
                live.Add(wall.EntityId);
                if (!_walls.TryGetValue(wall.EntityId, out var root)) { root = Wall(wall); _walls.Add(wall.EntityId, root); }
                float height = wall.HalfExtents.Y * 2;
                float rise = wall.Phase == RuntimeSolidPhase.Rising ? Mathf.Clamp01(wall.PhaseTick / (float)MankiMayhemTuning.WallRiseTicks)
                    : wall.Phase == RuntimeSolidPhase.Sinking ? 1f - Mathf.Clamp01(wall.PhaseTick / (float)MankiMayhemTuning.WallSinkTicks) : 1f;
                root.position = new Vector3(wall.Center.X, wall.Center.Y - height * (1 - rise), wall.Center.Z);
                root.rotation = Quaternion.Euler(0, wall.Yaw * Mathf.Rad2Deg, 0);
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    child.gameObject.SetActive(wall.HitsLeft > 0 && (!child.name.StartsWith("Crack")
                        || int.Parse(child.name.Substring(6)) < wall.HitBudget - wall.HitsLeft + (wall.HitBudget == MankiMayhemTuning.CrackedWallHits ? 1 : 0)));
                }
            }
            foreach (var id in _walls.Keys.Where(id => !live.Contains(id)).ToArray())
            { Object.Destroy(_walls[id].gameObject); _walls.Remove(id); }
            foreach (var e in events)
            {
                if (e.Kind is not (MankiMayhemEventKind.WallHit or MankiMayhemEventKind.WallBroken) || _wallEffects.ContainsKey(e.Key)) continue;
                var wall = _sim.MayhemWalls.FirstOrDefault(w => w.EntityId == e.EntityId);
                if (wall.EntityId == 0) continue;
                var root = new GameObject("Wall impact debris").transform; root.SetParent(_root, false);
                root.position = new Vector3(wall.Center.X, .8f, wall.Center.Z);
                var audio = root.gameObject.AddComponent<AudioSource>(); audio.spatialBlend = 0f; audio.volume = .18f;
                audio.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
                audio.clip = e.Kind == MankiMayhemEventKind.WallBroken ? _wallBreak : _wallHit;
                if (audio.clip != null) audio.Play();
                for (int i = 0; i < (e.Kind == MankiMayhemEventKind.WallBroken ? 8 : 3); i++)
                {
                    var piece = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(piece.GetComponent<Collider>());
                    piece.transform.SetParent(root, false); piece.transform.localScale = new Vector3(.35f, .15f, .18f);
                    piece.GetComponent<Renderer>().sharedMaterial = _gold;
                }
                _wallEffects.Add(e.Key, root);
            }
            foreach (var pair in _wallEffects.ToArray())
            {
                float age = ((long)_sim.CurrentTick - pair.Key.MatchTick) / 60f;
                if (age < 0 || age > .7f) { Object.Destroy(pair.Value.gameObject); _wallEffects.Remove(pair.Key); continue; }
                for (int i = 0; i < pair.Value.childCount; i++)
                {
                    var piece = pair.Value.GetChild(i); float angle = (i * 2.4f + pair.Key.OperationIndex) % (Mathf.PI * 2);
                    piece.localPosition = new Vector3(Mathf.Sin(angle) * age * 3, age * 3 - age * age * 7, Mathf.Cos(angle) * age * 3);
                    piece.localRotation = Quaternion.Euler(i * 27 + age * 160, age * 200, i * 36);
                }
            }
        }

        public void UpdateView()
        {
            UpdateShow();
            _view.Present(_sim, id => _fighters[id].transform.position, _camera());
        }

        private LineRenderer Circle(Transform parent, string name, Material material, float width, float progress)
        {
            var line = Line(parent, name, material, width); SetCircle(line, progress); return line;
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
        private static void SetCircle(LineRenderer line, float progress)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(64 * progress) + 1);
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)(count - 1) * Mathf.PI * 2f * progress;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * MankiMayhemTuning.BlastRadius);
            }
        }
        public void Dispose()
        {
            _projectiles.UnregisterOwner(ServerSimulation.MayhemHostEntityId);
            DisposeShow();
            _view.Dispose(); Host.Dispose();
            Object.Destroy(_root.gameObject);
            Object.Destroy(_ink); Object.Destroy(_gold); Object.Destroy(_hot);
        }
    }
}
