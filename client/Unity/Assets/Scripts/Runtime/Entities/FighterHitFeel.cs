using System.Collections.Generic;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Per-layer strength multipliers for <see cref="FighterHitFeel"/>; zero disables a layer.
    /// 1 is the per-tier calibration below. The owner picked "high" (1.6 for every layer) from
    /// the 2026-10-16 review; tune a layer down here.
    /// </summary>
    [System.Serializable]
    public struct HitFeelStrength
    {
        public float Shake;
        public float Flash;
        public float Squash;
        public float Lean;
        public float Trail;

        public static readonly HitFeelStrength Default = new() { Shake = 1.6f, Flash = 1.6f, Squash = 1.6f, Lean = 1.6f, Trail = 1.6f };
    }

    /// <summary>
    /// Presentation-only hit feel for one fighter: victim shake during Hitstop, a hit flash,
    /// squash/stretch/lean on two pivots between the renderer root and the model, and a smoke
    /// trail while launched. It follows accepted hits from <see cref="CombatFeedback"/> and the
    /// state the renderer applies; the simulated root, hitboxes and clips are untouched.
    /// </summary>
    [DefaultExecutionOrder(900)] // deform before BoneChainSpring (1000) samples bone positions
    public sealed class FighterHitFeel : MonoBehaviour
    {
        // Victim tiers follow ImpactTier: Light, Medium, Heavy, Launch.
        private static readonly float[] ShakeMeters = { 0.035f, 0.05f, 0.075f, 0.1f };
        private static readonly float[] CrushAmount = { 0.06f, 0.09f, 0.12f, 0.15f };
        private static readonly float[] RecoilDegrees = { 4f, 6f, 9f, 12f };
        private static readonly float[] FlashWhite = { 0.55f, 0.7f, 0.85f, 0.9f };
        // Attacker weights follow the first hitbox's damage: light, medium, heavy.
        private static readonly float[] CoilDegrees = { 4f, 6f, 8f };
        private static readonly float[] CoilSquash = { 0.05f, 0.07f, 0.1f };
        private static readonly float[] StrikeDegrees = { 5f, 7f, 10f };
        private static readonly float[] StrikeStretch = { 0.08f, 0.11f, 0.15f };

        private const float SpringHz = 4.5f;
        private const float SpringDamping = 0.3f;
        private const float LaunchSpeed = 8f;       // m/s where the launch stretch and trail begin
        private const float LaunchFullSpeed = 22f;  // m/s where they saturate
        private const float MaxLaunchStretch = 0.4f;
        private const float AttackReleaseSeconds = 0.06f;
        // Air dodge: stretch along the dodge and lean into it, easing out over the burst.
        private const float DodgeStretch = 0.15f;
        private const float DodgeLeanDegrees = 7f;

        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

        /// <summary>Layer strengths; review tooling overrides them per fighter.</summary>
        public HitFeelStrength Strength = HitFeelStrength.Default;

        /// <summary>Review captures show the default flash regardless of the local Reduced Flashing setting.</summary>
        public bool IgnoreReducedFlashing;

        /// <summary>
        /// Raised when this fighter's armour absorbs an accepted hit, for cosmetic reactions
        /// (Bonk's "?"; see <see cref="SlopArena.Client.World.ArmorGlowPresentation"/>).
        /// </summary>
        public event System.Action? ArmorAbsorbed;

        private PlayerRenderer _renderer = null!;
        private Transform? _axis, _counter;
        private readonly List<SkinnedMeshRenderer> _skins = new();
        private MaterialPropertyBlock? _block;
        private bool _flashApplied;

        // Sustained rim glows (an armour read) held by presentation cues; the latest wins.
        private const float GlowRimStrength = 2.4f;
        private const float GlowRimPower = 1.2f;
        private const float GlowPulseHz = 4f;
        private readonly List<(Object key, Color color)> _glows = new();

        // Victim reaction.
        private bool _hit, _frozen, _armored;
        private int _tier;
        private Vector3 _hitDirection;
        private ushort _freezeTotal, _freezeRemaining;
        private float _releaseTime;
        private float _flashTime = float.NegativeInfinity;
        private Color _flashTint = Color.white;

        // Attacker coil/strike: targets from the attack timeline, current values eased out.
        private bool _attacking;
        private Vector3 _attackAxis = Vector3.up;
        private float _attackStretchTarget, _attackLeanTarget, _attackStretch, _attackLean;

        // Air dodge.
        private bool _dodging;
        private Vector3 _dodgeDirection;
        private int _dodgeTicks;
        private float _dodgeTickTime;

        // Launch.
        private Vector3 _lastPosition, _velocity;
        private bool _hasPosition;
        private float _launch;
        private byte _deaths;
        private MovementFeedbackEffect? _smoke, _streaks;

        // Review diagnostics.
        private float _peakLaunch, _peakSpeed;
        private int _trailStarts, _flashFrames, _smokeMissing;

        // Applied pivot pose, kept for suspension around authoritative pose sampling.
        private Vector3 _axisPosition, _counterPosition, _axisScale = Vector3.one;
        private Quaternion _axisRotation = Quaternion.identity, _counterRotation = Quaternion.identity;
        private int _suspended;

        public void Init(PlayerRenderer renderer) => _renderer = renderer;

        /// <summary>Insert the deform pivots above a model instance; repeat calls only refresh renderers.</summary>
        public void AttachModel(Transform model)
        {
            if (_renderer == null || model == null) return;
            if (_counter == null || model.parent != _counter)
            {
                _axis = new GameObject("Hit Feel Pivot").transform;
                _axis.SetParent(_renderer.transform, false);
                _counter = new GameObject("Hit Feel Counter").transform;
                _counter.SetParent(_axis, false);
                model.SetParent(_counter, false);
            }
            ClearFlash();
            _skins.Clear();
            model.GetComponentsInChildren(true, _skins);
            ResetReaction();
        }

        public void ReceiveHit(in SpellResolver.HitResult hit, ImpactTier tier, Color attackerTint)
        {
            _hit = true;
            _tier = Mathf.Clamp((int)tier, 0, ShakeMeters.Length - 1);
            _armored = hit.ArmorProtected;
            var horizontal = new Vector3(hit.DirX, 0f, hit.DirZ);
            horizontal = horizontal.sqrMagnitude > 1e-6f ? horizontal.normalized : -transform.forward;
            float angle = hit.KnockbackAngle * Mathf.Deg2Rad;
            _hitDirection = (horizontal * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
            _freezeTotal = _freezeRemaining = hit.HitstopTicks;
            _frozen = hit.HitstopTicks > 0;
            _releaseTime = Time.time;
            _flashTime = Time.time;
            _flashTint = attackerTint;
            if (hit.ArmorProtected) ArmorAbsorbed?.Invoke();
        }

        /// <summary>
        /// Holds a rim glow in <paramref name="color"/> until <see cref="EndGlow"/> with the same
        /// key, e.g. for an attack's armour window. A hit flash still plays over it.
        /// </summary>
        public void BeginGlow(Object key, Color color)
        {
            EndGlow(key);
            _glows.Add((key, color));
        }

        public void EndGlow(Object key) => _glows.RemoveAll(glow => glow.key == key);

        /// <summary>Call once per applied simulation tick, after the renderer has applied the state.</summary>
        public void OnState(in CharacterState state, CharacterDefinition? definition, bool attackAirborne)
        {
            var position = new Vector3(state.PX, state.PY, state.PZ);
            if (state.Deaths != _deaths)
            {
                _deaths = state.Deaths;
                _hasPosition = false;
                ResetReaction();
            }
            if (_hasPosition)
            {
                Vector3 raw = (position - _lastPosition) / SlopArena.Shared.Simulation.TickDt;
                // Respawns and authoritative corrections are not motion.
                _velocity = raw.sqrMagnitude > 60f * 60f ? Vector3.zero : Vector3.Lerp(_velocity, raw, 0.5f);
            }
            _lastPosition = position;
            _hasPosition = true;

            if (_hit && _frozen)
            {
                _freezeRemaining = state.HitstopTicks;
                if (state.HitstopTicks == 0)
                {
                    _frozen = false;
                    _releaseTime = Time.time;
                }
            }

            bool flying = (state.HitstunTicks > 0 || state.InPostHitstunFlight)
                && !state.IsGrounded && state.HitstopTicks == 0;
            _launch = flying
                ? Mathf.Clamp01((_velocity.magnitude - LaunchSpeed) / (LaunchFullSpeed - LaunchSpeed))
                : 0f;
            _peakLaunch = Mathf.Max(_peakLaunch, _launch);
            if (flying) _peakSpeed = Mathf.Max(_peakSpeed, _velocity.magnitude);
            _dodging = state.State == ActionState.AirDodgeMovement;
            if (_dodging)
            {
                _dodgeDirection = new Vector3(state.DashDirX, 0f, state.DashDirZ);
                _dodgeTicks = DefenseConfig.AirDodgeMovementTicks - state.StateTicks;
                _dodgeTickTime = Time.time;
            }
            UpdateAttack(in state, definition, attackAirborne);
        }

        /// <summary>Remove the deformation while an authoritative pose is sampled.</summary>
        public void SuspendDeformation()
        {
            if (_suspended++ > 0 || _axis == null || _counter == null) return;
            _axis.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _axis.localScale = Vector3.one;
            _counter.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>Pair with <see cref="SuspendDeformation"/>; nested pairs restore on the outermost resume.</summary>
        public void ResumeDeformation()
        {
            if (_suspended == 0 || --_suspended > 0) return;
            WritePivots();
        }

        /// <summary>Map an undeformed world point through the current deformation, e.g. a sampled blade.</summary>
        public Vector3 DeformPoint(Vector3 world)
        {
            if (_axis == null || _counter == null) return world;
            Matrix4x4 deform = Matrix4x4.TRS(_axisPosition, _axisRotation, _axisScale)
                * Matrix4x4.TRS(_counterPosition, _counterRotation, Vector3.one);
            Transform root = _renderer.transform;
            return root.TransformPoint(deform.MultiplyPoint3x4(root.InverseTransformPoint(world)));
        }

        private void UpdateAttack(in CharacterState state, CharacterDefinition? definition, bool airborne)
        {
            _attacking = false;
            if (definition == null || state.State != ActionState.Attacking || state.AttackSlot == 0
                || state.HitstunTicks > 0)
                return;
            var ability = definition.GetSlotAbility((byte)(state.AttackSlot - 1), airborne);
            if (ability?.Stages is not { Length: > 0 } stages) return;
            var hitboxes = stages[Mathf.Min(state.ComboStage, stages.Length - 1)].HitboxEvents;
            if (hitboxes is not { Length: > 0 }) return;

            int elapsed = state.AttackElapsedTicks;
            int first = int.MaxValue, latest = -1;
            float firstDamage = 0f, latestDamage = 0f;
            foreach (var hitbox in hitboxes)
            {
                int trigger = hitbox.TriggerTick;
                if (trigger < first)
                {
                    first = trigger;
                    firstDamage = hitbox.Damage;
                }
                if (trigger <= elapsed && trigger > latest)
                {
                    latest = trigger;
                    latestDamage = hitbox.Damage;
                }
            }

            _attacking = true;
            if (latest < 0)
            {
                // Coil: crouch and lean back, accelerating into the first active tick.
                int weight = Weight(firstDamage);
                float u = first <= 0 ? 1f : Mathf.Clamp01((float)elapsed / first);
                float ease = u * u * (3f - 2f * u);
                _attackAxis = Vector3.up;
                _attackStretchTarget = -CoilSquash[weight] * ease;
                _attackLeanTarget = -CoilDegrees[weight] * ease;
            }
            else
            {
                // Strike: snap forward on each active tick, then settle. Elapsed ticks
                // stop during Hitstop, so the pose holds through a confirmed hit.
                int weight = Weight(latestDamage);
                float pulse = Spring((elapsed - latest) * SlopArena.Shared.Simulation.TickDt) * (latest == first ? 1f : 0.6f);
                _attackAxis = Vector3.forward;
                _attackStretchTarget = StrikeStretch[weight] * pulse;
                _attackLeanTarget = StrikeDegrees[weight] * pulse;
            }
        }

        private void LateUpdate()
        {
            if (_renderer == null || _axis == null || _counter == null) return;
            float now = Time.time;

            if (_attacking)
            {
                _attackStretch = _attackStretchTarget;
                _attackLean = _attackLeanTarget;
            }
            else
            {
                float keep = Mathf.Exp(-Time.deltaTime / AttackReleaseSeconds);
                _attackStretch *= keep;
                _attackLean *= keep;
            }

            Vector3 axis = Vector3.up, leanAxis = Vector3.right, shake = Vector3.zero;
            float stretch = 0f, lean = 0f, pivotHeight = 0f;
            Quaternion toLocal = Quaternion.Inverse(_renderer.transform.rotation);
            if (_hit)
            {
                Vector3 direction = toLocal * _hitDirection;
                var horizontal = new Vector3(direction.x, 0f, direction.z);
                float crush = _armored ? 0f : CrushAmount[_tier] * Strength.Squash;
                float recoil = _armored ? 0f : RecoilDegrees[_tier] * Strength.Lean;
                float k;
                if (_frozen)
                {
                    k = 1f;
                    float decay = _freezeTotal > 0 ? (float)_freezeRemaining / _freezeTotal : 0f;
                    float sign = (_freezeRemaining & 1) == 0 ? 1f : -1f;
                    float amplitude = ShakeMeters[_tier] * Strength.Shake * (_armored ? 0.5f : 1f);
                    shake = direction * (amplitude * decay * sign);
                }
                else
                {
                    float t = now - _releaseTime;
                    k = Spring(t);
                    if (t > 0.6f && _launch <= 0f) _hit = false;
                }
                axis = direction;
                stretch = -crush * k;
                // Positive rotation about up×h tips the head along h, away from the attacker.
                if (horizontal.sqrMagnitude > 1e-6f)
                {
                    leanAxis = Vector3.Cross(Vector3.up, horizontal.normalized);
                    lean = recoil * k;
                }
            }
            else if (Mathf.Abs(_attackStretch) > 1e-4f || Mathf.Abs(_attackLean) > 0.01f)
            {
                axis = _attackAxis;
                stretch = _attackStretch * Strength.Squash;
                lean = _attackLean * Strength.Lean;
            }
            else if (_dodging && _dodgeDirection.sqrMagnitude > 1e-6f)
            {
                float elapsed = _dodgeTicks + Mathf.Clamp01((now - _dodgeTickTime) / SlopArena.Shared.Simulation.TickDt);
                float t = Mathf.Clamp01(elapsed / DefenseConfig.AirDodgeMovementTicks);
                float k = Mathf.Pow(1f - t, 1.4f);
                Vector3 direction = toLocal * _dodgeDirection.normalized;
                axis = direction;
                stretch = DodgeStretch * k;
                // Positive rotation about up×d tips the head along the dodge.
                leanAxis = Vector3.Cross(Vector3.up, direction);
                lean = DodgeLeanDegrees * k;
                pivotHeight = -_renderer.ModelYOffset;   // the capsule centre
            }

            if (_launch > 0f && Strength.Squash > 0f && _velocity.sqrMagnitude > 1e-4f)
            {
                // Stretch along the flight about the body centre, not the feet.
                axis = toLocal * _velocity.normalized;
                stretch += MaxLaunchStretch * _launch * Strength.Squash;
                pivotHeight = CenterHeight() * _launch;
            }

            SetPivots(axis, stretch, leanAxis, lean, shake, pivotHeight);
            UpdateFlash(now);
            UpdateTrail();
        }

        private void SetPivots(Vector3 axis, float stretch, Vector3 leanAxis, float leanDegrees,
            Vector3 shake, float pivotHeight)
        {
            if (Mathf.Abs(stretch) < 1e-4f && Mathf.Abs(leanDegrees) < 0.01f && shake.sqrMagnitude < 1e-8f)
            {
                _axisPosition = _counterPosition = Vector3.zero;
                _axisRotation = _counterRotation = Quaternion.identity;
                _axisScale = Vector3.one;
            }
            else
            {
                // Scale along `axis` about p, then lean: T(p)·L·R·S·R⁻¹·T(−p).
                var pivot = new Vector3(0f, pivotHeight, 0f);
                Quaternion toAxis = axis.sqrMagnitude > 1e-6f
                    ? Quaternion.FromToRotation(Vector3.up, axis.normalized)
                    : Quaternion.identity;
                Quaternion fromAxis = Quaternion.Inverse(toAxis);
                float along = Mathf.Max(0.2f, 1f + stretch);
                float across = 1f / Mathf.Sqrt(along);
                _axisPosition = pivot + shake;
                _axisRotation = Quaternion.AngleAxis(leanDegrees, leanAxis) * toAxis;
                _axisScale = new Vector3(across, along, across);
                _counterRotation = fromAxis;
                _counterPosition = -(fromAxis * pivot);
            }
            if (_suspended == 0) WritePivots();
        }

        private void WritePivots()
        {
            if (_axis == null || _counter == null) return;
            _axis.SetLocalPositionAndRotation(_axisPosition, _axisRotation);
            _axis.localScale = _axisScale;
            _counter.SetLocalPositionAndRotation(_counterPosition, _counterRotation);
        }

        /// <summary>
        /// Two white frames scaled by tier, then a four-frame rim pulse in the attacker's
        /// colour that keeps the fighter's own colours. Reduced Flashing keeps only the pulse.
        /// </summary>
        private void UpdateFlash(float now)
        {
            // Frames at the 60 Hz presentation rate; the epsilon absorbs Time.time rounding.
            float frames = (now - _flashTime) * 60f + 0.01f;
            float white = 0f, rim = 0f;
            bool reduced = !IgnoreReducedFlashing && ClientSettingsService.Instance.ReducedFlashing;
            if (Strength.Flash > 0f && frames >= 0f)
            {
                if (!reduced && frames < 2f)
                    white = Mathf.Min(0.9f, FlashWhite[_tier] * Strength.Flash);
                float rimStart = reduced ? 0f : 2f;
                if (frames >= rimStart && frames < rimStart + 4f)
                    rim = (1f - (frames - rimStart) / 4f) * Mathf.Min(1f, Strength.Flash);
            }
            // A held glow pulses gently; Reduced Flashing keeps it steady.
            float glow = 0f;
            Color glowColor = Color.white;
            _glows.RemoveAll(held => held.key == null);
            if (_glows.Count > 0)
            {
                glowColor = _glows[_glows.Count - 1].color;
                glow = reduced ? 0.85f : 0.75f + 0.25f * Mathf.Sin(now * GlowPulseHz * Mathf.PI * 2f);
            }
            if (white <= 0f && rim <= 0f && glow <= 0f)
            {
                ClearFlash();
                return;
            }
            _block ??= new MaterialPropertyBlock();
            foreach (var skin in _skins)
            {
                if (skin == null) continue;
                skin.GetPropertyBlock(_block);
                _block.SetColor(FlashColorId, Color.white);
                _block.SetFloat(FlashAmountId, white);
                if (rim > 0f)
                {
                    _block.SetColor(RimColorId, _flashTint);
                    _block.SetFloat(RimStrengthId, Mathf.Lerp(MatchVisualStyle.RimStrength, 2f, rim));
                    _block.SetFloat(RimPowerId, Mathf.Lerp(MatchVisualStyle.RimPower, 1.2f, rim));
                }
                else if (glow > 0f)
                {
                    _block.SetColor(RimColorId, glowColor);
                    _block.SetFloat(RimStrengthId, Mathf.Lerp(MatchVisualStyle.RimStrength, GlowRimStrength, glow));
                    _block.SetFloat(RimPowerId, Mathf.Lerp(MatchVisualStyle.RimPower, GlowRimPower, glow));
                }
                skin.SetPropertyBlock(_block);
            }
            _flashApplied = true;
            if (white > 0f || rim > 0f) _flashFrames++;
        }

        private void ClearFlash()
        {
            if (!_flashApplied) return;
            // Dropping the block restores SRP batching for the fighter.
            foreach (var skin in _skins)
                if (skin != null) skin.SetPropertyBlock(null);
            _flashApplied = false;
        }

        private void UpdateTrail()
        {
            if (_launch <= 0f || Strength.Trail <= 0f)
            {
                EndTrail();
                return;
            }
            Vector3 center = _renderer.transform.position + Vector3.up * CenterHeight();
            if (_smoke == null)
            {
                _smoke = MovementFeedbackEffect.BeginLaunchTrail(center);
                if (_smoke == null) _smokeMissing++;
                else _trailStarts++;
            }
            _smoke?.FollowLaunchTrail(center, _launch, Strength.Trail);
            // Wind streaks only on hard launches, pointing back along the flight.
            if (_launch > 0.35f)
            {
                _streaks ??= MovementFeedbackEffect.BeginDashTrail(center, -_velocity);
                _streaks?.FollowDashTrail(center, -_velocity);
            }
            else if (_streaks != null)
            {
                _streaks.EndDashTrail();
                _streaks = null;
            }
        }

        private void EndTrail()
        {
            _smoke?.EndLaunchTrail();
            _smoke = null;
            _streaks?.EndDashTrail();
            _streaks = null;
        }

        /// <summary>A fresh simulation (Arcade retry): drop reactions, glows and the motion history.</summary>
        public void ResetForFreshSimulation()
        {
            _deaths = 0;
            _hasPosition = false;
            _glows.Clear();
            ResetReaction();
        }

        private void ResetReaction()
        {
            _hit = _frozen = _attacking = _dodging = false;
            _attackStretch = _attackLean = _attackStretchTarget = _attackLeanTarget = 0f;
            _velocity = Vector3.zero;
            _launch = 0f;
            _flashTime = float.NegativeInfinity;
            ClearFlash();
            EndTrail();
            SetPivots(Vector3.up, 0f, Vector3.right, 0f, Vector3.zero, 0f);
        }

        /// <summary>Counters for review tooling; not used by gameplay presentation.</summary>
        public object Diagnostics() => new
        {
            skins = _skins.Count, peakLaunch = _peakLaunch, peakFlightSpeed = _peakSpeed,
            trailStarts = _trailStarts, smokeMissing = _smokeMissing, flashFrames = _flashFrames
        };

        private float CenterHeight() => Mathf.Max(0f, _renderer.CapsuleHeight * 0.5f - _renderer.ModelYOffset);

        private static int Weight(float damage) => damage >= 11f ? 2 : damage >= 6f ? 1 : 0;

        /// <summary>Damped spring released from 1 at t = 0; overshoots once, then settles.</summary>
        private static float Spring(float seconds)
        {
            if (seconds <= 0f) return 1f;
            float omega = 2f * Mathf.PI * SpringHz;
            float damped = omega * Mathf.Sqrt(1f - SpringDamping * SpringDamping);
            return Mathf.Exp(-SpringDamping * omega * seconds) * Mathf.Cos(damped * seconds);
        }

        private void OnDisable()
        {
            ClearFlash();
            EndTrail();
        }
    }
}
