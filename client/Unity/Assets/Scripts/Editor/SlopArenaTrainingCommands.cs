#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    public static class SlopArenaTrainingCommands
    {
        internal const int MaxSequenceTicks = 600;
        private const double WallDeadlineSeconds = 30d;
        private static SequenceRun _activeRun;

        [CliCommand(
            "sloparena.training.run",
            "Mutate live Training gameplay with a bounded semantic input sequence and return actual tick/state/hit/event receipts; no rollback is performed.",
            MainThreadRequired = true,
            Tags = new[] { "training/simulation" })]
        public static async Task<SlopArenaTrainingRunResult> Run(
            [CliArg("steps", "JSON array of positive-tick steps. Optional fields: moveX/moveY, jumpHeld/jumpPressed, downHeld/downPressed, shieldHeld/shieldPressed, grabPressed, dashPressed, faceToCamera, toggleLock, retargetPressed, slot (1/3/4/A/E/R/F), isAiming, facingYaw, aimYaw, aimDistance, aimPitch, targetEntityId (decimal string), lockMode. Held values persist for each step; *Pressed fields and slot apply on its first tick only. Maximum 600 Shared ticks; mutates live Training state without rollback.", Required = true)]
            string steps)
        {
            RequireRunnableEditor();
            var parsed = ParseSteps(steps);
            var match = ResolveActiveTrainingMatch();
            if (!match.CaptureSequenceReady)
                throw new InvalidOperationException("Training match is not initialized, active, unpaused, and unfrozen.");
            if (_activeRun != null)
                throw new InvalidOperationException("A Training input sequence is already running.");
            if (match.CaptureInputOverrideActive)
                throw new InvalidOperationException("Training capture input is already owned by another capture operation.");

            var participantIds = new List<ulong> { MatchConfig.LocalEntityId };
            for (int index = 0; index < match.NpcCount; index++)
                participantIds.Add(match.GetNpcIdAt(index));
            ValidateTargets(parsed, new HashSet<ulong>(participantIds));

            var run = new SequenceRun(match, parsed, participantIds);
            return await run.Execute();
        }

        internal static List<TrainingSequenceStep> ParseSteps(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("steps must be a JSON array.", nameof(json));

            List<TrainingSequenceStep> steps;
            try
            {
                JArray input;
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    FloatParseHandling = FloatParseHandling.Double,
                    DateParseHandling = DateParseHandling.None
                })
                {
                    input = JArray.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read())
                        throw new JsonReaderException("Unexpected content after the steps array.");
                }
                foreach (var item in input)
                {
                    var stepObject = item as JObject;
                    if (stepObject == null)
                        throw new JsonReaderException("Every step must be an object.");
                    foreach (JProperty property in stepObject.Properties())
                    {
                        bool valid = property.Name switch
                        {
                            "ticks" or "facingYaw" or "aimYaw" or "aimDistance" or "aimPitch"
                                => property.Value.Type == JTokenType.Integer,
                            "moveX" or "moveY"
                                => property.Value.Type is JTokenType.Integer or JTokenType.Float,
                            "jumpPressed" or "jumpHeld" or "downPressed" or "downHeld"
                                or "shieldPressed" or "shieldHeld" or "grabPressed" or "dashPressed" or "faceToCamera"
                                or "toggleLock" or "retargetPressed" or "isAiming"
                                => property.Value.Type == JTokenType.Boolean,
                            "slot" or "targetEntityId" or "lockMode"
                                => property.Value.Type == JTokenType.String,
                            _ => false
                        };
                        if (!valid)
                            throw new JsonReaderException($"Step field '{property.Name}' has an unsupported name or JSON token type.");
                    }
                    if (stepObject.GetValue("ticks")?.Type != JTokenType.Integer)
                        throw new JsonReaderException("Every step must provide ticks as a JSON integer.");
                }
                steps = input.ToObject<List<TrainingSequenceStep>>(JsonSerializer.Create(
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("steps must be a valid JSON array containing only supported semantic input fields: " + exception.Message,
                    nameof(json), exception);
            }
            if (steps == null || steps.Count == 0)
                throw new ArgumentException("steps must contain at least one step.", nameof(json));
            if (steps.Count > MaxSequenceTicks)
                throw new ArgumentException($"steps may contain at most {MaxSequenceTicks} entries.", nameof(json));

            int totalTicks = 0;
            foreach (var step in steps)
            {
                if (step == null)
                    throw new ArgumentException("steps must not contain null entries.", nameof(json));
                if (step.Ticks <= 0)
                    throw new ArgumentException("Every step must request a positive tick count.", nameof(json));
                if (step.Ticks > MaxSequenceTicks - totalTicks)
                    throw new ArgumentException($"The total requested simulation duration must not exceed {MaxSequenceTicks} ticks.", nameof(json));
                totalTicks += step.Ticks;

                ValidateAxis(step.MoveX, "moveX");
                ValidateAxis(step.MoveY, "moveY");
                if (step.MoveX * step.MoveX + step.MoveY * step.MoveY > 1.0001f)
                    throw new ArgumentException("moveX/moveY must describe a direction with magnitude at most 1.", nameof(json));
                step.WireSlot = MapSlot(step.Slot);
                step.TargetEntityIdByte = ParseTargetByte(step.TargetEntityId);
                step.LockModeValue = ParseLockMode(step.LockMode);
                if (step.FacingYaw.HasValue && (step.FacingYaw.Value < -18000 || step.FacingYaw.Value > 18000))
                    throw new ArgumentException("facingYaw must be between -18000 and 18000 degrees × 100.", nameof(json));
                if (step.AimYaw.HasValue && (step.AimYaw.Value < -18000 || step.AimYaw.Value > 18000))
                    throw new ArgumentException("aimYaw must be between -18000 and 18000 degrees × 100.", nameof(json));
                if (step.AimPitch.HasValue && (step.AimPitch.Value < -9000 || step.AimPitch.Value > 9000))
                    throw new ArgumentException("aimPitch must be between -9000 and 9000 degrees × 100.", nameof(json));
                if (step.AimDistance.HasValue && (step.AimDistance.Value < 0 || step.AimDistance.Value > 6500))
                    throw new ArgumentException("aimDistance must be between 0 and 6500 centimeters.", nameof(json));
            }
            return steps;
        }

        internal static byte MapSlot(string slot)
        {
            if (slot == null) return AbilitySlots.None;
            if (!CanonicalSlotProjection.TryGet(false, slot, out var ground)
                || !CanonicalSlotProjection.TryGet(true, slot, out var air)
                || ground.Ordinal >= CanonicalSlotProjection.VariantSlotCount
                || air.Ordinal != ground.Ordinal + CanonicalSlotProjection.VariantSlotCount)
                throw new ArgumentException($"Unknown canonical slot label '{slot}'. Use 1, 3, 4, A, E, R, or F.");

            return CanonicalSlotProjection.ToWire(ground);
        }

        internal static bool HasConsumedStepTicks(int requestedTicks, int observedTicks)
            => requestedTicks > 0 && observedTicks == requestedTicks;

        internal static short ToWireYaw(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians))
                throw new ArgumentException("Facing yaw must be a finite radian value.", nameof(radians));
            float canonicalDegrees = Mathf.DeltaAngle(0f, radians * Mathf.Rad2Deg);
            return (short)Mathf.Clamp(Mathf.RoundToInt(canonicalDegrees * 100f), -18000, 18000);
        }

        internal static InputState BuildInput(TrainingSequenceStep step, bool firstTick, short fallbackFacingYaw = 0)
        {
            short facingYaw = (short)(step.FacingYaw ?? fallbackFacingYaw);
            return new InputState
            {
                MoveX = step.MoveX,
                MoveY = step.MoveY,
                Up = step.MoveY > 0.3f,
                Left = step.MoveX < -0.3f,
                Right = step.MoveX > 0.3f,
                Down = step.DownHeld,
                DownPressed = firstTick && step.DownPressed,
                Jump = firstTick && step.JumpPressed,
                JumpHeld = step.JumpHeld,
                ShieldHeld = step.ShieldHeld,
                ShieldPressed = firstTick && step.ShieldPressed,
                GrabPressed = firstTick && step.GrabPressed,
                DashPressed = firstTick && step.DashPressed,
                FaceToCamera = firstTick && step.FaceToCamera,
                ToggleLock = firstTick && step.ToggleLock,
                RetargetPressed = firstTick && step.RetargetPressed,
                ActiveSlot = firstTick ? step.WireSlot : AbilitySlots.None,
                IsAiming = step.IsAiming,
                FacingYaw = facingYaw,
                AimYaw = (short)(step.AimYaw ?? facingYaw),
                AimPitch = (short)(step.AimPitch ?? 0),
                AimDistance = (ushort)(step.AimDistance ?? 0),
                TargetEntityId = step.TargetEntityIdByte,
                LockMode = step.LockModeValue
            };
        }

        private static void ValidateAxis(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < -1f || value > 1f)
                throw new ArgumentException($"{name} must be a finite value between -1 and 1.");
        }

        private static byte ParseTargetByte(string targetEntityId)
        {
            if (targetEntityId == null) return 0;
            if (!ulong.TryParse(targetEntityId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
                || id > byte.MaxValue)
                throw new ArgumentException("targetEntityId must be a decimal string for an entity ID from 0 to 255.");
            return (byte)id;
        }

        private static TargetLockMode ParseLockMode(string mode)
        {
            if (mode == null) return TargetLockMode.Never;
            if (string.Equals(mode, "Never", StringComparison.OrdinalIgnoreCase)) return TargetLockMode.Never;
            if (string.Equals(mode, "Always", StringComparison.OrdinalIgnoreCase)) return TargetLockMode.Always;
            if (string.Equals(mode, "OnHit", StringComparison.OrdinalIgnoreCase)) return TargetLockMode.OnHit;
            throw new ArgumentException("lockMode must be Never, Always, or OnHit.");
        }

        private static void ValidateTargets(IEnumerable<TrainingSequenceStep> steps, HashSet<ulong> participants)
        {
            foreach (var step in steps)
            {
                if (step.TargetEntityIdByte != 0 && !participants.Contains(step.TargetEntityIdByte))
                    throw new ArgumentException($"targetEntityId '{step.TargetEntityId}' is not a registered Training player/NPC ID.");
            }
        }

        private static void RequireRunnableEditor()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("sloparena.training.run requires Play Mode.");
            if (EditorApplication.isPaused)
                throw new InvalidOperationException("sloparena.training.run refuses a paused Editor and will not step or resume it.");
            if (Time.timeScale <= 0f)
                throw new InvalidOperationException("sloparena.training.run refuses a frozen simulation and will not change Time.timeScale.");
            if (MatchConfig.Mode != GameMode.Training)
                throw new InvalidOperationException("sloparena.training.run is Training-only; Solo and PvP are unsupported.");
        }

        private static TrainingMatch ResolveActiveTrainingMatch()
        {
            var scene = SceneManager.GetActiveScene();
            var matches = UnityEngine.Object.FindObjectsByType<TrainingMatch>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(match => match != null && match.isActiveAndEnabled && match.gameObject.scene == scene)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected exactly one active TrainingMatch in scene '{scene.name}', found {matches.Length}.");
            return matches[0];
        }

        private sealed class SequenceRun
        {
            private readonly TrainingMatch _match;
            private readonly IReadOnlyList<TrainingSequenceStep> _steps;
            private readonly List<ulong> _participantIds;
            private readonly HashSet<ulong> _participantSet;
            private readonly object _owner = new object();
            private readonly TaskCompletionSource<SlopArenaTrainingRunResult> _completion =
                new TaskCompletionSource<SlopArenaTrainingRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly SlopArenaTrainingRunResult _result;
            private readonly Scene _scene;
            private readonly double _deadline;
            private uint _lastTick;
            private int _stepIndex;
            private int _ticksInStep;
            private SequenceStepReceipt _currentReceipt;
            private bool _subscribed;
            private bool _ownsInput;
            private bool _cleaned;

            public SequenceRun(TrainingMatch match, IReadOnlyList<TrainingSequenceStep> steps, List<ulong> participantIds)
            {
                _match = match;
                _steps = steps;
                _participantIds = participantIds;
                _participantSet = new HashSet<ulong>(participantIds);
                _scene = SceneManager.GetActiveScene();
                _deadline = EditorApplication.timeSinceStartup + WallDeadlineSeconds;
                _lastTick = match.CaptureTick;
                _currentReceipt = NewStepReceipt(0);
                _result = new SlopArenaTrainingRunResult
                {
                    Success = false,
                    MutatesTrainingState = true,
                    Scene = _scene.name,
                    StartTick = _lastTick,
                    EndTick = _lastTick,
                    Steps = new List<SequenceStepReceipt>()
                };
            }

            public async Task<SlopArenaTrainingRunResult> Execute()
            {
                if (_activeRun != null)
                    throw new InvalidOperationException("A Training input sequence is already running.");
                if (!_match.TryAcquireCaptureInputSequence(_owner))
                    throw new InvalidOperationException("Training capture input is already owned by another capture operation.");
                _ownsInput = true;
                _activeRun = this;
                try
                {
                    _match.CaptureSimulationTickCompleted += OnSimulationTickCompleted;
                    _subscribed = true;
                    EditorApplication.update += OnEditorUpdate;
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                    EditorApplication.pauseStateChanged += OnEditorPauseStateChanged;
                    SceneManager.activeSceneChanged += OnActiveSceneChanged;
                    SceneManager.sceneUnloaded += OnSceneUnloaded;
                    _match.SetCaptureSequenceInput(_owner, BuildInput(_steps[0], true, CurrentFacingYaw()));
                    return await _completion.Task;
                }
                catch (Exception exception)
                {
                    Fail("Sequence setup or observation failed: " + exception.Message);
                    return await _completion.Task;
                }
                finally
                {
                    Cleanup();
                }
            }

            private void OnSimulationTickCompleted(uint tick)
            {
                if (_completion.Task.IsCompleted) return;
                try
                {
                    if (tick != unchecked(_lastTick + 1u))
                    {
                        Fail($"Observed a non-consecutive Training simulation tick: expected {unchecked(_lastTick + 1u)}, received {tick}.");
                        return;
                    }

                    _lastTick = tick;
                    _ticksInStep++;
                    _result.CompletedTicks++;
                    _result.EndTick = tick;
                    _currentReceipt.StartTick = _ticksInStep == 1 ? tick : _currentReceipt.StartTick;
                    _currentReceipt.EndTick = tick;
                    _currentReceipt.ObservedTicks = _ticksInStep;
                    CopyTickObservations();
                    if (!CanContinue(out string failure))
                    {
                        if (HasConsumedStepTicks(_steps[_stepIndex].Ticks, _ticksInStep))
                        {
                            _currentReceipt.Completed = true;
                            _currentReceipt.Entities = CaptureEntities();
                            _result.Steps.Add(_currentReceipt);
                            _result.CompletedSteps++;
                            _ticksInStep = 0;
                        }
                        Fail(failure);
                        return;
                    }

                    if (HasConsumedStepTicks(_steps[_stepIndex].Ticks, _ticksInStep))
                    {
                        _currentReceipt.Completed = true;
                        _currentReceipt.Entities = CaptureEntities();
                        _result.Steps.Add(_currentReceipt);
                        _result.CompletedSteps++;
                        _stepIndex++;
                        _ticksInStep = 0;
                        if (_stepIndex == _steps.Count)
                        {
                            _result.Success = true;
                            _result.Failure = null;
                            Complete();
                            return;
                        }
                        _currentReceipt = NewStepReceipt(_stepIndex);
                        _match.SetCaptureSequenceInput(_owner, BuildInput(_steps[_stepIndex], true, CurrentFacingYaw()));
                    }
                    else
                    {
                        _match.SetCaptureSequenceInput(_owner, BuildInput(_steps[_stepIndex], false, CurrentFacingYaw()));
                    }
                }
                catch (Exception exception)
                {
                    Fail("Sequence observation failed: " + exception.Message);
                }
            }

            private void CopyTickObservations()
            {
                foreach (var hit in _match.CaptureLastTickHits)
                {
                    if (!_participantSet.Contains(hit.OwnerEntityId) && !_participantSet.Contains(hit.TargetEntityId))
                        continue;
                    _currentReceipt.Hits.Add(new SequenceHitReceipt
                    {
                        MatchTick = hit.MatchTick,
                        OwnerEntityId = DecimalId(hit.OwnerEntityId),
                        TargetEntityId = DecimalId(hit.TargetEntityId),
                        ActivationId = DecimalId(hit.ActivationId),
                        AttackSequence = hit.AttackSequence,
                        AttackSlot = hit.AttackSlot,
                        Blocked = hit.Blocked,
                        Damage = hit.Damage,
                        ImpactForce = hit.ImpactForce,
                        HitstopTicks = hit.HitstopTicks,
                        StunTicks = hit.StunTicks,
                        HitX = hit.HitX,
                        HitY = hit.HitY,
                        HitZ = hit.HitZ
                    });
                }
                foreach (var presentation in _match.CaptureLastTickPresentationEvents)
                {
                    if (!_participantSet.Contains(presentation.EntityId))
                        continue;
                    _currentReceipt.PresentationEvents.Add(new SequencePresentationReceipt
                    {
                        MatchTick = presentation.MatchTick,
                        EntityId = DecimalId(presentation.EntityId),
                        PresentationId = presentation.PresentationId,
                        OperationIndex = presentation.OperationIndex,
                        AttackSequence = presentation.AttackSequence,
                        Source = presentation.Source.ToString(),
                        WorldX = presentation.WorldX,
                        WorldY = presentation.WorldY,
                        WorldZ = presentation.WorldZ,
                        WorldYaw = presentation.WorldYaw
                    });
                }
            }

            private List<SequenceEntityReceipt> CaptureEntities()
            {
                var entities = new List<SequenceEntityReceipt>(_participantIds.Count);
                foreach (ulong id in _participantIds)
                {
                    var state = _match.GetCaptureState(id);
                    if (state.EntityId != id)
                        throw new InvalidOperationException($"Registered Training entity {DecimalId(id)} disappeared during the sequence.");
                    entities.Add(new SequenceEntityReceipt
                    {
                        EntityId = DecimalId(id),
                        X = state.PX,
                        Y = state.PY,
                        Z = state.PZ,
                        VelocityX = state.VX,
                        VelocityY = state.VY,
                        VelocityZ = state.VZ,
                        State = state.State.ToString(),
                        StateTicks = state.StateTicks,
                        MatchState = state.MatchState.ToString(),
                        Grounded = state.IsGrounded,
                        DamagePercent = state.DamagePercent,
                        Deaths = state.Deaths,
                        AttackSlot = state.AttackSlot,
                        BufferedSlot = state.BufferedSlot,
                        ComboStage = state.ComboStage,
                        AttackElapsedTicks = state.AttackElapsedTicks,
                        HitstunTicks = state.HitstunTicks,
                        HitstopTicks = state.HitstopTicks,
                        JumpsLeft = state.JumpsLeft,
                        AirDodgesLeft = state.AirDodgesLeft,
                        ChargeTicks = state.ChargeTicks,
                        TargetEntityId = DecimalId(state.TargetEntityId),
                        LockOn = state.LockOn,
                        InteractionId = DecimalId(state.InteractionId),
                        InteractionPartnerId = DecimalId(state.InteractionPartnerId)
                    });
                }
                return entities;
            }

            private SequenceStepReceipt NewStepReceipt(int index)
                => new SequenceStepReceipt
                {
                    Index = index,
                    RequestedTicks = _steps[index].Ticks,
                    Hits = new List<SequenceHitReceipt>(),
                    PresentationEvents = new List<SequencePresentationReceipt>()
                };

            private short CurrentFacingYaw()
                => ToWireYaw(_match.GetCaptureState(MatchConfig.LocalEntityId).FacingYaw);

            private void OnEditorUpdate()
            {
                try
                {
                    if (!CanContinue(out string failure))
                        Fail(failure);
                }
                catch (Exception exception)
                {
                    Fail("Sequence context check failed: " + exception.Message);
                }
            }

            private bool CanContinue(out string failure)
            {
                if (EditorApplication.timeSinceStartup >= _deadline)
                {
                    failure = $"Training sequence exceeded its {WallDeadlineSeconds:0}-second wall-clock deadline.";
                    return false;
                }
                if (!EditorApplication.isPlaying)
                {
                    failure = "Play Mode ended while the Training sequence was running.";
                    return false;
                }
                if (EditorApplication.isPaused || _match == null || !_match.isActiveAndEnabled)
                {
                    failure = "Training sequence stopped because the Editor or Training match was paused or disabled.";
                    return false;
                }
                if (Time.timeScale <= 0f)
                {
                    failure = "Training sequence stopped because simulation timeScale became zero; it was not changed.";
                    return false;
                }
                if (MatchConfig.Mode != GameMode.Training)
                {
                    failure = "Training sequence stopped because the active mode changed away from Training.";
                    return false;
                }
                if (SceneManager.GetActiveScene().handle != _scene.handle
                    || _match.gameObject.scene.handle != _scene.handle)
                {
                    failure = "Training sequence stopped because the active scene changed.";
                    return false;
                }
                if (!_match.CaptureSequenceReady)
                {
                    failure = "Training sequence stopped because the match is no longer initialized or the in-match pause menu froze simulation.";
                    return false;
                }
                failure = null;
                return true;
            }

            private void OnPlayModeStateChanged(PlayModeStateChange state)
            {
                if (state is PlayModeStateChange.ExitingPlayMode or PlayModeStateChange.EnteredEditMode)
                    Fail("Play Mode ended while the Training sequence was running.");
            }

            private void OnEditorPauseStateChanged(PauseState state)
            {
                if (state == PauseState.Paused)
                    Fail("Training sequence stopped because the Editor was paused; it will not step or resume the session.");
            }

            private void OnActiveSceneChanged(Scene previous, Scene next)
            {
                if (next.handle != _scene.handle)
                    Fail("Training sequence stopped because the active scene changed.");
            }

            private void OnSceneUnloaded(Scene unloaded)
            {
                if (unloaded.handle == _scene.handle)
                    Fail("Training sequence stopped because its Training scene was unloaded.");
            }

            private void Fail(string failure)
            {
                if (_completion.Task.IsCompleted) return;
                _result.Success = false;
                _result.Failure = failure;
                if (_ticksInStep > 0)
                {
                    _currentReceipt.Completed = false;
                    try
                    {
                        _currentReceipt.Entities = CaptureEntities();
                        _currentReceipt.StateTick = _lastTick;
                    }
                    catch (Exception) { }
                    _result.Steps.Add(_currentReceipt);
                }
                Complete();
            }

            private void Complete()
            {
                Cleanup();
                _completion.TrySetResult(_result);
            }

            private void Cleanup()
            {
                if (_cleaned) return;
                _cleaned = true;
                if (_subscribed)
                {
                    _match.CaptureSimulationTickCompleted -= OnSimulationTickCompleted;
                    _subscribed = false;
                }
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.pauseStateChanged -= OnEditorPauseStateChanged;
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                if (_ownsInput)
                {
                    _match.ReleaseCaptureInputSequence(_owner);
                    _ownsInput = false;
                }
                if (ReferenceEquals(_activeRun, this))
                    _activeRun = null;
            }
        }

        private static string DecimalId(ulong id) => id.ToString(CultureInfo.InvariantCulture);
    }

    internal sealed class TrainingSequenceStep
    {
        [JsonProperty("ticks", Required = Required.Always)] public int Ticks { get; set; }
        [JsonProperty("moveX")] public float MoveX { get; set; }
        [JsonProperty("moveY")] public float MoveY { get; set; }
        [JsonProperty("jumpPressed")] public bool JumpPressed { get; set; }
        [JsonProperty("jumpHeld")] public bool JumpHeld { get; set; }
        [JsonProperty("downHeld")] public bool DownHeld { get; set; }
        [JsonProperty("downPressed")] public bool DownPressed { get; set; }
        [JsonProperty("shieldHeld")] public bool ShieldHeld { get; set; }
        [JsonProperty("shieldPressed")] public bool ShieldPressed { get; set; }
        [JsonProperty("grabPressed")] public bool GrabPressed { get; set; }
        [JsonProperty("dashPressed")] public bool DashPressed { get; set; }
        [JsonProperty("faceToCamera")] public bool FaceToCamera { get; set; }
        [JsonProperty("toggleLock")] public bool ToggleLock { get; set; }
        [JsonProperty("retargetPressed")] public bool RetargetPressed { get; set; }
        [JsonProperty("slot")] public string Slot { get; set; }
        [JsonProperty("isAiming")] public bool IsAiming { get; set; }
        [JsonProperty("facingYaw")] public int? FacingYaw { get; set; }
        [JsonProperty("aimYaw")] public int? AimYaw { get; set; }
        [JsonProperty("aimDistance")] public int? AimDistance { get; set; }
        [JsonProperty("aimPitch")] public int? AimPitch { get; set; }
        [JsonProperty("targetEntityId")] public string TargetEntityId { get; set; }
        [JsonProperty("lockMode")] public string LockMode { get; set; }
        [JsonIgnore] public byte WireSlot { get; set; }
        [JsonIgnore] public byte TargetEntityIdByte { get; set; }
        [JsonIgnore] public TargetLockMode LockModeValue { get; set; }
    }

    public sealed class SlopArenaTrainingRunResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("failure", NullValueHandling = NullValueHandling.Ignore)] public string Failure { get; set; }
        [JsonProperty("mutatesTrainingState")] public bool MutatesTrainingState { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("startTick")] public uint StartTick { get; set; }
        [JsonProperty("endTick")] public uint EndTick { get; set; }
        [JsonProperty("completedTicks")] public int CompletedTicks { get; set; }
        [JsonProperty("completedSteps")] public int CompletedSteps { get; set; }
        [JsonProperty("steps")] public List<SequenceStepReceipt> Steps { get; set; }
    }

    public sealed class SequenceStepReceipt
    {
        [JsonProperty("index")] public int Index { get; set; }
        [JsonProperty("requestedTicks")] public int RequestedTicks { get; set; }
        [JsonProperty("observedTicks")] public int ObservedTicks { get; set; }
        [JsonProperty("startTick")] public uint StartTick { get; set; }
        [JsonProperty("endTick")] public uint EndTick { get; set; }
        [JsonProperty("stateTick", NullValueHandling = NullValueHandling.Ignore)] public uint? StateTick { get; set; }
        [JsonProperty("completed")] public bool Completed { get; set; }
        [JsonProperty("entities")] public List<SequenceEntityReceipt> Entities { get; set; }
        [JsonProperty("hits")] public List<SequenceHitReceipt> Hits { get; set; }
        [JsonProperty("presentationEvents")] public List<SequencePresentationReceipt> PresentationEvents { get; set; }
    }

    public sealed class SequenceEntityReceipt
    {
        [JsonProperty("entityId")] public string EntityId { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("z")] public float Z { get; set; }
        [JsonProperty("velocityX")] public float VelocityX { get; set; }
        [JsonProperty("velocityY")] public float VelocityY { get; set; }
        [JsonProperty("velocityZ")] public float VelocityZ { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("stateTicks")] public ushort StateTicks { get; set; }
        [JsonProperty("matchState")] public string MatchState { get; set; }
        [JsonProperty("grounded")] public bool Grounded { get; set; }
        [JsonProperty("damagePercent")] public ushort DamagePercent { get; set; }
        [JsonProperty("deaths")] public byte Deaths { get; set; }
        [JsonProperty("attackSlot")] public byte AttackSlot { get; set; }
        [JsonProperty("bufferedSlot")] public byte BufferedSlot { get; set; }
        [JsonProperty("comboStage")] public byte ComboStage { get; set; }
        [JsonProperty("attackElapsedTicks")] public ushort AttackElapsedTicks { get; set; }
        [JsonProperty("hitstunTicks")] public ushort HitstunTicks { get; set; }
        [JsonProperty("hitstopTicks")] public ushort HitstopTicks { get; set; }
        [JsonProperty("jumpsLeft")] public byte JumpsLeft { get; set; }
        [JsonProperty("airDodgesLeft")] public byte AirDodgesLeft { get; set; }
        [JsonProperty("chargeTicks")] public ushort ChargeTicks { get; set; }
        [JsonProperty("targetEntityId")] public string TargetEntityId { get; set; }
        [JsonProperty("lockOn")] public bool LockOn { get; set; }
        [JsonProperty("interactionId")] public string InteractionId { get; set; }
        [JsonProperty("interactionPartnerId")] public string InteractionPartnerId { get; set; }
    }

    public sealed class SequenceHitReceipt
    {
        [JsonProperty("matchTick")] public uint MatchTick { get; set; }
        [JsonProperty("ownerEntityId")] public string OwnerEntityId { get; set; }
        [JsonProperty("targetEntityId")] public string TargetEntityId { get; set; }
        [JsonProperty("activationId")] public string ActivationId { get; set; }
        [JsonProperty("attackSequence")] public byte AttackSequence { get; set; }
        [JsonProperty("attackSlot")] public byte AttackSlot { get; set; }
        [JsonProperty("blocked")] public bool Blocked { get; set; }
        [JsonProperty("damage")] public float Damage { get; set; }
        [JsonProperty("impactForce")] public float ImpactForce { get; set; }
        [JsonProperty("hitstopTicks")] public ushort HitstopTicks { get; set; }
        [JsonProperty("stunTicks")] public ushort StunTicks { get; set; }
        [JsonProperty("hitX")] public float HitX { get; set; }
        [JsonProperty("hitY")] public float HitY { get; set; }
        [JsonProperty("hitZ")] public float HitZ { get; set; }
    }

    public sealed class SequencePresentationReceipt
    {
        [JsonProperty("matchTick")] public uint MatchTick { get; set; }
        [JsonProperty("entityId")] public string EntityId { get; set; }
        [JsonProperty("presentationId")] public string PresentationId { get; set; }
        [JsonProperty("operationIndex")] public int OperationIndex { get; set; }
        [JsonProperty("attackSequence")] public byte AttackSequence { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("worldX")] public float WorldX { get; set; }
        [JsonProperty("worldY")] public float WorldY { get; set; }
        [JsonProperty("worldZ")] public float WorldZ { get; set; }
        [JsonProperty("worldYaw")] public float WorldYaw { get; set; }
    }
}
#endif
