#nullable enable
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.Client.UI
{
    public enum GameMode { Training, Solo, PvP, Arcade, Tutorial }
    public enum MatchTransport { None, DevelopmentUdp, SteamP2P }

    public enum SeatKind : byte { Open, Cpu, LocalPlayer }

    public struct SoloSeat
    {
        public SeatKind Kind;
        public CharacterClass Fighter;
        public CpuDifficulty Difficulty;
        public int DeviceId;
    }

    public static class MatchConfig
    {
        public static GameMode Mode = GameMode.Training;
        public static LocalPlayMode LocalMode = LocalPlayMode.Brawl;
        public static MatchTransport Transport = MatchTransport.None;
        public static System.Guid? MatchId;

        public static SteamMatchDescriptor? SteamDescriptor;
        public static SlopArena.Shared.CharacterClass PlayerClass
            = SlopArena.Shared.CharacterClass.FightGuy;
        public static string PlayerPackageId = "fightguy";
        public static string ArenaName = "training";
        public static bool IsHost = true;
        public static string ServerIP = "127.0.0.1";
        public static int ServerPort = 9876;

        /// <summary>One opponent in the match: entity ID, selector, and stable package ID.</summary>
        public sealed class OpponentInfo
        {
            public ulong EntityId { get; }
            public SlopArena.Shared.CharacterClass Class { get; }
            public string PackageId { get; }

            public OpponentInfo(ulong entityId, SlopArena.Shared.CharacterClass @class, string packageId = "")
            {
                EntityId = entityId;
                Class = @class;
                PackageId = packageId ?? "";
            }
        }

        /// <summary>P2–P4 of a Solo match (ADR-0041). P1 is always the local player with PlayerClass.</summary>
        public static readonly SoloSeat[] SoloSeats = new SoloSeat[3];

        static MatchConfig() => ResetSoloSeats();

        public static ref SoloSeat Seat(int playerNumber)
        {
            if (playerNumber < 2 || playerNumber > 4)
                throw new System.ArgumentOutOfRangeException(nameof(playerNumber));
            return ref SoloSeats[playerNumber - 2];
        }

        public static int SoloOpponentCount
        {
            get
            {
                int count = 0;
                foreach (var seat in SoloSeats)
                    if (seat.Kind != SeatKind.Open) count++;
                return count;
            }
        }

        public static int JoinSoloSeat(int deviceId)
        {
            if (deviceId <= 0) return 0;
            for (int player = 2; player <= 4; player++)
                if (Seat(player).Kind == SeatKind.LocalPlayer && Seat(player).DeviceId == deviceId)
                    return player;
            for (int player = 2; player <= 4; player++)
            {
                ref var seat = ref Seat(player);
                if (seat.Kind == SeatKind.LocalPlayer) continue;
                if (seat.Fighter == CharacterClass.None) seat.Fighter = CharacterClass.FightGuy;
                seat.Kind = SeatKind.LocalPlayer;
                seat.DeviceId = deviceId;
                return player;
            }
            return 0;
        }

        public static void LeaveSoloSeat(int playerNumber)
        {
            ref var seat = ref Seat(playerNumber);
            seat.Kind = SoloOpponentCount > 1 ? SeatKind.Open : SeatKind.Cpu;
            seat.DeviceId = 0;
            seat.Difficulty = CpuDifficulty.Normal;
        }

        public static bool RemoveSoloCpu(int playerNumber)
        {
            ref var seat = ref Seat(playerNumber);
            if (seat.Kind != SeatKind.Cpu || SoloOpponentCount <= 1) return false;
            seat.Kind = SeatKind.Open;
            seat.DeviceId = 0;
            return true;
        }

        private static void ResetSoloSeats()
        {
            System.Array.Clear(SoloSeats, 0, SoloSeats.Length);
            Seat(2) = new SoloSeat
            {
                Kind = SeatKind.Cpu, Fighter = CharacterClass.FightGuy, Difficulty = CpuDifficulty.Normal,
            };
        }

        // Per-match entity IDs assigned by the master server at match start
        // (issue #35). The local player drives LocalEntityId; the opponents
        // come from Opponents (issue #36).
        public static ulong LocalEntityId = 1;

        /// <summary>All non-local players, in master-server roster order.</summary>
        public static List<OpponentInfo> Opponents = new();

        /// <summary>Stocks per player (ADR-0007, issue #38). Must match the game
        /// server's rule; the master server push carries it when present, else 3.</summary>
        public static int MaxStocks = SlopArena.Shared.MatchDefaults.DefaultMaxStocks;

        /// <summary>What the online Match plays (ADR-0040); Master's push carries it, else a stock fight.</summary>
        public static MatchFormat Format = MatchFormat.Stock;
        public static uint MayhemSeed = 41;
        public static bool IsLocalMayhem => Mode == GameMode.Solo && Format == MatchFormat.MankiMayhem;
        public static FriendlyFireMode FriendlyFire;
        public static bool BallOnly;
        public static Dictionary<ulong, SlopballCamp> Camps = new();
        public static Dictionary<ulong, byte> Colours = new();

        public static void ClearActiveMatch()
        {
            Format = MatchFormat.Stock;
            FriendlyFire = FriendlyFireMode.Off;
            BallOnly = false;
            Camps.Clear();
            Colours.Clear();
            Mode = GameMode.Training;
            Transport = MatchTransport.None;
            SteamDescriptor = null;
            MatchId = null;

            LocalEntityId = 1;
            Opponents.Clear();
            MaxStocks = SlopArena.Shared.MatchDefaults.DefaultMaxStocks;
        }

        public static void Reset()
        {
            ClearActiveMatch();
            LocalMode = LocalPlayMode.Brawl;
            PlayerPackageId = "fightguy";
            PlayerClass = SlopArena.Shared.CharacterClass.FightGuy;
            ResetSoloSeats();
            ArenaName = "training";
            IsHost = true;
            ServerIP = "127.0.0.1";
            ServerPort = 9876;
        }
    }
}
