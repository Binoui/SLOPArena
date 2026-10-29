#nullable enable
using System.Collections.Generic;
using UnityEngine;


namespace SlopArena.Client.Entities
{
    internal enum MovementFeedbackKind : byte
    {
        Dash,
        Jump,
        DoubleJump,
        Landing,
        HeavyLanding,
        FastFall,
        Launch,
        Respawn,
        Invincibility,
        AirRing,
        GroundRing,
        LaunchSmoke,
    }

    /// <summary>
    /// Pooled, presentation-only movement effects adapted from Cartoon FX prefabs. Events are
    /// raised from successive authoritative CharacterState snapshots; this component never
    /// drives gameplay state.
    /// </summary>
    internal sealed class MovementFeedbackEffect : MonoBehaviour
    {
        private const int PrewarmCount = 3;
        private static readonly Dictionary<MovementFeedbackKind, Stack<MovementFeedbackEffect>> Pools = new();
        private static readonly Dictionary<MovementFeedbackKind, GameObject> Prefabs = new();
        private static bool _prewarmed;

        private ParticleSystem[] _particles;
        private MovementFeedbackKind _kind;
        private float _releaseAt;
        private bool _continuous;
        private Vector3 _trailPosition;
        private float _trailCarry;
        private bool _active;

        public static void Prewarm()
        {
            if (_prewarmed)
                return;
            _prewarmed = true;

            foreach (MovementFeedbackKind kind in SourceKinds())
            {
                GameObject prefab = LoadPrefab(kind);
                if (prefab == null)
                    continue;

                Stack<MovementFeedbackEffect> pool = PoolFor(kind);
                for (int i = 0; i < PrewarmCount; i++)
                {
                    MovementFeedbackEffect effect = CreateInstance(kind, prefab);
                    effect.gameObject.SetActive(false);
                    pool.Push(effect);
                }
            }
        }

        public static void Spawn(Vector3 position, Vector3 direction, MovementFeedbackKind kind)
        {
            Prewarm();
            if (kind == MovementFeedbackKind.DoubleJump)
            {
                SpawnOne(position, direction, MovementFeedbackKind.DoubleJump);
                SpawnOne(position, direction, MovementFeedbackKind.AirRing);
                return;
            }
            if (kind == MovementFeedbackKind.HeavyLanding)
            {
                SpawnOne(position, direction, MovementFeedbackKind.HeavyLanding);
                SpawnOne(position, direction, MovementFeedbackKind.GroundRing);
                return;
            }
            if (kind == MovementFeedbackKind.Respawn)
            {
                SpawnRespawn(position, direction, Color.white);
                return;
            }

            SpawnOne(position, direction, kind);
        }
        public static void SpawnRespawn(
            Vector3 position, Vector3 direction, Color tint)
        {
            Prewarm();
            SpawnOne(position, direction, MovementFeedbackKind.Respawn, tint);
            SpawnOne(position, direction, MovementFeedbackKind.AirRing);
        }

        public static MovementFeedbackEffect BeginDashTrail(Vector3 position, Vector3 direction)
        {
            Prewarm();
            GameObject prefab = LoadPrefab(MovementFeedbackKind.Dash);
            if (prefab == null)
                return null;
            Stack<MovementFeedbackEffect> pool = PoolFor(MovementFeedbackKind.Dash);
            MovementFeedbackEffect effect = PopReusable(pool);
            effect ??= CreateInstance(MovementFeedbackKind.Dash, prefab);
            effect.ShowContinuousDash(position, direction);
            return effect;
        }

        public void FollowDashTrail(Vector3 position, Vector3 direction)
        {
            if (!_continuous)
                return;
            transform.position = position;
            transform.rotation = RotationFor(direction, MovementFeedbackKind.Dash);
        }

        public void EndDashTrail()
        {
            if (!_continuous)
                return;
            _continuous = false;
            StopParticles(false);
            _releaseAt = Clock + 0.55f;
        }
        public static MovementFeedbackEffect BeginInvincibility(
            Vector3 position, Color tint)
        {
            Prewarm();
            GameObject prefab = LoadPrefab(MovementFeedbackKind.Invincibility);
            if (prefab == null)
                return null;
            Stack<MovementFeedbackEffect> pool = PoolFor(MovementFeedbackKind.Invincibility);
            MovementFeedbackEffect effect = PopReusable(pool);
            effect ??= CreateInstance(MovementFeedbackKind.Invincibility, prefab);
            effect.ShowContinuousInvincibility(position, tint);
            return effect;
        }

        public void FollowInvincibility(Vector3 position)
        {
            if (!_continuous)
                return;
            transform.position = position;
        }

        public void EndInvincibility()
        {
            if (!_continuous)
                return;
            _continuous = false;
            StopParticles(false);
            _releaseAt = Clock + 0.35f;
        }

        /// <summary>Smoke puffs that follow a launched fighter; density and size track the launch.</summary>
        public static MovementFeedbackEffect? BeginLaunchTrail(Vector3 position)
        {
            Prewarm();
            GameObject prefab = LoadPrefab(MovementFeedbackKind.LaunchSmoke);
            if (prefab == null)
                return null;
            Stack<MovementFeedbackEffect> pool = PoolFor(MovementFeedbackKind.LaunchSmoke);
            MovementFeedbackEffect effect = PopReusable(pool);
            effect ??= CreateInstance(MovementFeedbackKind.LaunchSmoke, prefab);
            effect.ShowContinuousLaunch(position);
            return effect;
        }

        /// <param name="intensity">0–1 launch strength.</param>
        /// <param name="strength">Review/tuning multiplier; 1 is the default look.</param>
        public void FollowLaunchTrail(Vector3 position, float intensity, float strength)
        {
            if (!_continuous)
                return;
            // Emit along the path travelled since the last frame. Distance emission depends on the
            // prefab's emitter-velocity mode, so puffs are placed explicitly instead.
            float density = Mathf.Lerp(3f, 8f, intensity) * strength;
            float size = Mathf.Lerp(0.35f, 0.85f, intensity) * Mathf.Sqrt(strength);
            Vector3 from = _trailPosition;
            _trailCarry += Vector3.Distance(from, position) * density;
            int count = Mathf.Min((int)_trailCarry, 12);
            _trailCarry -= count;
            for (int i = 0; i < count; i++)
            {
                var puff = new ParticleSystem.EmitParams
                {
                    position = Vector3.Lerp(from, position, (i + 1f) / count) + Random.insideUnitSphere * 0.08f,
                    startSize = size * Random.Range(0.7f, 1f),
                    // Warm grey still reads against white props and bright skies.
                    startColor = new Color(0.8f, 0.77f, 0.74f, 0.85f),
                    applyShapeToPosition = false,
                };
                foreach (ParticleSystem particle in _particles)
                    if (particle != null)
                        particle.Emit(puff, 1);
            }
            _trailPosition = position;
            transform.position = position;
        }

        public void EndLaunchTrail()
        {
            if (!_continuous)
                return;
            _continuous = false;
            StopParticles(false);
            _releaseAt = Clock + 0.8f;
        }


        private static void SpawnOne(
            Vector3 position, Vector3 direction, MovementFeedbackKind kind, Color? tint = null)
        {
            GameObject prefab = LoadPrefab(kind);
            if (prefab == null)
                return;

            Stack<MovementFeedbackEffect> pool = PoolFor(kind);
            MovementFeedbackEffect effect = PopReusable(pool);
            effect ??= CreateInstance(kind, prefab);
            effect.Show(position, direction, tint);
        }
        private static MovementFeedbackEffect PopReusable(
            Stack<MovementFeedbackEffect> pool)
        {
            while (pool.Count > 0)
            {
                MovementFeedbackEffect effect = pool.Pop();
                if (effect == null)
                    continue;
                if (effect.HasLiveParticles)
                    return effect;
                Destroy(effect.gameObject);
            }
            return null;
        }

        private static MovementFeedbackEffect CreateInstance(MovementFeedbackKind kind, GameObject prefab)
        {
            GameObject root = new($"Movement Feedback ({kind})")
            {
                hideFlags = HideFlags.DontSave,
            };
            DontDestroyOnLoad(root);

            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = prefab.name;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour.GetType().Name == "CFXR_Effect")
                    behaviour.enabled = false;
            }

            MovementFeedbackEffect effect = root.AddComponent<MovementFeedbackEffect>();
            effect._kind = kind;
            effect._particles = visual.GetComponentsInChildren<ParticleSystem>(true);
            effect.ConfigureParticles();
            return effect;
        }

        private void ConfigureParticles()
        {
            foreach (ParticleSystem particle in _particles)
            {
                // Instantiated prefabs may auto-play on enable. Stop and clear
                // before changing MainModule.duration; Unity rejects duration
                // changes while a system is still playing.
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main;
                main.stopAction = ParticleSystemStopAction.None;
                main.loop = false;
                if (_kind is MovementFeedbackKind.Dash or MovementFeedbackKind.FastFall
                    or MovementFeedbackKind.Launch)
                    main.duration = Mathf.Min(main.duration, 0.18f);
            }
        }

        private bool HasLiveParticles
        {
            get
            {
                if (_particles == null || _particles.Length == 0)
                    return false;
                foreach (ParticleSystem particle in _particles)
                {
                    if (particle == null)
                        return false;
                }
                return true;
            }
        }

        private void StopParticles(bool clear)
        {
            if (_particles == null)
                return;
            foreach (ParticleSystem particle in _particles)
            {
                if (particle == null)
                    continue;
                particle.Stop(
                    true,
                    clear
                        ? ParticleSystemStopBehavior.StopEmittingAndClear
                        : ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Show(
            Vector3 position, Vector3 direction, Color? tint = null)
        {
            gameObject.SetActive(true);
            transform.position = position;
            transform.rotation = RotationFor(direction, _kind);
            transform.localScale = Vector3.one * ScaleFor(_kind);

            foreach (ParticleSystem particle in _particles)
            {
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                if (tint.HasValue)
                {
                    var main = particle.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(tint.Value);
                }
                particle.Play(true);
            }

            _active = true;
            _releaseAt = Clock + LifetimeFor(_kind);
        }
        private void ShowContinuousInvincibility(Vector3 position, Color tint)
        {
            gameObject.SetActive(true);
            transform.position = position;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one * ScaleFor(MovementFeedbackKind.Invincibility);

            foreach (ParticleSystem particle in _particles)
            {
                particle.gameObject.SetActive(true);
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(true);
                var main = particle.main;
                main.loop = true;
                main.duration = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(tint);
                main.maxParticles = 32;
                var emission = particle.emission;
                emission.rateOverTime = 5f;
                particle.Play(true);
            }

            _active = true;
            _continuous = true;
            _releaseAt = float.PositiveInfinity;
        }

        private void ShowContinuousDash(Vector3 position, Vector3 direction)
        {
            gameObject.SetActive(true);
            transform.position = position;
            transform.rotation = RotationFor(direction, MovementFeedbackKind.Dash);
            transform.localScale = Vector3.one * ScaleFor(MovementFeedbackKind.Dash);

            foreach (ParticleSystem particle in _particles)
            {
                particle.gameObject.SetActive(true);
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(true);
                var main = particle.main;
                main.loop = true;
                main.duration = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 96;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                var emission = particle.emission;
                emission.rateOverTime = 45f;
                var trails = particle.trails;
                if (trails.enabled)
                {
                    trails.worldSpace = true;
                    trails.lifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
                }
                particle.Play(true);
            }

            _active = true;
            _continuous = true;
            _releaseAt = float.PositiveInfinity;
        }

        private void ShowContinuousLaunch(Vector3 position)
        {
            gameObject.SetActive(true);
            transform.SetPositionAndRotation(position, Quaternion.identity);
            transform.localScale = Vector3.one;
            _trailPosition = position;
            _trailCarry = 0f;

            foreach (ParticleSystem particle in _particles)
            {
                particle.gameObject.SetActive(true);
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(true);
                var main = particle.main;
                main.loop = true;
                main.duration = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 96;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.25f);
                main.gravityModifier = 0f;
                // FollowLaunchTrail emits every puff; the module would add bursts or a rate.
                var emission = particle.emission;
                emission.enabled = false;
                particle.Play(true);
            }

            _active = true;
            _continuous = true;
            _releaseAt = float.PositiveInfinity;
        }

        private static float Clock
        {
            get
            {
#if UNITY_EDITOR
                if (Time.captureDeltaTime > 0f) return Time.time;
#endif
                return Time.unscaledTime;
            }
        }

        private void Update()
        {
            if (!_active || _continuous || Clock < _releaseAt)
                return;

            StopParticles(true);

            _active = false;
            gameObject.SetActive(false);
            PoolFor(_kind).Push(this);
        }

        private static Quaternion RotationFor(Vector3 direction, MovementFeedbackKind kind)
        {
            if (kind == MovementFeedbackKind.AirRing)
                return Quaternion.Euler(90f, 0f, 0f);
            if (kind is MovementFeedbackKind.Jump or MovementFeedbackKind.DoubleJump
                or MovementFeedbackKind.Landing or MovementFeedbackKind.HeavyLanding
                or MovementFeedbackKind.GroundRing)
                return Quaternion.identity;
            if (direction.sqrMagnitude < 0.001f)
                return Quaternion.identity;
            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private static float ScaleFor(MovementFeedbackKind kind) => kind switch
        {
            MovementFeedbackKind.Dash => 1.2f,
            MovementFeedbackKind.Jump => 0.75f,
            MovementFeedbackKind.DoubleJump => 0.48f,
            MovementFeedbackKind.AirRing => 0.5f,
            MovementFeedbackKind.GroundRing => 0.72f,
            MovementFeedbackKind.Landing => 0.34f,
            MovementFeedbackKind.HeavyLanding => 0.54f,
            MovementFeedbackKind.FastFall => 0.55f,
            MovementFeedbackKind.Launch => 0.65f,
            MovementFeedbackKind.Respawn => 0.95f,
            MovementFeedbackKind.Invincibility => 0.62f,
            _ => 0.5f,
        };

        private static float LifetimeFor(MovementFeedbackKind kind) => kind switch
        {
            MovementFeedbackKind.Dash or MovementFeedbackKind.FastFall => 0.45f,
            MovementFeedbackKind.AirRing => 0.8f,
            MovementFeedbackKind.GroundRing => 0.55f,
            MovementFeedbackKind.Launch => 0.65f,
            MovementFeedbackKind.Respawn => 1.0f,
            _ => 1.1f,
        };

        private static string ResourcePath(MovementFeedbackKind kind) => kind switch
        {
            MovementFeedbackKind.Dash or MovementFeedbackKind.FastFall
                or MovementFeedbackKind.Launch
                => "VFX/SlopArena/MovementWindTrails",
            MovementFeedbackKind.Jump or MovementFeedbackKind.DoubleJump
                or MovementFeedbackKind.Landing or MovementFeedbackKind.HeavyLanding
                or MovementFeedbackKind.Respawn or MovementFeedbackKind.LaunchSmoke
                => "VFX/SlopArena/MovementMagicPoof",
            MovementFeedbackKind.Invincibility or MovementFeedbackKind.AirRing
                => "VFX/SlopArena/MovementAirRing",
            MovementFeedbackKind.GroundRing => "VFX/SlopArena/MovementGroundHit",
            _ => null,
        };

        private static GameObject LoadPrefab(MovementFeedbackKind kind)
        {
            if (Prefabs.TryGetValue(kind, out GameObject prefab))
                return prefab;
            prefab = Resources.Load<GameObject>(ResourcePath(kind));
            Prefabs[kind] = prefab;
            return prefab;
        }

        private static Stack<MovementFeedbackEffect> PoolFor(MovementFeedbackKind kind)
        {
            if (Pools.TryGetValue(kind, out Stack<MovementFeedbackEffect> pool))
                return pool;
            pool = new Stack<MovementFeedbackEffect>();
            Pools.Add(kind, pool);
            return pool;
        }

        private static MovementFeedbackKind[] SourceKinds() => new[]
        {
            MovementFeedbackKind.Dash,
            MovementFeedbackKind.Jump,
            MovementFeedbackKind.DoubleJump,
            MovementFeedbackKind.Landing,
            MovementFeedbackKind.HeavyLanding,
            MovementFeedbackKind.FastFall,
            MovementFeedbackKind.Launch,
            MovementFeedbackKind.Respawn,
            MovementFeedbackKind.Invincibility,
            MovementFeedbackKind.AirRing,
            MovementFeedbackKind.GroundRing,
            MovementFeedbackKind.LaunchSmoke,
        };
    }
}
