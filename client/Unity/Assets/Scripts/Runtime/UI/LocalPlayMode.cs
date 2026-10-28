using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.UI
{
    public enum LocalPlayMode { Brawl, Slopball, CharacterCourse, MankiMayhem }
    public enum LocalSeatPolicy { FourSeats, OneSeat, HumanCourseSeats }

    public sealed class LocalPlayModeInfo
    {
        public LocalPlayMode Mode { get; }
        public string Title { get; }
        public string Rule { get; }
        public string Chips { get; }
        public string PosterClass { get; }
        public LocalSeatPolicy Seats { get; }
        public bool HasStageStep { get; }
        public bool CourseFirst => Mode == LocalPlayMode.CharacterCourse;
        public int TotalSteps => HasStageStep || CourseFirst ? 3 : 2;
        public string FighterAction { get; }
        public GameMode GameMode { get; }
        public MatchFormat Format { get; }
        public string Arena { get; }
        public string Scene { get; }

        internal LocalPlayModeInfo(LocalPlayMode mode, string title, string rule, string chips,
            string posterClass, LocalSeatPolicy seats, bool stage, string action, GameMode gameMode,
            MatchFormat format, string arena, string scene)
        {
            Mode = mode; Title = title; Rule = rule; Chips = chips; PosterClass = posterClass;
            Seats = seats; HasStageStep = stage; FighterAction = action; GameMode = gameMode;
            Format = format; Arena = arena; Scene = scene;
        }

        public void Apply()
        {
            MatchConfig.Mode = GameMode;
            MatchConfig.Format = Format;
            MatchConfig.IsHost = true;
            if (!string.IsNullOrEmpty(Arena)) MatchConfig.ArenaName = Arena;
        }

        public void Launch()
        {
            Apply();
            if (Format == MatchFormat.MankiMayhem)
                MatchConfig.MayhemSeed = (uint)Random.Range(1, int.MaxValue);
            SceneManager.LoadScene(Scene);
        }
    }

    public static class LocalPlayModes
    {
        public static readonly IReadOnlyList<LocalPlayModeInfo> All = new[]
        {
            new LocalPlayModeInfo(LocalPlayMode.Brawl, "BRAWL", "Knock everyone off. Last fighter standing wins.",
                "1–4 · CPUS · COUCH", "local-poster--brawl", LocalSeatPolicy.FourSeats, true, "SELECT STAGE",
                GameMode.Solo, MatchFormat.Stock, "", "Arena_Offline"),
            new LocalPlayModeInfo(LocalPlayMode.Slopball, "SLOPBALL", "Land the ball beyond the net. A miss resets your combo.",
                "1 PLAYER", "local-poster--slopball", LocalSeatPolicy.OneSeat, false, "START SLOPBALL",
                GameMode.Arcade, MatchFormat.Stock, World.SlopballMatch.ArenaKey, World.SlopballMatch.SceneName),
            new LocalPlayModeInfo(LocalPlayMode.CharacterCourse, "SLOP RALLY", "Solo time trial, or race friends for targets and finish points.",
                "1–4 · HUMAN SEATS · COUCH", "local-poster--course", LocalSeatPolicy.HumanCourseSeats, false, "START COURSE",
                GameMode.Arcade, MatchFormat.Stock, "", World.CharacterCourseMatch.SceneName),
            new LocalPlayModeInfo(LocalPlayMode.MankiMayhem, "MANKI MAYHEM", "Manki calls the round. Survive it to score.",
                "2–4 · CPUS · COUCH", "local-poster--mayhem", LocalSeatPolicy.FourSeats, false, "START MAYHEM",
                GameMode.Solo, MatchFormat.MankiMayhem, "manki_mayhem", "Arena_Offline")
        };

        public static LocalPlayModeInfo Current => Get(MatchConfig.LocalMode);
        public static LocalPlayModeInfo Get(LocalPlayMode mode) => All[(int)mode];

        public static void Open(LocalPlayMode mode)
        {
            MatchConfig.ClearActiveMatch();
            MatchConfig.LocalMode = mode;
            var info = Current;
            info.Apply();
            if (info.Seats == LocalSeatPolicy.HumanCourseSeats)
                for (int player = 2; player <= 4; player++)
                    if (MatchConfig.Seat(player).Kind == SeatKind.Cpu)
                    { MatchConfig.Seat(player).Kind = SeatKind.Open; MatchConfig.Seat(player).DeviceId = 0; }
            if (info.Format == MatchFormat.MankiMayhem)
            {
                var roster = new[] { CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bashie };
                for (int player = 2; player <= 4; player++)
                    if (MatchConfig.Seat(player).Kind == SeatKind.Open)
                        MatchConfig.Seat(player) = new SoloSeat { Kind = SeatKind.Cpu, Fighter = roster[player - 2], Difficulty = CpuDifficulty.Normal };
            }
            FrontendController.Show(info.CourseFirst ? FrontendPage.StageSelect : FrontendPage.FighterSelect);
        }
    }
}
