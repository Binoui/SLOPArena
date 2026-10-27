using System.Linq;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.Input;
using SlopArena.Client.Entities;
using SlopArena.Client.Animation;
using SlopArena.Client.Simulation;

namespace SlopArena.Client.World
{
    public abstract class MatchBase : MonoBehaviour
    {
        public static MatchBase Active { get; private set; }

        protected virtual void Awake() => Active = this;

        // Subclasses provide the bridge (local sim or network)
        protected abstract ISimulationBridge Bridge { get; }

        [Header("Entities")]
        [SerializeField] protected PlayerRenderer _playerRenderer;

        [Header("Player Character Assets (drag-drop — fallback to Resources.Load if empty)")]
        [SerializeField] private GameObject _playerModelPrefab;
        [SerializeField] private CharacterAnimationConfig _playerAnimConfig;
        [SerializeField] private WeaponAttachConfig _playerWeaponConfig;

        [Header("Input")]
        [SerializeField] protected InputController _inputController;
        [SerializeField] protected CameraMount _cameraMount;
        [Header("Aiming")]
        [SerializeField] protected AimHandler _aimHandler;
        [SerializeField] protected HUDManager _hudManager;
        [SerializeField] private Texture2D _crosshairTexture;
        [SerializeField] private float _crosshairSize = 32f;

        // Read from MatchConfig so PvP can use the master-assigned entity ID
        // (issue #35); training leaves it at the default 1.
        protected static ulong PlayerEntityId => MatchConfig.LocalEntityId;

        protected bool _showCrosshair;
        protected CharacterDefinition _playerDef = null!;
        protected UnityEngine.Camera _mainCamera;
        private readonly TimelinePresentationDispatcher _timelinePresentations = new();
        private readonly Dictionary<ulong, WeaponAttach> _weapons = new();
        private readonly HashSet<ulong> _activeSwordOwners = new();
        protected MatchPauseMenu _pauseMenu;
        private Mesh _cameraCollisionMesh;

        /// <summary>True while the in-match pause menu is open (issue #77).</summary>
        protected bool IsPaused => _pauseMenu != null && _pauseMenu.IsPaused;

        protected abstract void OnMatchStart();
        protected abstract void OnMatchFixedUpdate();

        private void Start()
        {
            UISFX.PlayMatchMusic(MatchConfig.ArenaName);
            _pauseMenu = gameObject.AddComponent<MatchPauseMenu>();
            _pauseMenu.Init(_cameraMount, _inputController, LeaveMatch,
                _hudManager != null ? _hudManager.Document : null,
                freezeSimulation: this is not PvPMatch, onRetry: PauseRetry);
            OnMatchStart();
            gameObject.AddComponent<MatchVisualStyle>().Apply();
        }
        private void FixedUpdate()
        {
            OnMatchFixedUpdate();
            UpdateCameraFraming();
        }

        private readonly List<FramingSubject> _framingSubjects = new();

        private void UpdateCameraFraming()
        {
            if (_cameraMount == null || _playerRenderer == null || Bridge == null) return;

            var states = Bridge.GetAllStates();
            if (!states.TryGetValue(PlayerEntityId, out var player)) return;
            var playerPosition = new Vector3(player.PX, player.PY, player.PZ);
            var bounds = new Bounds(playerPosition, Vector3.zero);
            Vector3? target = null;
            _framingSubjects.Clear();
            float nearestDistance = float.PositiveInfinity;
            ulong nearestId = ulong.MaxValue;
            bool stockMatch = MatchConfig.Mode == GameMode.Solo && !MatchConfig.IsLocalMayhem
                || MatchConfig.Mode == GameMode.PvP && MatchConfig.Format == MatchFormat.Stock;
            foreach (var pair in states)
            {
                if (pair.Key == PlayerEntityId || stockMatch && pair.Value.Deaths >= MatchConfig.MaxStocks)
                    continue;
                var state = pair.Value;
                var position = new Vector3(state.PX, state.PY, state.PZ);
                bounds.Encapsulate(position);
                _framingSubjects.Add(new FramingSubject(pair.Key, position,
                    !state.IsGrounded && (state.HitstunTicks > 0 || state.InPostHitstunFlight)));
                // Prefer Shared's selected target. The nearest fallback is camera-only:
                // it never changes target selection, facing, or attack admission.
                float distance = (position - playerPosition).sqrMagnitude;
                if (pair.Key == player.TargetEntityId)
                {
                    target = position;
                    nearestDistance = -1f;
                }
                else if (distance < nearestDistance || distance == nearestDistance && pair.Key < nearestId)
                {
                    target = position;
                    nearestDistance = distance;
                    nearestId = pair.Key;
                }
            }
            _cameraMount.SetFramingSubjects(playerPosition, target, bounds, player.IsGrounded, _framingSubjects);
        }
        protected virtual void OnDestroy()
        {
            if (Active == this) Active = null;
            PresentationFloor.Clear();
            _aimHandler?.ResetPresentation();
            _timelinePresentations.Clear();
            foreach (var weapon in _weapons.Values)
                if (weapon != null) weapon.SetHitboxTrailActive(false);
            _weapons.Clear();
            if (_cameraCollisionMesh != null)
                Destroy(_cameraCollisionMesh);
        }

        // ── Leave match (pause menu) ─────────────────────────────────────────

        /// <summary>
        /// Pause-menu "LEAVE MATCH": mode-specific early exit (issue #210).
        /// Solo and Training return to the frontend Fighter Select page with
        /// their local choices retained; PvPMatch overrides this to leave its
        /// GameServer membership. The persistent chat connection survives.
        /// The UDP client cleans itself up on scene unload.
        /// </summary>
        protected virtual void LeaveMatch()
        {
            FrontendController.Show(FrontendPage.FighterSelect);
        }

        /// <summary>Arcade challenges offer a full fresh attempt from the pause menu; other modes do not.</summary>
        protected virtual Action PauseRetry => null;

        // ── Shared setup helpers ────────────────────────────────────────────

        protected bool SetupPlayerRenderer(MatchContentEntry entry, ArenaDefinition arena)
            => SetupRenderer(_playerRenderer, entry, arena, PlayerEntityId, true);

        protected bool SetupRenderer(
            PlayerRenderer renderer, MatchContentEntry entry, ArenaDefinition arena,
            ulong entityId, bool local)
        {
            if (renderer == null || entry == null)
            {
                Debug.LogError($"[{GetType().Name}] Renderer and content entry are required.");
                return false;
            }

            var def = entry.Definition;
            CharacterAnimationCatalog animationCatalog = null;
            GameObject rig = null;
            WeaponAttachConfig cookedWeaponConfig = null;
            if (entry.CookedCharacterPackage != null &&
                !CookedCharacterClientAssetResolver.TryResolve(entry, out animationCatalog, out rig, out cookedWeaponConfig, out var error))
            {
                Debug.LogError($"[{GetType().Name}] Cooked client assets failed for {entry.Identity.PackageId}: {error}");
                return false;
            }

            var animationOverride = local && entry.CookedCharacterPackage == null ? _playerAnimConfig : null;
            if (!renderer.Configure(def, entry.BakedAnimation, animationCatalog,
                    rig ?? (local ? _playerModelPrefab : null), out var configurationError, animationOverride))
            {
                Debug.LogError($"[{GetType().Name}] Renderer configuration failed: {configurationError}");
                return false;
            }

            renderer.EntityId = entityId;
            renderer.SetBlastLines(arena);
            var projectiles = FindFirstObjectByType<ProjectileVFXManager>() ?? gameObject.AddComponent<ProjectileVFXManager>();
            projectiles.RegisterOwner(entityId, animationCatalog, renderer);
            renderer.BindProjectilePresentation(projectiles);
            var feedback = FindFirstObjectByType<CombatFeedback>() ?? gameObject.AddComponent<CombatFeedback>();
            feedback.RegisterRenderer(renderer);
            _timelinePresentations.Register(entityId, renderer, animationCatalog);

            var weaponConfig = entry.CookedCharacterPackage != null
                ? cookedWeaponConfig
                : local && _playerWeaponConfig != null
                    ? _playerWeaponConfig
                    : Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{def.Class}");
            var weapon = renderer.GetComponent<WeaponAttach>() ?? renderer.gameObject.AddComponent<WeaponAttach>();
            weapon.Init(renderer, weaponConfig);
            _weapons[entityId] = weapon;
            return true;
        }
        protected void PresentTimelineEvents()
            => _timelinePresentations.Tick(Bridge.LastTickPresentationEvents);

        protected void RegisterHostPresentation(ulong id, Transform root, CharacterAnimationCatalog catalog)
            => _timelinePresentations.RegisterHost(id, root, catalog);

        /// <summary>Online rollback: stop the presentations of events the GameHost ruled out.</summary>
        protected void RetractTimelineEvents(IReadOnlyList<TimelinePresentationEvent> events)
            => _timelinePresentations.Retract(events);

        /// <summary>Arcade retry: the bridge now runs a fresh simulation; end what the old one presented.</summary>
        protected void ResetSimulationPresentation()
        {
            _timelinePresentations.ResetForFreshSimulation();
            _activeSwordOwners.Clear();
            ApplySwordTrailOwners();
            _aimHandler?.ResetPresentation();
            _showCrosshair = false;
        }

        /// <summary>Show sword trails for the simulation's active melee hitboxes (Training, and
        /// the predicted world online), including early removal.</summary>
        protected void UpdateSwordTrailsFromResolver(SpellResolver resolver)
        {
            _activeSwordOwners.Clear();
            if (resolver != null)
                foreach (var hitbox in resolver.GetActiveHitboxes())
                    if (IsSwordHitbox(in hitbox))
                        _activeSwordOwners.Add(hitbox.OwnerId);
            ApplySwordTrailOwners();
        }

        private void ApplySwordTrailOwners()
        {
            foreach (var entry in _weapons)
                if (entry.Value != null)
                    entry.Value.SetHitboxTrailActive(_activeSwordOwners.Contains(entry.Key));
        }

        internal static bool IsSwordHitbox(in Hitbox hitbox)
            => hitbox.Active && hitbox.TracksBone
                && hitbox.SourceEvent.BoneName == "_weapon_hilt"
                && hitbox.SourceEvent.EndBoneName == "_weapon_tip";

        protected void SetupCamera()
        {
            if (_cameraMount == null) return;
            _cameraMount.SetTarget(_playerRenderer.transform);
            _cameraMount.ResetView(_playerRenderer.transform);
            _hudManager?.SetCamera(_cameraMount?.RenderCamera ?? UnityEngine.Camera.main);
            var brain = FindFirstObjectByType<CinemachineBrain>();
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, 0.2f);
        }

        /// <summary>
        /// Instantiate the stage's visual prefab (Resources/Stages/&lt;arena.Name&gt;.prefab)
        /// under a "Stage" root. Gameplay collision comes from the baked .arena.
        /// A Unity mesh mirrors those triangles only for camera obstruction queries.
        /// </summary>
        protected Transform StageVisual { get; private set; }

        protected void SpawnStageVisual(ArenaDefinition arena, bool cameraObstruction = true)
        {
            PresentationFloor.Use(arena.Heightmap);
            CameraObstruction.SeeThroughThinWalls = false;
            // Prototype: only the Slopball court, in every mode that plays on it, lets the camera ignore walls.
            CameraObstruction.CutAwayOccluders = cameraObstruction && arena.Name == SlopballMatch.ArenaKey;
            var stageRoot = new GameObject("Stage");
            var triangles = arena.CollisionTriangles;
            if (cameraObstruction && triangles != null && triangles.Length > 0)
            {
                var vertices = new Vector3[triangles.Length * 3];
                var indices = new int[triangles.Length * 6];
                for (int i = 0; i < triangles.Length; i++)
                {
                    var triangle = triangles[i];
                    int v = i * 3;
                    vertices[v] = new Vector3(triangle.AX, triangle.AY, triangle.AZ);
                    vertices[v + 1] = new Vector3(triangle.BX, triangle.BY, triangle.BZ);
                    vertices[v + 2] = new Vector3(triangle.CX, triangle.CY, triangle.CZ);
                    // Shared triangles are two-sided; camera queries must be too.
                    int t = i * 6;
                    indices[t] = indices[t + 3] = v;
                    indices[t + 1] = indices[t + 5] = v + 1;
                    indices[t + 2] = indices[t + 4] = v + 2;
                }
                _cameraCollisionMesh = new Mesh
                {
                    name = $"{arena.Name} Camera Collision",
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                    vertices = vertices,
                    triangles = indices
                };
                // Excluded from default aim raycasts; cameras explicitly query this layer.
                var collision = new GameObject("Camera Collision") { layer = LayerMask.NameToLayer("Ignore Raycast") };
                collision.transform.SetParent(stageRoot.transform, false);
                collision.AddComponent<MeshCollider>().sharedMesh = _cameraCollisionMesh;
            }
            var prefab = Resources.Load<GameObject>($"Stages/{arena.Name}");
            if (prefab == null)
            {
                Debug.LogWarning($"[{GetType().Name}] No stage visual '{arena.Name}' (missing Resources/Stages/{arena.Name}.prefab) — running collision-only.");
                return;
            }
            var visual = Instantiate(prefab, stageRoot.transform);
            StageVisual = visual.transform;
            // A stage whose art exports the see-through anchor (Hamster Habitat's clear tubes)
            // lets the camera look through its thin walls; every other stage keeps them solid.
            foreach (var child in visual.GetComponentsInChildren<Transform>(true))
                if (child.name == CameraObstruction.SeeThroughMarker)
                    CameraObstruction.SeeThroughThinWalls = true;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            // A stage whose art command built a "Stage Lights" rig is lit by that rig alone, as in
            // Arena_PvP, which has no lights. Switch off the scene's own directional lights (Arena_Offline's
            // key/fill/rim) so Training and Solo match PvP instead of stacking both and washing out.
            if (visual.transform.Find("Stage Lights") != null)
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional && !light.transform.IsChildOf(visual.transform))
                        light.enabled = false;
        }

        protected void SetupHUD(CharacterDefinition def, IReadOnlyList<HUDManager.HudPlayer>? extraPlayers = null)
        {
            // One panel per player (local + opponents), sorted by entity ID so
            // panels read P1..PN left to right regardless of who is local (issue #38).
            // extraPlayers: training NPCs (not in MatchConfig.Opponents — that is a
            // PvP-only roster) get a panel the same way, so their damage % shows.
            var players = new List<HUDManager.HudPlayer>(
                MatchConfig.Opponents.Count + 1 + (extraPlayers?.Count ?? 0))
            {
                OnlineHudPlayer(PlayerEntityId, def.Class, isLocal: true)
            };
            foreach (var opp in MatchConfig.Opponents)
                players.Add(OnlineHudPlayer(opp.EntityId, opp.Class, isLocal: false));
            if (extraPlayers != null)
                players.AddRange(extraPlayers);
            players.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            int maxStocks = MatchConfig.Mode is GameMode.Solo or GameMode.PvP
                ? MatchConfig.MaxStocks
                : 0;
            if (MatchConfig.Format.IsSlopball() || MatchConfig.Format.IsCourse() || MatchConfig.Format == MatchFormat.MankiMayhem) maxStocks = 0;
            _hudManager?.Initialize(Bridge.GetState, players, maxStocks);
            _hudManager?.SetCharacterDefinition(def);
            _hudManager?.SetCamera(_cameraMount?.RenderCamera ?? UnityEngine.Camera.main);
        }

        /// <summary>
        /// A fight card for one match entity. Online Room matches carry the
        /// Master roster, which names the player and ties their chat identity
        /// to the fighter for speech slips; offline cards keep the fighter name.
        /// </summary>
        protected static HUDManager.HudPlayer OnlineHudPlayer(ulong entityId, CharacterClass @class, bool isLocal)
        {
            string label = $"P{entityId}";
            if (MatchConfig.Mode == GameMode.PvP && ClientSession.MatchRoster != null)
                foreach (var member in ClientSession.MatchRoster)
                {
                    if (member.EntityId <= 0 || (ulong)member.EntityId != entityId)
                        continue;
                    string name = string.IsNullOrWhiteSpace(member.Name) ? null : member.Name;
                    string chatId = member.SteamId > 0
                        ? member.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null;
                    return new HUDManager.HudPlayer(entityId, label, @class, isLocal, name, chatId, playerNumber: (int)entityId);
                }
            return new HUDManager.HudPlayer(entityId, label, @class, isLocal, playerNumber: (int)entityId);
        }

        protected void SetupAimHandler(CharacterDefinition def)
        {
            _aimHandler?.Init(_cameraMount, _cameraMount?.RenderCamera,
                _playerRenderer.transform, def.CapsuleHeight, ResolveAimTarget);
            _aimHandler?.BindTargetPresentation(_hudManager?.TargetingRoot);
        }

        private Vector3? ResolveAimTarget(ulong entityId)
        {
            var simulation = Bridge switch
            {
                LocalSimulationBridge local => local.InternalSim,
                RollbackSimulationBridge online => online.Simulation,
                _ => null,
            };
            return simulation != null && simulation.TryGetCombatTargetPosition(entityId, out var position)
                ? new Vector3(position.X, position.Y, position.Z) : null;
        }

        /// <summary>
        /// Instantiate the global ground-shadow indicator (ADR-0018 / issue #127):
        /// one quiet ground marker pinned to the arena floor under every renderer.
        /// </summary>
        protected void SetupLockIndicator(PlayerRenderer[] renderers, ArenaDefinition arena)
        {
            var go = new GameObject("LockTargetIndicator");
            go.transform.SetParent(transform, false);
            var indicator = go.AddComponent<TargetIndicator>();
            indicator.Init(Bridge.GetState, renderers, arena, PlayerEntityId, _cameraMount?.RenderCamera,
                id => MatchConfig.Format == MatchFormat.SlopballVersus && MatchConfig.Camps.TryGetValue(id, out var camp)
                    ? camp : SlopballCamp.None, CourseColours.ForEntity, LocalAccentOf);
            go.AddComponent<HomingDashLockOn>().Init(Bridge.GetState, renderers, ResolveAimTarget, _cameraMount?.RenderCamera);
        }

        /// <summary>Couch play: extra Local Players and their facing-ring accent; null for everyone else.</summary>
        protected virtual Func<ulong, Color?> LocalAccentOf => null;


        /// <summary>
        /// Pick the nearest enemy within 20m that is closest to screen center.
        /// Returns entity ID (cast to byte) or 0 if none found.
        /// </summary>
        protected byte PickScreenTarget(PlayerRenderer[] renderers, UnityEngine.Camera cam)
        {
            if (cam == null || renderers == null || renderers.Length == 0 || _playerRenderer == null)
                return 0;

            byte bestId = 0;
            float bestScreenDist = float.MaxValue;
            Vector2 screenCenter = new(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            Vector3 playerPos = _playerRenderer.transform.position;

            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer == _playerRenderer || renderer.EntityId == 0)
                    continue;

                Vector3 worldPos = renderer.transform.position;
                Vector3 screenPos3 = cam.WorldToScreenPoint(worldPos);
                if (screenPos3.z < 0) continue;

                float screenDist = Vector2.Distance(new Vector2(screenPos3.x, screenPos3.y), screenCenter);
                float worldDist = Vector3.Distance(playerPos, worldPos);

                if (screenDist < bestScreenDist && worldDist <= 20f)
                {
                    bestScreenDist = screenDist;
                    bestId = (byte)renderer.EntityId;
                }
            }
            return bestId;
        }

        protected virtual void OnGUI()
        {
            if (IsPaused) return; // crosshair hidden behind the pause panel
            if (!_showCrosshair) return;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            if (_crosshairTexture != null)
            {
                float half = _crosshairSize * 0.5f;
                GUI.DrawTexture(new Rect(cx - half, cy - half, _crosshairSize, _crosshairSize), _crosshairTexture);
            }
            else
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), "+", style);
            }
        }

    }
}
