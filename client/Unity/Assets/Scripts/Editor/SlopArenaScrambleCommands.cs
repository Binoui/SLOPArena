#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class SlopArenaScrambleCommands
    {
        [CliCommand("sloparena.scramble.run", "Drive a local Scramble through bounded per-seat semantic inputs and freeze at its last completed tick.", MainThreadRequired = true, Tags = new[] { "arcade", "replay" })]
        public static async Task<object> Run(
            [CliArg("inputs", "JSON object of seat IDs to Training semantic step arrays; every seat must cover the same 1–600 ticks.", Required = true)] string inputs)
        {
            double deadline = EditorApplication.timeSinceStartup + 15;
            CharacterCourseMatch match;
            while ((match = Object.FindFirstObjectByType<CharacterCourseMatch>()) == null || !match.Ready)
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("Scramble did not become ready.");
                await Task.Delay(50);
            }
            if (!match.IsScramble || EditorApplication.isPaused || Time.timeScale <= 0 || match.ScrambleProbeInputActive || match.Simulation.OnlineCourse.Value.Ended)
                throw new InvalidOperationException("Run requires a ready, unpaused, unfinished Scramble without another input probe.");
            var plans = JObject.Parse(inputs).Properties().ToDictionary(p => ulong.Parse(p.Name),
                p => SlopArenaTrainingCommands.ParseSteps(p.Value.ToString(Formatting.None)));
            var ids = match.Simulation.CoursePlayers.Select(p => p.EntityId).ToArray();
            if (!plans.Keys.OrderBy(id => id).SequenceEqual(ids)) throw new ArgumentException("Supply exactly the live racer seats.");
            int duration = plans.First().Value.Sum(p => p.Ticks);
            if (duration is < 1 or > 600 || plans.Values.Any(p => p.Sum(s => s.Ticks) != duration))
                throw new ArgumentException("Every racer needs the same 1–600 ticks.");
            int completed = 0; bool waiting = match.ScrambleCountdown != 0;
            var completion = new TaskCompletionSource<bool>();
            var events = new List<object>(); var hits = new List<object>();
            deadline = EditorApplication.timeSinceStartup + 30;
            void Apply()
            {
                foreach (var plan in plans)
                {
                    int offset = completed; var step = plan.Value[0];
                    foreach (var candidate in plan.Value) { step = candidate; if (offset < step.Ticks) break; offset -= step.Ticks; }
                    short yaw = (short)(match.Simulation.GetState(plan.Key).FacingYaw * Mathf.Rad2Deg * 100);
                    match.SetScrambleProbeInput(plan.Key, waiting ? default(InputState) : SlopArenaTrainingCommands.BuildInput(step, offset == 0, yaw));
                }
            }
            void Tick(uint tick)
            {
                if (completion.Task.IsCompleted) return;
                if (waiting) { if (match.ScrambleCountdown == 0) { waiting = false; Apply(); } return; }
                foreach (var e in match.Simulation.LastTickOnlineCourseEvents)
                    events.Add(new { tick, e.EntityId, kind = e.Event.Kind.ToString(), e.Event.Index, e.Event.ElapsedTicks });
                foreach (var hit in match.Simulation.LastTickHits) hits.Add(new { tick, hit.OwnerEntityId, hit.TargetEntityId, hit.Damage });
                if (++completed == duration || match.Simulation.OnlineCourse.Value.Ended)
                { match.ProbeFrozen = true; completion.TrySetResult(true); }
                else Apply();
            }
            void Check()
            {
                if (match == null || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    completion.TrySetException(new InvalidOperationException("Scramble input run stopped or exceeded its wall deadline."));
            }
            match.ProbeTickCompleted += Tick; EditorApplication.update += Check;
            try
            {
                Apply(); match.ProbeFrozen = false; await completion.Task;
                return new { success = true, completedTicks = completed, events, hits, state = State(match) };
            }
            finally
            {
                EditorApplication.update -= Check;
                if (match != null)
                {
                    match.ProbeFrozen = true; match.ProbeTickCompleted -= Tick;
                    foreach (ulong id in ids) match.SetScrambleProbeInput(id, null);
                }
            }
        }
        [CliCommand("sloparena.scramble.status", "Read local Scramble state, points and Shared positions.", MainThreadRequired = true, Tags = new[] { "arcade", "replay" })]
        public static object Status()
        {
            var match = Object.FindFirstObjectByType<CharacterCourseMatch>();
            if (match == null || !match.Ready || !match.IsScramble) throw new InvalidOperationException("No ready local Scramble.");
            return new { success = true, state = State(match) };
        }
        private static object State(CharacterCourseMatch match) => new
        {
            tick = match.SimulationTick, countdown = match.ScrambleCountdown, course = match.Simulation.OnlineCourse,
            scramble = match.Simulation.Scramble, outcome = match.Simulation.EvaluateMatch(),
            players = match.Simulation.CoursePlayers.Select(p => new { p.EntityId, p.Points, p.Place, p.LeashTicks, p.Progress,
                course = p.Course, fighter = match.Simulation.GetState(p.EntityId) }).ToArray()
        };
    }
}
#endif
