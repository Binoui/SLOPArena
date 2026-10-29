#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using InputState = SlopArena.Shared.InputState;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SlopArena.EditorTools
{
    [InitializeOnLoad]
    public static class SlopArenaGamepadCommands
    {
        internal sealed class PadFrame
        {
            public int Frame;
            public GamepadState State;
        }

        private static VirtualPadTimeline? _pad;
        public static bool Active => _pad != null;
        private static TrainingMatch? _targetMatch;
        private static Func<uint, InputState>? _targetInput;

        static SlopArenaGamepadCommands()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RemovePad;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) RemovePad();
            };
        }

        [CliCommand("sloparena.input.pad-run", "Play a frame-indexed JSON controller timeline through the real Input System. Removes its virtual pad on completion or failure.",
            MainThreadRequired = true, Tags = new[] { "capture", "input" })]
        public static async Task<object> Run(
            [CliArg("timeline", "Repository-relative JSON file below tmp/ or scenarios/. Shape {frames:300,events:[{frame:0,leftStick:[0,0],down:[\"LeftShoulder\"]},{frame:30,up:[\"LeftShoulder\"]}]}.", Required = true)] string timeline,
            [CliArg("target-timeline", "Optional Training CPU semantic timeline. Shape {ticks:300,space:\"camera\",events:[{tick:0,move:[1,0]},{tick:40,jump:true,jumpHeld:true},{tick:65,airDodge:true}]}. Starts with the first pad frame and clears on completion.")] string? targetTimeline = null)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0f)
                throw new InvalidOperationException("Use unpaused, running Play Mode.");
            if (_pad != null || VirtualPadTimeline.Active) throw new InvalidOperationException("A pad timeline is already running.");
            var training = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            if (training != null && training.CaptureInputSequenceOwned)
                throw new InvalidOperationException("Training input is already owned.");
            if (InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                throw new InvalidOperationException("The pad timeline requires the normal Dynamic Input System update mode.");
            var plan = Parse(ReadTimeline(timeline), out int duration);
            TrainingMatch? match = null;
            InputState[]? targetPlan = null;
            if (targetTimeline != null)
            {
                match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
                if (match == null || !match.CaptureSequenceReady || match.CaptureInputOverrideActive || match.CaptureTargetInput != null)
                    throw new InvalidOperationException("A ready, unowned Training duel is required for target input.");
                var mount = UnityEngine.Object.FindFirstObjectByType<CameraMount>();
                targetPlan = ParseTarget(ReadTimeline(targetTimeline), mount.GetCameraYawDeg() * Mathf.Deg2Rad,
                    match.GetCaptureState(match.SelectedNpcId).FacingYaw);
                if (targetPlan.Length != duration) throw new ArgumentException("Target ticks must equal pad frames.");
            }
            var expanded = Expand(plan, duration);
            int frame = 0, lastUnityFrame = -1;
            uint? startTick = null;
            double deadline = EditorApplication.timeSinceStartup + 180;
            var pad = new VirtualPadTimeline(expanded, () =>
            {
                if (lastUnityFrame == Time.frameCount) return frame - 1;
                lastUnityFrame = Time.frameCount;
                if (!startTick.HasValue && match != null && targetPlan != null)
                {
                    startTick = match.CaptureTick;
                    _targetMatch = match;
                    _targetInput = tick => tick - startTick.Value < targetPlan.Length
                        ? targetPlan[(int)(tick - startTick.Value)] : default;
                    match.CaptureTargetInput = _targetInput;
                }
                return frame++;
            });
            _pad = pad;
            try
            {
                while (frame < duration)
                {
                    if (_pad != pad || !EditorApplication.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0f)
                        throw new InvalidOperationException("Play Mode stopped, paused, or the pad timeline was cancelled.");
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Pad timeline exceeded 180 seconds.");
                    await Task.Delay(10);
                }
                // Let Update and the next simulation tick consume the final controller state before removal.
                int consumed = Time.frameCount;
                while (EditorApplication.isPlaying && !EditorApplication.isPaused && Time.frameCount <= consumed
                    && EditorApplication.timeSinceStartup < deadline) await Task.Delay(10);
                return new { success = true, frames = frame, events = plan.Count, targetStartTick = startTick,
                    targetTicks = targetPlan?.Length, lastUsedGamepad = HumanInputActions.LastUsedGamepad };
            }
            finally
            {
                RemovePad();
            }
        }

        [CliCommand("sloparena.input.pad-stop", "Remove the capture timeline's virtual controller. Safe when none is active.",
            MainThreadRequired = true, Tags = new[] { "capture", "input" })]
        public static object Stop()
        {
            RemovePad();
            return new { success = true, active = false };
        }

        private static void RemovePad()
        {
            ClearTarget();
            var pad = _pad;
            _pad = null;
            pad?.Dispose();
        }

        [CliCommand("sloparena.training.target-clear", "Clear this capture runner's target timeline. Safe when none is active.",
            MainThreadRequired = true, Tags = new[] { "capture", "training" })]
        public static object ClearTarget()
        {
            if (_targetMatch != null && _targetMatch.CaptureTargetInput == _targetInput)
                _targetMatch.CaptureTargetInput = null;
            _targetMatch = null;
            _targetInput = null;
            return new { success = true, active = false };
        }

        private static string ReadTimeline(string timeline)
        {
            string path = (timeline ?? "").Replace('\\', '/');
            if (Path.IsPathRooted(path) || path.Split('/').Any(x => x is "" or "." or "..")
                || !(path.StartsWith("tmp/", StringComparison.Ordinal) || path.StartsWith("scenarios/", StringComparison.Ordinal)))
                throw new ArgumentException("timeline must be repository-relative below tmp/ or scenarios/.");
            return File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", path)));
        }

        internal static InputState[] ParseTarget(string json, float cameraYaw, float facing)
        {
            var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Only(root, "ticks", "space", "events");
            int duration = Integer(root["ticks"], 1, 600, "ticks");
            string space = root["space"] == null ? "camera" : root["space"]?.Type == JTokenType.String
                ? (string)root["space"]! : throw new ArgumentException("space must be camera or world.");
            if (space is not ("camera" or "world")) throw new ArgumentException("space must be camera or world.");
            if (root["events"] is not JArray events || events.Count > 600)
                throw new ArgumentException("events must be an array of at most 600 entries.");
            int previous = -1;
            var patches = new Dictionary<int, JObject>();
            foreach (var token in events)
            {
                if (token is not JObject item) throw new ArgumentException("Each target event must be an object.");
                Only(item, "tick", "move", "jump", "jumpHeld", "airDodge", "shield", "fastFall");
                int tick = Integer(item["tick"], 0, duration - 1, "tick");
                if (tick <= previous) throw new ArgumentException("Target events must have strictly ascending ticks.");
                previous = tick;
                patches.Add(tick, item);
            }
            var result = new InputState[duration];
            var state = new InputState { FacingYaw = TargetYaw(facing), AimYaw = TargetYaw(facing) };
            for (int tick = 0; tick < duration; tick++)
            {
                state = state.WithoutEdges();
                if (patches.TryGetValue(tick, out var patch))
                {
                    if (patch["move"] != null)
                    {
                        var move = Stick(patch["move"]!);
                        float yaw = space == "camera" ? cameraYaw : 0f;
                        state.MoveX = move.x * Mathf.Cos(yaw) + move.y * Mathf.Sin(yaw);
                        state.MoveY = -move.x * Mathf.Sin(yaw) + move.y * Mathf.Cos(yaw);
                        state.Up = state.MoveY > 0.3f;
                        state.Left = state.MoveX < -0.3f;
                        state.Right = state.MoveX > 0.3f;
                        if (move.sqrMagnitude > 0)
                            state.FacingYaw = state.AimYaw = TargetYaw(Mathf.Atan2(state.MoveX, state.MoveY));
                    }
                    state.Jump = Boolean(patch, "jump", false);
                    state.JumpHeld = Boolean(patch, "jumpHeld", state.JumpHeld);
                    bool shield = Boolean(patch, "shield", state.ShieldHeld);
                    bool airDodge = Boolean(patch, "airDodge", false);
                    state.ShieldPressed = shield && !state.ShieldHeld || airDodge;
                    state.ShieldHeld = shield;
                    bool down = Boolean(patch, "fastFall", state.Down);
                    state.DownPressed = down && !state.Down;
                    state.Down = down;
                }
                result[tick] = state;
            }
            return result;
        }

        private static short TargetYaw(float radians) =>
            (short)Mathf.RoundToInt(Mathf.DeltaAngle(0, radians * Mathf.Rad2Deg) * 100);

        private static bool Boolean(JObject item, string name, bool fallback)
        {
            if (item[name] == null) return fallback;
            if (item[name]!.Type != JTokenType.Boolean) throw new ArgumentException(name + " must be boolean.");
            return (bool)item[name]!;
        }

        internal static List<PadFrame> Parse(string json, out int duration)
        {
            var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Only(root, "frames", "events");
            duration = Integer(root["frames"], 1, 600, "frames");
            if (root["events"] is not JArray events || events.Count > 1200) throw new ArgumentException("events must be an array of at most 1200 entries.");
            var result = new List<PadFrame>();
            var state = new GamepadState();
            int previous = -1;
            foreach (var token in events)
            {
                if (token is not JObject item) throw new ArgumentException("Each event must be an object.");
                Only(item, "frame", "leftStick", "rightStick", "down", "up", "leftTrigger", "rightTrigger");
                int frame = Integer(item["frame"], 0, duration - 1, "frame");
                if (frame < previous) throw new ArgumentException("Events must be in ascending frame order.");
                previous = frame;
                if (item["leftStick"] != null) state.leftStick = Stick(item["leftStick"]!);
                if (item["rightStick"] != null) state.rightStick = Stick(item["rightStick"]!);
                if (item["leftTrigger"] != null) state.leftTrigger = Number(item["leftTrigger"]!, 0, 1);
                if (item["rightTrigger"] != null) state.rightTrigger = Number(item["rightTrigger"]!, 0, 1);
                Buttons(item["down"], true, ref state);
                Buttons(item["up"], false, ref state);
                result.Add(new PadFrame { Frame = frame, State = state });
            }
            return result;
        }

        internal static GamepadState[] Expand(List<PadFrame> patches, int duration)
        {
            var frames = new GamepadState[duration];
            int index = 0;
            var state = new GamepadState();
            for (int frame = 0; frame < duration; frame++)
            {
                while (index < patches.Count && patches[index].Frame == frame) state = patches[index++].State;
                frames[frame] = state;
            }
            return frames;
        }

        private static void Buttons(JToken? token, bool down, ref GamepadState state)
        {
            if (token == null) return;
            if (token is not JArray buttons) throw new ArgumentException("down and up must be button-name arrays.");
            foreach (var button in buttons)
            {
                if (button.Type != JTokenType.String || !Enum.TryParse((string?)button, false, out GamepadButton parsed)
                    || !Enum.IsDefined(typeof(GamepadButton), parsed)) throw new ArgumentException("Unknown GamepadButton name.");
                state = state.WithButton(parsed, down);
            }
        }

        private static Vector2 Stick(JToken token)
        {
            if (token is not JArray pair || pair.Count != 2) throw new ArgumentException("A stick must have two numeric components in [-1,1].");
            var value = new Vector2(Number(pair[0], -1, 1), Number(pair[1], -1, 1));
            if (value.sqrMagnitude > 1.001f) throw new ArgumentException("Stick magnitude must be at most one.");
            return value;
        }

        private static int Integer(JToken? token, int min, int max, string name)
        {
            if (token?.Type != JTokenType.Integer || (long)token < min || (long)token > max)
                throw new ArgumentException($"{name} must be an integer in [{min},{max}].");
            return (int)token;
        }

        private static float Number(JToken token, float min, float max)
        {
            if (token.Type is not (JTokenType.Float or JTokenType.Integer)) throw new ArgumentException("Expected a number.");
            float value = (float)token;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new ArgumentException($"Number must be in [{min},{max}].");
            return value;
        }

        private static void Only(JObject item, params string[] names)
        {
            foreach (var property in item.Properties())
                if (!names.Contains(property.Name)) throw new ArgumentException("Unknown timeline field " + property.Name);
        }

        [CliCommand("sloparena.input.pad-test", "Check timeline holds, release edges and validation without creating a controller.",
            MainThreadRequired = true, Tags = new[] { "input", "testing" })]
        public static object Test()
        {
            var frames = Parse("{\"frames\":60,\"events\":[{\"frame\":0,\"down\":[\"LeftShoulder\"]},{\"frame\":5,\"down\":[\"South\"],\"leftStick\":[0.5,0]},{\"frame\":9,\"up\":[\"South\",\"LeftShoulder\"]}]}", out int duration);
            uint shoulder = 1u << (int)GamepadButton.LeftShoulder;
            uint south = 1u << (int)GamepadButton.South;
            if (duration != 60 || (frames[1].State.buttons & (shoulder | south)) != (shoulder | south)
                || frames[2].State.buttons != 0 || frames[2].State.leftStick != new Vector2(0.5f, 0))
                throw new InvalidOperationException("Timeline did not retain omitted controls or release the chord.");
            var invalid = new[]
            {
                "{\"frames\":0,\"events\":[]}", "{\"frames\":601,\"events\":[]}",
                "{\"frames\":60,\"events\":[{\"frame\":60}]}",
                "{\"frames\":60,\"events\":[{\"frame\":5},{\"frame\":4}]}",
                "{\"frames\":60,\"events\":[{\"frame\":0,\"down\":[\"NotAButton\"]}]}",
                "{\"frames\":60,\"events\":[{\"frame\":0,\"leftStick\":[1,1]}]}",
                "{\"frames\":60,\"events\":[{\"frame\":0,\"leftTrigger\":2}]}",
                "{\"frames\":60,\"events\":[{\"frame\":0,\"unknown\":true}]}",
                "{\"frames\":60,\"frames\":20,\"events\":[]}"
            };
            foreach (string json in invalid)
            {
                bool rejected = false;
                try { Parse(json, out _); }
                catch (Exception error) when (error is ArgumentException or Newtonsoft.Json.JsonException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Accepted an invalid timeline: " + json);
            }
            return new { success = true, rejected = invalid.Length, virtualPadActive = _pad != null };
        }

        [CliCommand("sloparena.training.target-test", "Check target movement coordinates, held inputs, press edges and rejected timelines without running Training.",
            MainThreadRequired = true, Tags = new[] { "training", "testing" })]
        public static object TestTarget()
        {
            var plan = ParseTarget("{\"ticks\":5,\"events\":[{\"tick\":0,\"move\":[0,1],\"jump\":true,\"jumpHeld\":true},{\"tick\":2,\"shield\":true},{\"tick\":4,\"jumpHeld\":false,\"shield\":false,\"airDodge\":true,\"fastFall\":true}]}", Mathf.PI / 2, 0);
            if (Mathf.Abs(plan[0].MoveX - 1) > 0.001f || Mathf.Abs(plan[0].MoveY) > 0.001f
                || !plan[0].Jump || plan[1].Jump || !plan[1].JumpHeld || !plan[2].ShieldPressed
                || plan[3].ShieldPressed || !plan[3].ShieldHeld || plan[4].ShieldHeld
                || !plan[4].ShieldPressed || plan[4].JumpHeld || !plan[4].DownPressed)
                throw new InvalidOperationException("Target timeline movement, holds or edge lifetime failed.");
            var world = ParseTarget("{\"ticks\":1,\"space\":\"world\",\"events\":[{\"tick\":0,\"move\":[0,1]}]}", Mathf.PI / 2, 0);
            if (world[0].MoveX != 0 || world[0].MoveY != 1)
                throw new InvalidOperationException("World movement changed with camera yaw.");
            var invalid = new[]
            {
                "{\"ticks\":0,\"events\":[]}", "{\"ticks\":601,\"events\":[]}",
                "{\"ticks\":2,\"events\":[{\"tick\":2}]}",
                "{\"ticks\":2,\"events\":[{\"tick\":0},{\"tick\":0}]}",
                "{\"ticks\":2,\"events\":[{\"tick\":1},{\"tick\":0}]}",
                "{\"ticks\":2,\"events\":[{\"tick\":0,\"jump\":1}]}",
                "{\"ticks\":2,\"events\":[{\"tick\":0,\"shield\":true,\"airDodge\":1}]}",
                "{\"ticks\":2,\"events\":[{\"tick\":0,\"move\":[1,1]}]}",
                "{\"ticks\":2,\"space\":\"screen\",\"events\":[]}",
                "{\"ticks\":2,\"events\":[{\"tick\":0,\"unknown\":true}]}",
                "{\"ticks\":2,\"ticks\":1,\"events\":[]}"
            };
            foreach (string json in invalid)
            {
                bool rejected = false;
                try { ParseTarget(json, 0, 0); }
                catch (Exception error) when (error is ArgumentException or Newtonsoft.Json.JsonException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Accepted an invalid target timeline: " + json);
            }
            return new { success = true, rejected = invalid.Length, targetTimelineActive = _targetInput != null };
        }

        [CliCommand("sloparena.training.arrange", "Reset a disposable Training duel and place its selected CPU in front of the gameplay camera. Does not change camera settings or inject player input.",
            MainThreadRequired = true, Tags = new[] { "capture", "training" })]
        public static async Task<object> Arrange(
            [CliArg("distance", "Initial horizontal separation in metres, 1–25.")] float distance = 8,
            [CliArg("behavior", "Idle, Attack, Shield or Avoid.")] string behavior = "Idle",
            [CliArg("damage", "Selected CPU starting damage percent, 0–300.")] int damage = 0,
            [CliArg("no-cooldowns", "Use Training's no-cooldowns option for the local player.")] bool noCooldowns = false,
            [CliArg("facing", "Explicit world facing in degrees; omitted keeps the camera-yaw default.")] float? facing = null,
            [CliArg("form", "Starting form 0 or 1; omitted preserves the current form.")] int? form = null)
        {
            if (!float.IsFinite(distance) || distance < 1 || distance > 25 || damage < 0 || damage > 300
                || !Enum.TryParse(behavior, false, out NpcAiMode mode) || !Enum.IsDefined(typeof(NpcAiMode), mode))
                throw new ArgumentException("Use distance 1–25 and a supported CPU behavior.");
            double deadline = EditorApplication.timeSinceStartup + 20;
            TrainingMatch? match;
            while ((match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>()) == null || !match.CaptureSequenceReady)
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException("No ready Training match.");
                await Task.Delay(30);
            }
            if (form.HasValue && form.Value is not (0 or 1)) throw new ArgumentException("form must be 0 or 1.");
            var mount = UnityEngine.Object.FindFirstObjectByType<CameraMount>();
            var arranged = TrainingArrangement.Apply(match, mount, new Arrangement(facing * Mathf.Deg2Rad, false,
                form.HasValue ? (byte?)form.Value : null, distance, mode, damage, noCooldowns));
            return new { success = true, distance, behavior, facing = arranged.FacingRadians * Mathf.Rad2Deg,
                form = arranged.Form, player = new[] { arranged.Player.x, arranged.Player.y, arranged.Player.z },
                target = new[] { arranged.Dummy.x, arranged.Dummy.y, arranged.Dummy.z } };
        }
    }
}
#endif
