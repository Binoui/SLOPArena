#nullable enable
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.Client.UI
{
    public enum GameMode { Training, Solo, PvP, Arcade, Tutorial }
    public enum MatchTransport { None, DevelopmentUdp, SteamP2P }

    public static class MatchConfig
    {
        public static GameMode Mode = GameMode.Training;
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

        public static SlopArena.Shared.CharacterClass SoloBotClass
            = SlopArena.Shared.CharacterClass.FightGuy;
        public static CpuDifficulty SoloCpuDifficulty = CpuDifficulty.Normal;
        /// <summary>Couch play (ADR-0041): Solo's P2 is a second Local Player instead of the CPU.</summary>
        public static bool SoloP2Human;
        /// <summary>InputDevice.deviceId of the pad P2 joined with; 0 while P2 waits to join.</summary>
        public static int SoloP2DeviceId;

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
        public static bool FriendlyFire;
        public static bool BallOnly;
        public static Dictionary<ulong, SlopballCamp> Camps = new();
        public static Dictionary<ulong, byte> Colours = new();

        public static void ClearActiveMatch()
        {
            Format = MatchFormat.Stock;
            FriendlyFire = false;
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
            PlayerPackageId = "fightguy";
            PlayerClass = SlopArena.Shared.CharacterClass.FightGuy;
            SoloBotClass = SlopArena.Shared.CharacterClass.FightGuy;
            SoloCpuDifficulty = CpuDifficulty.Normal;
            SoloP2Human = false;
            SoloP2DeviceId = 0;
            ArenaName = "training";
            IsHost = true;
            ServerIP = "127.0.0.1";
            ServerPort = 9876;
        }
    }
}
