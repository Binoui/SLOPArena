#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Shared.Telemetry;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;
using SlopArena.Client.UI;

namespace SlopArena.Client.Diagnostics
{
    /// <summary>
    /// Opt-in local log of one Training or Solo CPU match (Settings → Gameplay). It records
    /// every swing with its closest approach to the target, contacts, practice-control
    /// changes and one frame per simulation tick (rendered camera, device, camera input,
    /// fighters). Files stay under <see cref="Directory"/> and are read by
    /// <c>tools/SessionLogReport</c>. Presentation-only: it never feeds the simulation.
    /// </summary>
    public sealed class PracticeSessionLog : IDisposable
    {
        private const int FlushIntervalTicks = 300;

        private readonly MatchRecorder _recorder = new() { RecordPositionSamples = false };
        private readonly SessionLogWriter _writer;
        private readonly CharacterDefinition _fallbackDefinition;
        private readonly List<SessionFighterFrame> _fighters = new();
        private int _lastTick = -1;
        private bool _closed;

        public string FilePath { get; }

        public static string Directory => Path.Combine(Application.persistentDataPath, "SessionLogs");

        private PracticeSessionLog(string path, Stream stream, SessionHeader header, CharacterDefinition fallbackDefinition)
        {
            FilePath = path;
            _fallbackDefinition = fallbackDefinition;
            _writer = new SessionLogWriter(stream, header);
        }

        /// <summary>Create the session file, or return null after logging why it could not be created.</summary>
        public static PracticeSessionLog? TryStart(SessionHeader header, CharacterDefinition fallbackDefinition)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string player = header.Fighters.FirstOrDefault(x => x.Role == "player")?.Character ?? "player";
                string cpu = header.Fighters.FirstOrDefault(x => x.Role == "cpu")?.Character ?? "none";
                string name = FormattableString.Invariant(
                    $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Slug(header.Mode)}-{Slug(player)}-vs-{Slug(cpu)}{SessionLogFormat.FileSuffix}");
                string path = Path.Combine(Directory, name);
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                Debug.Log($"[SessionLog] Recording {path}");
                return new PracticeSessionLog(path, stream, header, fallbackDefinition);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[SessionLog] Not recording: {exception.Message}");
                return null;
            }
        }

        public static SessionFighter Fighter(ulong entityId, string role, MatchContentEntry entry,
            string? cpuDifficulty = null, string? cpuBehavior = null) => new()
        {
            EntityId = entityId,
            Role = role,
            Character = entry.Definition.Class.ToString(),
            PackageId = entry.Identity.PackageId,
            PackageVersion = entry.Identity.Version,
            CookedContentHash = entry.Identity.CookedContentHash,
            CapsuleHeight = entry.Definition.CapsuleHeight,
            CapsuleRadius = entry.Definition.CapsuleRadius,
            CpuDifficulty = cpuDifficulty,
            CpuBehavior = cpuBehavior,
        };

        public static void DescribePresentation(SessionHeader header, CameraMount? mount, UnityEngine.Camera? camera)
        {
            var settings = ClientSettingsService.Instance;
            header.AppVersion = Application.version;
            header.EngineVersion = Application.unityVersion;
            header.Platform = Application.platform.ToString();
            header.Editor = Application.isEditor;
            header.Input = new SessionInputSettings
            {
                CameraSensitivityPercent = settings.CameraSensitivity * 100f,
                InvertCameraHorizontal = settings.InvertCameraHorizontal,
                InvertCameraVertical = settings.InvertCameraVertical,
                StickDeadzonePercent = settings.StickDeadzone,
                AutoLockMode = settings.AutoLockMode.ToString(),
            };
            if (mount == null) return;
            var pitch = mount.PitchRangeDegrees;
            header.Camera = new SessionCameraSettings
            {
                VerticalFovDegrees = camera != null ? camera.fieldOfView : 0f,
                OrbitRadius = mount.GetOrbitRadius(),
                MinPitchDegrees = pitch.x,
                MaxPitchDegrees = pitch.y,
                StickYawRateDegrees = mount.StickYawRateDegrees,
                LockAssist = mount.AssistsLockedTarget,
                LockYawDeadZoneDegrees = mount.LockYawDeadZoneDegrees,
                LockYawSpeedDegrees = mount.LockYawSpeedDegrees,
            };
        }

        /// <summary>Call immediately before the local simulation consumes this tick's inputs.</summary>
        public void BeforeTick(ServerSimulation sim, int tick, IReadOnlyDictionary<ulong, InputState> inputs)
        {
            if (_closed) return;
            _recorder.RecordPresses(sim, tick, inputs, _fallbackDefinition);
        }

        /// <summary>Call immediately after the local simulation tick.</summary>
        public void AfterTick(ServerSimulation sim, int tick, IReadOnlyDictionary<ulong, InputState> inputs,
            CameraMount? mount, UnityEngine.Camera? camera)
        {
            if (_closed) return;
            try
            {
                _recorder.RecordTick(sim, tick, inputs, _fallbackDefinition);
                foreach (var hit in sim.LastTickHits)
                    _writer.WriteHit(new SessionHit
                    {
                        Tick = tick,
                        Attacker = hit.OwnerEntityId,
                        Target = hit.TargetEntityId,
                        AttackSlot = hit.AttackSlot,
                        ActivationId = hit.ActivationId,
                        Airborne = hit.Airborne,
                        Damage = hit.Damage,
                        Blocked = hit.Blocked,
                        HitX = hit.HitX, HitY = hit.HitY, HitZ = hit.HitZ,
                        HitstopTicks = hit.HitstopTicks,
                    });
                _writer.WriteFrame(BuildFrame(sim, tick, mount, camera));
                _writer.WriteSettledSwings(_recorder.Record.Swings, tick);
                _lastTick = tick;
                if (tick % FlushIntervalTicks == 0) _writer.Flush();
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[SessionLog] Stopped after a write failure: {exception.Message}");
                Abandon();
            }
        }

        public void LogEvent(int tick, string kind, string? detail = null)
        {
            if (_closed) return;
            try
            {
                _writer.WriteEvent(new SessionEvent { Tick = tick, Kind = kind, Detail = detail });
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[SessionLog] Stopped after a write failure: {exception.Message}");
                Abandon();
            }
        }

        /// <summary>Write the remaining swings and the end record, then close the file.</summary>
        public void Finish(string reason)
        {
            if (_closed) return;
            _closed = true;
            try
            {
                _writer.Complete(_recorder.Record.Swings, new SessionEnd
                {
                    EndedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Ticks = _lastTick + 1,
                    Reason = reason,
                });
                Debug.Log($"[SessionLog] Saved {FilePath} ({_lastTick + 1} ticks, {_recorder.Record.Swings.Count} swings)");
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[SessionLog] Could not finish {FilePath}: {exception.Message}");
            }
        }

        public void Dispose() => Finish("closed");

        private void Abandon()
        {
            _closed = true;
            try { _writer.Dispose(); }
            catch (IOException) { }
        }

        private SessionFrame BuildFrame(ServerSimulation sim, int tick, CameraMount? mount, UnityEngine.Camera? camera)
        {
            var frame = new SessionFrame
            {
                Tick = tick,
                Gamepad = HumanInputActions.LastUsedGamepad && Gamepad.current != null,
            };
            if (camera != null)
            {
                var pose = camera.transform;
                var position = pose.position;
                var rotation = pose.rotation;
                frame.CamX = position.x; frame.CamY = position.y; frame.CamZ = position.z;
                frame.CamQX = rotation.x; frame.CamQY = rotation.y; frame.CamQZ = rotation.z; frame.CamQW = rotation.w;
                frame.VerticalFov = camera.fieldOfView;
                frame.Aspect = camera.aspect;
            }
            if (mount != null)
            {
                var (mouse, stick, peak) = mount.TakeLookSample();
                frame.MouseLookDegrees = mouse;
                frame.StickLookDegrees = stick;
                frame.StickPeak = peak;
                frame.CameraMode = (byte)mount.Mode;
            }
            _fighters.Clear();
            foreach (var (id, state) in sim.GetAllStates())
                _fighters.Add(new SessionFighterFrame
                {
                    Id = id,
                    PX = state.PX, PY = state.PY, PZ = state.PZ,
                    VX = state.VX, VY = state.VY, VZ = state.VZ,
                    FacingYaw = state.FacingYaw,
                    Grounded = state.IsGrounded,
                    State = (byte)state.State,
                    LockOn = state.LockOn,
                    Target = state.TargetEntityId,
                    Damage = state.DamagePercent,
                    Hitstun = state.HitstunTicks,
                });
            _fighters.Sort((a, b) => a.Id.CompareTo(b.Id));
            frame.Fighters = _fighters.ToArray();
            return frame;
        }

        private static string Slug(string value)
        {
            var chars = value.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
            return chars.Length == 0 ? "x" : new string(chars);
        }
    }
}
