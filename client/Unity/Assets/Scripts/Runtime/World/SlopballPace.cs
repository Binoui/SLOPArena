using System;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// A temporary trial of three Slopball paces from the 2026-10-18 feel pass: the baked profile,
    /// the same arcs played 1.3× faster, and a heavier "line drive" ball on lower arcs that stays
    /// on screen. The pause menu cycles it and restarts the attempt. Each pace is its own Shared
    /// profile, so its bests are kept apart. Once the maintainer picks one, bake it into the court
    /// (SlopArenaSlopballCommands) and remove this switch.
    /// </summary>
    public static class SlopballPace
    {
        public enum Id { Now, Brisk, LineDrive }

        public static Id Current { get; set; } = Id.Now;

        public static string Label(Id id) => id switch { Id.Brisk => "BRISK", Id.LineDrive => "LINE DRIVE", _ => "NOW" };

        public static Id Next(Id id) => (Id)(((int)id + 1) % 3);

        /// <summary>The court with the chosen pace's ball and serve profile; the baked one for Now or an invalid pace.</summary>
        public static ArenaDefinition Apply(ArenaDefinition arena, Id id)
        {
            if (id == Id.Now || arena.Arcade?.Ball is not ArcadeBallDefinition ball || arena.Arcade.Slopball is not SlopballDefinition rules)
                return arena;
            ArcadeBallDefinition paced;
            SlopballDefinition pacedRules;
            if (id == Id.Brisk)
            {
                // Time-scaled by k: gravity k², drag and launches k, flight ticks 1/k. The arcs keep their shape.
                const float k = 1.3f;
                paced = new ArcadeBallDefinition(ball.Spawn, ball.InitialVelocity, ball.Radius, ball.Gravity * k * k,
                    ball.Restitution, ball.LaunchScale * k, ball.LinearDrag * k);
                pacedRules = Rules(rules, Ticks(rules.EasyFlightTicks / k), Ticks(rules.HardFlightTicks / k), Ticks(rules.FlightJitterTicks / k));
            }
            else
            {
                // Line drive: a heavier ball with less drag on lower, quicker arcs (peaks about 3.3-4.7 m).
                paced = new ArcadeBallDefinition(ball.Spawn, ball.InitialVelocity, ball.Radius, 10f, ball.Restitution, 3f, .2f);
                pacedRules = Rules(rules, 84, 66, 14);
            }
            var stage = arena.Arcade;
            var copy = arena;
            copy.Arcade = new ArcadeStageDefinition(stage.ProfileId, stage.GameplayRevision, stage.AllowedPackageIds, paced, pacedRules);
            try { pacedRules.ValidateCourt(in copy); }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[Slopball] Pace {Label(id)} does not fit this court ({exception.Message}); using the baked profile.");
                return arena;
            }
            return copy;
        }

        private static ushort Ticks(float ticks) => (ushort)Mathf.RoundToInt(ticks);

        private static SlopballDefinition Rules(SlopballDefinition rules, ushort easy, ushort hard, ushort jitter)
            => new(rules.NetZ, rules.NetHalfThickness, rules.NetTop, rules.FloorY, rules.Seed, rules.ServeGapTicks,
                easy, hard, jitter, rules.RampReturns, rules.LandingHalfWidth, rules.LandingMinZ, rules.LandingMaxZ,
                rules.SpawnHalfWidth, rules.SpawnHalfDepth);
    }
}
