using System.Collections;
using System.IO;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Editor capture harness for issue #154. It fixes only scenario setup, then drives
    /// the same InputState -> TrainingMatch -> ServerSimulation path used by gameplay.
    /// </summary>
    public sealed class VisualBaselineCaptureController : MonoBehaviour
    {
        private const ulong PlayerId = 1;
        private const ulong NpcId = 100;

        [SerializeField] private TrainingMatch _match;
        [SerializeField] private string _arenaKey = "splash_deck";
        [SerializeField] private bool _runOnStart;

        private string OutputDirectory => Path.Combine(
            Directory.GetParent(Application.dataPath)!.Parent!.Parent!.FullName,
            "docs", "evidence", "visual-baseline", _arenaKey);

        private IEnumerator Start()
        {
            if (!_runOnStart)
                yield break;

            yield return null;
            Directory.CreateDirectory(OutputDirectory);
            yield return RunCapturePass();
            _match.ClearCaptureInput();
            Debug.Log($"[VisualBaseline] Capture complete: {OutputDirectory}");
        }

        [ContextMenu("Run Capture Pass")]
        private void RunFromInspector()
        {
            if (Application.isPlaying)
                StartCoroutine(RunCapturePass());
        }

        private IEnumerator RunCapturePass()
        {
            // Baselines capture exact simulation steps, not interpolated in-between frames.
            _match.SetRenderInterpolation(false);
            SetupNeutral();
            yield return SettleAndCapture("vb-01-neutral-spacing");

            SetupNeutral();
            _match.SetCaptureInput(new InputState { Jump = true, JumpHeld = true });
            yield return new WaitForFixedUpdate();
            _match.SetCaptureInput(default);
            yield return WaitUntilOrFail(
                () => !_match.GetCaptureState(PlayerId).IsGrounded, "airborne before air dodge", 120);
            _match.SetCaptureInput(new InputState { ShieldHeld = true, ShieldPressed = true });
            yield return WaitForState(ActionState.AirDodgeMovement, "vb-02-air-dodge");
            SetupNeutral();
            _match.SetCaptureInput(new InputState { Jump = true, JumpHeld = true });
            yield return new WaitForFixedUpdate();
            _match.SetCaptureInput(default);
            yield return WaitForApex("vb-03-jump");
            yield return WaitForLanding("vb-04-landing");

            float heavyDistance = 0f;
            yield return CaptureHit(AbilitySlots.Normal1, 0, "vb-05-light-hit", _ => { });
            yield return CaptureHit(AbilitySlots.Normal3, 0, "vb-06-heavy-hit", d => heavyDistance = d);
            yield return CaptureHit(AbilitySlots.Normal2, 60, "vb-07-launch", _ => { });

            SetupCombat(999, heavyDistance);
            yield return new WaitForFixedUpdate();
            byte deaths = _match.GetCaptureState(NpcId).Deaths;
            _match.SetCaptureInput(new InputState { ActiveSlot = AbilitySlots.Normal3 });
            yield return new WaitForFixedUpdate();
            _match.SetCaptureInput(default);
            yield return WaitUntilOrFail(
                () => _match.GetCaptureState(NpcId).Deaths != deaths,
                "KO", 360);
            Capture("vb-08-ko");

            yield return WaitUntilOrFail(
                () => {
                    var state = _match.GetCaptureState(NpcId);
                    return state.Deaths != deaths && state.InvincibilityTicks > 0;
                },
                "respawn", 360);
            Capture("vb-09-respawn");
            yield return null;
            _match.SetRenderInterpolation(true);
        }

        private IEnumerator CaptureHit(byte slot, ushort damage, string file, System.Action<float> connectedAt)
        {
            for (float distance = 0.2f; distance <= 2.2f; distance += 0.2f)
            {
                SetupCombat(damage, distance);
                yield return new WaitForFixedUpdate();
                _match.SetCaptureInput(new InputState { ActiveSlot = slot });
                yield return new WaitForFixedUpdate();
                _match.SetCaptureInput(default);
                for (int tick = 0; tick < 90; tick++)
                {
                    foreach (var hit in _match.CaptureLastTickHits)
                    {
                        if (hit.Blocked) continue;
                        connectedAt(distance);
                        Capture(file);
                        yield return null;
                        yield break;
                    }
                    yield return new WaitForFixedUpdate();
                }
            }
            throw new System.TimeoutException($"Visual baseline attack never connected: {file}");
        }
        private void SetupNeutral()
        {
            SetFighter(PlayerId, -2f, Mathf.PI * 0.5f, 0);
            SetFighter(NpcId, 2f, -Mathf.PI * 0.5f, 0);
            _match.SetCaptureInput(default);
        }

        private void SetupCombat(ushort npcDamage, float distance)
        {
            SetFighter(PlayerId, 0f, 0f, 0);
            SetFighter(NpcId, 0f, Mathf.PI, npcDamage);
            var player = _match.GetCaptureState(PlayerId);
            player.PZ = -distance * 0.5f;
            _match.SetCaptureState(PlayerId, player);
            var npc = _match.GetCaptureState(NpcId);
            npc.PZ = distance * 0.5f;
            _match.SetCaptureState(NpcId, npc);
            _match.SetCaptureInput(default);
        }

        private void SetFighter(ulong id, float x, float yaw, ushort damage)
        {
            var state = _match.GetCaptureState(id);
            state.PX = x;
            state.PZ = 0f;
            state.VX = state.VY = state.VZ = 0f;
            state.KVX = state.KVY = state.KVZ = 0f;
            state.FacingYaw = yaw;
            state.AimYaw = yaw;
            state.State = ActionState.Idle;
            state.StateTicks = 0;
            state.AttackSlot = 0;
            state.AttackElapsedTicks = 0;
            state.AnimLockTicks = 0;
            state.HitstopTicks = 0;
            state.HitstunTicks = 0;
            state.DamagePercent = damage;
            state.IsGrounded = true;
            state.InvincibilityTicks = 0;
            _match.SetCaptureState(id, state);
        }

        private IEnumerator SettleAndCapture(string file)
        {
            yield return new WaitForSeconds(0.25f);
            Capture(file);
            yield return null;
        }

        private IEnumerator WaitForState(ActionState state, string file)
        {
            yield return WaitUntilOrFail(() => _match.GetCaptureState(PlayerId).State == state, state.ToString(), 120);
            Capture(file);
            _match.SetCaptureInput(default);
            yield return null;
        }

        private IEnumerator WaitForApex(string file)
        {
            yield return WaitUntilOrFail(() => {
                var state = _match.GetCaptureState(PlayerId);
                return !state.IsGrounded && Mathf.Abs(state.VY) < 1f;
            }, "jump apex", 180);
            Capture(file);
            yield return null;
        }

        private IEnumerator WaitForLanding(string file)
        {
            yield return WaitUntilOrFail(() => _match.GetCaptureState(PlayerId).IsGrounded, "landing", 240);
            Capture(file);
            yield return null;
        }

        private static IEnumerator WaitUntilOrFail(System.Func<bool> predicate, string label, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                if (predicate())
                    yield break;
                yield return new WaitForFixedUpdate();
            }
            throw new System.TimeoutException($"Visual baseline scenario timed out: {label}");
        }

        private void Capture(string file)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, file + ".png"));
            Debug.Log($"[VisualBaseline] Captured {file}");
        }
    }
}
