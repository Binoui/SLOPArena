#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SlopArena.Client.Camera;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace SlopArena.EditorTools
{
    public sealed class TakePlan
    {
        public string MoveId = "";
        public int Ticks, SettleTicks = 30;
        public PlayerDrive Player;
        public Func<int, SlopArena.Shared.InputState> Dummy;
        public float FacingRadians, GameplayPitch = 35;
        public float ViewDistance = 8, ViewFov = 40, ViewLook = 0.3f, FrameAhead;
        public IReadOnlyList<(string Name, float Yaw, float Pitch)> Views;
        public FrameSize Size;
        public int Quality = 88;
        public bool CaptureHitboxes;
        public string OutputDirectory = "";
    }

    public abstract class PlayerDrive { private protected PlayerDrive() { } }
    public sealed class SemanticDrive : PlayerDrive { public Func<int, SlopArena.Shared.InputState> Input; }
    public sealed class PadDrive : PlayerDrive { public IReadOnlyList<GamepadState> Frames; }

    public sealed class TickRecord
    {
        public int Frame;
        public uint MatchTick;
        public CharacterState Fighter, Dummy;
        public bool GroundedBefore;
        public string ObservedSlot;
        public SpellResolver.HitResult[] Hits;
        public TimelinePresentationEvent[] Cues;
        public Hitbox[] CheckedHitboxes = Array.Empty<Hitbox>();
        public readonly Dictionary<string, float[]> WorldToClip = new();
        public readonly Dictionary<string, RectInt> Boxes = new();
        public readonly Dictionary<string, uint> ViewTicks = new();
    }

    public sealed class TakeResult
    {
        public bool Success;
        public string Failure;
        public int FramesWritten, EditorPausesResumed;
        public readonly List<TickRecord> Records = new();
    }

    [DefaultExecutionOrder(-100)]
    public sealed class MoveTakeRig : MonoBehaviour
    {
        private TrainingMatch _match;
        private CameraMount _mount;
        private TakePlan _plan;
        private readonly List<CaptureView> _views = new();
        private HashSet<int> _existingRenderers;
        private readonly object _owner = new();
        private int _tick, _pending = -1;
        private bool _ownsInput, _ownsStepping;
        private StreamWriter _frames;
        private float _yaw, _pitch;
        private bool _interpolation;
        private VirtualPadTimeline _pad;
        private Func<uint, SlopArena.Shared.InputState> _dummyInput;
        private readonly Dictionary<string, (int Frame, uint Tick)> _rendered = new();

        public bool Done { get; private set; }
        public TakeResult Result { get; } = new();

        public static MoveTakeRig Begin(TrainingMatch match, CameraMount mount, UnityEngine.Camera template, TakePlan plan)
        {
            var host = new GameObject("Move Take (temporary)");
            host.SetActive(false);
            var rig = host.AddComponent<MoveTakeRig>();
            rig._match = match; rig._mount = mount; rig._plan = plan;
            rig._tick = -plan.SettleTicks;
            rig._yaw = mount.GetCameraYawDeg(); rig._pitch = mount.GetCameraPitchDeg();
            rig._interpolation = match.RenderInterpolationEnabled;
            try
            {
                if (match.CaptureTargetInput != null) throw new InvalidOperationException("Training target input is already owned.");
                if (!match.TryAcquireCaptureInputSequence(rig._owner))
                    throw new InvalidOperationException("Training capture input is already owned.");
                rig._ownsInput = true;
                if (!match.TryAcquireCaptureStepping(rig._owner))
                    throw new InvalidOperationException("Training capture stepping is already owned.");
                rig._ownsStepping = true;
                match.SetRenderInterpolation(false);
                if (plan.Player is PadDrive pad) rig._pad = new VirtualPadTimeline(pad.Frames, () => rig._tick);
                if (plan.Dummy != null)
                {
                    rig._dummyInput = _ => rig._tick < 0 ? default : plan.Dummy(rig._tick);
                    match.CaptureTargetInput = rig._dummyInput;
                }
                Directory.CreateDirectory(plan.OutputDirectory);
                rig._frames = new StreamWriter(Path.Combine(plan.OutputDirectory, "frames.jsonl"));
                rig._existingRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Select(r => r.GetInstanceID()).ToHashSet();
                foreach (var spec in plan.Views)
                {
                    var view = CaptureView.Create(spec.Name, template, plan.Size);
                    view.Camera.enabled = false;
                    rig._views.Add(view);
                    Directory.CreateDirectory(Path.Combine(plan.OutputDirectory, spec.Name));
                }
                host.SetActive(true);
                RenderPipelineManager.beginCameraRendering += rig.OnBeginCamera;
                rig.StartCoroutine(rig.WriteFrames());
                return rig;
            }
            catch { DestroyImmediate(host); throw; }
        }

        private void Update()
        {
            if (Done || _pending >= 0) return;
            try
            {
                if (!_mount.isActiveAndEnabled) throw new InvalidOperationException("CameraMount was disabled; sight holes need its targets.");
                PinCamera();
                var input = _plan.Player is SemanticDrive semantic && _tick >= 0
                    ? semantic.Input(_tick) : new SlopArena.Shared.InputState
                    {
                        FacingYaw = WireYaw(_plan.FacingRadians), AimYaw = WireYaw(_plan.FacingRadians)
                    };
                if (_plan.Player is PadDrive && _tick >= 0) _match.ClearCaptureSequenceInput(_owner);
                else _match.SetCaptureSequenceInput(_owner, input);
                bool groundedBefore = _match.GetCaptureState(MatchConfig.LocalEntityId).IsGrounded;
                uint before = _match.CaptureTick;
                _match.CaptureStep(_owner);
                if (_match.CaptureTick != before + 1) throw new InvalidOperationException("Capture did not advance exactly one Shared tick.");
                if (_tick >= 0)
                {
                    Result.Records.Add(new TickRecord
                    {
                        Frame = _tick, MatchTick = _match.CaptureTick, GroundedBefore = groundedBefore,
                        ObservedSlot = _match.CaptureSimulation.GetActiveMove(MatchConfig.LocalEntityId)?.Slot.Id,
                        Fighter = _match.GetCaptureState(MatchConfig.LocalEntityId),
                        Dummy = _match.GetCaptureState(_match.SelectedNpcId),
                        Hits = _match.CaptureLastTickHits.ToArray(), Cues = _match.CaptureLastTickPresentationEvents.ToArray(),
                        CheckedHitboxes = _plan.CaptureHitboxes
                            ? _match.CaptureSimulation.Resolver.LastTickCheckedHitboxes.ToArray() : Array.Empty<Hitbox>()
                    });
                    _pending = _tick;
                }
                else _tick++;
            }
            catch (Exception error) { Abort(error.ToString()); }
        }

        private void PinCamera()
        {
            _mount.SetCameraYawDeg(_plan.FacingRadians * Mathf.Rad2Deg);
            _mount.SetCameraPitchDeg(_plan.GameplayPitch);
        }

        private void OnBeginCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (Done || _pending < 0) return;
            foreach (var view in _views)
                if (view.Camera == camera) _rendered[view.Name] = (Time.frameCount, _match.CaptureTick);
        }

        private void LateUpdate()
        {
            if (Done || _pending < 0) return;
            try
            {
                PinCamera();
                var state = Result.Records[_pending].Fighter;
                Vector3 look = new Vector3(state.PX, state.PY, state.PZ) + Vector3.up * _plan.ViewLook
                    + new Vector3(Mathf.Sin(_plan.FacingRadians), 0, Mathf.Cos(_plan.FacingRadians)) * _plan.FrameAhead;
                for (int i = 0; i < _views.Count; i++)
                {
                    var spec = _plan.Views[i];
                    float yaw = _plan.FacingRadians + spec.Yaw * Mathf.Deg2Rad, pitch = spec.Pitch * Mathf.Deg2Rad;
                    var forward = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), -Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                    _views[i].Place(look - forward * _plan.ViewDistance, look, _plan.ViewFov);
                    _views[i].Camera.enabled = true;
                }
            }
            catch (Exception error) { Abort(error.ToString()); }
        }

        private IEnumerator WriteFrames()
        {
            var end = new WaitForEndOfFrame();
            while (!Done)
            {
                yield return end;
                if (_pending < 0 || Done) continue;
                try
                {
                    var record = Result.Records[_pending];
                    var bounds = ActionBounds(record);
                    foreach (var view in _views)
                    {
                        if (!_rendered.TryGetValue(view.Name, out var rendered) || rendered.Frame != Time.frameCount
                            || rendered.Tick != record.MatchTick || _match.CaptureTick != record.MatchTick)
                            throw new InvalidOperationException("A view did not render this frame at the captured Shared tick.");
                        view.WriteJpg(Path.Combine(_plan.OutputDirectory, view.Name, $"{record.Frame:0000}.jpg"), _plan.Quality);
                        record.Boxes[view.Name] = view.Project(bounds);
                        if (_plan.CaptureHitboxes)
                        {
                            var matrix = view.Camera.projectionMatrix * view.Camera.worldToCameraMatrix;
                            record.WorldToClip[view.Name] = Enumerable.Range(0, 16).Select(i => matrix[i / 4, i % 4]).ToArray();
                        }
                        record.ViewTicks[view.Name] = rendered.Tick;
                        Result.FramesWritten++;
                    }
                    _frames.WriteLine(JsonConvert.SerializeObject(FrameReceipt(record)));
                    _frames.Flush();
                    _pending = -1;
                    _tick++;
                    if (_tick == _plan.Ticks) { Result.Success = true; Done = true; _frames.Dispose(); _frames = null; }
                }
                catch (Exception error) { Abort(error.ToString()); }
            }
        }

        private Bounds ActionBounds(TickRecord record)
        {
            var centre = new Vector3(record.Fighter.PX, record.Fighter.PY, record.Fighter.PZ);
            var bounds = new Bounds(centre, Vector3.one);
            bounds.Encapsulate(new Bounds(new Vector3(record.Dummy.PX, record.Dummy.PY, record.Dummy.PZ), Vector3.one));
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy
                    && (renderer.GetComponentInParent<PlayerRenderer>() != null || !_existingRenderers.Contains(renderer.GetInstanceID()))
                    && (renderer.bounds.center - centre).sqrMagnitude <= 144f)
                    bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        public static object FrameReceipt(TickRecord record) => new
        {
            frame = record.Frame, matchTick = record.MatchTick,
            groundedBefore = record.GroundedBefore, observedSlot = record.ObservedSlot, deltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime,
            fighter = StateReceipt(record.Fighter), dummy = StateReceipt(record.Dummy),
            hits = record.Hits.Select(hit => new { owner = hit.OwnerEntityId.ToString(), target = hit.TargetEntityId.ToString(),
                damage = hit.Damage, blocked = hit.Blocked, slot = hit.AttackSlot, matchTick = hit.MatchTick }),
            cues = record.Cues.Select(cue => new { id = cue.PresentationId, owner = cue.EntityId.ToString(),
                pos = new[] { cue.WorldX, cue.WorldY, cue.WorldZ } }),
            boxes = record.Boxes.ToDictionary(p => p.Key, p => new[] { p.Value.xMin, p.Value.yMin, p.Value.xMax, p.Value.yMax }),
            viewTicks = record.ViewTicks,
            checkedHitboxes = record.CheckedHitboxes.Select(box => new
            {
                owner = box.OwnerId.ToString(), emission = box.EmissionId.ToString(),
                shape = box.Shape.ToString(), radius = box.Radius,
                start = new[] { box.X, box.Y, box.Z }, end = new[] { box.EndX, box.EndY, box.EndZ },
                ageTicks = box.AgeTicks, durationTicks = box.DurationTicks,
                previousRadius = box.PreviousRadius, ringHalfWidth = box.RingHalfWidth, ringHalfHeight = box.RingHalfHeight,
                sweep = (box.Sweep ?? Array.Empty<HitboxPose>()).Select(pose => new
                { start = new[] { pose.X, pose.Y, pose.Z }, end = new[] { pose.EndX, pose.EndY, pose.EndZ } })
            }),
            worldToClip = record.WorldToClip
        };

        private static object StateReceipt(CharacterState state) => new
        {
            id = state.EntityId.ToString(), pos = new[] { state.PX, state.PY, state.PZ }, state = state.State.ToString(),
            slot = state.AttackSlot, elapsed = state.AttackElapsedTicks, grounded = state.IsGrounded,
            facing = state.FacingYaw * Mathf.Rad2Deg, form = state.Form.Active, damage = state.DamagePercent
        };

        private static short WireYaw(float radians) => (short)Mathf.RoundToInt(Mathf.DeltaAngle(0, radians * Mathf.Rad2Deg) * 100);

        public void Abort(string reason)
        {
            if (Done) return;
            Result.Failure = reason; Done = true;
            _frames?.Dispose(); _frames = null;
            foreach (var view in _views) if (view.Camera != null) view.Camera.enabled = false;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Abort("The move take was destroyed.");
            _pad?.Dispose();
            foreach (var view in _views) view.Dispose();
            if (_match != null)
            {
                if (_ownsInput) _match.ReleaseCaptureInputSequence(_owner);
                if (_ownsStepping) _match.ReleaseCaptureStepping(_owner);
                if (_dummyInput != null && _match.CaptureTargetInput == _dummyInput) _match.CaptureTargetInput = null;
                _match.SetRenderInterpolation(_interpolation);
            }
            if (_mount != null) { _mount.SetCameraYawDeg(_yaw); _mount.SetCameraPitchDeg(_pitch); }
        }
    }
}
#endif
