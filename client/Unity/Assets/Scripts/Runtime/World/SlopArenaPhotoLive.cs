#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using SlopArena.Client.Camera;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Disposable burst capture for live Slopball, Character Course or Tutorial gameplay: each
    /// requested tick of the real match is read back from the composited Game View, HUD included,
    /// with the chat overlay hidden. An optional free camera replaces the gameplay orbit, or the
    /// orbit is held at a fixed yaw/pitch. Never included in a player build.
    /// </summary>
    public sealed class SlopArenaPhotoLive : MonoBehaviour
    {
        public ArcadeBallMatch? Ball;
        public CharacterCourseMatch? Course;
        public TutorialMatch? Tutorial;
        public UnityEngine.Camera RenderCamera = null!;
        public CameraMount? Mount;
        public float? HoldYaw, HoldPitch;
        public PhotoCamera? Free;
        /// <summary>For a free camera: follow "ball" or "fighter" instead of the fixed look point.</summary>
        public string FreeFollow = "";
        public int From, To = 120, Step = 1, Quality = 92;
        public string OutputDirectory = "";

        private uint _firstTick;
        private bool _started;
        private Texture2D? _pixels;
        private readonly StringBuilder _states = new();
        private Vector3 _look, _position;
        private bool _aimed;

        /// <summary>Ticks since Go: Slopball counts its own ticks, a course reports its elapsed ticks.</summary>
        public int Elapsed { get; private set; } = -1;
        public int FramesWritten { get; private set; }
        public string? Failure { get; private set; }
        public bool Done => Elapsed >= To;

        private ServerSimulation? Simulation => Ball != null ? Ball.Simulation : Course != null ? Course.Simulation : Tutorial != null ? Tutorial.Simulation : null;

        private void OnEnable()
        {
            if (Ball != null) Ball.ProbeTickCompleted += OnTick;
            if (Course != null) Course.ProbeTickCompleted += OnTick;
            if (Tutorial != null) Tutorial.ProbeTickCompleted += OnTick;
            StartCoroutine(Capture());
        }

        private void OnDisable()
        {
            if (Ball != null) Ball.ProbeTickCompleted -= OnTick;
            if (Course != null) Course.ProbeTickCompleted -= OnTick;
            if (Tutorial != null) Tutorial.ProbeTickCompleted -= OnTick;
            if (_pixels != null) Destroy(_pixels);
        }

        private void OnTick(uint tick)
        {
            var sim = Simulation;
            if (sim == null) return;
            if (sim.Course is CharacterCourseState course)
            {
                if (course.Phase is CharacterCoursePhase.Ready or CharacterCoursePhase.Countdown) return;
                Elapsed = (int)course.ElapsedTicks;
            }
            else
            {
                if (!_started)
                {
                    if (sim.Slopball is SlopballState countdown && countdown.Phase == SlopballPhase.Countdown) return;
                    _started = true;
                    _firstTick = tick;
                }
                Elapsed = (int)(tick - _firstTick);
            }
            var fighter = sim.GetState(1);
            _states.Append("{\"tick\":").Append(Elapsed)
                .Append(",\"fighter\":[").Append(Num(fighter.PX)).Append(',').Append(Num(fighter.PY)).Append(',').Append(Num(fighter.PZ))
                .Append("],\"state\":\"").Append(fighter.State).Append("\",\"slot\":").Append(fighter.AttackSlot)
                .Append(",\"grounded\":").Append(fighter.IsGrounded ? "true" : "false")
                .Append(",\"facing\":").Append(Num(fighter.FacingYaw * Mathf.Rad2Deg));
            if (sim.ArcadeBall is ArcadeBallState b)
                _states.Append(",\"ball\":[").Append(Num(b.Position.X)).Append(',').Append(Num(b.Position.Y)).Append(',').Append(Num(b.Position.Z))
                    .Append("],\"ballPercent\":").Append(Num(b.DamagePercent)).Append(",\"ballActive\":").Append(b.Active ? "true" : "false");
            if (sim.Slopball is SlopballState s)
                _states.Append(",\"phase\":\"").Append(s.Phase).Append("\",\"returns\":").Append(s.Returns);
            if (sim.Course is CharacterCourseState c)
                _states.Append(",\"phase\":\"").Append(c.Phase).Append('"');
            _states.Append("}\n");
        }

        private void LateUpdate()
        {
            HideChat();
            if (Free == null)
            {
                if (Mount != null && HoldYaw.HasValue) Mount.SetCameraYawDeg(HoldYaw.Value);
                if (Mount != null && HoldPitch.HasValue) Mount.SetCameraPitchDeg(HoldPitch.Value);
                return;
            }
            var sim = Simulation;
            if (RenderCamera == null || sim == null) return;
            Vector3 look = Free.LookAt, position = Free.Position;
            Vector3? followed = null;
            if (FreeFollow == "ball" && sim.ArcadeBall is ArcadeBallState ball && ball.Active)
                followed = new Vector3(ball.Position.X, ball.Position.Y, ball.Position.Z);
            else if (FreeFollow == "fighter")
            {
                var fighter = sim.GetState(1);
                followed = new Vector3(fighter.PX, fighter.PY, fighter.PZ);
            }
            if (followed.HasValue)
            {
                look = followed.Value + Free.LookOffset;
                if (Free.Move) position = look + (Free.Position - Free.LookAt);
            }
            float blend = _aimed ? Mathf.Clamp01(Free.Ease) : 1f;
            _aimed = true;
            _look = Vector3.Lerp(_look, look, blend);
            _position = Vector3.Lerp(_position, position, blend);
            RenderCamera.fieldOfView = Free.Fov;
            Vector3 forward = _look - _position;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            RenderCamera.transform.SetPositionAndRotation(_position,
                Quaternion.LookRotation(forward, Free.Up) * Quaternion.Euler(0f, 0f, Free.Roll));
        }

        /// <summary>Store shots show the match HUD only: chat attaches itself to the HUD document.</summary>
        public static void HideChat()
        {
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var root = document.rootVisualElement;
                if (root == null) continue;
                root.Query<VisualElement>("chat-root").ForEach(element => element.style.display = DisplayStyle.None);
                root.Query<VisualElement>("chat-region-hint").ForEach(element => element.style.display = DisplayStyle.None);
            }
        }

        private IEnumerator Capture()
        {
            var endOfFrame = new WaitForEndOfFrame();
            int captured = -1;
            while (!Done && Failure == null)
            {
                yield return endOfFrame;
                if (Elapsed < From || Elapsed == captured || (Elapsed - From) % Step != 0) continue;
                captured = Elapsed;
                try
                {
                    int width = Screen.width, height = Screen.height;
                    if (_pixels == null || _pixels.width != width || _pixels.height != height)
                    {
                        if (_pixels != null) Destroy(_pixels);
                        _pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                    }
                    _pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    Directory.CreateDirectory(OutputDirectory);
                    File.WriteAllBytes(Path.Combine(OutputDirectory, $"{Elapsed:0000}.jpg"), _pixels.EncodeToJPG(Quality));
                    FramesWritten++;
                }
                catch (Exception error)
                {
                    Failure = error.ToString();
                    Debug.LogException(error);
                }
            }
        }

        public void WriteStates()
        {
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, "states.jsonl"), _states.ToString());
        }

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
#endif
