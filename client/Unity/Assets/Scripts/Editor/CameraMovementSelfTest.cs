using System;
using System.Reflection;
using System.Threading.Tasks;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using Unity.Cinemachine;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SlopArena.EditorTools
{
    public static class CameraMovementSelfTest
    {
        [CliCommand("sloparena.camera.test-movement",
            "Check camera/fighter synchronization during real held-stick movement in a disposable live Training session.",
            MainThreadRequired = true, Tags = new[] { "camera", "testing" })]
        public static async Task<object> Run(
            [CliArg("verify", "Fail if the camera follows an older fighter pose; false returns diagnostic measurements.")] bool verify = true)
        {
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            var mount = UnityEngine.Object.FindFirstObjectByType<CameraMount>();
            var input = UnityEngine.Object.FindFirstObjectByType<InputController>();
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || match == null
                || !match.CaptureSequenceReady || match.CaptureInputOverrideActive || mount == null
                || input == null || input.GetMovement().sqrMagnitude > 0.01f)
                throw new InvalidOperationException("Use an initialized, unpaused disposable Training session with neutral human input and no input override.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var playerField = typeof(CameraMount).GetField("_player", flags);
            var offsetField = typeof(CameraMount).GetField("_framingOffset", flags);
            var lookOffsetField = typeof(CameraMount).GetField("_lookOffset", flags);
            var cm = mount.GetComponent<CinemachineCamera>();
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pad = InputSystem.AddDevice<Gamepad>("Camera Movement Regression");
            double deadline = EditorApplication.timeSinceStartup + 15d;
            int frames = 0, lastFrame = -1, movingFrames = 0;
            float maximumFollowError = 0f, maximumLookError = 0f, maximumFacingStep = 0f;
            float previousFacing = 0f;
            bool haveFacing = false;

            void Observe(CinemachineBrain brain)
            {
                if (completed.Task.IsCompleted || brain.OutputCamera != mount.RenderCamera
                    || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                frames++;
                if (frames == 20)
                    InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(-1f, 0f) });
                if (frames > 25 && input.GetMovement().sqrMagnitude > 0.1f)
                {
                    movingFrames++;
                    var player = (Transform)playerField.GetValue(mount);
                    var offset = (Vector3)offsetField.GetValue(mount);
                    var lookOffset = (Vector3)lookOffsetField.GetValue(mount);
                    maximumFollowError = Mathf.Max(maximumFollowError,
                        Vector3.Distance(cm.Follow.position, player.position + offset));
                    Vector3 expectedLook = cm.LookAt == cm.Follow
                        ? player.position + offset : player.position + lookOffset;
                    maximumLookError = Mathf.Max(maximumLookError, Vector3.Distance(cm.LookAt.position, expectedLook));
                    float facing = match.GetCaptureState(MatchConfig.LocalEntityId).FacingYaw * Mathf.Rad2Deg;
                    if (haveFacing && frames > 40)
                        maximumFacingStep = Mathf.Max(maximumFacingStep, Mathf.Abs(Mathf.DeltaAngle(previousFacing, facing)));
                    previousFacing = facing;
                    haveFacing = true;
                }
                if (frames >= 120) completed.TrySetResult(true);
            }

            void Watchdog()
            {
                if (!EditorApplication.isPlaying || match == null || EditorApplication.timeSinceStartup > deadline)
                    completed.TrySetException(new InvalidOperationException("The movement check did not finish within 15 seconds of live frames."));
            }

            CinemachineCore.CameraUpdatedEvent.AddListener(Observe);
            EditorApplication.update += Watchdog;
            try
            {
                await completed.Task;
                if (movingFrames < 30)
                    throw new InvalidOperationException($"The real held-stick input path was not exercised: {movingFrames} moving frames.");
                if (verify && Mathf.Max(maximumFollowError, maximumLookError) > 0.005f)
                    throw new InvalidOperationException(
                        $"{mount.Style} sampled an older fighter pose: follow error {maximumFollowError:F4} m, look error {maximumLookError:F4} m over {movingFrames} movement frames.");
                return new { success = true, style = mount.Style.ToString(), frames, movingFrames,
                    maximumFollowError, maximumLookError, maximumFacingStep };
            }
            finally
            {
                CinemachineCore.CameraUpdatedEvent.RemoveListener(Observe);
                EditorApplication.update -= Watchdog;
                InputSystem.RemoveDevice(pad);
            }
        }
    }
}
