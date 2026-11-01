using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
	public partial class ServerSimulation
	{
		private readonly ArenaDefinition _arena;
		private readonly Dictionary<ulong, CharacterState> _states = new();
		private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
		private readonly Dictionary<ulong, byte> _kos = new();
		private readonly Dictionary<ulong, (ulong attackerId, uint tick, byte slot)> _lastHitCredits = new();
		private readonly Dictionary<ulong, (ulong attackerId, uint tick, byte slot)> _lastHitContexts = new();
		private readonly Dictionary<ulong, ulong> _lastActivationIds = new();
		private ulong _nextActivationId;
		private ulong _nextInteractionId;
		private ulong[] _interactionScratch = Array.Empty<ulong>();
		private int[] _grabTriangles = Array.Empty<int>();
		private readonly List<(ulong grabber, ulong target, float distance)> _grabCandidates = new();
		private readonly HashSet<ulong> _clashedThisTick = new();
		/// <summary>False for online local prediction; capture, release and interruption remain server-owned.</summary>
		public bool PredictCoupledInteractions { get; set; } = true;

		private uint _tick;
		public void SetTick(uint tick) => _tick = tick;
		private readonly List<TimelinePresentationEvent> _presentationEvents = new();
		private const uint KillCreditWindowTicks = 180;

		/// <summary>Authoritative death context captured before cancellation and respawn.</summary>
		public struct DeathEvent
		{
			public uint Tick;
			public ulong EntityId;
			public CharacterState State;
			public ulong KillerEntityId;
			public ulong LastHitEntityId;
			public uint LastHitTick;
			public byte LastHitSlot;
			public string Boundary;
		}

		/// <summary>Blast deaths captured during the most recent simulation tick.</summary>
		public List<DeathEvent> LastTickDeaths { get; } = new();

		private readonly Dictionary<ulong, BakedAnimationData> _bakedData = new();
		private readonly Dictionary<ulong, int> _animFrames = new();
		private readonly Dictionary<ulong, int> _prevAnimIndex = new();
		private List<SpellResolver.EntityData> _lastEntityList = new();
		private readonly List<SpellResolver.EntityData> _attackEntities = new();
		private readonly List<SpellResolver.EntityData> _lungeHurtboxes = new();
		private readonly HashSet<ulong> _shieldSurfaceEntities = new();
		public List<SpellResolver.HitResult> LastTickHits { get; } = new();
		/// <summary>Exact surfaces consumed by the latest attack collision pass, including active shields.</summary>
		public IReadOnlyList<SpellResolver.EntityData> LastTickAttackEntities { get; private set; }
			= Array.Empty<SpellResolver.EntityData>();
		private readonly HashSet<ulong> _lastTickAcceptedActions = new();
		private readonly HashSet<ulong> _damagedThisTick = new();
		/// <summary>Fighters frozen by hitstop this tick; their melee windows pause with their move.</summary>
		private readonly HashSet<ulong> _hitstopFrozenThisTick = new();
		/// <summary>Entity IDs whose actions were accepted during the most recent tick.</summary>
		public IReadOnlyCollection<ulong> LastTickAcceptedActions
			=> _lastTickAcceptedActions;
		private readonly HashSet<ulong> _lastTickTouchdowns = new();
		/// <summary>Entity IDs with a genuine airborne-to-ground touchdown during the most recent tick.</summary>
		public IReadOnlyCollection<ulong> LastTickTouchdowns
			=> _lastTickTouchdowns;
		private readonly HashSet<ulong> _settledCrouchCandidates = new();
		private readonly HashSet<ulong> _lastTickOrdinaryActionOpportunities = new();
		public IReadOnlyCollection<ulong> LastTickOrdinaryActionOpportunities
			=> _lastTickOrdinaryActionOpportunities;
		private readonly Dictionary<ulong, DownActionAdmissionReason> _lastTickDownAdmissions = new();
		/// <summary>Actual grounded Down admission result for the most recent tick.</summary>
		public IReadOnlyDictionary<ulong, DownActionAdmissionReason> LastTickDownAdmissions
			=> _lastTickDownAdmissions;
		private readonly SpellResolver _spellResolver = new();
		private readonly Func<ulong, ulong, bool>? _attackTargetFilter;
		/// <summary>Authoritative KOs credited during the current match.</summary>
		public byte GetKOs(ulong entityId) => _kos.TryGetValue(entityId, out var kos) ? kos : (byte)0;

		private readonly Dictionary<ulong, (float x, float y, float z, float yaw)> _respawnPositions = new();
		// ── Ability pool ──
		private readonly Dictionary<ulong, ServerAbility> _activeAbilities = new();
		private bool _abilityTickPhaseComplete;
		private readonly IMatchRule _rule;
		private readonly DownActionTuning _downActionTuning;
		/// <summary>Immutable combat settings owned by this simulation.</summary>
		public CombatTuning CombatTuning { get; }
		private readonly ArenaCollision.BlastLines _blastLines;
		/// <summary>Ticks of invincibility granted on respawn (60 = 1s at 60Hz). Issue #37.</summary>
		public ushort RespawnInvincibilityTicks { get; set; } = 60;
	/// <summary>Training-only opt-in: when set, this entity's slot cooldowns are never
	/// applied and never gate ability start. Null = normal cooldowns (PvP default).</summary>
	public ulong? NoCooldownsEntityId;

		/// <summary>Authoring-only opt-in: suspend ambient gravity for one entity.
		/// Authored velocity and owned vertical motion still run. Null = normal match gravity.</summary>
		public ulong? NoGravityEntityId;

		/// <param name="rule">Win-condition rule (elimination + match end). Defaults to stock mode, 3 stocks.</param>
		/// <param name="downActionTuning">Immutable down-action tuning. Defaults to the shared production values.</param>
		/// <param name="combatTuning">Immutable combat tuning. Defaults to the shipped profile.</param>
		public ServerSimulation(ArenaDefinition arena, IMatchRule? rule = null, DownActionTuning? downActionTuning = null,
			CombatTuning? combatTuning = null)
		{
			_arena = arena;
			_blastLines = ArenaCollision.ResolveBlastLines(in arena);
			_rule = rule ?? new StockMatchRule(3);
			_attackTargetFilter = _rule is SlopballCoopMatchRule or SlopballVersusMatchRule or CourseMatchRule or MankiMayhemMatchRule ? CanAttackTarget : null;
			_downActionTuning = downActionTuning ?? DownActionTuning.Default;
			CombatTuning = combatTuning ?? SlopArena.Shared.CombatTuning.Default;
			if (arena.Arcade?.Ball is ArcadeBallDefinition ball)
            {
                _arcadeBall = new ArcadeBallSimulation(ball, in arena);
                if (arena.Arcade.Slopball is SlopballDefinition rules)
                {
                    rules.ValidateCourt(in arena);
                    if (_rule is SlopballVersusMatchRule versus)
                        _volley = new SlopballVersusSimulation(rules, versus, _arcadeBall);
                    else _slopball = new SlopballSimulation(rules, ball, _arcadeBall, rules.CeilingOf(in arena));
                }
            }
            if (arena.Arcade?.Course is CharacterCourseDefinition course)
            {
                course.ValidateArena(in arena);
                if (_rule is CourseMatchRule courseRule) _onlineCourse = new OnlineCourseSimulation(course, courseRule.Format);
                else _course = new CharacterCourseSimulation(course);
            }
            if (_rule is MankiMayhemMatchRule)
            {
                if (arena.Arcade?.Mayhem == null) throw new ArgumentException("Mayhem needs a Mayhem arena.", nameof(rule));
                arena.Arcade.Mayhem.ValidateArena(in arena);
                _mayhem = new MankiMayhemSimulation(arena.Arcade.Mayhem);
            }
            if (_rule is CourseMatchRule && _onlineCourse == null)
                throw new ArgumentException("Online courses need a course arena.", nameof(rule));
            if (_rule is SlopballCoopMatchRule && _slopball == null)
                throw new ArgumentException("Co-op Slopball needs a Slopball court.", nameof(rule));
            if (_rule is SlopballVersusMatchRule && _volley == null)
                throw new ArgumentException("Versus Slopball needs a Slopball court.", nameof(rule));
		}

        /// <summary>Game-wide connecting-hit freeze, preserving shipped default timing.</summary>
        public static ushort ComputeHitstopTicks(float damage)
            => (ushort)Math.Max(1, Math.Min(12, (int)(damage / 3f + 6f)));

		public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
		{
            if ((_slopball != null || _course != null) && _rule is not SlopballCoopMatchRule
                && _states.Count > 0 && !_states.ContainsKey(id))
                throw new ArgumentException("Solo Arcade challenges admit one fighter.", nameof(id));
			if ((_course != null || _onlineCourse != null) && id >= CharacterCourseSimulation.FirstInputAlias)
				throw new ArgumentException("Course target aliases and object identities are reserved.", nameof(id));
			if (_arcadeBall != null && (id == ArcadeBallSimulation.TargetInputId || id >= ArcadeBallSimulation.FirstEntityId))
				throw new ArgumentException("Arcade reserves the ball target alias and object identity range.", nameof(id));
            if (_mayhem != null && (id == 0 || id >= ArcadeBallSimulation.FirstEntityId
                || !_states.ContainsKey(id) && (_states.Count >= 4 || _mayhem.State.Seed != 0)))
                throw new ArgumentException("Mayhem reserves object identities and admits up to four fighters before Begin.", nameof(id));
			_defs[id] = def;
                if (_onlineCourse != null) _onlineCourse.Register(id, initialState);
			initialState.EntityId = id;
			// The seat's starting form (ADR-0051); fighters without forms ignore it.
			if (def.ForForm(initialState.Form.Active).Form != initialState.Form.Active) initialState.Form = default;
            Flying.Refuel(ref initialState, def.ForForm(initialState.Form.Active));
			_states[id] = initialState;
			_kos[id] = 0;
			_lastHitCredits.Remove(id);
			_lastHitContexts.Remove(id);
			_lastActivationIds.Remove(id);

			if (baked != null) _bakedData[id] = baked;
			_animFrames[id] = 0;
			_prevAnimIndex[id] = -1;
			SortStatesByEntity();
	}

		public void SetRespawnPosition(ulong entityId, float x, float y, float z, float yaw = 0f)
		{
			_respawnPositions[entityId] = (x, y, z, yaw);
		}

		public void RemoveEntity(ulong id)
		{
			if (PredictCoupledInteractions && _states.TryGetValue(id, out var state))
				InterruptInteraction(ref state);
			state = _states.TryGetValue(id, out var current) ? current : default;
			RetireAbility(id, ref state, AbilityExit.Removed);
			_states.Remove(id);
			_defs.Remove(id);
			_bakedData.Remove(id);
			_animFrames.Remove(id);
			_prevAnimIndex.Remove(id);
			_respawnPositions.Remove(id);
			_kos.Remove(id);
			_lastActivationIds.Remove(id);
			_lastHitCredits.Remove(id);
			_lastHitContexts.Remove(id);
            foreach (var victim in _lastHitCredits.Where(pair => pair.Value.attackerId == id).Select(pair => pair.Key).ToArray())
                _lastHitCredits.Remove(victim);
            foreach (var victim in _lastHitContexts.Where(pair => pair.Value.attackerId == id).Select(pair => pair.Key).ToArray())
                _lastHitContexts.Remove(victim);
		}


		public CharacterState GetState(ulong id) => _states.TryGetValue(id, out var s) ? s : default;
		public CharacterDefinition GetDefinition(ulong id) => TryDef(id, out var d) ? d : null;
		/// <summary>The definition a fighter plays with now. Every per-fighter definition read goes through
		/// here; snapshots alone read the registered definition to re-link stored content (ADR-0051).</summary>
		private CharacterDefinition Def(ulong id) => _defs[id].ForForm(FormOf(id));
		private bool TryDef(ulong id, out CharacterDefinition def)
		{
			if (!_defs.TryGetValue(id, out def)) return false;
			def = def.ForForm(FormOf(id));
			return true;
		}
		private byte FormOf(ulong id) => _states.TryGetValue(id, out var state) ? state.Form.Active : (byte)0;
		public void SetState(ulong id, CharacterState state) => _states[id] = state;
        private enum AbilityExit : byte { Completed, Cancelled, Superseded, Removed, Reset }

        private void CancelAttackRuntime(ulong id, ref CharacterState state, bool removeProjectiles = false)
            => RetireAbility(id, ref state, removeProjectiles ? AbilityExit.Reset
                : state.State is (ActionState.Attacking or ActionState.Aiming) ? AbilityExit.Cancelled : AbilityExit.Superseded);

        private void RetireAbility(ulong id, ref CharacterState state, AbilityExit exit)
        {
            if (exit is AbilityExit.Reset or AbilityExit.Removed)
            {
                state.Flight = default;
                if (exit == AbilityExit.Reset) Flying.Refuel(ref state, Def(id));
                if (state.State == ActionState.Flying) state.State = ActionState.Idle;
            }
            CookedShellRide? ride = null;
            if (_activeAbilities.TryGetValue(id, out var ability))
            {
                // A ride move that completes puts her on the ride (ADR-0050).
                if (exit == AbilityExit.Completed && ability.Move?.ShellPolicy == ShellPolicy.Ride)
                    ride = TryDef(id, out var owner) ? owner.Shell?.Ride : null;
                if (exit != AbilityExit.Completed)
                {
                    if (exit == AbilityExit.Cancelled)
                        ability.OnCancel(ref state);
                    else
                    {
                        var discardedCancellationState = state;
                        ability.OnCancel(ref discardedCancellationState);
                    }
                    _spellResolver.RemoveOwnedHitboxes(id, ability.ActivationId,
                        preserveProjectiles: exit != AbilityExit.Removed && exit != AbilityExit.Reset);
                }
                ability.ClearArmorWindow();
                if (exit != AbilityExit.Removed && exit != AbilityExit.Reset)
                    ApplyAbilityCooldown(id, ability, ref state);
                _activeAbilities.Remove(id);
            }
            if (exit == AbilityExit.Removed)
                _spellResolver.RemoveOwnerEffects(id);
            else if (exit == AbilityExit.Reset)
                _spellResolver.RemoveContactBombs(id);
            ushort completionRecoveryLockTicks = exit == AbilityExit.Completed ? state.AnimLockTicks : (ushort)0;
            state.ClearAttackFields();
            state.AnimLockTicks = completionRecoveryLockTicks;
            if (exit == AbilityExit.Cancelled && state.State is (ActionState.Attacking or ActionState.Aiming))
                state.State = ActionState.Idle;
            if (state.Flight.Active && state.State == ActionState.Idle) state.State = ActionState.Flying;
            if (ride != null)
                EnterRide(ref state, ride);
        }

        private void ApplyAbilityCooldown(ulong id, ServerAbility ability, ref CharacterState state)
        {
            if (ability.Slot + 1 == AbilitySlots.E && state.Flight.Active) return;
            if (ability.Slot < AbilitySlots.Count && NoCooldownsEntityId != id)
                Forms.SetCooldown(ref state, (byte)(ability.Slot + 1), ability.CharacterDef?.Form ?? 0, ability.Cooldown);
        }


		public Dictionary<ulong, CharacterState> GetAllStates() => _states;
		/// <summary>Latest activation identity for an entity, including lingering projectiles.</summary>
		public ulong GetLastActivationId(ulong entityId)
			=> _lastActivationIds.TryGetValue(entityId, out var id) ? id : 0;
		public List<SpellResolver.EntityData> GetLastEntityData() => _lastEntityList;
		public SpellResolver Resolver => _spellResolver;
		public IReadOnlyList<TimelinePresentationEvent> GetPresentationEvents(bool clear = false)
		{
			var snapshot = new List<TimelinePresentationEvent>(_presentationEvents);
			if (clear) _presentationEvents.Clear();
			return snapshot;
		}

		public void ClearPresentationEvents() => _presentationEvents.Clear();

		// ── Ability pool management ──

		/// <summary>
		/// Activate a server ability for an entity.
		/// Calls OnStart and registers the ability for per-tick updates.
		/// </summary>
		/// <param name="fromShell">She was in the shell when the press arrived, before any IASA cancel (ADR-0045).</param>
		public void ActivateAbility(ulong entityId, ServerAbility ability, byte slot, CharacterDefinition def, short? activationAimYaw = null, InputState activationInput = default, bool fromShell = false)
		{
			if (!_states.TryGetValue(entityId, out var state)) return;
			unchecked
			{
				_nextActivationId++;
				if (_nextActivationId == 0) _nextActivationId = 1;
			}
			ability.ActivationId = _nextActivationId;
			_lastActivationIds[entityId] = ability.ActivationId;
			ability.Resolver = _spellResolver;
			ability.OwnerSimulation = this;
			ability.ActivationInput = activationInput;
			ability.SimulationStates = _states;
			ability.BakedData = _bakedData.TryGetValue(entityId, out var b) ? b : null;
			ability.CharacterDef = def;
			ability.Arena = _arena;
			ability.PresentationSink = e => _presentationEvents.Add(e with { MatchTick = _tick });
			ability.Slot = slot;
			ability.AirborneAtStart = !state.IsGrounded;
            var cookedSlot = def.GetCookedSlotAbility((byte)(slot + 1), !state.IsGrounded);
            if (!Shell.StartMove(ref state, cookedSlot, fromShell))
            {
                bool preserveMomentum = cookedSlot?.PreserveMomentumOnStart ?? false;
                bool attackCarry = state.IsGrounded
                    && cookedSlot?.AllowSlideCarry == true
                    && (state.State == ActionState.Sliding
                        || state.State == ActionState.Run && !preserveMomentum);
                state.SlideAttackCarryActive = false;
                state.AttackCarryFrom = attackCarry && state.State == ActionState.Run ? AttackCarryOrigin.Run : AttackCarryOrigin.Slide;
                if (attackCarry)
                {
                    float capRatio = state.AttackCarryFrom == AttackCarryOrigin.Run
                        ? DownActionTuning.RunAttackEntryCapRatio
                        : DownActionTuning.AttackEntryCapRatio;
                    Simulation.ClampHorizontalSpeed(ref state, def.Movement.RunSpeed * capRatio);
                    state.SlideAttackCarryActive = true;
                }
                if (state.IsGrounded && !preserveMomentum && !attackCarry)
                {
                    state.VX = 0f;
                    state.VZ = 0f;
                }
            }
            // Activation aim is the camera direction for moves that consume it on start.
            // Apply it before OnStart so capabilities cache the current input, not the
            // previous tick's state.
            if (activationAimYaw.HasValue)
                state.AimYaw = activationAimYaw.Value * 0.01f * (MathF.PI / 180f);
			// Presentation-only restart marker: same-slot IASA keeps the attacking
			// state, slot, and stage unchanged, so clients need an explicit edge.
			unchecked { state.AttackSequence++; }
			ability.PresentationAttackSequence = state.AttackSequence;
			ability.PresentationOperationIndex = -1;
			ability.ClearArmorWindow();
			state.ClearStartupAimCorrection();
			state.BufferedSlot = 0;
			// The homing dash arrival window and the ride are timed by StateTicks; its expiry must not end the attack.
			if (state.State is ActionState.HomingDashArrive or ActionState.Shell)
				state.StateTicks = 0;
            bool aimingAbility = cookedSlot?.AimMode is { } aimMode && aimMode != AuthoringAimMode.None;
            if (aimingAbility && ability is CookedTimelineAbility { UsesStartupAimCorrection: true })
                state.FacingYaw = state.AimYaw;
			ability.OnStart(ref state, def);
			if (_abilityTickPhaseComplete)
				ability.AccountForCurrentContactFrame();
            if (aimingAbility && !state.AttackCorrectionOwned)
                state.FacingYaw = activationAimYaw.HasValue
                    ? activationAimYaw.Value * 0.01f * (MathF.PI / 180f)
                    : state.AimYaw;
			state.AnimIndex = ability.AnimIndex;
			if (state.State != ActionState.Attacking && state.State != ActionState.Aiming)
			{
				ApplyAbilityCooldown(entityId, ability, ref state);
				_states[entityId] = state;
				return;
			}
			state.AttackSlot = (byte)(slot + 1);
            // An accepted ability leaves low posture immediately; do not carry a
            // settled stance or a previously captured brace into the attack.
            state.CrouchSettled = false;
            state.QueuedCrouchBrace = false;
            // ADR-0015 / issue #115: recovery-designated moves reset the float window.
            if (cookedSlot?.IsRecoveryMove == true)
                state.AirTimeTicks = 0;
			_states[entityId] = state;
			_activeAbilities[entityId] = ability;
			_lastTickAcceptedActions.Add(entityId);
		}


		/// <summary>
		/// Get the active ability for an entity, or null if none.
		/// </summary>
		public ServerAbility? GetActiveAbility(ulong entityId)
		{
			return _activeAbilities.TryGetValue(entityId, out var a) ? a : null;
		}

        public ActiveMoveView? GetActiveMove(ulong entityId)
        {
            if (!_activeAbilities.TryGetValue(entityId, out var ability)
                || !_states.TryGetValue(entityId, out var state))
                return null;
            if (ability is CookedTimelineAbility timeline)
                return timeline.GetMoveView(in state);
            var slot = ability.Move;
            if (slot == null) return null;
            var pose = ActiveMoveView.ResolvePose(slot, ability.AnimIndex, state.AttackElapsedTicks);
            int stage = Math.Min(state.ComboStage, slot.Timeline.Stages.Count - 1);
            int elapsed = state.AttackElapsedTicks;
            for (int i = 0; i < stage; i++) elapsed -= slot.Timeline.Stages[i].DurationTicks;
            return new ActiveMoveView(slot, stage, (ushort)Math.Max(0, elapsed), ability.AirborneAtStart,
                ability.AnimIndex, pose);
        }

		/// <summary>
		/// Tick all active abilities. Called after simulation each frame.
		/// Abilities that set AttackSlot=0 (via EndAbility) are auto-deactivated.
		/// Abilities are also interrupted (without calling OnEnd) when the state
		/// is no longer Attacking — e.g. dash cancelling an attack, or idle.
		/// </summary>
		public void TickAbilities(Dictionary<ulong, InputState> inputs)
		{
			// Collect entities whose ability ended this tick (can't modify dict during iteration)
			var ended = new List<ulong>();

			foreach (ulong id in SortedAbilityOwners())
			{
				if (!_activeAbilities.TryGetValue(id, out var ability)) continue;
				if (!_states.TryGetValue(id, out var state)) continue;
				if (!TryDef(id, out var def)) continue;
				bool frozenForTick = ability.FrozenForSimulationTick;
				ability.FrozenForSimulationTick = false;

				if (state.State != ActionState.Attacking && state.State != ActionState.Aiming)
				{
					RetireAbility(id, ref state, AbilityExit.Superseded);
					_states[id] = state;
					if (Simulation.OnDebugLog != null)
						Simulation.OnDebugLog.Invoke(
							$"[AbilityInterrupt] entity={id} slot={ability.Slot} state={state.State} — deactivated");
					continue;
				}

				var input = inputs.TryGetValue(id, out var i) ? i : default;

				// Hitstop pauses the attacker's ability (ADR-0012): timers pause, so recovery
				// extends symmetrically with the victim's lock. Do NOT interrupt — the ability
				// resumes when the freeze expires.
				if (state.HitstopTicks > 0 || frozenForTick)
				{
					_states[id] = state;
					continue;
				}

				ability.TickArmorWindow();
				try
				{
					// A running move keeps the form it started in (ADR-0051).
					ability.Tick(ref state, ref input, ability.CharacterDef ?? def);
				}
				finally
				{
					ability.EndArmorTick();
				}
				state.AnimIndex = ability.AnimIndex;
				// Check if ability ended itself (EndAbility set AttackSlot=0)
				if (state.AttackSlot == 0)
				{
					ended.Add(id);
					_states[id] = state; // Persist EndAbility changes (State=Idle, AttackSlot=0)
				}
				else
				{
					_states[id] = state;
				}
			}

			// Deactivate ended abilities (cooldown still needs applying)
			foreach (var id in ended)
			{
				if (_activeAbilities.TryGetValue(id, out var ability)
				    && _states.TryGetValue(id, out var state))
				{
					RetireAbility(id, ref state, AbilityExit.Completed);
					_states[id] = state; // Persist cooldown + buffered slot clear
				}
				else
					_activeAbilities.Remove(id);
			}
		}

		public static List<SpellResolver.EntityData> BuildEntitiesFromState(
			CharacterState state, CharacterDefinition def, BakedAnimationData baked,
			string targetAnim, int animFrame, ulong entityId = 0)
			=> BuildEntitiesFromState(state, def, baked, targetAnim, (float)animFrame, entityId);

		/// <summary>
		/// Hurtbox volumes for a state posed at a baked frame, which may be fractional
		/// (attacks run on their clip clock); the pose interpolates between frames.
		/// </summary>
		public static List<SpellResolver.EntityData> BuildEntitiesFromState(
			CharacterState state, CharacterDefinition def, BakedAnimationData baked,
			string targetAnim, float animFrame, ulong entityId = 0)
		{
			var list = new List<SpellResolver.EntityData>();
			AppendEntitiesFromState(state, def, baked, targetAnim, animFrame, entityId, list);
			return list;
		}

		private static void AppendEntitiesFromState(CharacterState state, CharacterDefinition def,
			BakedAnimationData baked, string targetAnim, float animFrame, ulong entityId,
			List<SpellResolver.EntityData> list)
		{
			if (baked != null && def.HurtboxBoneDefs != null && def.HurtboxBoneDefs.Count > 0)
			{
				int animIdx = baked.FindAnimIndex(targetAnim);
				if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
				if (animIdx >= 0)
				{
					int fc = baked.Animations[animIdx].FrameCount;
					if (animFrame > fc - 1) animFrame = fc - 1;
					if (animFrame < 0f) animFrame = 0f;
					float yaw = state.FacingYaw;
					float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
                    for (int i = 0; i < def.HurtboxBoneDefs.Count; i++)
                    {
                        var hbd = def.HurtboxBoneDefs[i];
                        Resolve(hbd.BoneName, hbd.OffX, hbd.OffY, hbd.OffZ, out float wx, out float wy, out float wz);
                        float ex = wx, ey = wy, ez = wz;
                        if (hbd.EndBoneName != null)
                            Resolve(hbd.EndBoneName, hbd.EndOffX, hbd.EndOffY, hbd.EndOffZ, out ex, out ey, out ez);
                        list.Add(new SpellResolver.EntityData
                        {
                            Id = entityId, PosX = wx, PosY = wy, PosZ = wz,
                            Radius = hbd.Radius, Shape = hbd.EndBoneName != null ? HitboxShape.Capsule : HitboxShape.Sphere,
                            EndX = ex, EndY = ey, EndZ = ez,
                            InvincibilityTicks = state.InvincibilityTicks,
                            Active = true,
                        });
                    }
                    void Resolve(string name, float ox, float oy, float oz, out float x, out float y, out float z)
                    {
                        int index = baked.FindBoneIndex(name);
                        // Fitted radii follow the drawn body; attack bones retain HurtboxBoneScale.
                        if (index < 0 || !HitboxGeometry.BakedPointToWorld(in state, def, baked, animIdx, animFrame, index, 1f,
                            out x, out y, out z))
                            throw new InvalidOperationException($"Hurtbox bone '{name}' is missing from the admitted pose.");
                        x += ox * cos + oz * sin;
                        y += oy;
                        z += -ox * sin + oz * cos;
                        HitboxGeometry.ApplyAttackPosePitch(in state, def, ref x, ref y, ref z);
                    }
				}
			}
			else if (def.HurtboxCapsules != null)
			{
				float cos = MathF.Cos(state.FacingYaw);
				float sin = MathF.Sin(state.FacingYaw);
				foreach (var cap in def.HurtboxCapsules)
				{
					float sx = state.PX + (cap.Sx * cos) + (cap.Sz * sin);
					float sy = state.PY + cap.Sy;
					float sz = state.PZ + ((-cap.Sx * sin) + (cap.Sz * cos));
					float ex = state.PX + (cap.Ex * cos) + (cap.Ez * sin);
					float ey = state.PY + cap.Ey;
					float ez = state.PZ + ((-cap.Ex * sin) + (cap.Ez * cos));
					HitboxGeometry.ApplyAttackPosePitch(in state, def, ref sx, ref sy, ref sz);
					HitboxGeometry.ApplyAttackPosePitch(in state, def, ref ex, ref ey, ref ez);
					list.Add(new SpellResolver.EntityData
					{
						Id = entityId, PosX = sx, PosY = sy, PosZ = sz, Radius = cap.Radius,
						Shape = (sx != ex || sy != ey || sz != ez) ? HitboxShape.Capsule : HitboxShape.Sphere,
						EndX = ex, EndY = ey, EndZ = ez,
						InvincibilityTicks = state.InvincibilityTicks,
						Active = true,
					});
				}
			}
		}

        /// <summary>
        /// Resolve the animation name and baked frame for hurtbox bone lookup. Attacks use
        /// their clip clock, the stage's elapsed ticks mapped onto the clip exactly like
        /// hitboxes (<see cref="HitboxGeometry.AttackFrame"/>), so a fighter's body and
        /// weapon share one pose; other states step a per-entity frame counter.
        /// Returns false when there's no valid baked data for this entity or animation index is invalid.
        /// Side effects: advances _animFrames and _prevAnimIndex for the entity.
        /// </summary>
        private bool ResolveBoneAnimFrame(ulong id, CharacterState state, CharacterDefinition def,
            out BakedAnimationData baked, out string targetAnim, out float bakedFrame, bool advanceFrame = true)
        {
            baked = null!;
            targetAnim = null!;
            bakedFrame = 0f;

            if (!_bakedData.TryGetValue(id, out baked!) || def.HurtboxBoneDefs == null || def.HurtboxBoneDefs.Count == 0)
                return false;
            // Low posture tracks are static frame-zero poses. They are selected only
            // when the state is grounded and the baked payload contains the exact
            // semantic binding; absent legacy content falls through to upright
            // selection without fabricating low hurtboxes.
            if (state.State == ActionState.Flying && !string.IsNullOrEmpty(def.FlyingAnim)
                && baked.FrameCountFor(def.FlyingAnim) > 0)
            {
                targetAnim = def.FlyingAnim;
                // Flight's small cosmetic bob uses one stable, rollback-independent body sample.
                bakedFrame = 0f;
                return true;
            }
            if (state.State == ActionState.Shell && !string.IsNullOrEmpty(def.ShellAnim)
                && baked.FindAnimIndex(def.ShellAnim) >= 0)
            {
                targetAnim = def.ShellAnim;
                bakedFrame = 0;
                return true;
            }
            if (state.IsGrounded && (state.State is ActionState.Crouching or ActionState.Sliding))
            {
                string lowAnim = state.State == ActionState.Crouching ? def.CrouchAnim : def.SlideAnim;
                int lowIdx = string.IsNullOrEmpty(lowAnim) ? -1 : baked.FindAnimIndex(lowAnim);
                if (lowIdx >= 0)
                {
                    targetAnim = lowAnim;
                    bakedFrame = 0;
                    return true;
                }
            }


            if (state.State is ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery
                or ActionState.HomingDashFloat or ActionState.HomingDash)
                targetAnim = string.IsNullOrEmpty(def.AirDodgeAnim) ? def.DashAnim : def.AirDodgeAnim;
            else if ((state.State is ActionState.Attacking or ActionState.Aiming) && state.AttackSlot > 0)
            {
                if (GetActiveMove(id) is { } move)
                    targetAnim = move.Pose.AnimationId;
            }
            // A captive fighter wears the small-hit pose, as its client presentation does.
            else if (ActionStates.Captive(state.State)) targetAnim = def.HitSmallAnim;
            else if (state.State == ActionState.Hitstun) targetAnim = state.HitstunLevel switch
            {
                1 => def.HitMediumAnim,
                2 => def.HitHardAnim,
                _ => def.HitSmallAnim,
            };
            else if (!state.IsGrounded) targetAnim = state.VY > 0 ? def.JumpAnim : def.FallAnim;
            else if ((state.VX * state.VX) + (state.VZ * state.VZ) > 1f) targetAnim = def.RunAnim;
            else targetAnim = def.IdleAnim;

            int animIdx = baked.FindAnimIndex(targetAnim);
            if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
            if (animIdx < 0) return false;

            int fc = baked.Animations[animIdx].FrameCount;
            int prevAnim = _prevAnimIndex.TryGetValue(id, out var p) ? p : -1;
            int frame = _animFrames.TryGetValue(id, out var f) ? f : 0;
            if (prevAnim != animIdx) frame = 0;
            if (advanceFrame)
            {
                _prevAnimIndex[id] = animIdx;
                _animFrames[id] = (frame + 1) % fc;
            }

            bakedFrame = frame;
            if ((state.State is ActionState.Attacking or ActionState.Aiming) && state.AttackSlot > 0
                && GetActiveMove(id) is { } active)
                bakedFrame = active.Pose.FrameAt(fc);

            return true;
        }

        internal bool LungeReachesOpponent(in CharacterState attacker, ReadOnlySpan<Hitbox> reach, float sin, float cos)
        {
            foreach (var pair in _states)
            {
                var target = pair.Value;
                if (pair.Key == attacker.EntityId || !CanAttackTarget(attacker.EntityId, pair.Key) || IsEliminated(target)
                    || (target.PX - attacker.PX) * sin + (target.PZ - attacker.PZ) * cos <= 0f)
                    continue;
                var def = Def(pair.Key);
                _lungeHurtboxes.Clear();
                if (ResolveBoneAnimFrame(pair.Key, target, def, out var baked, out var anim, out var frame, false))
                    AppendEntitiesFromState(target, def, baked, anim, frame, pair.Key, _lungeHurtboxes);
                else
                    AppendEntitiesFromState(target, def, null!, "idle", 0, pair.Key, _lungeHurtboxes);
                for (int pose = 0; pose < reach.Length; pose++)
                {
                    var hitbox = reach[pose];
                    hitbox.X += attacker.PX; hitbox.Y += attacker.PY; hitbox.Z += attacker.PZ;
                    hitbox.EndX += attacker.PX; hitbox.EndY += attacker.PY; hitbox.EndZ += attacker.PZ;
                    for (int i = 0; i < _lungeHurtboxes.Count; i++)
                        if (SpellResolver.CapsuleCollision(hitbox, _lungeHurtboxes[i],
                            out _, out _, out _, out _, out _, out _, out _))
                            return true;
                }
            }
            return false;
        }

		/// <summary>
		/// Landing lag (issue #125 / ADR-0021 §3) + aerial landing termination (drift fix):
		/// when an AIR-STARTED ability (<see cref="ServerAbility.AirborneAtStart"/>) is still
		/// active when the character lands, the aerial ENDS on the landing frame and — unless
		/// the landing fell in an auto-cancel window — a no-input/no-movement lock applies for
		/// the stage's <c>LandingLagTicks</c>. An airborne aim hold is the exception: it remains
		/// active across landing so the player can release and throw from the ground.
		/// Otherwise a grounded aerial keeps the character in Attacking on the floor, which
		/// skips ProcessNormalMovement's friction and lets a lunge move (Cyclone) drive the
		/// character across the stage with only dash to stop it.
		///
		/// Detection: airborne at tick start + grounded after SimulateTick = a landing.
		/// The <c>VY &lt;= 0</c> guard excludes upward transitions. Only genuinely
		/// air-started moves read their airborne variant's landing lag. A ground move
		/// that is launched and lands mid-move (e.g. a mutual LMB trade) keeps its GROUND
		/// spec — its landing carries no lag, because the ground spec declares none
		/// (ADR-0021 §3: landing lag belongs to aerials, not to ground normals).
		///
		/// Landing frame is pre-lock for ordinary actions by design (ADR-0021 §3): the
		/// input gates run inside SimulateTick BEFORE this applies the lock, so a press on
		/// the landing frame itself is processed pre-lock. An air-jump can cancel on that
		/// frame; dash and abilities stay blocked by their own gates, so there is no free
		/// cancel. Every later locked tick is fully gated.
		/// </summary>
		private void ApplyLandingLag(ref CharacterState state, CharacterDefinition def, bool wasGrounded, ServerAbility? activeAbility)
		{
			if (wasGrounded || !state.IsGrounded) return; // no landing this tick
			if (state.VY > 0f) return;                    // upward transition, not a landing
			if (state.State != ActionState.Attacking && state.State != ActionState.Aiming) return;
			if (state.AttackSlot == 0 || state.LandingLagTicks > 0) return;
			// Only AIR-started moves terminate on landing (drift fix). Specials continue their
			// aerial timeline on the ground; a ground move keeps its own ground behavior.
			if (activeAbility == null || !activeAbility.AirborneAtStart) return;
			if (state.AttackSlot is AbilitySlots.A or AbilitySlots.E or AbilitySlots.R or AbilitySlots.F)
				return;
			// Some capabilities deliberately use landing as their action trigger. They must
			// survive this frame so their next tick can transition into the landing action.
			if (activeAbility is CookedTimelineAbility landingContinuation
			    && landingContinuation.ContinuesThroughLanding) return;
			// Aim holds own release timing and remain active across an air-to-ground transition.
			if (state.State == ActionState.Aiming &&
			    (activeAbility is IAimHoldCapability ||
			     activeAbility is CookedTimelineAbility timelineAbility && timelineAbility.IsHoldingAim)) return;

			var move = GetActiveMove(state.EntityId);
            var cooked = move?.Slot;
			ushort landingLagTicks;
			ushort autoCancelBeforeTicks;
			ushort autoCancelAfterTicks;
			int elapsed;
			if (cooked == null) return;
            int stageIndex = Math.Min(state.ComboStage, cooked.Timeline.Stages.Count - 1);
            var stage = cooked.Timeline.Stages[stageIndex];
			landingLagTicks = stage.LandingLagTicks;
			autoCancelBeforeTicks = stage.AutoCancelBeforeTicks;
			autoCancelAfterTicks = stage.AutoCancelAfterTicks;
            elapsed = state.AttackElapsedTicks;
            for (int i = 0; i < stageIndex; i++) elapsed -= cooked.Timeline.Stages[i].DurationTicks;
			bool autoCancel = (autoCancelBeforeTicks > 0 && elapsed <= autoCancelBeforeTicks)
				|| (autoCancelAfterTicks > 0 && elapsed >= autoCancelAfterTicks);

			// The aerial always ENDS on landing. Auto-cancel: end with no lock at all (Melee's
			// AC: no landing commitment). Otherwise the landing lag (possibly 0) is the ONLY
			// cost — no riding out the stage's remaining recovery + IASA.
            RetireAbility(state.EntityId, ref state, AbilityExit.Cancelled);
			if (autoCancel)
				return;
			state.LandingLagTicks = landingLagTicks;
        }


		private bool CanTakeOrdinaryAbilityAction(in CharacterState state, CharacterDefinition def)
		{
			if (state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BlockStunTicks > 0
				|| state.LandingLagTicks > 0)
				return false;
			bool iasaUnlocked = Simulation.IsIasaUnlocked(in state, GetActiveMove(state.EntityId));
			if (!ActionStates.AdmitsActions(state.State) && state.State != ActionState.HomingDashArrive && !iasaUnlocked)
				return false;
			if (state.AnimLockTicks > 0 && !iasaUnlocked)
				return false;

			bool airborne = !state.IsGrounded;
			for (byte slot = 1; slot <= AbilitySlots.Count; slot++)
			{
				var cooked = def.GetCookedSlotAbility(slot, airborne);
				if (cooked == null)
					continue;
				if (state.GetCooldown(slot) != 0)
					continue;
				int maxCharges = cooked.ChargePool?.MaxCharges ?? 0;
				if (maxCharges > 0 && state.ChargeStockSpent >= maxCharges)
					continue;
				if (_spellResolver.HasReturningProjectile(state.EntityId, slot))
					continue;
				return true;
			}
			return false;
		}

        private static bool IsGroundedNormal(byte slot)
            => slot == AbilitySlots.Slot1
                || slot == AbilitySlots.Slot3
                || slot == AbilitySlots.Slot4;

        /// <summary>
        /// Cancel a ready grounded normal on directional movement, at the same pre-tick
        /// boundary as an explicit ability interrupt. This path deliberately does not
        /// participate in explicit-attack cancellation: a rejected attack input must not
        /// silently turn into a movement cancel.
        /// </summary>
        private bool TryCancelGroundedNormalOnMovement(
            ulong id, ref CharacterState state, CharacterDefinition def, in InputState input)
        {
            if (!state.IsGrounded || state.State != ActionState.Attacking
                || !IsGroundedNormal(state.AttackSlot)
                || ((input.MoveX * input.MoveX) + (input.MoveY * input.MoveY) <= 1e-4f))
                return false;

            if (state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BlockStunTicks > 0
                || state.LandingLagTicks > 0
                || !Simulation.IsIasaUnlocked(in state, GetActiveMove(state.EntityId))
                || !_activeAbilities.ContainsKey(id))
                return false;
            RetireAbility(id, ref state, AbilityExit.Cancelled);
            return true;
        }

		/// <summary>
		/// Homing dash admission (ADR-0042): every state except being hit and attacks, out of
		/// shield included. An attack's IASA window counts as free, exactly as for other actions.
		/// </summary>
		private bool TryStartHomingDash(ulong id, ref CharacterState state)
		{
			if (state.HomingDashCooldownTicks > 0 && id != NoCooldownsEntityId
				|| !state.IsGrounded && state.HomingDashAirUsed
				|| state.InteractionId != 0
				|| state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BlockStunTicks > 0
				|| state.LandingLagTicks > 0
				|| Simulation.HasKnockback(state) || HasQueuedShieldLaunch(in state))
				return false;
			bool iasaUnlocked = Simulation.IsIasaUnlocked(in state, GetActiveMove(id));
			if ((state.AnimLockTicks > 0 || _activeAbilities.ContainsKey(id)) && !iasaUnlocked)
				return false;
			if (state.State is ActionState.Hitstun or ActionState.Aiming or ActionState.GrabAttempt
				or ActionState.Grabbing or ActionState.Grabbed or ActionState.Throwing
				or ActionState.HomingDashFloat or ActionState.HomingDash or ActionState.HomingDashMiss
				|| ActionStates.Captive(state.State))
				return false;

			if (_activeAbilities.ContainsKey(id))
				RetireAbility(id, ref state, AbilityExit.Cancelled);
			ulong targetId = state.TargetEntityId;
			float dx = 0f, dy = 0f, dz = 0f;
			if (targetId != 0
				&& IsEligibleEnemy(id, targetId, state.PX, state.PZ, LockRangeMeters, out _)
				&& TryGetCombatTargetPosition(targetId, out var target))
			{
				dx = target.X - state.PX; dy = HomingDashRise(targetId, target.Y, in state, Def(id), airborneDash: !state.IsGrounded); dz = target.Z - state.PZ;
			}
			else
				targetId = 0;
			Simulation.StartHomingDash(ref state, targetId, dx, dy, dz);
			return true;
		}

		/// <summary>Steer the homing dash toward its target before this tick's movement.</summary>
		private void StepHomingDash(ref CharacterState state, CharacterDefinition def)
		{
			ulong targetId = state.HomingDashTargetId;
			if (targetId == 0
				|| !IsEligibleEnemy(state.EntityId, targetId, state.PX, state.PZ, float.MaxValue, out _)
				|| !TryGetCombatTargetPosition(targetId, out var target))
			{
				state.HomingDashTargetId = 0;
				Simulation.StepHomingDash(ref state, false, 0f, 0f, 0f, 0f, def.Movement.FloatWindowTicks);
				return;
			}
			float targetRadius = TryDef(targetId, out var targetDef) ? targetDef.CapsuleRadius : 0f;
			Simulation.StepHomingDash(ref state, true,
				target.X - state.PX, HomingDashRise(targetId, target.Y, in state, def, state.HomingDashAirUsed), target.Z - state.PZ,
				def.CapsuleRadius + targetRadius + HomingDashConfig.ArrivalGap, def.Movement.FloatWindowTicks);
		}

		/// <summary>
		/// Height the dash climbs toward a target. A grounded dash never climbs, so its follow-up is
		/// a ground normal; it can still drop off a ledge toward a lower target. An airborne dash
		/// (<see cref="CharacterState.HomingDashAirUsed"/> until landing) aims its feet
		/// <see cref="HomingDashConfig.AirAimLift"/> above a fighter's feet so it arrives in the air
		/// for an aerial, and centre to centre at other targets.
		/// </summary>
		private float HomingDashRise(ulong targetId, float targetY, in CharacterState state, CharacterDefinition def,
			bool airborneDash)
		{
			if (!TryDef(targetId, out var targetDef))
				return airborneDash ? targetY - state.PY : MathF.Min(0f, targetY - state.PY);
			float feet = targetY - targetDef.CapsuleHeight * 0.5f - (state.PY - def.CapsuleHeight * 0.5f);
			return airborneDash ? feet + targetDef.CapsuleHeight * HomingDashConfig.AirAimLift : MathF.Min(0f, feet);
		}

		private static bool HasQueuedShieldLaunch(in CharacterState state)
			=> state.QueuedKBResolvedForce || state.QueuedKBZero
				|| state.QueuedKBBase != 0f || state.QueuedKBGrowth != 0f
				|| state.QueuedKBForce != 0f || state.QueuedKBDamage != 0f
				|| state.QueuedKBStun > 0
				|| state.QueuedKBDirX != 0f || state.QueuedKBDirZ != 0f
				|| state.QueuedKBAngle != 0 || state.QueuedCrouchBrace;
		private bool CanAcceptGroundShield(
            in CharacterState state, CharacterDefinition def, in InputState input)
		{
			if (!input.ShieldHeld || input.GrabPressed || input.Jump || !state.IsGrounded
				|| state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BlockStunTicks > 0
				|| state.LandingLagTicks > 0 || state.InteractionId != 0
				|| Simulation.HasKnockback(state) || HasQueuedShieldLaunch(in state)
                || state.AnimLockTicks > 0 && !Simulation.IsIasaUnlocked(in state, GetActiveMove(state.EntityId)))
				return false;

			return ActionStates.AdmitsActions(state.State) || state.State is ActionState.Attacking
				or ActionState.Shielding or ActionState.HomingDashArrive;
		}

		private bool CanAcceptGroundShieldAfterTimers(
			in CharacterState state, CharacterDefinition def, in InputState input)
		{
			if (CanAcceptGroundShield(in state, def, in input)) return true;
			var nextState = state;
			if (nextState.AnimLockTicks > 0) nextState.AnimLockTicks--;
			if (nextState.AttackElapsedTicks < ushort.MaxValue) nextState.AttackElapsedTicks++;
            return CanAcceptGroundShield(in nextState, def, in input);
		}

		private bool HandleShieldPreTickAdmission(
			ulong id, ref CharacterState state, ref InputState input,
			Dictionary<ulong, InputState> inputs, CharacterDefinition def)
		{
			if (input.ShieldHeld && (input.GrabPressed || input.Jump) && input.ActiveSlot != 0)
			{
				input.ActiveSlot = 0;
				inputs[id] = input;
			}
			bool shieldState = state.State is ActionState.Shielding or ActionState.ShieldDrop;
            bool shieldRequest = CanAcceptGroundShieldAfterTimers(in state, def, in input);
			if (!shieldState && !shieldRequest) return false;

			if (shieldRequest && _activeAbilities.TryGetValue(id, out var active)
				&& active.OwnsVerticalMotion)
				return false;

			if (_activeAbilities.ContainsKey(id))
				RetireAbility(id, ref state, AbilityExit.Superseded);

			if (input.ActiveSlot != 0)
			{
				input.ActiveSlot = 0;
				inputs[id] = input;
			}
			_states[id] = state;
			return true;
		}

		private void PreTickAbilities(Dictionary<ulong, InputState> inputs)
		{
			// ── Pre-sim: Activate server abilities from inputs ──
			// Snapshot keys to avoid collection-modified during ActivateAbility writes
			ulong[] entityIds = new ulong[_states.Count];
			_states.Keys.CopyTo(entityIds, 0);
			foreach (var id in entityIds)
			{
				if (!_states.TryGetValue(id, out var state)) continue;
				if (IsEliminated(state)) continue;
				var input = inputs.TryGetValue(id, out var i) ? i : default;
                var def = Def(id);
                if (state.Flight.Active && input.ActiveSlot == AbilitySlots.E && state.HitstopTicks == 0)
                {
                    Flying.End(ref state, def, id != NoCooldownsEntityId, beforeTimers: true);
                    input.ActiveSlot = 0;
                    inputs[id] = input;
                    _states[id] = state;
                    continue;
                }
				if (input.DashPressed && TryStartHomingDash(id, ref state))
				{
					input.ActiveSlot = 0;
					input.Jump = false;
					input.ShieldPressed = false;
					input.GrabPressed = false;
					inputs[id] = input;
					_states[id] = state;
					_lastTickAcceptedActions.Add(id);
					continue;
				}
                bool heldForBeeline = PrepareBeelinePress(ref state, ref input);
                inputs[id] = input;
                _states[id] = state;
                if (heldForBeeline) continue;
				if (HandleShieldPreTickAdmission(id, ref state, ref input, inputs, def))
					continue;
				if (CanTakeOrdinaryAbilityAction(state, def))
					_lastTickOrdinaryActionOpportunities.Add(id);
                bool connectedSwap = CanSwapCancel(id, in state, input.ActiveSlot);
                if (connectedSwap && state.HitstopTicks > 0 && state.HitstunTicks == 0
                    && state.BlockStunTicks == 0 && state.LandingLagTicks == 0)
                {
                    state.BufferedSlot = AbilitySlots.F;
                    _states[id] = state;
                }
				bool bufferedPress = input.ActiveSlot == 0 && state.BufferedSlot > 0
					&& (ActionStates.AdmitsActions(state.State) || state.State == ActionState.HomingDashArrive
                        || CanSwapCancel(id, in state, state.BufferedSlot))
					&& !input.Jump && !input.ShieldHeld && !input.GrabPressed;
				if (bufferedPress)
					input.ActiveSlot = state.BufferedSlot;
                connectedSwap = CanSwapCancel(id, in state, input.ActiveSlot);
				if (input.ActiveSlot == 0)
				{
					if (TryCancelGroundedNormalOnMovement(id, ref state, def, input))
						_states[id] = state;
					continue;
				}

				// IASA early-out (issue #124): an attack stage that has passed its IasaTicks
				// releases the anim lock for ability inputs — the press interrupts the recovery.
				// IasaTicks = 0 (default) keeps the full ADR-0014 lock. Only the AnimLockTicks
				// term relaxes: hitstun, hitstop and landing lag always block — attacker
				// hitstop (FreezesOwner) keeps State == Attacking, so relaxing those too
				// would let a press cancel the attack mid-freeze (ADR-0012), and landing lag
				// is a hard no-input lock that IASA must not bypass.
				bool iasaUnlocked = Simulation.IsIasaUnlocked(in state, GetActiveMove(state.EntityId));
				if (state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BlockStunTicks > 0
					|| state.LandingLagTicks > 0
					|| (state.AnimLockTicks > 0 && !iasaUnlocked && !connectedSwap)) continue; // ADR-0014
				if (!ActionStates.AdmitsActions(state.State) && state.State != ActionState.Attacking
					&& state.State != ActionState.HomingDashArrive) continue;
				if (bufferedPress)
				{
					state.BufferedSlot = 0;
					_states[id] = state;
				}


				bool airborne = state.Flight.Active || !state.IsGrounded;
				var cookedSlot = def.GetCookedSlotAbility(input.ActiveSlot, airborne);

				if (cookedSlot == null)
				{
					var rejected = input;
					rejected.ActiveSlot = 0;
					inputs[id] = rejected;
					continue;
				}

				_activeAbilities.TryGetValue(id, out var currentAbility);
				ushort cooldown = state.GetCooldown(input.ActiveSlot);
				// A self-recast must respect the cooldown pending on the outgoing move.
				if (cooldown == 0 && currentAbility != null
					&& currentAbility.Slot + 1 == input.ActiveSlot)
					cooldown = currentAbility.Cooldown;
				if (cooldown > 0 && id != NoCooldownsEntityId)
				{
					if (Simulation.OnDebugLog != null)
						Simulation.OnDebugLog.Invoke(
							$"[Cooldown] BLOCKED slot={input.ActiveSlot} cooldown={cooldown} entity={id}");
					continue;
				}

                int maxCharges = cookedSlot.ChargePool?.MaxCharges ?? 0;
				if (maxCharges > 0 && state.ChargeStockSpent >= maxCharges
				    || _spellResolver.HasReturningProjectile(id, input.ActiveSlot))
				{
					// A spent charge pool, or this slot's returning projectile still in flight (one lid).
					var blockedInput = input;
					blockedInput.ActiveSlot = 0;
					inputs[id] = blockedInput;
					continue;
				}
                if (input.ActiveSlot == AbilitySlots.E && Flying.Parameters(def) != null && state.Flight.FuelTicksLeft == 0)
                    continue;
                var ability = new CookedTimelineAbility(cookedSlot);

				// ── IASA interrupt ──
				// An active ability whose stage has passed its IasaTicks is dropped without
				// OnEnd (same semantics as hitstun/dash interrupts) so the new ability takes
				// over. Placed AFTER the activation gates (cooldown and cooked charge pool)
				// so a blocked press never cancels the current attack. The move was
				// used — its cooldown still applies, mirroring the dash-cancel path in
				// TickAbilities. Attack state is cleared so the new ability's OnStart begins
				// clean and no stale buffered press double-fires when the new attack's lock
				// expires.
				if (state.Flight.Active && !Flying.KeepsFlight(input.ActiveSlot))
                {
                    Flying.End(ref state, def, id != NoCooldownsEntityId, beforeTimers: true);
                    state.IsGrounded = false;
                    _states[id] = state;
                }
                bool fromShell = Shell.IsTucked(state.State, GetActiveMove(id)?.Slot);
				if (currentAbility != null)
				{
					if (!iasaUnlocked && !connectedSwap) continue;
					RetireAbility(id, ref state, AbilityExit.Cancelled);
					_states[id] = state;
				}

				ability.Cooldown = cookedSlot.CooldownTicks;

				ActivateAbility(id, ability, (byte)(input.ActiveSlot - 1), def, input.AimYaw, input, fromShell);

                // Spend charges from the cooked pool; they return only through regen.
                if (maxCharges > 0 && _states.TryGetValue(id, out var afterState))
                {
                    afterState.SpendCharge(cookedSlot.ChargePool!.RegenTicks);
                    _states[id] = afterState;
                }

				// Consume input so SimulateTick doesn't also try to start an attack
				var consumedInput = input;
				consumedInput.ActiveSlot = 0;
				inputs[id] = consumedInput;
			}
		}

        /// <summary>The active move has connected and can be cancelled by F; admission still enforces cooldown and locks.</summary>
        public bool CanSwapCancel(ulong id)
            => _states.TryGetValue(id, out var state) && CanSwapCancel(id, in state, AbilitySlots.F);

        private bool CanSwapCancel(ulong id, in CharacterState state, byte slot)
            => slot == AbilitySlots.F && state.State == ActionState.Attacking && Def(id).AlternateForm != null
                && _activeAbilities.TryGetValue(id, out var ability)
                && ability is CookedTimelineAbility { Connected: true };

		private bool HasGrabAttempts()
		{
			foreach (var state in _states.Values)
				if (state.State == ActionState.GrabAttempt
					&& state.InteractionPhase == (byte)DefenseInteractionPhase.Attempt)
					return true;
			return false;
		}

		private bool HasCoupledInteraction()
		{
			foreach (var state in _states.Values)
				if (state.InteractionId != 0)
					return true;
			return false;
		}

		/// <summary>Copies each fighter's returning projectile slot into its state, for the CPU and the HUD.</summary>
		private void MirrorReturningProjectileSlots()
		{
			int count = CopyInteractionIds();
			for (int i = 0; i < count; i++)
			{
				ulong id = _interactionScratch[i];
				var state = _states[id];
				byte slot = _spellResolver.ReturningProjectileSlot(id);
				if (state.ReturningProjectileSlot == slot) continue;
				state.ReturningProjectileSlot = slot;
				_states[id] = state;
			}
		}

		private int CopyInteractionIds()
		{
			int count = _states.Count;
			if (_interactionScratch.Length < count)
				Array.Resize(ref _interactionScratch, count);
			_states.Keys.CopyTo(_interactionScratch, 0);
			Array.Sort(_interactionScratch, 0, count);
			return count;
		}

		private bool IsActiveGrabAttempt(in CharacterState state)
		{
			if (state.State != ActionState.GrabAttempt
				|| state.InteractionPhase != (byte)DefenseInteractionPhase.Attempt)
				return false;
			ushort activeEnd = (ushort)(DefenseConfig.GrabWhiffRecoveryTicks
				+ DefenseConfig.GrabActiveTicks);
			return state.StateTicks > DefenseConfig.GrabWhiffRecoveryTicks
				&& state.StateTicks <= activeEnd;
		}

		private bool CanPlaceVictim(in CharacterState grabber, in CharacterState victim,
			CharacterDefinition grabberDef, CharacterDefinition victimDef,
			out float x, out float y, out float z)
		{
			x = y = z = 0f;
			var geometry = grabberDef.CaptureGeometry;
			if (geometry == null) return false;
			float yaw = grabber.CapturedYaw * (MathF.PI / 18000f);
			float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
			float dx = geometry.VictimAnchor.X;
			float dy = geometry.VictimAnchor.Y - geometry.AttackerAnchor.Y;
			float dz = geometry.VictimAnchor.Z;
			x = grabber.PX + dx * cos + dz * sin;
			y = grabber.PY + dy;
			z = grabber.PZ - dx * sin + dz * cos;
			float shiftX = x - victim.PX, shiftY = y - victim.PY, shiftZ = z - victim.PZ;
			if (shiftX * shiftX + shiftY * shiftY + shiftZ * shiftZ
				> geometry.Reach * geometry.Reach) return false;
			if (x <= _blastLines.KillMinX || x >= _blastLines.KillMaxX
				|| y <= _blastLines.KillHeight || y >= _blastLines.KillTop
				|| z <= _blastLines.KillMinZ || z >= _blastLines.KillMaxZ)
				return false;
			int required = _arena.CollisionTriangles?.Length ?? 0;
			if (_grabTriangles.Length < required) Array.Resize(ref _grabTriangles, required);
			int count = ArenaCollision.GetCandidateTrianglesForSweep(
				victim.PX, victim.PY, victim.PZ, x, y, z,
				victimDef.CapsuleRadius, victimDef.CapsuleHeight, in _arena, _grabTriangles);
			if (ArenaCollision.SweepCapsule(victim.PX, victim.PY, victim.PZ,
				x, y, z, victimDef.CapsuleRadius, victimDef.CapsuleHeight,
				in _arena, _grabTriangles, count, out var contact)
				&& contact.Time < 0.999f && MathF.Abs(contact.NormalY) < 0.6f)
				return false;
			return true;
		}

		private static bool ClipGrabAxis(float start, float end, float minimum, float maximum,
			ref float enter, ref float exit)
		{
			float delta = end - start;
			if (MathF.Abs(delta) < 0.000001f) return start >= minimum && start <= maximum;
			float first = (minimum - start) / delta, last = (maximum - start) / delta;
			if (first > last) (first, last) = (last, first);
			enter = MathF.Max(enter, first);
			exit = MathF.Min(exit, last);
			return enter <= exit;
		}

		private bool OverlapsGrabVolume(ulong targetId, in CharacterState grabber,
			CookedCaptureGeometry geometry, float sin, float cos)
		{
			float bottom = grabber.PY + geometry.OffsetY - geometry.Height * 0.5f;
			foreach (var shape in _lastEntityList)
			{
				if (shape.Id != targetId || !shape.Active) continue;
				float ax = shape.PosX - grabber.PX, az = shape.PosZ - grabber.PZ;
				float bx = shape.EndX - grabber.PX, bz = shape.EndZ - grabber.PZ;
				float enter = 0f, exit = 1f, radius = shape.Radius;
				if (ClipGrabAxis(ax * sin + az * cos, bx * sin + bz * cos,
						-radius, geometry.Reach + radius, ref enter, ref exit)
					&& ClipGrabAxis(ax * cos - az * sin, bx * cos - bz * sin,
						-geometry.Width * 0.5f - radius, geometry.Width * 0.5f + radius,
						ref enter, ref exit)
					&& ClipGrabAxis(shape.PosY, shape.EndY,
						bottom - radius, bottom + geometry.Height + radius,
						ref enter, ref exit))
					return true;
			}
			return false;
		}

		private ulong FindGrabTarget(ulong grabberId, in CharacterState grabber)
		{
			if (!TryDef(grabberId, out var grabberDef)
				|| grabberDef.CaptureGeometry == null) return 0;
			var geometry = grabberDef.CaptureGeometry;
			float yaw = grabber.CapturedYaw * (MathF.PI / 18000f);
			float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
			ulong closestId = 0;
			float closestForward = float.PositiveInfinity;
			float closestSide = float.PositiveInfinity;
			foreach (var (targetId, target) in _states)
			{
				if (targetId == grabberId || !CanAttackTarget(grabberId, targetId) || target.InteractionId != 0
					|| target.State == ActionState.CutHeld
					|| !TryDef(targetId, out var targetDef)
					|| IsEliminated(target) || target.InvincibilityTicks > 0)
					continue;
				float dx = target.PX - grabber.PX, dz = target.PZ - grabber.PZ;
				float forward = dx * sin + dz * cos;
				float side = MathF.Abs(dx * cos - dz * sin);
				if (!OverlapsGrabVolume(targetId, in grabber, geometry, sin, cos)
					|| !CanPlaceVictim(in grabber, in target, grabberDef, targetDef,
						out _, out _, out _))
					continue;
				if (forward < closestForward || forward == closestForward
					&& (side < closestSide || side == closestSide && targetId < closestId))
				{
					closestId = targetId;
					closestForward = forward;
					closestSide = side;
				}
			}
			return closestId;
		}

		private void ResolveGrabAttempts()
		{
			if (!PredictCoupledInteractions)
				return;
			if (!HasGrabAttempts())
				return;

			int count = CopyInteractionIds();
			_grabCandidates.Clear();
			_clashedThisTick.Clear();
			for (int i = 0; i < count; i++)
			{
				ulong id = _interactionScratch[i];
				if (!_states.TryGetValue(id, out var state) || !IsActiveGrabAttempt(in state)
					|| _damagedThisTick.Contains(id) || state.HitstopTicks > 0
					|| !state.IsGrounded || state.VX * state.VX + state.VZ * state.VZ > 0.0025f)
					continue;
				ulong targetId = FindGrabTarget(id, in state);
				if (targetId == 0) continue;
				var target = _states[targetId];
				float dx = target.PX - state.PX, dz = target.PZ - state.PZ;
				_grabCandidates.Add((id, targetId, dx * dx + dz * dz));
			}
			// Reciprocal contact uses the same snapshot, before any capture mutates it.
			foreach (var candidate in _grabCandidates)
			{
				if (candidate.grabber >= candidate.target
					|| !_states.TryGetValue(candidate.target, out var target)
					|| !IsActiveGrabAttempt(in target)
					|| !_states.TryGetValue(candidate.grabber, out var grabber)
					|| !IsActiveGrabAttempt(in grabber))
					continue;
				foreach (var reciprocal in _grabCandidates)
					if (reciprocal.grabber == candidate.target
						&& reciprocal.target == candidate.grabber)
					{
						_clashedThisTick.Add(candidate.grabber);
						_clashedThisTick.Add(candidate.target);
						ClashGrabAttempts(candidate.grabber, candidate.target, grabber, target);
						break;
					}
			}
			_grabCandidates.Sort(static (a, b) =>
			{
				int result = a.distance.CompareTo(b.distance);
				return result != 0 ? result : a.grabber.CompareTo(b.grabber);
			});
			foreach (var candidate in _grabCandidates)
			{
				if (!_states.TryGetValue(candidate.grabber, out var grabber)
					|| !IsActiveGrabAttempt(in grabber)
					|| !_states.TryGetValue(candidate.target, out var target)
					|| target.InteractionId != 0
					|| _clashedThisTick.Contains(candidate.grabber)
					|| _clashedThisTick.Contains(candidate.target))
					continue;
				CaptureInteraction(candidate.grabber, candidate.target, grabber, target);
			}
		}

		private void ClashGrabAttempts(ulong firstId, ulong secondId,
			CharacterState first, CharacterState second)
		{
			first.StateTicks = DefenseConfig.GrabClashRecoveryTicks;
			first.InteractionId = 0;
			first.InteractionPartnerId = 0;
			first.InteractionPhase = (byte)DefenseInteractionPhase.None;
			first.InteractionTick = 0;
			second.StateTicks = DefenseConfig.GrabClashRecoveryTicks;
			second.InteractionId = 0;
			second.InteractionPartnerId = 0;
			second.InteractionPhase = (byte)DefenseInteractionPhase.None;
			second.InteractionTick = 0;
			_states[firstId] = first;
			_states[secondId] = second;
		}

		private void CaptureInteraction(ulong grabberId, ulong targetId,
			CharacterState grabber, CharacterState target)
		{
			if (!CanPlaceVictim(in grabber, in target, Def(grabberId), Def(targetId),
				out float x, out float y, out float z)) return;
			unchecked
			{
				_nextInteractionId++;
				if (_nextInteractionId == 0)
					_nextInteractionId = 1;
			}

			ulong interactionId = _nextInteractionId;
			RetireAbility(grabberId, ref grabber, AbilityExit.Superseded);
			RetireAbility(targetId, ref target, AbilityExit.Superseded);
			grabber.ClearAttackFields();
			target.ClearAttackFields();
			grabber.State = ActionState.Grabbing;
			grabber.StateTicks = DefenseConfig.GrabHoldTicks;
			grabber.InteractionId = interactionId;
			grabber.InteractionPartnerId = targetId;
			grabber.InteractionPhase = (byte)DefenseInteractionPhase.Captured;
			grabber.InteractionTick = _tick;
			grabber.VX = grabber.VY = grabber.VZ = 0f;
			grabber.FacingYaw = grabber.CapturedYaw * (MathF.PI / 18000f);
			target.State = ActionState.Grabbed;
			target.StateTicks = DefenseConfig.GrabHoldTicks;
			target.InteractionId = interactionId;
			target.InteractionPartnerId = grabberId;
			target.InteractionPhase = (byte)DefenseInteractionPhase.Captured;
			target.InteractionTick = _tick;
			target.CapturedYaw = grabber.CapturedYaw;
			target.FacingYaw = target.AimYaw = grabber.FacingYaw;
			target.PX = x; target.PY = y; target.PZ = z;
			target.VX = target.VY = target.VZ = 0f;
			_states[grabberId] = grabber;
			_states[targetId] = target;
		}

		private void UpdateCoupledInteractions(bool releaseDue, Dictionary<ulong, InputState> inputs)
		{
			int count = CopyInteractionIds();
			for (int i = 0; i < count; i++)
			{
				ulong ownerId = _interactionScratch[i];
				if (!_states.TryGetValue(ownerId, out var owner) || owner.InteractionId == 0)
					continue;
				// Leave death-bound positions intact until CheckBlastDeaths commits
				// the paired interruption and respawn barrier later in this tick.
				if (IsOutsideBlastZone(in owner)
					|| _states.TryGetValue(owner.InteractionPartnerId, out var dyingPartner)
						&& IsOutsideBlastZone(in dyingPartner))
					continue;
				if (owner.InteractionPhase == (byte)DefenseInteractionPhase.CommandHold)
				{
					if (!CommandHolds(ownerId, in owner))
					{
						InterruptInteraction(ref owner);
						_states[ownerId] = owner;
					}
					continue;
				}
				if (owner.InteractionPhase == (byte)DefenseInteractionPhase.Reeling)
				{
					if (!TetherHolds(ownerId, in owner))
					{
						InterruptInteraction(ref owner);
						_states[ownerId] = owner;
					}
					continue;
				}
				if (owner.State == ActionState.Grabbed)
				{
					if (!_states.TryGetValue(owner.InteractionPartnerId, out var grabber)
						|| grabber.State is not (ActionState.Grabbing or ActionState.Throwing)
						|| grabber.InteractionId != owner.InteractionId
						|| grabber.InteractionPartnerId != ownerId
						|| grabber.InteractionPhase != owner.InteractionPhase)
					{
						InterruptInteraction(ref owner);
						_states[ownerId] = owner;
					}
					continue;
				}
				if (owner.State is not (ActionState.Grabbing or ActionState.Throwing))
				{
					InterruptInteraction(ref owner);
					_states[ownerId] = owner;
					continue;
				}

				ulong interactionId = owner.InteractionId;
				ulong partnerId = owner.InteractionPartnerId;
				if ((owner.InteractionPhase != (byte)DefenseInteractionPhase.Captured
						&& !GrabThrow.IsWindup(owner.InteractionPhase))
					|| !_states.TryGetValue(partnerId, out var partner)
					|| partner.InteractionId != interactionId
					|| partner.InteractionPartnerId != ownerId
					|| partner.InteractionPhase != owner.InteractionPhase
					|| partner.State != ActionState.Grabbed
					|| !owner.IsGrounded || IsEliminated(owner) || IsEliminated(partner)
					|| !CanPlaceVictim(in owner, in partner, Def(ownerId), Def(partnerId),
						out float x, out float y, out float z))
				{
					InterruptInteraction(ref owner);
					_states[ownerId] = owner;
					continue;
				}

				// Movement is simulated once between these two interaction passes. Keep
				// the victim attached to the final, collision-resolved grabber position.
				partner.PX = x; partner.PY = y; partner.PZ = z;
				partner.VX = owner.VX; partner.VY = owner.VY; partner.VZ = owner.VZ;
				partner.FacingYaw = partner.AimYaw = owner.FacingYaw;
				partner.CapturedYaw = owner.CapturedYaw;

				uint elapsed = _tick - owner.InteractionTick;
				if (owner.State == ActionState.Grabbing && !releaseDue
					&& (elapsed >= DefenseConfig.GrabHoldTicks
						|| inputs.TryGetValue(ownerId, out var grabInput) && grabInput.ActiveSlot > 0))
				{
                    inputs.TryGetValue(ownerId, out var throwInput);
                    BeginGrabThrow(ref owner, ref partner, in throwInput);
					elapsed = 0;
					_lastTickAcceptedActions.Add(ownerId);
				}

				if (owner.State == ActionState.Throwing
					&& elapsed >= DefenseConfig.ThrowReleaseTicks && releaseDue)
				{
                    ReleaseGrabThrow(ref owner, ref partner);
					_states[ownerId] = owner;
					_states[partnerId] = partner;
					continue;
				}

                MirrorGrabPairClocks(ref owner, ref partner, elapsed);
				_states[ownerId] = owner;
				_states[partnerId] = partner;

			}
		}

		private void InterruptInteraction(ref CharacterState affected)
		{
			ulong interactionId = affected.InteractionId;
			if (interactionId == 0)
				return;

			ulong partnerId = affected.InteractionPartnerId;
			if (_states.TryGetValue(partnerId, out var partner)
				&& partner.InteractionId == interactionId)
			{
				if (ActionStates.InPairedInteraction(partner.State))
				{
					partner.ClearAttackFields();
					partner.State = partner.HitstunTicks > 0
						? ActionState.Hitstun : ActionState.Idle;
					partner.StateTicks = 0;
				}
				MarkInteractionTerminal(ref partner, interactionId);
				_states[partnerId] = partner;
			}
			if (ActionStates.InPairedInteraction(affected.State) && affected.HitstunTicks == 0)
			{
				affected.ClearAttackFields();
				affected.State = ActionState.Idle;
				affected.StateTicks = 0;
			}
			MarkInteractionTerminal(ref affected, interactionId);
		}

		private void MarkInteractionTerminal(ref CharacterState state, ulong interactionId)
		{
			state.InteractionId = 0;
			state.InteractionPartnerId = 0;
			state.LastTerminalInteractionId = interactionId;
			state.InteractionTerminalTick = _tick;
			state.InteractionPhase = (byte)DefenseInteractionPhase.Terminal;
		}

		private void SimulateMovement(Dictionary<ulong, InputState> inputs)
		{
			// ── Step 1: Simulate each entity ──
			// Snapshot keys to avoid collection-modified when writing _states[id] = state
			ulong[] simIds = new ulong[_states.Count];
			_states.Keys.CopyTo(simIds, 0);
			foreach (var id in simIds)
			{
				if (!_states.TryGetValue(id, out var state)) continue;
				// Eliminated (0 stocks / rule) — frozen spectator, no physics (issue #37).
				if (IsEliminated(state)) continue;
				if (!TryDef(id, out var def)) continue; // state exists but no definition — invalid entity, skip (never simulate)
				if (state.InteractionId != 0
					&& (IsOutsideBlastZone(in state)
						|| _states.TryGetValue(state.InteractionPartnerId, out var dyingPartner)
							&& IsOutsideBlastZone(in dyingPartner)))
					continue;
				var input = inputs.TryGetValue(id, out var i2) ? i2 : default;
				var beforeMovement = state;
				bool wasGrounded = state.IsGrounded;
				_activeAbilities.TryGetValue(id, out var activeAbility);
				// Hitstop at the start of the tick freezes the fighter for all of it, even when
				// this step spends the last frozen tick: its ability and melee windows skip the tick.
				bool frozen = state.HitstopTicks > 0;
				if (frozen)
					_hitstopFrozenThisTick.Add(id);
				if (activeAbility != null)
					activeAbility.FrozenForSimulationTick = frozen;
				Flying.Tick(ref state, input, def, id != NoCooldownsEntityId);
                bool verticalMotionOwned = state.Flight.Active || activeAbility?.OwnsVerticalMotion == true;
				if (!frozen && state.State is ActionState.HomingDashFloat or ActionState.HomingDash)
					StepHomingDash(ref state, def);
                Simulation.SimulateTick(ref state, def, input, _arena,
					out bool ordinaryActionOpportunity, out bool movementActionAccepted,
					_downActionTuning, verticalMotionOwned, GetActiveMove(id),
					NoGravityEntityId == id || state.Flight.Active ? 0f : activeAbility?.GravityMultiplier ?? 1f, CombatTuning);

				if (state.State is ActionState.Shielding or ActionState.AirDodgeMovement
                    || state.State == ActionState.GrabAttempt && beforeMovement.State != ActionState.GrabAttempt)
                    RetireAbility(id, ref state, AbilityExit.Superseded);
                if (state.State == ActionState.GrabAttempt && beforeMovement.State != ActionState.GrabAttempt)
                    BeginGrabStartupAim(ref state, in input);

				if (ordinaryActionOpportunity)
					_lastTickOrdinaryActionOpportunities.Add(id);
				if (movementActionAccepted)
					_lastTickAcceptedActions.Add(id);
				if (Simulation.LastDownAdmissionReason is DownActionAdmissionReason downReason)
					_lastTickDownAdmissions[id] = downReason;
				else if ((input.Down || input.DownPressed) && (wasGrounded || state.IsGrounded))
				{
					// The pre-sim ability phase can consume a Down request before the
					// grounded movement helper runs. Preserve the same gate precedence.
					_lastTickDownAdmissions[id] = _lastTickAcceptedActions.Contains(id)
						? DownActionAdmissionReason.ActionAccepted
						: verticalMotionOwned
							? DownActionAdmissionReason.MotionOwned
							: DownActionAdmissionReason.Locked;
				}
				if (!wasGrounded && state.IsGrounded
					&& state.State != ActionState.LedgeHang && state.VY <= 0f)
					_lastTickTouchdowns.Add(id);
				// Landing lag (issue #125 / ADR-0021 §3): land mid-aerial → lock, unless the
				// landing frame falls in an auto-cancel window. Only air-started moves resolve
				// their airborne variant's landing lag (ground moves keep their ground spec).
				if (!state.Flight.Active && !ResolveShellTouchdown(id, ref state, def, wasGrounded, activeAbility))
					ApplyLandingLag(ref state, def, wasGrounded, activeAbility);
                if (!state.Flight.Active && state.IsGrounded) Flying.Refuel(ref state, def);
                _states[id] = state;
            }


            // ── Step 1b: Tick server-side abilities (overrides movement, spawns hitboxes) ──
			TickAbilities(inputs);
            UpdateGrabStartupAim(simIds);
			_abilityTickPhaseComplete = true;

			// ── Step 1c: Landing lag freeze (issue #125 / ADR-0021 §3) ──
			// The lock is "no input, no movement": the aerial has already ENDED on the landing
			// frame (ApplyLandingLag), but the residual lunge/air drift is still live. Zero
			// velocity every lagged tick so the character plants and stays pinned — the lock
			// is the only cost, not an overlay on the move's remaining recovery.
			foreach (var id in simIds)
			{
				if (!_states.TryGetValue(id, out var lagState) || lagState.LandingLagTicks == 0) continue;
				if (lagState.VX != 0f || lagState.VY != 0f || lagState.VZ != 0f)
				{
					lagState.VX = 0f; lagState.VY = 0f; lagState.VZ = 0f;
					_states[id] = lagState;
				}
			}
			ResolvePushboxes(simIds);
		}

		/// <summary>
		/// Resolve stable body pushboxes after all movement and ability movement for this tick.
		/// Pushboxes are horizontal cylinders derived from each character definition; they are
		/// deliberately separate from animation-driven attack hurtboxes.
		/// </summary>
		private void ResolvePushboxes(ulong[] simIds)
		{
			Array.Sort(simIds);
			for (int i = 0; i < simIds.Length; i++)
			{
				ulong firstId = simIds[i];
				if (!_states.TryGetValue(firstId, out var first)
				    || !TryDef(firstId, out var firstDef)
				    || IsEliminated(first))
					continue;

				for (int j = i + 1; j < simIds.Length; j++)
				{
					ulong secondId = simIds[j];
					if (!_states.TryGetValue(secondId, out var second)
					    || !TryDef(secondId, out var secondDef)
					    || IsEliminated(second))
						continue;
                    // She rides under the fighters her wave carries.
                    if (first.State == ActionState.Shell || second.State == ActionState.Shell)
                        continue;
                    // A line being reeled in, or a command hold, has one writer: the thrower's tether (ADR-0048), the grabber's grab (ADR-0057).
                    if (first.State is ActionState.Reeled or ActionState.CommandGrabbed && first.InteractionId != 0
                        || second.State is ActionState.Reeled or ActionState.CommandGrabbed && second.InteractionId != 0)
                        continue;
                    if ((_activeAbilities.TryGetValue(firstId, out var firstAbility)
                            && firstAbility.IgnoresFighterPushboxes)
                        || (_activeAbilities.TryGetValue(secondId, out var secondAbility)
                            && secondAbility.IgnoresFighterPushboxes))
                        continue;

					if (first.InteractionId != 0 && first.InteractionId == second.InteractionId)
						continue;
					float verticalReach = (firstDef.CapsuleHeight + secondDef.CapsuleHeight) * 0.5f;
					if (MathF.Abs(first.PY - second.PY) >= verticalReach)
						continue;

					float dx = second.PX - first.PX;
					float dz = second.PZ - first.PZ;
					float distanceSquared = dx * dx + dz * dz;
					float radiusSum = firstDef.CapsuleRadius + secondDef.CapsuleRadius;
					if (distanceSquared >= radiusSum * radiusSum)
						continue;

					float distance;
					if (distanceSquared > 0.000001f)
					{
						distance = MathF.Sqrt(distanceSquared);
						dx /= distance;
						dz /= distance;
					}
					else
					{
						distance = 0f;
						float angle = ((firstId ^ secondId) & 1UL) == 0UL ? 0f : MathF.PI * 0.5f;
						dx = MathF.Sin(angle);
						dz = MathF.Cos(angle);
					}

					float penetration = radiusSum - distance;
					bool firstFixed = first.State is ActionState.GrabAttempt or ActionState.CutHeld
                        || (firstAbility?.HoldsFighterPushboxPosition ?? false);
					bool secondFixed = second.State is ActionState.GrabAttempt or ActionState.CutHeld
                        || (secondAbility?.HoldsFighterPushboxPosition ?? false);
					if (firstFixed && secondFixed)
						continue;

					float firstCorrection = secondFixed ? 1f : firstFixed ? 0f : 0.5f;
					float secondCorrection = firstFixed ? 1f : secondFixed ? 0f : 0.5f;
					first.PX -= dx * penetration * firstCorrection;
					first.PZ -= dz * penetration * firstCorrection;
					second.PX += dx * penetration * secondCorrection;
					second.PZ += dz * penetration * secondCorrection;

					// Body contact is a positional stop, not momentum transfer. Remove
					// only each fighter's velocity component directed into the other
					// pushbox; preserve separating and tangential velocity.
					if (!firstFixed)
					{
						float firstNormalVelocity = first.VX * dx + first.VZ * dz;
						if (firstNormalVelocity > 0f)
						{
							first.VX -= dx * firstNormalVelocity;
							first.VZ -= dz * firstNormalVelocity;
						}
					}
					if (!secondFixed)
					{
						float secondNormalVelocity = second.VX * dx + second.VZ * dz;
						if (secondNormalVelocity < 0f)
						{
							second.VX -= dx * secondNormalVelocity;
							second.VZ -= dz * secondNormalVelocity;
						}
					}

					_states[firstId] = first;
					_states[secondId] = second;
				}
			}
			// Pushbox correction can move a character into a wall. Recover only the
			// bounded stage overlap; never snap to a different platform.
			for (int k = 0; k < simIds.Length; k++)
			{
				ulong id = simIds[k];
				if (!_states.TryGetValue(id, out var state)
				    || !TryDef(id, out var def)
				    || IsEliminated(state))
					continue;
				Simulation.RecoverStageOverlap(ref state, def, _arena);
				_states[id] = state;
			}
		}

		/// <summary>
		/// Capture the only tick boundary that can earn a settled crouch. This runs before
		/// ability admission, so an attack accepted on the same tick cannot manufacture a
		/// brace-eligible stance after the fact.
		/// </summary>
		private void CaptureSettledCrouchCandidates()
		{
			foreach (var (id, state) in _states)
			{
				if (!_defs.ContainsKey(id) || IsEliminated(state)
					|| !state.IsGrounded || state.State != ActionState.Crouching
					|| state.VX != 0f || state.VZ != 0f
					|| state.HitstopTicks != 0
					|| _activeAbilities.ContainsKey(id)
					|| !Simulation.IsGroundLowEligible(in state))
					continue;
				_settledCrouchCandidates.Add(id);
			}
		}

		/// <summary>
		/// Commit settled crouch only for a candidate that stayed stationary and eligible
		/// through movement, ability ticks, landing-lag freezing, and pushbox resolution.
		/// A frozen prior stance is intentionally untouched because it has no candidate.
		/// </summary>
		private void FinalizeSettledCrouch()
		{
			foreach (var id in _settledCrouchCandidates)
			{
				if (!_states.TryGetValue(id, out var state))
					continue;
				bool settled = !_lastTickAcceptedActions.Contains(id)
					&& !_activeAbilities.ContainsKey(id)
					&& state.HitstopTicks == 0
					&& state.IsGrounded
					&& state.State == ActionState.Crouching
					&& state.VX == 0f && state.VZ == 0f
					&& Simulation.IsGroundLowEligible(in state);
				state.CrouchSettled = settled;
				_states[id] = state;
			}
		}
		/// <summary>
		/// Server-side validation shared by client acquisition, sticky lock checks, and
		/// nearest-target selection.
		/// </summary>
		private bool IsEligibleEnemy(ulong selfId, ulong targetId, float selfX, float selfZ,
			float maxRange, out float distanceSquared)
		{
			distanceSquared = 0f;
			if (targetId == selfId || _onlineCourse != null && !CanAttackTarget(selfId, targetId) || _rule is SlopballCoopMatchRule && _states.ContainsKey(targetId)
				|| _rule is SlopballVersusMatchRule versus && _states.ContainsKey(targetId)
				    && (versus.BallOnly || versus.CampOf(selfId) == versus.CampOf(targetId))
				|| !TryGetCombatTargetPosition(targetId, out var target))
				return false;

			float dx = target.X - selfX;
			float dz = target.Z - selfZ;
			distanceSquared = dx * dx + dz * dz;
			return distanceSquared <= maxRange * maxRange;
		}
		/// <summary>Find the nearest eligible enemy, breaking equal-distance ties by ID.</summary>

		private ulong FindClosestEnemy(ulong selfId, float selfX, float selfZ, float maxRange, out float outDist)
		{
			ulong closest = 0;
			float best = maxRange * maxRange;
			foreach (ulong targetId in CombatTargetIds())
			{
				if (!IsEligibleEnemy(selfId, targetId, selfX, selfZ, maxRange, out float distanceSquared))
					continue;
				if (closest == 0 || distanceSquared < best
					|| distanceSquared == best && targetId < closest)
				{
					best = distanceSquared;
					closest = targetId;
				}
			}
			outDist = MathF.Sqrt(best);
			return closest;
		}

		/// <summary>Persistent lock and soft target acquisition both use a 20m horizontal range.</summary>
		private const float LockRangeMeters = 20f;

		private ulong ResolveClientTarget(ulong selfId, float selfX, float selfZ, in InputState input)
		{
			ulong selected = ResolveObjectTargetAlias(input.TargetEntityId, selfId);
			return selected != 0 && IsEligibleEnemy(selfId, selected, selfX, selfZ,
				LockRangeMeters, out _) ? selected : 0;
		}
		/// <summary>
		/// The one "current target" of target-using specials (the ring, the dive, the cannonball, the ride): the
		/// selected target when it is an eligible enemy in lock range, else the closest one. Its combat position is
		/// a fighter's capsule centre or an object's position.
		/// </summary>
		internal bool TryGetCurrentTarget(in CharacterState s, out ulong targetId, out ArcadePoint position)
		{
			targetId = s.TargetEntityId;
			if (targetId == 0 || !IsEligibleEnemy(s.EntityId, targetId, s.PX, s.PZ, LockRangeMeters, out _))
				targetId = FindClosestEnemy(s.EntityId, s.PX, s.PZ, LockRangeMeters, out _);
			if (targetId != 0 && TryGetCombatTargetPosition(targetId, out position))
				return true;
			targetId = 0;
			position = default;
			return false;
		}

		/// <summary>Resolve a straight shot toward the current eligible target at launch.</summary>
		internal void ResolveAutoTargetProjectileAim(ref CharacterState state,
			float launchOffsetX, float launchOffsetY, float launchOffsetZ, out float yaw, out float pitch)
		{
			yaw = state.FacingYaw;
			pitch = 0f;
			if (!TryGetCurrentTarget(in state, out ulong targetId, out var target))
				return;

			float dx = target.X - state.PX;
			float dz = target.Z - state.PZ;
			if (dx * dx + dz * dz > 0.000001f)
				yaw = MathF.Atan2(dx, dz);
			state.FacingYaw = yaw;
			float cosYaw = MathF.Cos(yaw);
			float sinYaw = MathF.Sin(yaw);
			dx -= launchOffsetX * cosYaw + launchOffsetZ * sinYaw;
			dz -= -launchOffsetX * sinYaw + launchOffsetZ * cosYaw;
			if (dx * dx + dz * dz > 0.000001f)
				yaw = MathF.Atan2(dx, dz);
			float targetY = CombatTargetAimY(targetId, target.Y);
			pitch = MathF.Atan2(targetY - state.PY - launchOffsetY, MathF.Sqrt(dx * dx + dz * dz));
		}

		internal void BeginStartupAimCorrection(ref CharacterState state, CookedStartupAimCorrectionOperation operation, in InputState input)
		{
			state.AttackCorrectionOwned = true;
			state.AttackCorrectionStartYaw = state.FacingYaw;
			state.AttackPosePitch = 0f;
			ulong preferred = state.LockOn && !input.ToggleLock && !input.RetargetPressed
				? state.TargetEntityId : ResolveObjectTargetAlias(input.TargetEntityId, state.EntityId);
			ulong targetId = 0;
			float bestDistance = float.MaxValue;
			if (!input.RetargetPressed && IsStartupCorrectionTarget(state, preferred, operation, acquire: true, out _))
				targetId = preferred;
			else
			{
				foreach (ulong candidate in CombatTargetIds())
				{
					if (!IsStartupCorrectionTarget(state, candidate, operation, acquire: true, out float distance))
						continue;
					if (targetId == 0 || distance < bestDistance || distance == bestDistance && candidate < targetId)
					{
						targetId = candidate;
						bestDistance = distance;
					}
				}
			}
			state.AttackCorrectionTargetId = targetId;
			state.AttackCorrectionTargetDeaths = CombatTargetDeaths(targetId);
			state.AttackCorrectionActive = targetId != 0;
		}

		internal void UpdateStartupAimCorrection(ref CharacterState state, CookedStartupAimCorrectionOperation operation, bool applyPose)
		{
			if (!state.AttackCorrectionActive)
				return;
			if (!IsStartupCorrectionTarget(state, state.AttackCorrectionTargetId, operation, acquire: false, out _))
			{
				state.AttackCorrectionActive = false;
				return;
			}
			TryGetCombatTargetPosition(state.AttackCorrectionTargetId, out var target);
			if (CombatTargetDeaths(state.AttackCorrectionTargetId) != state.AttackCorrectionTargetDeaths)
			{
				state.AttackCorrectionActive = false;
				return;
			}
			if (!applyPose)
				return;
			float dx = target.X - state.PX;
			float dz = target.Z - state.PZ;
			float horizontalSquared = dx * dx + dz * dz;
			const float radiansPerDegree = MathF.PI / 180f;
			float targetYaw = horizontalSquared > 0.000001f ? MathF.Atan2(dx, dz) : state.FacingYaw;
			float yawOffset = Math.Clamp(CorrectionAngleDelta(targetYaw - state.AttackCorrectionStartYaw),
				-operation.MaxYawDegrees * radiansPerDegree, operation.MaxYawDegrees * radiansPerDegree);
			float yawStep = operation.YawDegreesPerSecond * radiansPerDegree / 60f;
			state.FacingYaw += Math.Clamp(CorrectionAngleDelta(state.AttackCorrectionStartYaw + yawOffset - state.FacingYaw),
				-yawStep, yawStep);
			if (operation.MaxPitchDegrees > 0f)
			{
				float sourceHipY = Def(state.EntityId).BoneYToWorldY(state.PY, 0f);
				// A size-scaled upper-body reference; animation cannot move the aim point.
				float targetTorsoY = CombatTargetAimY(state.AttackCorrectionTargetId, target.Y);
				float targetPitch = Math.Clamp(MathF.Atan2(targetTorsoY - sourceHipY, MathF.Sqrt(horizontalSquared)),
					-operation.MaxPitchDegrees * radiansPerDegree, operation.MaxPitchDegrees * radiansPerDegree);
				float pitchStep = operation.PitchDegreesPerSecond * radiansPerDegree / 60f;
				state.AttackPosePitch += Math.Clamp(targetPitch - state.AttackPosePitch, -pitchStep, pitchStep);
			}
		}

		private bool IsStartupCorrectionTarget(in CharacterState state, ulong targetId,
			CookedStartupAimCorrectionOperation operation, bool acquire, out float distanceSquared)
		{
			if (targetId == 0 || !IsEligibleEnemy(state.EntityId, targetId, state.PX, state.PZ,
				operation.AcquisitionRange, out distanceSquared))
			{
				distanceSquared = 0f;
				return false;
			}
			TryGetCombatTargetPosition(targetId, out var target);
			float dy = target.Y - state.PY;
			distanceSquared += dy * dy;
			if (distanceSquared > operation.AcquisitionRange * operation.AcquisitionRange)
				return false;
			if (!acquire)
				return true;
			float dx = target.X - state.PX;
			float dz = target.Z - state.PZ;
			return dx * dx + dz * dz <= 0.000001f
				|| MathF.Abs(CorrectionAngleDelta(MathF.Atan2(dx, dz) - state.FacingYaw))
					<= operation.AcquisitionHalfAngleDegrees * (MathF.PI / 180f);
		}

		private static float CorrectionAngleDelta(float angle)
		{
			while (angle > MathF.PI) angle -= 2f * MathF.PI;
			while (angle < -MathF.PI) angle += 2f * MathF.PI;
			return angle;
		}

		/// <summary>
		/// Resolve a sticky lock target and soft-target selection for each entity. Client
		/// selection is validated only when a lock is acquired; retarget always chooses
		/// the nearest eligible enemy on the server.
		/// </summary>
		private void ProcessTargetLock(Dictionary<ulong, InputState> inputs)
		{
			ulong[] ids = new ulong[_states.Count];
			_states.Keys.CopyTo(ids, 0);
			foreach (var id in ids)
			{
				if (!_states.TryGetValue(id, out var state)) continue;
				if (state.InteractionId != 0) continue;
				bool hasInput = inputs.TryGetValue(id, out var input);
				var mode = hasInput && (byte)input.LockMode <= (byte)TargetLockMode.OnHit
					? input.LockMode : TargetLockMode.Never;
				// Each fresh ball is the co-op objective. Manual unlock still suppresses auto-lock.
				if (_rule is SlopballCoopMatchRule or SlopballVersusMatchRule) mode = TargetLockMode.Always;
				bool acquiring = false;
				bool retarget = hasInput && input.RetargetPressed;
				if (hasInput && input.ToggleLock)
				{
					if (state.LockOn)
					{
						state.LockOn = false;
						state.AutoLockSuppressed = true;
						state.TargetEntityId = 0;
					}
					else
					{
						state.LockOn = true;
						state.AutoLockSuppressed = false;
						acquiring = true;
					}
				}
				if (retarget)
				{
					state.LockOn = true;
					state.AutoLockSuppressed = false;
					acquiring = true;
				}

				ulong targetId = state.TargetEntityId;
				bool previousTargetValid = targetId != 0
					&& IsEligibleEnemy(id, targetId, state.PX, state.PZ, LockRangeMeters, out _);
				// Let a new toggle/retarget resolve before validating the previous target.
				if (state.LockOn && !previousTargetValid && !acquiring)
				{
					state.LockOn = false;
					state.TargetEntityId = 0;
				}
				if (!state.LockOn && mode == TargetLockMode.Always && !state.AutoLockSuppressed)
				{
					state.LockOn = true;
					acquiring = true;
				}

				if (state.LockOn)
				{
					if (retarget)
						targetId = FindClosestEnemy(id, state.PX, state.PZ, LockRangeMeters, out _);
					else if (acquiring || !previousTargetValid)
					{
						targetId = hasInput
							? ResolveClientTarget(id, state.PX, state.PZ, in input) : 0;
						if (targetId == 0)
							targetId = FindClosestEnemy(id, state.PX, state.PZ, LockRangeMeters, out _);
					}
					if (targetId == 0)
						state.LockOn = false;
				}
				else
				{
					targetId = hasInput
						? ResolveClientTarget(id, state.PX, state.PZ, in input) : 0;
					if (targetId == 0)
						targetId = FindClosestEnemy(id, state.PX, state.PZ, LockRangeMeters, out _);
				}
				state.TargetEntityId = targetId;
				_states[id] = state;
			}
		}


		private List<SpellResolver.EntityData> BuildHurtboxList()
		{
			// ── Step 2: Build entity list for hit detection ──
			// Unified pose resolution (spec #119): every entity's hurtboxes come from
			// BuildEntitiesFromState — the same function the Ability Lab preview uses —
			// so what the tool displays is exactly what collides.
			var entityList = new List<SpellResolver.EntityData>();
			foreach (var kvp in _states)
			{
				ulong id = kvp.Key;
				var state = kvp.Value;
				var def = Def(id);

				// Eliminated (0 stocks / rule) — untargetable spectator (issue #37).
				if (IsEliminated(state)) continue;

				if (ResolveBoneAnimFrame(id, state, def, out var baked, out var targetAnim, out var bakedFrame))
				{
					entityList.AddRange(BuildEntitiesFromState(state, def, baked, targetAnim, bakedFrame, id));
				}
				else if (def.HurtboxCapsules != null)
				{
					// No baked data / no bone defs → capsule fallback.
					entityList.AddRange(BuildEntitiesFromState(state, def, null!, "idle", 0, id));
				}
			}
			if (_arcadeBall != null && _arcadeBall.State.Active) entityList.Add(_arcadeBall.CollisionTarget);
            if (_course != null) entityList.AddRange(_course.CollisionTargets());
            if (_onlineCourse != null) entityList.AddRange(_onlineCourse.CollisionTargets());
            if (_mayhem != null) entityList.AddRange(_mayhem.Solids.CollisionTargets());
			_lastEntityList = entityList;
			return entityList;
		}

		private List<SpellResolver.EntityData> BuildAttackEntities(List<SpellResolver.EntityData> hurtboxes)
		{
			bool hasShield = false;
			foreach (var (_, state) in _states)
				if (state.State == ActionState.Shielding && !IsEliminated(state))
				{
					hasShield = true;
					break;
				}
			if (!hasShield) return hurtboxes;

			_attackEntities.Clear();
			_shieldSurfaceEntities.Clear();
			foreach (var hurtbox in hurtboxes)
			{
				if (!_states.TryGetValue(hurtbox.Id, out var shieldState) || shieldState.State != ActionState.Shielding)
				{
					_attackEntities.Add(hurtbox);
					continue;
				}
				var body = hurtbox; body.ProtectedByShield = true; _attackEntities.Add(body);
                if (_shieldSurfaceEntities.Add(hurtbox.Id))
					AddShieldSurface(hurtbox.Id, _states[hurtbox.Id]);
			}
			// Missing bone tracks do not remove a fighter's active guard.
			foreach (var (id, state) in _states)
				if (state.State == ActionState.Shielding && !IsEliminated(state)
					&& _shieldSurfaceEntities.Add(id))
					AddShieldSurface(id, state);
			return _attackEntities;
		}

		private void AddShieldSurface(ulong id, in CharacterState state)
		{
			_attackEntities.Add(new SpellResolver.EntityData
			{
				Id = id, PosX = state.PX, PosY = state.PY, PosZ = state.PZ,
				EndX = state.PX, EndY = state.PY, EndZ = state.PZ,
				Radius = Def(id).ShieldRadius, Shape = HitboxShape.Sphere,
				InvincibilityTicks = state.InvincibilityTicks, Active = true,
				ShieldSurface = true,
			});
		}


        private bool IsCrouchBraceEligible(ulong id, in CharacterState state, CharacterDefinition def)
        {
            if (!state.IsGrounded
                || state.State != ActionState.Crouching
                || state.AttackSlot != 0
                || !state.CrouchSettled
                || state.VX != 0f || state.VZ != 0f
                || !Simulation.IsGroundLowEligible(in state)
                || _activeAbilities.ContainsKey(id)
                || string.IsNullOrEmpty(def.CrouchAnim)
                || def.HurtboxBoneDefs == null || def.HurtboxBoneDefs.Count == 0
                || !_bakedData.TryGetValue(id, out var baked))
                return false;

            return baked.FrameCountFor(def.CrouchAnim) > 0;
        }


		private static (float x, float z) GetBlockPushbackDirection(
			in SpellResolver.HitResult hit, in CharacterState targetState,
			in CharacterState attackerState, bool attackerExists)
		{
			float dx = attackerExists ? targetState.PX - attackerState.PX : hit.DirX;
			float dz = attackerExists ? targetState.PZ - attackerState.PZ : hit.DirZ;
			float lengthSquared = dx * dx + dz * dz;
			if (lengthSquared <= 0.000001f)
			{
				dx = hit.DirX;
				dz = hit.DirZ;
				lengthSquared = dx * dx + dz * dz;
			}
			if (lengthSquared <= 0.000001f)
			{
				dx = -MathF.Sin(targetState.FacingYaw);
				dz = -MathF.Cos(targetState.FacingYaw);
				lengthSquared = dx * dx + dz * dz;
			}
			float inverseLength = 1f / MathF.Sqrt(lengthSquared);
			return (dx * inverseLength, dz * inverseLength);
		}

		private void ApplyShieldBlockPushback(
			ref CharacterState targetState, CharacterDefinition targetDef, float dirX, float dirZ)
		{
			if (!targetState.IsGrounded) return;

			var displaced = targetState;
			Simulation.MoveThroughStage(ref displaced, targetDef, _arena,
				dirX * 0.10f, 0f, dirZ * 0.10f);
			if (!displaced.IsGrounded && targetState.IsGrounded) return;
			if (!ArenaCollision.HasTriangles(_arena)
				&& _arena.Heightmap.Data is { Length: > 0 }
				&& _arena.Heightmap.Sample(displaced.PX, displaced.PZ) == float.MinValue)
				return;

			targetState.PX = displaced.PX;
			targetState.PY = displaced.PY;
			targetState.PZ = displaced.PZ;
			targetState.IsGrounded = displaced.IsGrounded;
		}

		private void ResolveShieldBlock(
			in SpellResolver.HitResult hit, ref CharacterState targetState,
			ref CharacterState attackerState, bool attackerExists)
		{
			ushort freeze = ComputeHitstopTicks(hit.Damage);
			int incomingStun = Math.Clamp(
				(int)MathF.Ceiling(MathF.Max(0f, hit.Damage) * 0.6f) + 2, 4, 15);
			targetState.BlockStunTicks = (ushort)Math.Max(targetState.BlockStunTicks, incomingStun);
			targetState.HitstopTicks = Math.Max(targetState.HitstopTicks, freeze);
			targetState.BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact;

			var direction = GetBlockPushbackDirection(
				in hit, in targetState, in attackerState, attackerExists);
			ApplyShieldBlockPushback(
				ref targetState, Def(hit.TargetEntityId), direction.x, direction.z);

			if (hit.FreezesOwner && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
				attackerState.HitstopTicks = Math.Max(attackerState.HitstopTicks, freeze);

			_states[hit.TargetEntityId] = targetState;
			if (attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
				_states[hit.OwnerEntityId] = attackerState;

			var blockedHit = hit;
			blockedHit.Blocked = true;
			blockedHit.MatchTick = _tick;
			blockedHit.Damage = 0f;
			blockedHit.DirX = direction.x;
			blockedHit.DirZ = direction.z;
			blockedHit.ImpactForce = 0f;
			blockedHit.HitstopTicks = freeze;
			LastTickHits.Add(blockedHit);

			_presentationEvents.Add(new TimelinePresentationEvent(
				_tick, hit.TargetEntityId, BlockContactOperationIndex(in hit),
				"combat.block", hit.AttackSequence, PresentationEventSource.BlockContact,
				blockedHit.HitX, blockedHit.HitY, blockedHit.HitZ, targetState.FacingYaw));
		}

		private static int BlockContactOperationIndex(in SpellResolver.HitResult hit)
		{
			ulong foldedOwner = hit.OwnerEntityId ^ (hit.OwnerEntityId >> 32);
			return (int)(((foldedOwner & 0x07ff_ffffUL) << 4) | (hit.AttackSlot & 0x0fUL));
		}

		private void TryLockOnAfterHit(ulong entityId, ulong targetId,
			ref CharacterState state, Dictionary<ulong, InputState> inputs)
		{
			if (state.LockOn || state.AutoLockSuppressed
				|| !inputs.TryGetValue(entityId, out var input)
				|| input.LockMode != TargetLockMode.OnHit
				|| !IsEligibleEnemy(entityId, targetId, state.PX, state.PZ,
					LockRangeMeters, out _))
				return;

			state.LockOn = true;
			state.TargetEntityId = targetId;
		}

		/// <summary>
		/// The activation whose hitbox, projectile or blast made this contact, if its owner is still
		/// running it. Hit, block and object hooks belong to that move: a projectile from an earlier
		/// move must not reach the move its owner is in now (a blocked shuriken stopping a later dash).
		/// </summary>
		private Func<ulong, (bool Exists, float X, float Y, float Z)>? _returnTarget;

		/// <summary>Where a returning projectile homes in: its owner's chest, while he is in the match.</summary>
		private (bool Exists, float X, float Y, float Z) ReturnTarget(ulong ownerId)
			=> _states.TryGetValue(ownerId, out var owner) && TryDef(ownerId, out var def)
			   && !IsEliminated(owner)
				? (true, owner.PX, owner.PY + def.CapsuleHeight * .25f, owner.PZ)
				: (false, 0f, 0f, 0f);

		/// <summary>
		/// A returning projectile reached its owner. He catches it when he could act now (idle, running,
		/// crouching, sliding or starting a jump, on the ground or in the air, with no stun, freeze or
		/// landing lag), and when he is shielding, being hit or in a grab it is simply back on his
		/// shoulder. Busy (attacking, aiming, air dodging, grabbing, dropping a shield, landing), it hits
		/// him: an ordinary hit with no launch and a fixed stun, so armour and hitstop apply as usual.
		/// Returns true when the contact stays in the list as that self-hit.
		/// </summary>
		private bool ResolveReturnToOwner(ref SpellResolver.HitResult contact)
		{
			if (!_spellResolver.TryTakeReturningProjectile(contact.OwnerEntityId, contact.ActivationId, out var lid)
			    || !_states.TryGetValue(contact.OwnerEntityId, out var owner))
				return false;
			var config = lid.Return!.Value;
			if (CatchesReturningProjectile(in owner))
			{
				EmitReturnEvent(in lid, PresentationEventSource.ProjectileCatch, config.CatchPresentationId, in owner);
				return false;
			}
			EmitReturnEvent(in lid, PresentationEventSource.ProjectileSelfHit, config.SelfHitPresentationId, in owner);
			contact.Damage = config.SelfHitDamage;
			contact.BaseKnockback = 0f;
			contact.KnockbackGrowth = 0f;
			contact.KnockbackAngle = 0;
			contact.StunTicks = Math.Max((ushort)1, config.SelfHitStunTicks);
			contact.FixedHitstunTicks = config.SelfHitStunTicks;
			contact.FreezesOwner = false;
			return true;
		}

		/// <summary>
		/// Whether a returning projectile coming home is caught rather than hitting its owner: he is free
		/// (idle on the ground or in the air, running, crouching, sliding or in jump squat, with no hitstop,
		/// blockstun or landing lag), shielding, hanging on a ledge (a self-hit would knock him off the
		/// stage), held or already being hit. Anything else is busy: it bonks him.
		/// </summary>
		internal static bool CatchesReturningProjectile(in CharacterState owner)
		{
			bool held = owner.InteractionId != 0
				|| ActionStates.InPairedInteraction(owner.State);
			bool beingHit = owner.State == ActionState.Hitstun || owner.HitstunTicks > 0 || Simulation.HasKnockback(owner);
			bool free = owner.HitstopTicks == 0 && owner.BlockStunTicks == 0 && owner.LandingLagTicks == 0
				&& (ActionStates.AdmitsActions(owner.State) || owner.State == ActionState.JumpSquat);
			return held || beingHit || free || owner.State is ActionState.Shielding or ActionState.LedgeHang;
		}

		private void EmitReturnEvent(in Hitbox lid, PresentationEventSource source, string presentationId, in CharacterState owner)
		{
			if (string.IsNullOrEmpty(presentationId)) return;
			_presentationEvents.Add(new TimelinePresentationEvent(_tick, lid.OwnerId, lid.VisualOperationIndex,
				presentationId, lid.AttackSequence, source, lid.X, lid.Y, lid.Z, owner.FacingYaw)
			{
				Placement = new PresentationPlacement(DurationTicks: 90),
			});
		}

		private ServerAbility? ContactAbility(in SpellResolver.HitResult hit)
			=> _activeAbilities.TryGetValue(hit.OwnerEntityId, out var ability) && ability.ActivationId == hit.ActivationId
				? ability : null;

        private void ResolveHits(List<SpellResolver.EntityData> entityList, Dictionary<ulong, InputState> inputs)
		{
			// Bone-tracked hitboxes sweep with their limb: re-resolve positions from
			// the owners' current (post-movement) states before the collision pass.
            foreach (var state in _states.Values)
                if (IsOutsideBlastZone(in state)) _spellResolver.RemoveContactBombs(state.EntityId);
			_spellResolver.UpdateBoneHitboxes(_states);
			_spellResolver.UpdateReturningProjectiles(_returnTarget ??= ReturnTarget, _arena);
			_spellResolver.UpdateHomingProjectiles(_homingTarget ??= HomingTarget);
            _spellResolver.UpdatePerchFlights(_perchTargetFeet ??= PerchTargetFeet);
            _spellResolver.UpdateStickyProjectiles(StickyCarrier);
            SwatPerches();
            _spellResolver.BreakFragileProjectiles(_opposesProjectileOwner ??= OpposesProjectileOwner);

			// ── Step 3: Resolve hitboxes ──
			var attackEntities = BuildAttackEntities(entityList);
			LastTickAttackEntities = attackEntities;
			var hits = _spellResolver.Tick(attackEntities, orderMovingTargetContacts: _slopball != null,
				frozenOwners: _hitstopFrozenThisTick, canHitTarget: _attackTargetFilter, fighters: _states);
			PeckCarriers();
			EmitPerchScatters();
			AddDuePops(hits);
			hits.AddRange(_commandReleases);
			_commandReleases.Clear();
			for (int i = 0; i < hits.Count; i++)
			{
				var contact = hits[i];
				contact.ArmorProtected = _activeAbilities.TryGetValue(contact.TargetEntityId, out var defender)
					&& defender.HasArmor;
				hits[i] = contact;
			}
			for (int i = hits.Count - 1; i >= 0; i--)
			{
				var contact = hits[i];
				if (contact.ReturnPhase != 2 || contact.TargetEntityId != contact.OwnerEntityId) continue;
				if (ResolveReturnToOwner(ref contact))
					hits[i] = contact;
				else
					hits.RemoveAt(i);
			}
			ClashCatches(hits);
			LastTickHits.Clear();
			int hitContactOperationIndex = 0;
			foreach (var hit in hits)
			{
				// A hit on her earlier in this batch ended her ride and its wave.
				if (hit.OwnerWindow && (!_states.TryGetValue(hit.OwnerEntityId, out var windowOwner)
					|| windowOwner.State != ActionState.Shell))
					continue;
				if (_arcadeBall != null && hit.TargetEntityId == _arcadeBall.State.EntityId)
				{
					ResolveArcadeBallHit(hit, inputs, hitContactOperationIndex++);
					continue;
				}
				if (_onlineCourse != null && _onlineCourse.TryTarget(hit.TargetEntityId, out var racerCourse, out int racerTarget))
                {
                    ResolveCourseTargetHit(hit, racerTarget, inputs, hitContactOperationIndex++, racerCourse);
                    continue;
                }
                if (_course != null && _course.TryIndex(hit.TargetEntityId, out int courseTarget))
				{
					ResolveCourseTargetHit(hit, courseTarget, inputs, hitContactOperationIndex++);
					continue;
				}
                if (_mayhem != null && _mayhem.Solids.Contains(hit.TargetEntityId))
                { ResolveMayhemWallHit(hit); continue; }
				if (!_states.TryGetValue(hit.TargetEntityId, out var targetState)) continue;
				bool attackerExists = _states.TryGetValue(hit.OwnerEntityId, out var attackerState);

				// Invincible (respawn grace, dash) — the hit is fully ignored (issue #37).
				if (targetState.InvincibilityTicks > 0) continue;
				// Her wave catches instead of hitting (ADR-0050); it refuses fighters in a pair itself.
				if (attackerExists && hit.OwnerWindow)
				{
					CatchOnWave(in hit, ref attackerState, ref targetState, inputs, hitContactOperationIndex++);
					_states[hit.OwnerEntityId] = attackerState;
					_states[hit.TargetEntityId] = targetState;
					continue;
				}
				// A command grab's capture holds through a shield and armour; its own move's hits on the fighter it holds
				// land as damage and hitstop only, and its release is an ordinary hit (ADR-0057). A contact without damage
				// is never an ordinary hit, nor a hit contact.
				if (attackerExists && ContactAbility(hit)?.CommandGrab is { } grab)
				{
					bool ownHeldVictim = grab.Holding && targetState.State == ActionState.CommandGrabbed
						&& targetState.InteractionId != 0 && targetState.InteractionId == attackerState.InteractionId
						&& targetState.InteractionPartnerId == hit.OwnerEntityId;
					if (grab.Catches || ownHeldVictim || hit.Damage <= 0f)
					{
						if (!ownHeldVictim)
							CatchOnCommandGrab(in hit, grab, ref attackerState, ref targetState, inputs,
								hit.Damage > 0f ? hitContactOperationIndex++ : hitContactOperationIndex);
						else
							HitCommandGrabbedFighter(in hit, ref targetState, ref attackerState, inputs, hitContactOperationIndex++);
						_states[hit.OwnerEntityId] = attackerState;
						_states[hit.TargetEntityId] = targetState;
						continue;
					}
				}
				if (!PredictCoupledInteractions && targetState.InteractionId != 0) continue;
				if (targetState.State == ActionState.Shielding && !hit.Unblockable)
				{
					// A shield turns a returning projectile back on its way out.
					if (hit.ReturnPhase == 1)
						_spellResolver.TurnReturningProjectile(hit.OwnerEntityId, hit.ActivationId, hit.TargetEntityId);
					ResolveShieldBlock(hit, ref targetState, ref attackerState, attackerExists);
					if (attackerExists && ContactAbility(hit) is { } blockingAbility)
					{
						blockingAbility.OnShieldBlock(ref attackerState, ref targetState);
						_states[hit.OwnerEntityId] = attackerState;
					}
					continue;
				}
				// A tether's ring catches instead of hitting (ADR-0048); its unmarked hand hits (ADR-0051 section 10).
				if (attackerExists && ContactAbility(hit)?.Tether is { Launches: false } tether)
				{
					CatchOnTether(in hit, tether, ref attackerState, ref targetState, inputs, hitContactOperationIndex++);
					_states[hit.OwnerEntityId] = attackerState;
					_states[hit.TargetEntityId] = targetState;
					continue;
				}
				if (targetState.State == ActionState.CutHeld)
				{
					HitHeldFighter(in hit, ref targetState, ref attackerState, attackerExists, hitContactOperationIndex++);
					if (attackerExists) _states[hit.OwnerEntityId] = attackerState;
					_states[hit.TargetEntityId] = targetState;
					continue;
				}

				if (hit.Damage > 0f) _damagedThisTick.Add(hit.TargetEntityId);
                if (targetState.State is ActionState.HomingDashFloat or ActionState.HomingDash or ActionState.HomingDashArrive)
                    targetState.BufferedSlot = 0;
				// Snapshot settled crouch before reaction cleanup/callbacks. A later
				// replacement hit may overwrite this queue, but cannot earn a new stance.
				if (PredictCoupledInteractions && targetState.InteractionId != 0 && hit.Damage > 0f)
					InterruptInteraction(ref targetState);

				bool crouchBraceEligible = IsCrouchBraceEligible(
					hit.TargetEntityId, in targetState, Def(hit.TargetEntityId));
				if (!hit.ArmorProtected)
                {
                    Flying.End(ref targetState, Def(hit.TargetEntityId), hit.TargetEntityId != NoCooldownsEntityId);
                    Simulation.ClearMovementInterruptionFlags(ref targetState);
					// No armour on the ride: any hit pops her out and her wave lets go, and a fighter it carries
					// leaves it with that hit's own reaction (ADR-0050).
					if (targetState.State is ActionState.Shell or ActionState.Swept)
					{
						targetState.State = ActionState.Idle;
						targetState.StateTicks = 0;
					}
				}
				if (_mayhem == null && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
					_lastHitCredits[hit.TargetEntityId] = (hit.OwnerEntityId, _tick, hit.AttackSlot);
				if (hit.OwnerEntityId != 0)
					_lastHitContexts[hit.TargetEntityId] = (hit.OwnerEntityId, _tick, hit.AttackSlot);
				// Knockback direction: from attacker to target (not hitbox to target).
				// The hitbox offset can place it past the target, inverting the direction.
				// Smash convention: always push away from the attacker.
				float dirX = hit.DirX;
				float dirZ = hit.DirZ;
				if (attackerExists)
				{
					float aDx = targetState.PX - attackerState.PX;
					float aDz = targetState.PZ - attackerState.PZ;
					float aDist = MathF.Sqrt(aDx * aDx + aDz * aDz);
					if (aDist > 0.001f)
					{
						dirX = aDx / aDist;
						dirZ = aDz / aDist;
					}
				}
                    if (hit.KnockbackDirection == AuthoringKnockbackDirection.TowardOwner)
                    {
                        dirX = -dirX;
                        dirZ = -dirZ;
                    }

				// Hit-reaction facing: the victim turns to face the attacker (the direction the
				// hit came from — opposite the launch). Persists through the hitstun flight;
				// ProcessNormalMovement re-faces on the next input/land.
				if (!hit.ArmorProtected)
					targetState.FacingYaw = MathF.Atan2(-dirX, -dirZ);




				bool shove = IsMayhemShove(hit.OwnerEntityId, hit.TargetEntityId);
                ushort percentBefore = targetState.DamagePercent;
                float finalDamage = shove ? 0f : hit.Damage;
				targetState.DamagePercent += (ushort)finalDamage;
				if (targetState.DamagePercent > 999) targetState.DamagePercent = 999;


				// ── Hitstop (ADR-0019): freeze both (melee) or receiver only, defer the launch.
                ushort freeze = 0;
                // Capture the default once: the hook check must compare kbForce to this exact
            // stored value — re-computing the expression at the check is NOT bit-identical
            // (the editor JIT evaluates in 80-bit x87 precision, so round32(expr) !=
            // expr80 — every normal hit wrongly took the force path, bypassing the velocity scale).
            float kbForceDefault = hit.BaseKnockback + hit.KnockbackGrowth * (targetState.DamagePercent * 0.01f);
            float kbForce = kbForceDefault;
                bool hookSuppliedForce = false;
                bool hookZeroForce = false;
                CharacterState armoredReaction = default;
                if (attackerExists
                    && ContactAbility(hit) is { } attackerAbility
                    && TryDef(hit.OwnerEntityId, out var attackerDef))
                {
                    if (hit.ArmorProtected)
                        armoredReaction = targetState;
                    attackerAbility.OnHitEntity(ref attackerState, ref targetState,
                        attackerAbility.CharacterDef ?? attackerDef, Def(hit.TargetEntityId), ref finalDamage, ref kbForce);
                    if (hit.ArmorProtected)
                    {
                        targetState.KVX = armoredReaction.KVX;
                        targetState.KVY = armoredReaction.KVY;
                        targetState.KVZ = armoredReaction.KVZ;
                        targetState.HitstunTicks = armoredReaction.HitstunTicks;
                        targetState.HitstunLevel = armoredReaction.HitstunLevel;
                        targetState.FacingYaw = armoredReaction.FacingYaw;
                        targetState.State = armoredReaction.State;
                        targetState.IsGrounded = armoredReaction.IsGrounded;
                        targetState.AirTimeTicks = armoredReaction.AirTimeTicks;
                        targetState.AirDodgeRecoveryTicks = armoredReaction.AirDodgeRecoveryTicks;
                        targetState.StateTicks = armoredReaction.StateTicks;
                        targetState.WasAirborneDuringKnockback = armoredReaction.WasAirborneDuringKnockback;
                        targetState.InPostHitstunFlight = armoredReaction.InPostHitstunFlight;
                        targetState.LandingLagTicks = armoredReaction.LandingLagTicks;
                        targetState.IsFastFalling = armoredReaction.IsFastFalling;
                        targetState.JumpTakeoff = armoredReaction.JumpTakeoff;
                    }
                    hookSuppliedForce = kbForce != kbForceDefault;
                    hookZeroForce = hookSuppliedForce && kbForce <= 0f;
                }
                if (shove)
                {
                    targetState.DamagePercent = percentBefore; finalDamage = 0f;
                    kbForce = kbForceDefault; hookSuppliedForce = hookZeroForce = false;
                }
                bool ordinaryFormulaLaunch = !hookSuppliedForce;
                float launchBase = hookSuppliedForce ? 0f : hit.BaseKnockback;
                float launchGrowth = hookSuppliedForce ? 0f : hit.KnockbackGrowth;
                // A pure push (no damage, no stun: a shove such as one drop of Bashie's Hose Down stream) does not
                // freeze its target, so a stream of them keeps pushing instead of stuttering it in place.
                freeze = (shove ? hit.Damage : finalDamage) <= 0f && hit.StunTicks == 0 ? (ushort)0 : ComputeHitstopTicks(shove ? hit.Damage : finalDamage);
                // A flash cut holds its victim until the click instead (ADR-0052); armour takes an ordinary hit.
                if (freeze > 0 && !shove && !hit.ArmorProtected && attackerExists
                    && ContactAbility(hit)?.HeldCutFor(hit.TargetEntityId) is { } heldCut && heldCut.PopTick > _tick)
                    freeze = HoldCut(in hit, ref targetState, heldCut);
				if (freeze > 0)
				{
                    QueueHitReaction(in hit, ref targetState, ref attackerState, attackerExists, freeze,
                        dirX, dirZ, launchBase, launchGrowth, kbForce, finalDamage,
                        hookSuppliedForce, hookZeroForce, crouchBraceEligible && ordinaryFormulaLaunch);
				}

				else if (!hit.ArmorProtected)
				{
					if (hookSuppliedForce)
					{
						Simulation.ApplyKnockbackForce(ref targetState, dirX, dirZ,
							hit.KnockbackAngle, kbForce, hit.StunTicks, CombatTuning);
						Simulation.ApplyFixedHitstun(ref targetState, hit.FixedHitstunTicks, hit.StunTicks);
					}
					else if (hookZeroForce)
					{
						targetState.KVX = targetState.KVY = targetState.KVZ = 0f;
						targetState.HitstunTicks = 0;
						targetState.HitstunLevel = 0;
						targetState.State = ActionState.Idle;
					}
					else
					{
						Simulation.ApplyKnockback(ref targetState, dirX, dirZ,
							hit.KnockbackAngle, launchBase, launchGrowth,
							finalDamage, hit.StunTicks, Def(hit.TargetEntityId).Weight, combatTuning: CombatTuning);
						Simulation.ApplyFixedHitstun(ref targetState, hit.FixedHitstunTicks, hit.StunTicks);
						Simulation.ApplyCrouchBrace(
							ref targetState, crouchBraceEligible && ordinaryFormulaLaunch,
							_downActionTuning.CrouchLaunchMultiplier);
					}
					if (inputs.TryGetValue(hit.TargetEntityId, out var targetInput)
						&& (targetInput.MoveX != 0f || targetInput.MoveY != 0f))
					{
						targetState.DIX = targetInput.MoveX;
						targetState.DIY = targetInput.MoveY;
						Simulation.ApplySdi(ref targetState, targetState.DIX, targetState.DIY,
							Def(hit.TargetEntityId), _arena);
						Simulation.ApplyDirectionalInfluence(ref targetState);
						targetState.DIX = targetState.DIY = 0f;
					}
				}



				if (finalDamage > 0f && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
				{
					TryLockOnAfterHit(hit.OwnerEntityId, hit.TargetEntityId, ref attackerState, inputs);
					TryLockOnAfterHit(hit.TargetEntityId, hit.OwnerEntityId, ref targetState, inputs);
				}

				// Write the attacker's state back even when the owner has no active ability
				// (e.g. a projectile hitting after its ability ended) — the freeze must land.
				if (attackerExists) _states[hit.OwnerEntityId] = attackerState;
				float impactForce = 0f;
				if (!hit.ArmorProtected)
				{
					if (hookSuppliedForce)
					{
						impactForce = MathF.Max(0f, kbForce);
					}
					else
					{
						float mass = MathF.Max(0.01f, Def(hit.TargetEntityId).Weight + 100f);
						float braceMultiplier = ordinaryFormulaLaunch && crouchBraceEligible
							? _downActionTuning.CrouchLaunchMultiplier : 1f;
						impactForce = (launchBase
							+ launchGrowth * (targetState.DamagePercent * 0.01f + 1f)
							+ finalDamage * 0.1f) * 200f / mass * CombatTuning.KnockbackScale
							* braceMultiplier;
					}
				}
				var resolvedHit = hit;
				resolvedHit.MatchTick = _tick;
				resolvedHit.Damage = finalDamage;
				resolvedHit.DirX = dirX;
				resolvedHit.DirZ = dirZ;
				resolvedHit.ImpactForce = impactForce;
				resolvedHit.HitstopTicks = freeze;

				_states[hit.TargetEntityId] = targetState;
				LastTickHits.Add(resolvedHit);
				ContactAbility(in hit)?.OnHitConfirmed(in resolvedHit, in targetState, hitContactOperationIndex);
				if (finalDamage > 0f && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
					QueuePerchPop(in hit, in targetState);
				EmitHitPresentation(in hit, finalDamage, in attackerState, attackerExists, hitContactOperationIndex);
				hitContactOperationIndex++;
			}
		}

		/// <summary>The slot's (or the shell's) hit effect at a confirmed contact, weighted by its damage.</summary>
		private void EmitHitPresentation(in SpellResolver.HitResult hit, float finalDamage,
			in CharacterState attackerState, bool attackerExists, int operationIndex)
		{
			if (!TryDef(hit.OwnerEntityId, out var hitOwnerDefinition)
			    || hit.AttackSlot == 0 && !hit.OwnerWindow)
				return;
			string? hitPresentationId = hit.OwnerWindow
				? hitOwnerDefinition.Shell?.Ride?.HitPresentationId
				: hit.HitPresentationId
					?? hitOwnerDefinition.ForForm(hit.Form).GetCookedSlotAbility(hit.AttackSlot, hit.Airborne)?.HitPresentationId;
			if (string.IsNullOrEmpty(hitPresentationId))
				return;
			// Confirmed contact carries cosmetic weight, including through replay.
			float presentationScale = Math.Clamp(MathF.Sqrt(finalDamage / 6f), 0.6f, 1.6f);
			_presentationEvents.Add(new TimelinePresentationEvent(
				_tick, hit.OwnerEntityId, operationIndex,
				hitPresentationId!, hit.AttackSequence,
				PresentationEventSource.HitContact,
				hit.HitX, hit.HitY, hit.HitZ,
				attackerExists ? attackerState.FacingYaw : 0f)
			{
				Placement = new PresentationPlacement(
					LocalScaleX: presentationScale,
					LocalScaleY: presentationScale,
					LocalScaleZ: presentationScale,
					DurationTicks: 150),
			});
		}

		private void ProcessProjectileExplosions()
		{
			// ── Step 3b: Projectile explosions (entity hit + ground impact) ──
            // Ground collision for remaining active projectiles (samples heightmap per projectile)
            _spellResolver.CheckGroundCollision(_arena);

			// Spawn explosion hitboxes for all deactivated projectiles this tick
            // NOTE: The ProjectileExplosion config is baked at spawn time, so nothing here
            // applies owner-state changes — explosions are secondary effects detached from
            // the owner's state by the time they resolve. An ability MAY therefore bake
            // values into the config itself before Resolver.Spawn: custom payload explosions
            // and authored MankiBazooka configs both use this path.
			foreach (var (ex, ey, ez, explosion, ownerId, attackSlot, activationId, airborne, attackSequence) in _spellResolver.DrainPendingExplosions())
			{
                if (_mayhem != null && ownerId == MayhemHostEntityId)
                    foreach (ulong wall in _mayhem.Solids.InBlast(ex, ey, ez, explosion.Radius).ToArray())
                        _mayhem.HitWall(_tick, wall, wreck: true);
                if (_states.TryGetValue(ownerId, out var blastOwner))
                {
                    if (explosion.EndsOwnerActivation && GetActiveAbility(ownerId)?.ActivationId == activationId)
                    {
                        blastOwner.AnimLockTicks = 0;
                        RetireAbility(ownerId, ref blastOwner, AbilityExit.Cancelled);
                    }
                    float dx = blastOwner.PX - ex, dz = blastOwner.PZ - ez;
                    float distance = MathF.Sqrt(dx * dx + dz * dz);
                    if (explosion.SelfPushSpeed > 0f && dx * dx + dz * dz + (blastOwner.PY - ey) * (blastOwner.PY - ey)
                        <= explosion.Radius * explosion.Radius)
                    {
                        if (distance < .01f) { dx = -MathF.Sin(blastOwner.FacingYaw); dz = -MathF.Cos(blastOwner.FacingYaw); distance = 1f; }
                        blastOwner.VX = dx / distance * explosion.SelfPushSpeed * .7f;
                        blastOwner.VZ = dz / distance * explosion.SelfPushSpeed * .7f;
                        blastOwner.VY = explosion.SelfPushSpeed;
                        blastOwner.IsGrounded = false;
                    }
                    _states[ownerId] = blastOwner;
                }
                var (kbAngle, kbBase, kbGrowth) = explosion.Knockback.Resolve();
				_spellResolver.Spawn(new Hitbox
				{
					X = ex, Y = ey, Z = ez,
					Radius = explosion.Radius, Shape = HitboxShape.Sphere,
					EndX = ex, EndY = ey, EndZ = ez,
					Damage = explosion.Damage,
					BaseKnockback = kbBase,
					KnockbackGrowth = kbGrowth,
					KnockbackAngle = kbAngle,
					StunTicks = explosion.StunTicks,
					DurationTicks = explosion.DurationTicks,
					OwnerId = ownerId,
					AttackSlot = attackSlot,
					ActivationId = activationId,
					ActivationAirborne = airborne,
					AttackSequence = attackSequence,
					CanHitOwner = explosion.CanHitOwner,
					OwnerDamage = explosion.OwnerDamage,
					// A blast hits everything in its radius once (fighters, the owner when
					// allowed, balls and course targets), not just the first entity it meets.
					HitsMultipleOpponents = true,
				});
                float presentationScale = explosion.SelfPushSpeed > 0f ? explosion.Radius / 3f : 1f;
                if (!string.IsNullOrEmpty(explosion.ExplosionPresentationId))
                    _presentationEvents.Add(new TimelinePresentationEvent(
                        _tick, ownerId, explosion.PresentationOperationIndex,
                        explosion.ExplosionPresentationId, explosion.PresentationAttackSequence,
                        PresentationEventSource.CapabilityExplosion, ex, ey, ez, explosion.PresentationYaw)
                    {
                        Placement = new PresentationPlacement(
                            LocalScaleX: presentationScale,
                            LocalScaleY: presentationScale,
                            LocalScaleZ: presentationScale,
                            DurationTicks: 150),
                    });
			}
		}

		private bool IsOutsideBlastZone(in CharacterState state)
			=> state.PY < _blastLines.KillHeight || state.PY > _blastLines.KillTop
				|| state.PX < _blastLines.KillMinX || state.PX > _blastLines.KillMaxX
				|| state.PZ < _blastLines.KillMinZ || state.PZ > _blastLines.KillMaxZ;

		private void CheckBlastDeaths()
		{
			// ── Step 4: Blast zone death check (void + side + top; inactive planes are ±inf) ──
			var deadIds = new List<ulong>();
			foreach (var kvp in _states)
			{
				var s = kvp.Value;
				if (!PredictCoupledInteractions && s.InteractionId != 0) continue;
				if (IsOutsideBlastZone(in s))
					deadIds.Add(kvp.Key);
			}
			foreach (var id in deadIds)
			{
				var d = Def(id);
				var oldState = _states[id];
				string boundary = oldState.PY < _blastLines.KillHeight ? "bottom"
					: oldState.PY > _blastLines.KillTop ? "top"
					: oldState.PX < _blastLines.KillMinX ? "minX"
					: oldState.PX > _blastLines.KillMaxX ? "maxX"
					: oldState.PZ < _blastLines.KillMinZ ? "minZ"
					: "maxZ";
				ulong lastHitEntityId = 0;
				uint lastHitTick = 0;
				byte lastHitSlot = 0;
				ulong killerEntityId = 0;
				if (_lastHitContexts.TryGetValue(id, out var lastHit))
				{
					lastHitEntityId = lastHit.attackerId;
					lastHitTick = lastHit.tick;
					lastHitSlot = lastHit.slot;
				}
				if (_mayhem == null && _lastHitCredits.TryGetValue(id, out var credit)
				    && _tick - credit.tick <= KillCreditWindowTicks
				    && credit.attackerId != id
				    && _kos.ContainsKey(credit.attackerId))
					killerEntityId = credit.attackerId;
				LastTickDeaths.Add(new DeathEvent
				{
					Tick = _tick,
					EntityId = id,
					State = oldState,
					KillerEntityId = killerEntityId,
					LastHitEntityId = lastHitEntityId,
					LastHitTick = lastHitTick,
					LastHitSlot = lastHitSlot,
					Boundary = boundary,
				});
				if (PredictCoupledInteractions && oldState.InteractionId != 0)
					InterruptInteraction(ref oldState);

				RetireAbility(id, ref oldState, AbilityExit.Superseded);
				// A returning projectile in flight is back on his shoulder when he respawns.
				_spellResolver.RemoveReturningProjectiles(id);
                _spellResolver.RemoveContactBombs(id);
				_spellResolver.StopHomingAt(id);
                _spellResolver.ExplodeAttachedTo(id);
                ForgetPerches(id);
				if (killerEntityId != 0 && _kos[killerEntityId] < byte.MaxValue)
					_kos[killerEntityId]++;
				_lastHitCredits.Remove(id);
				_lastHitContexts.Remove(id);
				byte newDeaths = oldState.Deaths < byte.MaxValue ? (byte)(oldState.Deaths + 1) : oldState.Deaths;


				// Respawn point: per-entity override when set (MatchInstance/TrainingMatch
				// distribute spawn points), else deterministic by entity index so players
				// never all stack on SpawnPoints[0] (issue #37).
				float rpx, rpy, rpz, rpyaw;
				if (_respawnPositions.TryGetValue(id, out var rp))
				{
					rpx = rp.x; rpy = rp.y; rpz = rp.z; rpyaw = rp.yaw;
				}
				else
				{
					int idx = (int)((id - 1) % (ulong)Math.Max(1, _arena.SpawnPoints.Length));
					var sp = _arena.SpawnPoints[idx];
					rpx = sp.X; rpy = sp.Y; rpz = sp.Z; rpyaw = sp.Yaw;
				}

				var respawned = new CharacterState
				{
					PX = rpx, PY = rpy, PZ = rpz,
					FacingYaw = rpyaw,
					EntityId = id,
					State = ActionState.Idle,
					IsGrounded = true,
					IsFastFalling = false,
					JumpTakeoff = JumpTakeoff.Standing,
					SlideAttackCarryActive = false,
					AttackCarryFrom = AttackCarryOrigin.Slide,
					CrouchSettled = false,
					QueuedCrouchBrace = false,
					InPostHitstunFlight = false,
					JumpsLeft = d.Movement.MaxJumps, AirDodgesLeft = 1,
					Deaths = newDeaths, DamagePercent = 0,
					LastTerminalInteractionId = oldState.LastTerminalInteractionId,
					InteractionTerminalTick = oldState.InteractionTerminalTick,
					// She respawns in the form she was KO'd in (ADR-0051); cooldowns reset with the bank.
					Form = new FormState { Active = oldState.Form.Active },
					InteractionPhase = GrabThrow.IsRelease(oldState.InteractionPhase) ? oldState.InteractionPhase
                        : (byte)(oldState.LastTerminalInteractionId != 0
                            ? DefenseInteractionPhase.Terminal : DefenseInteractionPhase.None),
				};
				// The last terminal interaction identity survives KO to prevent a delayed
				// capture from resurrecting a pair after respawn.

				if (IsEliminated(respawned))
				{
					// Lost (0 stocks / rule) — spectator: frozen at its spawn point,
					// excluded from hurtboxes and physics (see BuildHurtboxList/
					// SimulateMovement), no input (see MatchInstance). Issue #37.
					respawned.InvincibilityTicks = 0;
				}
				else
				{
					// Still has stocks — respawn with brief invincibility (Smash convention).
					respawned.InvincibilityTicks = RespawnInvincibilityTicks;
				}
                Flying.Refuel(ref respawned, d);
                _states[id] = respawned;
			}
		}

        private void AdmitLowLandings(Dictionary<ulong, InputState> inputs)
        {
            foreach (var id in _lastTickTouchdowns)
            {
                var state = _states[id];
                var def = Def(id);
                state.IsFastFalling = false;
                if (state.IsGrounded && state.State != ActionState.JumpSquat)
                    Simulation.RefreshGroundResources(ref state, def.Movement);

                if (inputs.TryGetValue(id, out var input))
                {
                    bool wasLowState = state.State is ActionState.Crouching or ActionState.Sliding;
                    bool actionAccepted = _lastTickAcceptedActions.Contains(id);
                    bool abilityOwned = _activeAbilities.ContainsKey(id);
                    bool motionOwned = _activeAbilities.TryGetValue(id, out var landingAbility)
                        && landingAbility.OwnsVerticalMotion;
                    bool changed = Simulation.TryApplyGroundDown(
                        ref state, def.Movement, input, _downActionTuning,
                        actionAccepted, motionOwned, abilityOwned, true, out var reason);
                    if (input.Down || input.DownPressed || changed || wasLowState)
                        _lastTickDownAdmissions[id] = reason;
                }
                _states[id] = state;
            }
        }

		public void Tick(Dictionary<ulong, InputState> inputs)
		{
			_abilityTickPhaseComplete = false;
			try
			{
				LastTickDeaths.Clear();
				_lastTickDownAdmissions.Clear();
				_lastTickOrdinaryActionOpportunities.Clear();
				_lastTickAcceptedActions.Clear();
				_lastTickTouchdowns.Clear();
				_settledCrouchCandidates.Clear();
				_damagedThisTick.Clear();
				_hitstopFrozenThisTick.Clear();
				_commandReleases.Clear();
                if (_volley != null)
                {
                    _volley.BeginTick(_arcadeBall!, VersusServePosition);
                    if (_volley.State.Phase is SlopballVersusPhase.WaitingServe or SlopballVersusPhase.Ended)
                    { _tick++; _arcadeBall!.Hold(); LastTickHits.Clear(); _presentationEvents.Clear(); return; }
                }
                if (_slopball != null && !_slopball.BeginTick(_arcadeBall!))
                { LastTickHits.Clear(); _presentationEvents.Clear(); return; }
                if (_onlineCourse != null)
                {
                    if (!_onlineCourse.BeginTick())
                    { _tick++; LastTickHits.Clear(); _presentationEvents.Clear(); return; }
                    CaptureCoursePositions();
                }
                if (_course != null && !_course.BeginTick())
                { LastTickHits.Clear(); _presentationEvents.Clear(); return; }
                if (_slopball != null || _course != null) inputs = GateArcadeStartInput(inputs);
                ArcadePoint coursePrevious = default;
                if (_course != null)
                    foreach (var state in _states.Values) coursePrevious = new(state.PX, state.PY, state.PZ);
				_tick++;
                if (_mayhem != null && !_mayhem.BeginTick(_tick, _states, ResetMayhemFighters, _spellResolver))
                { LastTickHits.Clear(); _presentationEvents.Clear(); return; }
                if (_mayhem != null && inputs.Keys.Any(id => _mayhem.Inactive(id)))
                    inputs = inputs.Where(pair => !_mayhem.Inactive(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
				if (PredictCoupledInteractions && HasCoupledInteraction())
					UpdateCoupledInteractions(releaseDue: false, inputs);
				CaptureSettledCrouchCandidates();
                CaptureMayhemPositions();
				PreTickAbilities(inputs);

				ProcessTargetLock(inputs);

				AimHeldCutPops();
				SimulateMovement(inputs);
				ReleaseHeldCuts();
                ConstrainSlopballFighters();
                ConstrainMayhemFighters();
				// Grab contact is committed after ordinary damage has interrupted candidates.

				AdmitLowLandings(inputs);
				// Settlement is finalized after all movement, ability, lag, and pushbox work,
				// but before hurtbox construction and hit resolution.
				FinalizeSettledCrouch();
				SyncShellWindows();
				if (_volley?.State.Phase == SlopballVersusPhase.HeldServe) _arcadeBall!.Hold();
                else _arcadeBall?.Step(in _arena, _states, _defs);
                if (_slopball != null) _slopball.PlanMotion(_arcadeBall!);
                _volley?.PlanMotion(_arcadeBall!);

				var entityList = BuildHurtboxList();

				ResolveHits(entityList, inputs);
                if (_slopball != null) _slopball.FinishTick(_arcadeBall!);
                _volley?.FinishTick(_arcadeBall!);
				ResolveGrabAttempts();
				if (PredictCoupledInteractions && HasCoupledInteraction())
					UpdateCoupledInteractions(releaseDue: true, inputs);


				ProcessProjectileExplosions();

				CheckBlastDeaths();
                FinishCourseTick(coursePrevious);
                FinishOnlineCourseTick();
                _mayhem?.FinishTick(_tick, _states, LastTickDeaths.Select(d => d.EntityId), BenchMayhemFighter, _spellResolver);
				MirrorReturningProjectileSlots();
                if (_volley != null && _volley.Events.Any(e => e.Kind == SlopballEventKind.PointScored))
                    ResetVersusPoint();
			}
			finally
			{
				_abilityTickPhaseComplete = false;
			}
		}
	}
}
