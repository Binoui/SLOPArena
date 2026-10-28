using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.World
{
    public sealed partial class CharacterCourseMatch
    {
        private sealed class LocalRacer
        {
            public ulong Id;
            public MatchContentEntry Entry;
            public PlayerRenderer Renderer;
            public InputController Input;
            public AimHandler Aim;
            public InputActionMap Map;
            public SpawnPoint Spawn;
        }
        private readonly List<LocalRacer> _racers = new();
        private readonly Dictionary<ulong, InputState> _scrambleProbeInputs = new();
        private bool _scrambleActive;
        private ushort _scrambleCountdown;
        private CourseScrambleView _scrambleView;
        private CourseLeaderCamera _leaderCamera;
        public bool IsScramble => _scrambleActive;
        public ushort ScrambleCountdown => _scrambleCountdown;
        public bool ScrambleProbeInputActive => _scrambleProbeInputs.Count != 0;
        public void SetScrambleProbeInput(ulong id, InputState? input)
        {
            if (!_scrambleActive || !_racers.Any(r => r.Id == id)) throw new ArgumentException("No such local racer.");
            if (input.HasValue) _scrambleProbeInputs[id] = input.Value;
            else { _scrambleProbeInputs.Remove(id); _racers.Single(r => r.Id == id).Input.RequireReleaseBeforeHumanInput(); }
        }

        private void StartScramble(MatchContentCatalog catalog, ArenaDefinition arena, MatchContentEntry player)
        {
            _scrambleActive = true; MatchConfig.Format = MatchFormat.CourseScramble;
            _arena = arena; _course = arena.Arcade.Course; _playerDef = player.Definition;
            _bridge = new LocalSimulationBridge(arena, new CourseMatchRule(CourseFormat.Scramble));
            if (_combatFeedback == null) _combatFeedback = GetComponent<CombatFeedback>();
            if (_combatFeedback == null) _combatFeedback = gameObject.AddComponent<CombatFeedback>();
            if (_projectileVFX == null) _projectileVFX = GetComponent<ProjectileVFXManager>();
            if (_projectileVFX == null) _projectileVFX = gameObject.AddComponent<ProjectileVFXManager>();
            _combatFeedback.SetSimulation(_bridge); _projectileVFX.SetSimulation(Simulation);
            var seats = Enumerable.Range(2, 3).Where(p => MatchConfig.Seat(p).Kind == SeatKind.LocalPlayer).ToArray();
            var used = new HashSet<InputDevice>();
            for (int i = 0; i <= seats.Length; i++)
            {
                int seat = i == 0 ? 1 : seats[i - 1];
                var entry = i == 0 ? player : catalog.Resolve(MatchConfig.Seat(seat).Fighter);
                if (entry == null) { Debug.LogError($"[Scramble] P{seat}'s fighter is unavailable."); enabled = false; return; }
                var renderer = i == 0 ? _playerRenderer : new GameObject($"Course P{seat}").AddComponent<PlayerRenderer>();
                if (!SetupRenderer(renderer, entry, arena, (ulong)seat, i == 0)) { enabled = false; return; }
                var input = i == 0 ? _inputController : gameObject.AddComponent<InputController>();
                InputActionMap map = null;
                if (i > 0)
                {
                    var pad = CouchPads.ForSeat(seat);
                    if (pad != null) { used.Add(pad); map = HumanInputActions.CreatePlayerMap(pad); input.UsePlayerMap(map); }
                    else { map = HumanInputActions.CreatePlayerMap(Array.Empty<InputDevice>()); map.Disable(); input.UsePlayerMap(map); }
                }
                var aim = i == 0 ? _aimHandler : renderer.gameObject.AddComponent<AimHandler>();
                _racers.Add(new LocalRacer { Id = (ulong)seat, Entry = entry, Renderer = renderer, Input = input, Aim = aim, Map = map,
                    Spawn = MatchFormat.CourseScramble.StartFor(arena, i, seats.Length + 1) });
            }
            var devices = new List<InputDevice>();
            if (Keyboard.current != null) devices.Add(Keyboard.current);
            if (Mouse.current != null) devices.Add(Mouse.current);
            foreach (var pad in Gamepad.all) if (!used.Contains(pad)) devices.Add(pad);
            HumanInputActions.RestrictToDevices(devices.ToArray());
            SpawnStageVisual(arena);
            RegisterScrambleRacers();
            SetupHUD(_playerDef, _racers.Skip(1).Select(r => new HUDManager.HudPlayer(r.Id, $"P{r.Id}", r.Entry.Definition.Class, true, playerNumber: (int)r.Id)).ToArray());
            SetupCamera();
            foreach (var racer in _racers)
            {
                racer.Aim?.Init(_cameraMount, ViewCamera, racer.Renderer.transform, racer.Entry.Definition.CapsuleHeight,
                    id => Simulation.TryGetCombatTargetPosition(id, out var position) ? new Vector3(position.X, position.Y, position.Z) : null);
                if (racer.Aim != null) { racer.Aim.SharedCamera = true; racer.Aim.ControllerAim = racer.Id != PlayerEntityId; }
                racer.Aim?.BindTargetPresentation(_hudManager.TargetingRoot);
            }
            _view = new CharacterCourseView(_hudManager.Document.rootVisualElement, Course(arena.Name), RestartScramble, ReturnToArcade, online: true);
            _presentation = new CharacterCoursePresentation(Resources.Load<CoursePresentationSet>($"{CoursePresentationSet.ResourcesFolder}/{arena.Name}"),
                _course, transform, _view, _hudManager, () => ViewCamera);
            _scrambleView = new CourseScrambleView(_hudManager.Document.rootVisualElement, _view, RestartScramble, ReturnToArcade);
            _leaderCamera = gameObject.AddComponent<CourseLeaderCamera>();
            _leaderCamera.Initialize(_cameraMount, () => Simulation, _racers.ToDictionary(r => r.Id, r => r.Renderer.transform), _course);
            _retryInput = new ArcadeRetryInput();
            BeginScramble(false);
        }

        private void RegisterScrambleRacers()
        {
            foreach (var racer in _racers)
            {
                var spawn = racer.Spawn; var def = racer.Entry.Definition;
                _bridge.RegisterEntity(racer.Id, def, new CharacterState { PX = spawn.X, PY = spawn.Y, PZ = spawn.Z,
                    FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw, IsGrounded = true, JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1 }, racer.Entry.BakedAnimation);
                _bridge.SetRespawnPosition(racer.Id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
                racer.Renderer.transform.SetPositionAndRotation(new(spawn.X, spawn.Y, spawn.Z), Quaternion.Euler(0, spawn.Yaw * Mathf.Rad2Deg, 0));
            }
        }
        private void BeginScramble(bool retry)
        {
            _scrambleCountdown = retry ? RetryCountdownTicks : ArcadeRecordCompatibility.CourseCountdownTicks;
            _countdownBeat = -1; _wentLive = false;
#if UNITY_EDITOR
            ProbeFrozen = FreezeOnStartForProbe;
#endif
        }
        private void UpdateScramble()
        {
            if (!Ready) return;
            if (_retryInput.Poll(!IsPaused && !ChatInputGate.SuppressShortcuts)) { RestartScramble(); return; }
            foreach (var racer in _racers)
                if (IsPaused || ProbeFrozen || _scrambleCountdown != 0 || Simulation.OnlineCourse.Value.Ended || Simulation.IsEntityInactive(racer.Id))
                    racer.Input.RequireReleaseBeforeHumanInput();
                else racer.Input.Poll();
            if (Simulation.OnlineCourse.Value.Ended && !IsPaused) _cameraMount?.SetMode(CameraMode.FreeCursor);
            _presentation.Update();
            _view.Tick(Time.unscaledDeltaTime, ViewCamera, false);
            _scrambleView.Present(Simulation, IsPaused);
        }
        private void TickScramble()
        {
            if (!Ready || IsPaused || ProbeFrozen) return;
            if (_scrambleCountdown > 0)
            {
                int beat = (_scrambleCountdown + 59) / 60;
                if (beat != _countdownBeat) { _countdownBeat = beat; _hudManager.ShowSoloCountdown(beat.ToString(), 1f); }
                if (--_scrambleCountdown == 0)
                { Simulation.BeginOnlineCourse(); _wentLive = true; _hudManager.ShowSoloCountdown("SLOP IT OUT", .8f); }
                ProbeTickCompleted?.Invoke(SimulationTick);
                return;
            }
            if (Simulation.OnlineCourse.Value.Ended) { ProbeTickCompleted?.Invoke(SimulationTick); return; }
            var inputs = new Dictionary<ulong, InputState>();
            foreach (var racer in _racers)
            {
                byte slot = racer.Input.ConsumePendingSlotPress();
                var state = Simulation.GetState(racer.Id);
                byte target = PickScrambleTarget(state);
                var aim = racer.Aim != null ? racer.Aim.Evaluate(state, slot, racer.Entry.Definition, racer.Input,
                    _bridge.GetActiveMove(racer.Id)?.Slot) : AimContext.None;
                inputs[racer.Id] = _scrambleProbeInputs.TryGetValue(racer.Id, out var probe) ? probe
                    : racer.Input.BuildInputState(_cameraMount, racer.Renderer.transform.eulerAngles.y, slot, aim, null, target, aimFromFacing: true).input;
            }
            _bridge.Tick(inputs);
            foreach (var racer in _racers) racer.Renderer.ApplyServerState(Simulation.GetState(racer.Id), _bridge.GetActiveMove(racer.Id)?.Slot);
            var leader = Simulation.Scramble.Value.LeaderEntityId;
            _presentation.AfterTick(Simulation, Simulation.GetState(leader == 0 ? PlayerEntityId : leader), Simulation.LastTickOnlineCourseEvents.Select(e => e.Event).ToArray());
            foreach (var racer in _racers)
            {
                var owner = Simulation.GetState(racer.Id); Transform selected = null; float radius = 0;
                for (int i = 0; i < _presentation.Targets.Count; i++)
                {
                    bool broken = Simulation.IsCourseTargetBroken(racer.Id, i);
                    _presentation.Targets[i].gameObject.SetActive(!broken);
                    if (!broken && owner.TargetEntityId == Simulation.CourseTargetEntityId(racer.Id, i))
                    { selected = _presentation.Targets[i]; radius = _course.Targets[i].Radius; }
                }
                var rival = _racers.FirstOrDefault(r => r.Id == owner.TargetEntityId);
                if (rival != null) { selected = rival.Renderer.transform; radius = rival.Entry.Definition.CapsuleRadius; }
                racer.Aim?.UpdateObjectTargetPresentation(owner, selected, radius, null);
            }
            _projectileVFX.OnTick(); _combatFeedback.OnTick(); _hudManager.Refresh();
            UpdateSwordTrailsFromResolver(Simulation.Resolver); PresentTimelineEvents();
            _scrambleView.Present(Simulation, IsPaused); ProbeTickCompleted?.Invoke(SimulationTick);
        }
        private byte PickScrambleTarget(CharacterState owner)
        {
            if (ViewCamera == null || Simulation.IsEntityInactive(owner.EntityId)) return 0;
            byte selected = 0; float best = float.MaxValue;
            void Consider(Vector3 p, byte alias)
            {
                var screen = ViewCamera.WorldToViewportPoint(p);
                float x = p.x - owner.PX, z = p.z - owner.PZ;
                float score = (screen.x - .5f) * (screen.x - .5f) + (screen.y - .5f) * (screen.y - .5f);
                if (screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1 || x * x + z * z > 400 || score >= best) return;
                best = score; selected = alias;
            }
            for (int i = 0; i < _presentation.Targets.Count; i++)
                if (!Simulation.IsCourseTargetBroken(owner.EntityId, i)) Consider(_presentation.Targets[i].position, Simulation.CourseTargetInputAlias(i));
            foreach (var racer in _racers)
                if (racer.Id != owner.EntityId && !Simulation.IsEntityInactive(racer.Id)) Consider(racer.Renderer.transform.position, (byte)racer.Id);
            return selected;
        }
        private void RestartScramble()
        {
            if (!Ready) return;
            _bridge.Restart(); RegisterScrambleRacers(); _projectileVFX.SetSimulation(Simulation);
            ResetSimulationPresentation(); _view.ResetForRetry(); _presentation.Reset(); _scrambleView.Reset();
            foreach (var racer in _racers) { racer.Renderer.ResetForFreshSimulation(); racer.Input.RequireReleaseBeforeHumanInput(); racer.Aim?.ResetPresentation(); }
            _cameraMount.SetMode(CameraMode.Normal); _leaderCamera.ResetView(); BeginScramble(true);
        }
        private void DisposeScramble()
        {
            if (!_scrambleActive) return;
            _scrambleView?.Dispose();
            foreach (var racer in _racers) { racer.Map?.Disable(); racer.Map?.Dispose(); racer.Aim?.ResetPresentation(); }
            HumanInputActions.RestrictToDevices(null);
        }
    }
}
