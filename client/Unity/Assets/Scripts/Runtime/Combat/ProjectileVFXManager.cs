using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Reads hitboxes from a simulation's SpellResolver each tick and spawns/manages
    /// projectile VFX (KiShot, etc.). Moving hitboxes (|V| > 0) get a visual
    /// GameObject with a glowing sphere + trail + light that follows the hitbox.
    /// Training follows its simulation's removals for impact bursts; online, the predicted
    /// world is re-simulated, so removals repeat and are not followed (confirmed hits have
    /// their own presentation events).
    /// </summary>
    public class ProjectileVFXManager : MonoBehaviour
    {
        [Header("Projectile Visuals")]
        [SerializeField] private Color _projectileColor = new(0f, 0.7f, 1f);  // cyan energy
        [SerializeField] private float _sphereRadius = 0.25f;
        [SerializeField] private float _lightIntensity = 2f;
        [SerializeField] private float _lightRange = 5f;
        [SerializeField] private float _trailDuration = 0.2f;
        [SerializeField] private Color _emissionColor = new(0.3f, 0.8f, 1f);  // brighter emission

        [Header("Impact VFX")]
        [SerializeField] private float _impactLifetime = 1f;
        [SerializeField] private Color _impactColor = new(0f, 0.7f, 1f, 1f);

        private readonly Dictionary<(ulong Owner, ulong Id), MankiCanVisual> _canVisuals = new();
        private readonly List<(ulong Owner, ulong Id)> _goneCans = new();

        private void ClearCanVisuals(ulong? owner = null)
        {
            _goneCans.Clear();
            foreach (var pair in _canVisuals)
                if (!owner.HasValue || pair.Key.Owner == owner.Value)
                {
                    if (pair.Value != null) DestroyVisual(pair.Value.gameObject);
                    _goneCans.Add(pair.Key);
                }
            foreach (var key in _goneCans) _canVisuals.Remove(key);
        }

        private void UpdateCanVisuals()
        {
            _goneCans.Clear();
            _goneCans.AddRange(_canVisuals.Keys);
            foreach (var can in _resolver.MankiCans)
            {
                if (!_owners.ContainsKey(can.OwnerId)) continue;
                var key = (can.OwnerId, can.Id);
                _goneCans.Remove(key);
                if (!_canVisuals.TryGetValue(key, out var visual) || visual == null)
                    _canVisuals[key] = visual = MankiCanVisual.Create();
                visual.Apply(in can, _sim.CurrentTick);
            }
            foreach (var key in _goneCans)
            {
                if (_canVisuals[key] != null) DestroyVisual(_canVisuals[key].gameObject);
                _canVisuals.Remove(key);
            }
        }

        private ServerSimulation _sim;
        private SpellResolver _resolver;
        private ProjectileVFXConfig _config;

        private readonly Dictionary<ulong, (CharacterAnimationCatalog Catalog, PlayerRenderer Renderer)> _owners = new();
        private readonly Dictionary<ulong, ProjectilePresentation> _hazardVisuals = new();
        private readonly Dictionary<ulong, Transform> _hazardHands = new();
        private readonly Dictionary<ulong, Vector3> _launchOffsets = new();

        public void RegisterHazardOwner(ulong owner, CharacterAnimationCatalog catalog, ProjectilePresentation visual, Transform hand)
        {
            RegisterOwner(owner, catalog);
            _hazardVisuals[owner] = visual;
            _hazardHands[owner] = hand;
        }

        private ProjectilePresentation VisualFor(ulong owner, byte slot, bool airborne)
            => _hazardVisuals.TryGetValue(owner, out var visual) ? visual : ProfileFor(owner)?.FindProjectile(slot, airborne);

        public void RegisterOwner(ulong owner, CharacterAnimationCatalog catalog, PlayerRenderer renderer = null)
        {
            ClearOwnerVisuals(owner);
            _owners[owner] = (catalog, renderer);
        }

        public void UnregisterOwner(ulong owner, PlayerRenderer expectedRenderer = null)
        {
            if (expectedRenderer != null && _owners.TryGetValue(owner, out var current)
                && !ReferenceEquals(current.Renderer, expectedRenderer)) return;
            ClearOwnerVisuals(owner);
            _hazardVisuals.Remove(owner);
            _hazardHands.Remove(owner);
            _owners.Remove(owner);
        }

        private CharacterPresentationProfile ProfileFor(ulong owner)
            => _owners.TryGetValue(owner, out var entry) && entry.Catalog != null ? entry.Catalog.PresentationProfile : null;

        private void ClearOwnerVisuals(ulong owner)
        {
            ClearCanVisuals(owner);
            _goneLocal.Clear();
            foreach (var pair in _activeVisuals)
                if (pair.Key.Owner == owner) { DestroyVisual(pair.Value); _goneLocal.Add(pair.Key); }
            foreach (var key in _goneLocal)
            {
                _activeVisuals.Remove(key);
                _launchOffsets.Remove(key.Emission);
            }
            ClearHeldVisual(owner);
        }

        // A corrected history can reuse an emission number for a different owner or move.
        private readonly Dictionary<
            (ulong Owner, ulong Activation, int Operation, ulong Emission, byte Slot, bool Airborne), GameObject> _activeVisuals = new();
        private readonly HashSet<
            (ulong Owner, ulong Activation, int Operation, ulong Emission, byte Slot, bool Airborne)> _matchedLocal = new();
        private readonly List<
            (ulong Owner, ulong Activation, int Operation, ulong Emission, byte Slot, bool Airborne)> _goneLocal = new();

        private struct HeldProjectile
        {
            public GameObject Visual;
            public Transform Hand;
            public byte AttackSequence;
            public int Deaths;
            public byte Slot;
            public bool Airborne;
        }

        private readonly Dictionary<ulong, HeldProjectile> _heldVisuals = new();

        public void UpdateHeldVisual(ulong owner, CharacterState state, Transform hand, CookedSlotDefinition? move)
        {
            bool airborne = move?.IsAir ?? false;
            var row = ProfileFor(owner)?.FindProjectile(state.AttackSlot, airborne);
            bool enabled = !string.IsNullOrEmpty(row?.HeldBoneId);
            bool aiming = enabled && state.State == ActionState.Aiming;
            bool firing = enabled && state.State == ActionState.Attacking;
            if (_heldVisuals.TryGetValue(owner, out var held)
                && (held.AttackSequence != state.AttackSequence || held.Deaths != state.Deaths
                    || held.Slot != state.AttackSlot || held.Airborne != airborne))
                ClearHeldVisual(owner);
            if ((!aiming && !firing) || hand == null)
            {
                ClearHeldVisual(owner);
                return;
            }
            if (!_heldVisuals.TryGetValue(owner, out held))
            {
                if (!aiming) return;
                held = new HeldProjectile
                {
                    Visual = CreateProjectileVisual(owner, state.AttackSlot, airborne),
                    Hand = hand,
                    AttackSequence = state.AttackSequence,
                    Deaths = state.Deaths,
                    Slot = state.AttackSlot, Airborne = airborne,
                };
                held.Visual.transform.position = hand.position;
            }
            var charge = GetChargedProjectileParameters(move);
            if (held.Visual != null && charge != null)
                ScaleProjectileVisual(held.Visual, charge.GetRadius(state.ChargeTicks), charge,
                    owner, state.AttackSlot, airborne);
            _heldVisuals[owner] = held;
        }

        public void ClearHeldVisual(ulong owner)
        {
            if (!_heldVisuals.TryGetValue(owner, out var held)) return;
            DestroyVisual(held.Visual);
            _heldVisuals.Remove(owner);
        }

        private GameObject TakeHeldVisual(ulong owner, byte slot, bool airborne,
            byte? attackSequence = null)
        {
            if (!_heldVisuals.TryGetValue(owner, out var held)
                || held.Slot != slot || held.Airborne != airborne
                || (attackSequence.HasValue && held.AttackSequence != attackSequence.Value))
                return null;
            var visual = held.Visual;
            // The projectile itself proves launch; PvP's projectile packet can
            // arrive before the fighter's release-state packet.
            // Keep the consumed record until the activation ends: late/repeated aim
            // states must not recreate a second Iceball after launch.
            held.Visual = null;
            _heldVisuals[owner] = held;
            return visual;
        }

        private void LateUpdate()
        {
            foreach (var held in _heldVisuals.Values)
                if (held.Visual != null && held.Hand != null)
                    held.Visual.transform.position = held.Hand.position;
        }

        /// <summary>Training: follow this simulation, including impact bursts on hitbox removal.</summary>
        public void SetSimulation(ServerSimulation sim) => Attach(sim, followRemovals: true);

        /// <summary>
        /// Online: follow the predicted world. Its re-simulations repeat removals, so impact
        /// bursts are not taken from them; confirmed hits play their own presentation events.
        /// </summary>
        public void SetPredictedSimulation(ServerSimulation sim) => Attach(sim, followRemovals: false);

        private void Attach(ServerSimulation sim, bool followRemovals)
        {
            // An Arcade retry swaps in a fresh simulation; nothing from the old one may linger.
            if (_resolver != null)
                _resolver.OnHitboxRemoved -= OnHitboxRemoved;
            foreach (var visual in _activeVisuals.Values) DestroyVisual(visual);
            foreach (var held in _heldVisuals.Values) DestroyVisual(held.Visual);
            _activeVisuals.Clear();
            _launchOffsets.Clear();
            _heldVisuals.Clear();
            ClearCanVisuals();
            _sim = sim;
            _config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
            _resolver = sim.Resolver;
            if (_resolver != null && followRemovals)
                _resolver.OnHitboxRemoved += OnHitboxRemoved;
        }

        /// <summary>Call after _sim.Tick() each FixedUpdate.</summary>
        public void OnTick()
        {
            if (_resolver == null) return;

            UpdateCanVisuals();
            var hitboxes = _resolver.GetActiveHitboxes();
            _matchedLocal.Clear();
            for (int i = 0; i < hitboxes.Count; i++)
            {
                var hb = hitboxes[i];
                if (!_owners.ContainsKey(hb.OwnerId)) continue;
                float speedSq = hb.VX * hb.VX + hb.VY * hb.VY + hb.VZ * hb.VZ;
                if (speedSq <= 0.0001f && hb.StickyFuseTicks == 0)
                    continue;

                var projectileKey = (hb.OwnerId, hb.ActivationId, hb.VisualOperationIndex,
                    hb.EmissionId, hb.AttackSlot, hb.ActivationAirborne);
                _matchedLocal.Add(projectileKey);
                if (!_activeVisuals.TryGetValue(projectileKey, out var vis))
                {
                    vis = TakeHeldVisual(hb.OwnerId, hb.AttackSlot, hb.ActivationAirborne, hb.AttackSequence)
                        ?? (hb.MankiCan.HasValue ? MankiCanVisual.Create().gameObject
                            : CreateProjectileVisual(hb.OwnerId, hb.AttackSlot, hb.ActivationAirborne));
                    _activeVisuals[projectileKey] = vis;
                    if (_hazardHands.TryGetValue(hb.OwnerId, out var hand) && hand != null)
                        _launchOffsets[hb.EmissionId] = hand.position - new Vector3(hb.X, hb.Y, hb.Z);
                }
                vis.transform.position = new Vector3(hb.X, hb.Y, hb.Z);
                // The booth origin is gameplay metadata. Blend the cosmetic handoff over the
                // first eight ticks; collision and the rest of the flight remain Shared's.
                if (_hazardHands.ContainsKey(hb.OwnerId) && hb.AgeTicks < 9
                    && _launchOffsets.TryGetValue(hb.EmissionId, out var offset))
                    vis.transform.position += offset * Mathf.Clamp01(1f - (hb.AgeTicks - 1) / 8f);
                if (_hazardVisuals.TryGetValue(hb.OwnerId, out var hazard) && hb.AttachedEntityId != 0)
                {
                    float fraction = hb.FuseTicksLeft / (float)Mathf.Max(1, hb.StickyFuseTicks);
                    float pulse = SlopArena.Client.UI.ClientSettingsService.Instance.ReducedFlashing ? 0f
                        : .12f * Mathf.Sin(_sim.CurrentTick / 60f * Mathf.Lerp(26f, 8f, fraction));
                    vis.transform.localScale = Vector3.one * hazard.Scale * (1f + pulse);
                }
                ScaleProjectileVisual(vis, hb.Radius,
                    GetChargedProjectileParameters(_sim.GetDefinition(hb.OwnerId)?.GetCookedSlotAbility(hb.AttackSlot, hb.ActivationAirborne)),
                    hb.OwnerId, hb.AttackSlot, hb.ActivationAirborne);
                OrientProjectile(vis.transform, VisualFor(hb.OwnerId, hb.AttackSlot, hb.ActivationAirborne), hb.VX, hb.VY, hb.VZ);
            }

            _goneLocal.Clear();
            foreach (var kv in _activeVisuals)
            {
                if (_matchedLocal.Contains(kv.Key)) continue;
                _goneLocal.Add(kv.Key);
                DestroyVisual(kv.Value);
            }
            foreach (var id in _goneLocal)
            {
                _activeVisuals.Remove(id);
                _launchOffsets.Remove(id.Emission);
            }
        }

        private static CookedChargedProjectileCapabilityParameters GetChargedProjectileParameters(
            CookedSlotDefinition? cooked)
        {
            if (cooked == null) return null;
            foreach (var stage in cooked.Timeline.Stages)
            foreach (var operation in stage.Operations)
                if (operation is CookedStartCapabilityOperation
                    { Parameters: CookedChargedProjectileCapabilityParameters charge })
                    return charge;
            return null;
        }

        private void ScaleProjectileVisual(GameObject visual, float radius,
            CookedChargedProjectileCapabilityParameters charge, ulong owner, byte slot, bool airborne)
        {
            if (charge == null || visual == null) return;
            var row = ProfileFor(owner)?.FindProjectile(slot, airborne);
            float baseScale = row?.Prefab != null ? row.Scale : _sphereRadius * 2f;
            visual.transform.localScale = Vector3.one * (baseScale * radius / charge.Projectile.Radius);
        }

        private GameObject CreateProjectileVisual(ulong owner, byte attackSlot, bool airborne)
        {
            var row = VisualFor(owner, attackSlot, airborne);
            if (row?.Prefab != null)
            {
                try
                {
                    var visual = Instantiate(row.Prefab);
                    visual.transform.localScale = Vector3.one * row.Scale;
                    return visual;
                }
                catch (System.InvalidCastException)
                {
                    Debug.LogError($"[ProjectileVFX] Invalid prefab reference for owner={owner} slot={row.SlotId}; using fallback visual.");
                }
            }
            return BuildProjectileVisual();
        }

        private static void OrientProjectile(Transform visual, ProjectilePresentation row, float vx, float vy, float vz)
        {
            if (row?.Orientation != ProjectileOrientation.FlightPositiveY) return;
            var velocity = new Vector3(vx, vy, vz);
            if (velocity.sqrMagnitude > 0.0001f)
                visual.rotation = Quaternion.LookRotation(velocity) * Quaternion.Euler(90f, 0f, 0f);
        }

        private static void DestroyVisual(GameObject visual)
        {
            if (visual == null) return;
            // Trails marked to linger (smoke behind a rocket) detach and fade instead of vanishing.
            foreach (var linger in visual.GetComponentsInChildren<SlopArena.Client.World.LingerAfterProjectile>(true))
                if (Application.isPlaying) linger.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(visual);
            else UnityEngine.Object.DestroyImmediate(visual);
        }


        /// <summary>
        /// Build a glowing projectile visual GameObject.
        /// Composition: glowing sphere mesh + light + trail renderer.
        /// </summary>
        private GameObject BuildProjectileVisual()
        {
            var root = new GameObject("ProjectileVisual");
            root.transform.localScale = Vector3.one * _sphereRadius * 2f;

            // ── Glowing Sphere ──
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Core";
            sphere.transform.SetParent(root.transform, false);
            // Remove the auto-added collider (not needed for VFX)
            DestroyImmediate(sphere.GetComponent<SphereCollider>());

            var mr = sphere.GetComponent<MeshRenderer>();
            var coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coreMat.SetFloat("_Surface", 1f);               // Transparent
            coreMat.SetOverrideTag("RenderType", "Transparent");
            coreMat.SetInt("_ZWrite", 0);
            coreMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            coreMat.SetFloat("_DstBlend", (float)BlendMode.One);  // Additive
            coreMat.SetFloat("_AlphaClip", 0f);
            coreMat.renderQueue = 3000;
            coreMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            coreMat.color = Color.white;
            coreMat.SetColor("_EmissionColor", _emissionColor * 2f);
            coreMat.EnableKeyword("_EMISSION");
            mr.sharedMaterial = coreMat;

            // ── Light ──
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = _projectileColor;
            light.intensity = _lightIntensity;
            light.range = _lightRange;

            // ── Trail ──
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = _trailDuration;
            trail.startWidth = 1f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.1f;
            var trailMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            trailMat.SetFloat("_Surface", 1f);
            trailMat.SetOverrideTag("RenderType", "Transparent");
            trailMat.SetInt("_ZWrite", 0);
            trailMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            trailMat.SetFloat("_DstBlend", (float)BlendMode.One);
            trailMat.renderQueue = 3000;
            trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            trailMat.color = _projectileColor;
            trailMat.SetColor("_EmissionColor", _projectileColor * 1.5f);
            trailMat.EnableKeyword("_EMISSION");
            trail.sharedMaterial = trailMat;
            root.AddComponent<ProjectileMaterialOwner>().Materials = new[] { coreMat, trailMat };

            return root;
        }

        private void OnHitboxRemoved(Hitbox hb, float lastX, float lastY, float lastZ)
        {
            if (!_owners.ContainsKey(hb.OwnerId)) return;
            if (hb.Explosion.HasValue)
            {
                if (ProfileFor(hb.OwnerId)?.SuppressRemovalExplosion == true)
                    return;
                SpawnExplosion(hb, new Vector3(lastX, lastY, lastZ));
                return;
            }
            float speedSq = hb.VX * hb.VX + hb.VY * hb.VY + hb.VZ * hb.VZ;
            if (speedSq <= 0.0001f) return;
            // Contact/cancellation marks the hitbox inactive; configured hits have
            // their own confirmed effect (or shield feedback). Keep expiry bursts.
            if (!hb.Active && hb.AttackSlot > 0
                && !string.IsNullOrEmpty(_sim.GetDefinition(hb.OwnerId)
                    ?.GetCookedSlotAbility(hb.AttackSlot, hb.ActivationAirborne)?.HitPresentationId))
                return;
            SpawnImpact(new Vector3(lastX, lastY, lastZ));
        }

        private void SpawnExplosion(Hitbox hb, Vector3 position)
        {
            GameObject prefab = _config != null ? _config.ExplosionPrefab : null;
            float scale = _config != null ? _config.ExplosionScale : 1f;
            float visualScale = hb.Explosion.Value.Radius * scale;
            if (prefab == null) { SpawnImpact(position); return; }
            try
            {
                var go = Instantiate(prefab);
                go.transform.position = position;
                go.transform.localScale = Vector3.one * visualScale;
                Destroy(go, 2.5f);
            }
            catch (System.InvalidCastException)
            {
                Debug.LogError($"[ProjectileVFX] Invalid explosion prefab reference for owner={hb.OwnerId} slot={hb.AttackSlot}; using fallback impact.");
                SpawnImpact(position);
            }

        }
        private void SpawnImpact(Vector3 position)
        {
            // Procedural impact burst — no prefab dependency
            var go = new GameObject("KiShotImpact")
            {
                transform =
                {
                    position = position
                }
            };

            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent on an active GameObject auto-plays the system (playOnAwake
            // defaults to true), so configuring main.* — including duration — while it is
            // playing throws "Setting the duration while system is still playing is not
            // supported". Stop + clear before configuring (the error's own suggested remedy);
            // playOnAwake is disabled below and Play() is called explicitly at the end.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.3f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startColor = _impactColor;
            main.maxParticles = 20;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = false;

            var burst = ps.emission;
            burst.SetBurst(0, new ParticleSystem.Burst(0f, 12));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            // All three orbital axes must share the same curve mode or Unity throws
            // "Particle Orbital Velocity curves must all be in the same mode".
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(-90f, 90f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            renderer.sharedMaterial = mat;
            go.AddComponent<ProjectileMaterialOwner>().Materials = new[] { mat };
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_ZWrite", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.color = _impactColor;
            mat.SetColor("_EmissionColor", _impactColor * 2f);
            mat.EnableKeyword("_EMISSION");
            mat.renderQueue = 3000;

            ps.Play();
            Destroy(go, 1f);
        }

        private void OnDestroy()
        {
            if (_resolver != null)
                _resolver.OnHitboxRemoved -= OnHitboxRemoved;
            foreach (var visual in _activeVisuals.Values) DestroyVisual(visual);
            foreach (var held in _heldVisuals.Values) DestroyVisual(held.Visual);
            ClearCanVisuals();
            _owners.Clear();
        }
    }
}
