using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Reads server hitboxes from SpellResolver each tick and spawns/manages
    /// projectile VFX (KiShot, etc.). Moving hitboxes (|V| > 0) get a visual
    /// GameObject with a glowing sphere + trail + light that follows the hitbox.
    /// On hitbox removal, spawns an impact burst.
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

        private ServerSimulation _sim;
        private SpellResolver _resolver;
        private ProjectileVFXConfig _config;

        // Local Training visuals follow the Shared resolver; PvP visuals follow server snapshots.
        private readonly Dictionary<int, GameObject> _activeVisuals = new();
        private readonly Dictionary<int, CharacterClass> _activeProjectileClasses = new();
        private readonly HashSet<int> _matchedLocal = new();
        private readonly List<int> _goneLocal = new();
        private readonly Dictionary<(ulong owner, ulong activation, int operation), GameObject> _remoteVisuals = new();
        private readonly HashSet<(ulong owner, ulong activation, int operation)> _matchedRemote = new();
        private readonly List<(ulong owner, ulong activation, int operation)> _goneRemote = new();
        private uint _lastRemoteTick;
        private bool _hasRemoteTick;

        private struct HeldProjectile
        {
            public GameObject Visual;
            public Transform Hand;
            public byte AttackSequence;
            public int Deaths;
        }

        private readonly Dictionary<ulong, HeldProjectile> _heldVisuals = new();

        public void UpdateHeldVisual(ulong owner, CharacterState state, Transform hand, CharacterDefinition definition)
        {
            bool aiming = state.State == ActionState.Aiming && state.AttackSlot == AbilitySlots.A;
            bool firing = state.State == ActionState.Attacking && state.AttackSlot == AbilitySlots.A;
            if (_heldVisuals.TryGetValue(owner, out var held)
                && (held.AttackSequence != state.AttackSequence || held.Deaths != state.Deaths))
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
                    Visual = CreateProjectileVisual(CharacterClass.FightGuy, AbilitySlots.A, !state.IsGrounded),
                    Hand = hand,
                    AttackSequence = state.AttackSequence,
                    Deaths = state.Deaths,
                };
                held.Visual.transform.position = hand.position;
            }
            var charge = GetChargedProjectileParameters(definition, state.AttackSlot, !state.IsGrounded);
            if (held.Visual != null && charge != null)
                ScaleProjectileVisual(held.Visual, charge.GetRadius(state.ChargeTicks), charge,
                    CharacterClass.FightGuy, state.AttackSlot, !state.IsGrounded);
            _heldVisuals[owner] = held;
        }

        public void ClearHeldVisual(ulong owner)
        {
            if (!_heldVisuals.TryGetValue(owner, out var held)) return;
            DestroyVisual(held.Visual);
            _heldVisuals.Remove(owner);
        }

        private GameObject TakeHeldVisual(ulong owner, CharacterClass character, byte slot,
            byte? attackSequence = null)
        {
            if (character != CharacterClass.FightGuy || slot != AbilitySlots.A
                || !_heldVisuals.TryGetValue(owner, out var held)
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

        public void SetSimulation(ServerSimulation sim)
        {
            _sim = sim;
            _config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
            _resolver = sim.Resolver;
            if (_resolver != null)
                _resolver.OnHitboxRemoved += OnHitboxRemoved;
        }

        public void SetRemoteSnapshots()
        {
            _config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
        }

        /// <summary>Use only server projectile positions in PvP; missing entries despawn.</summary>
        public void OnSnapshot(ProjectileVisualPacket snapshot)
        {
            if (_hasRemoteTick && snapshot.Tick <= _lastRemoteTick) return;
            _lastRemoteTick = snapshot.Tick;
            _hasRemoteTick = true;
            _matchedRemote.Clear();
            foreach (var projectile in snapshot.Projectiles)
            {
                var key = (projectile.OwnerId, projectile.ActivationId, projectile.OperationIndex);
                _matchedRemote.Add(key);
                if (!_remoteVisuals.TryGetValue(key, out var visual))
                {
                    visual = TakeHeldVisual(projectile.OwnerId, projectile.Character, projectile.AttackSlot)
                        ?? CreateProjectileVisual(projectile.Character, projectile.AttackSlot, projectile.Airborne);
                    _remoteVisuals.Add(key, visual);
                }
                visual.transform.position = new Vector3(projectile.X, projectile.Y, projectile.Z);
                var charge = GetChargedProjectileParameters(
                    SlopArena.Client.ClientSession.MatchContentCatalog?.Resolve(projectile.Character)?.Definition,
                    projectile.AttackSlot, projectile.Airborne);
                ScaleProjectileVisual(visual, projectile.Radius, charge,
                    projectile.Character, projectile.AttackSlot, projectile.Airborne);
                OrientProjectile(visual.transform, projectile.Character, projectile.AttackSlot,
                    projectile.VX, projectile.VY, projectile.VZ);
            }
            _goneRemote.Clear();
            foreach (var visual in _remoteVisuals)
                if (!_matchedRemote.Contains(visual.Key))
                {
                    DestroyVisual(visual.Value);
                    _goneRemote.Add(visual.Key);
                }
            foreach (var key in _goneRemote) _remoteVisuals.Remove(key);
        }

        /// <summary>Call after _sim.Tick() each FixedUpdate.</summary>
        public void OnTick()
        {
            if (_resolver == null) return;

            var hitboxes = _resolver.GetActiveHitboxes();
            _matchedLocal.Clear();
            for (int i = 0; i < hitboxes.Count; i++)
            {
                var hb = hitboxes[i];
                float speedSq = hb.VX * hb.VX + hb.VY * hb.VY + hb.VZ * hb.VZ;
                CharacterClass character = _sim.GetDefinition(hb.OwnerId)?.Class ?? CharacterClass.None;
                if (speedSq <= 0.0001f)
                    continue;

                int projectileKey = ComputeHitboxKey(hb);
                _matchedLocal.Add(projectileKey);
                if (!_activeVisuals.TryGetValue(projectileKey, out var vis))
                {
                    vis = TakeHeldVisual(hb.OwnerId, character, hb.AttackSlot, hb.AttackSequence)
                        ?? CreateProjectileVisual(character, hb.AttackSlot, hb.ActivationAirborne);
                    _activeVisuals[projectileKey] = vis;
                    _activeProjectileClasses[projectileKey] = character;
                }
                vis.transform.position = new Vector3(hb.X, hb.Y, hb.Z);
                ScaleProjectileVisual(vis, hb.Radius,
                    GetChargedProjectileParameters(_sim.GetDefinition(hb.OwnerId), hb.AttackSlot, hb.ActivationAirborne),
                    character, hb.AttackSlot, hb.ActivationAirborne);
                OrientProjectile(vis.transform, character, hb.AttackSlot, hb.VX, hb.VY, hb.VZ);
            }

            _goneLocal.Clear();
            foreach (var kv in _activeVisuals)
            {
                if (_matchedLocal.Contains(kv.Key)) continue;
                _goneLocal.Add(kv.Key);
                DestroyVisual(kv.Value);
                _activeProjectileClasses.Remove(kv.Key);
            }
            foreach (var id in _goneLocal) _activeVisuals.Remove(id);
        }


        /// <summary>
        /// Stable hash key for a hitbox across ticks.
        /// Computes approximate spawn origin by reversing velocity * age.
        /// </summary>
        private static int ComputeHitboxKey(in Hitbox hb)
        {
            float tickDt = SlopArena.Shared.Simulation.TickDt;
            float t = hb.AgeTicks * tickDt;
            float ox = hb.X - hb.VX * t;
            float oy = hb.Y - hb.VY * t - 0.5f * hb.Gravity * t * t;
            float oz = hb.Z - hb.VZ * t;
            // Manual hash combine (System.HashCode unavailable in Unity profile)
            int hash = 17;
            hash = hash * 31 + (int)hb.OwnerId;
            hash = hash * 31 + (int)hb.ActivationId;
            hash = hash * 31 + (int)(hb.ActivationId >> 32);
            hash = hash * 31 + hb.VisualOperationIndex;
            hash = hash * 31 + Mathf.RoundToInt(ox * 10f);
            hash = hash * 31 + Mathf.RoundToInt(oy * 10f);
            hash = hash * 31 + Mathf.RoundToInt(oz * 10f);
            return hash;
        }

        private static CookedChargedProjectileCapabilityParameters GetChargedProjectileParameters(
            CharacterDefinition definition, byte slot, bool airborne)
        {
            var cooked = definition?.GetCookedSlotAbility(slot, airborne);
            if (cooked == null) return null;
            foreach (var stage in cooked.Timeline.Stages)
            foreach (var operation in stage.Operations)
                if (operation is CookedStartCapabilityOperation
                    { Parameters: CookedChargedProjectileCapabilityParameters charge })
                    return charge;
            return null;
        }

        private void ScaleProjectileVisual(GameObject visual, float radius,
            CookedChargedProjectileCapabilityParameters charge, CharacterClass character, byte slot, bool airborne)
        {
            if (charge == null || visual == null) return;
            float baseScale = FindProjectileEntry(character, slot, airborne)?.Scale ?? _sphereRadius * 2f;
            visual.transform.localScale = Vector3.one * (baseScale * radius / charge.Projectile.Radius);
        }

        private GameObject CreateProjectileVisual(CharacterClass character, byte attackSlot, bool airborne)
        {
            if (_config != null)
            {
                var entry = FindProjectileEntry(character, attackSlot, airborne);
                if (entry != null && entry.Prefab != null)
                {
                    try
                    {
                        var go = Instantiate(entry.Prefab);
                        go.transform.localScale = Vector3.one * entry.Scale;
                        return go;
                    }
                    catch (System.InvalidCastException)
                    {
                        Debug.LogError($"[ProjectileVFX] Invalid prefab reference for {entry.Character} slot={entry.AttackSlot} airborne={entry.Airborne}; using fallback visual.");
                    }
                }
            }
            return BuildProjectileVisual();
        }

        private ProjectileVisualEntry FindProjectileEntry(CharacterClass character, byte attackSlot, bool airborne)
        {
            if (_config?.ProjectileEntries == null) return null;
            for (int i = 0; i < _config.ProjectileEntries.Length; i++)
            {
                var e = _config.ProjectileEntries[i];
                if (e.Character == character && e.AttackSlot == attackSlot && e.Airborne == airborne)
                    return e;
            }
            return null;
        }
        /// <summary>Turns the root's +Y along the flight for visuals built point-first (kunai, rocket).</summary>
        private static void OrientProjectile(Transform visual, CharacterClass character, byte slot,
            float vx, float vy, float vz)
        {
            bool pointFirst = (character == CharacterClass.Wibou && slot == AbilitySlots.A)
                || (character == CharacterClass.Manki && slot == AbilitySlots.R);
            if (!pointFirst) return;
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
            mr.material = coreMat;

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
            trail.material = trailMat;

            return root;
        }

        private void OnHitboxRemoved(Hitbox hb, float lastX, float lastY, float lastZ)
        {
            if (hb.Explosion.HasValue)
            {
                if (_sim.GetDefinition(hb.OwnerId)?.Class == CharacterClass.Manki)
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
                    ?.GetSlotAbility((byte)(hb.AttackSlot - 1), hb.ActivationAirborne)?.HitPresentationId))
                return;
            SpawnImpact(new Vector3(lastX, lastY, lastZ));
        }

        private void SpawnExplosion(Hitbox hb, Vector3 position)
        {
            GameObject prefab = _config != null ? _config.ExplosionPrefab : null;
            float scale = _config != null ? _config.ExplosionScale : 1f;
            float visualScale = hb.Explosion.Value.Radius * scale;
            var cls = _activeProjectileClasses.TryGetValue(ComputeHitboxKey(hb), out var cachedClass)
                ? cachedClass
                : (_sim.GetDefinition(hb.OwnerId)?.Class ?? CharacterClass.None);
            byte presentationSlot = hb.AttackSlot != 0 ? hb.AttackSlot : (byte)(hb.Slot + 1);
            if (_config != null && _config.ExplosionOverrides != null)
            {
                for (int i = 0; i < _config.ExplosionOverrides.Length; i++)
                {
                    var overrideEntry = _config.ExplosionOverrides[i];
                    if (overrideEntry.Character == cls
                        && (overrideEntry.AttackSlot == 0 || overrideEntry.AttackSlot == presentationSlot))
                    {
                        prefab = overrideEntry.Prefab;
                        visualScale = overrideEntry.Scale;
                        break;
                    }
                }
            }
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
                Debug.LogError($"[ProjectileVFX] Invalid explosion prefab reference for {cls} slot={presentationSlot}; using fallback impact.");
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
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var mat = renderer.material;
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
            foreach (var visual in _remoteVisuals.Values) DestroyVisual(visual);
            foreach (var held in _heldVisuals.Values) DestroyVisual(held.Visual);
        }
    }
}
