#nullable enable
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.Client.UI
{
    public enum LocalPlayMode { Brawl, Slopball, CharacterCourse, MankiMayhem }
    public enum GameMode { Training, Solo, PvP, Arcade, Tutorial }
    public enum MatchTransport { None, DevelopmentUdp, SteamP2P }

    public enum SeatKind : byte { Open, Cpu, LocalPlayer }

    public struct SoloSeat
    {
        public SeatKind Kind;
        public FighterChoice? Choice { get; private set; }
        public byte PreferredForm { get; private set; }
        public string Fighter
        {
            get => Choice?.Revision.PackageId ?? "";
            set
            {
                var library = LocalFighterLibrary.Current;
                FighterChoice? choice;
                bool resolved = Choice != null && Choice.Revision.PackageId == value
                    ? library.TryRefreshChoice(Choice, out choice)
                    : library.TryChoose(value, library.HasForms(value) ? PreferredForm : (byte)0, out choice);
                if (resolved) SetFighter(choice!);
            }
        }
        public void SetFighter(FighterChoice choice)
        {
            Choice = choice ?? throw new System.ArgumentNullException(nameof(choice));
            if (choice.HasAlternateForm) PreferredForm = choice.StartingForm;
        }
        public void SetFighter(FighterChoiceState state)
        {
            var choice = state.Restore();
            if (choice == null) return;
            SetFighter(choice);
            if (!choice.HasAlternateForm && state.MenuForm is <= 1) PreferredForm = state.MenuForm.Value;
        }
        /// <summary>The picked starting form of a two-form fighter (ADR-0051); others ignore it.</summary>
        public byte Form
        {
            get => Choice?.StartingForm ?? 0;
            set
            {
                if (Choice == null) { LocalFighterLibrary.ReportDiagnostic("[MatchConfig] Seat has no admitted fighter."); return; }
                if (value > 1) { LocalFighterLibrary.ReportDiagnostic("[MatchConfig] Invalid menu form."); return; }
                SetFighter(Choice.WithForm(Choice.HasAlternateForm ? value : (byte)0));
                PreferredForm = value;
            }
        }
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
        public sealed class PlayerSelection
        {
            public FighterChoice Choice { get; }
            public CharacterClass Selector => Choice.LegacySelector ?? CharacterClass.None;
            public string PackageId => Choice.Revision.PackageId;
            public byte Form => Choice.StartingForm;
            public PlayerSelection(FighterChoice choice)
                => Choice = choice ?? throw new System.ArgumentNullException(nameof(choice));
            internal PlayerSelection WithForm(byte form) => new(Choice.WithForm(form));
        }
        private static PlayerSelection? _player;
        private static byte _preferredPlayerForm;
        public static PlayerSelection Selection { get { EnsurePlayer(); return _player!; } }
        public static CharacterClass PlayerClass { get { EnsurePlayer(); return _player?.Selector ?? CharacterClass.None; } }
        public static string PlayerPackageId { get { EnsurePlayer(); return _player?.PackageId ?? ""; } }
        public static byte PlayerForm
        {
            get => _player?.Form ?? 0;
            set
            {
                if (value > 1) { LocalFighterLibrary.ReportDiagnostic("[MatchConfig] Invalid menu form."); return; }
                if (_player != null) _player = _player.WithForm(_player.Choice.HasAlternateForm ? value : (byte)0);
                _preferredPlayerForm = value;
            }
        }
        public static void SetPlayerFighter(PlayerSelection selection)
        {
            _player = selection ?? throw new System.ArgumentNullException(nameof(selection));
            if (selection.Choice.HasAlternateForm) _preferredPlayerForm = selection.Form;
        }
        public static void SetPlayerFighter(FighterChoiceState state)
        {
            var choice = state.Restore();
            if (choice == null) return;
            SetPlayerFighter(choice);
            if (!choice.HasAlternateForm && state.MenuForm is <= 1) _preferredPlayerForm = state.MenuForm.Value;
        }
        public static void SetPlayerFighter(FighterChoice choice) => SetPlayerFighter(new PlayerSelection(choice));
        public static void SetPlayerFighter(string packageId)
        {
            var library = LocalFighterLibrary.Current;
            FighterChoice? choice;
            bool resolved = _player != null && _player.PackageId == packageId
                ? library.TryRefreshChoice(_player.Choice, out choice)
                : library.TryChoose(packageId, library.HasForms(packageId) ? _preferredPlayerForm : (byte)0, out choice);
            if (resolved) SetPlayerFighter(choice!);
        }
        public static void SetPlayerFighter(CharacterClass selector, string packageId)
        {
            if (ShippedRoster.ReadApplication().Manifest.Resolve(selector)?.PackageId != packageId)
                throw new System.ArgumentException("Selector and package disagree.");
            SetPlayerFighter(packageId);
        }
        public static void SetPlayerFighter(CharacterClass selector)
        {
            if (ShippedRoster.TryReadApplication(out var roster, out _) && roster!.Manifest.Resolve(selector) is { } entry)
                SetPlayerFighter(entry.PackageId);
            else LocalFighterLibrary.ReportDiagnostic("[MatchConfig] Unknown fighter selector.");
        }

        public static bool SetDefaultPlayerFighter()
        {
            if (!LocalFighterLibrary.Current.TryDefault(out var choice)) return false;
            SetPlayerFighter(choice!);
            return true;
        }

        static void EnsurePlayer()
        { if (_player == null) SetDefaultPlayerFighter(); }

        public static void SetPlayerFighter(MatchContentEntry entry)
        {
            if (entry == null) throw new System.ArgumentNullException(nameof(entry));
            SetPlayerFighter(entry.Choose(entry.Definition.AlternateForm != null ? _preferredPlayerForm : (byte)0));
        }

        public static FighterChoice? ResolveStoredFighter(string packageId, MatchContentCatalog catalog)
        {
            var entry = catalog.ResolvePackage(packageId);
            if (entry == null)
            {
                LocalFighterLibrary.ReportDiagnostic("[MatchConfig] Stored fighter cannot be resolved: " + packageId + "; using the default fighter.");
                if (ShippedRoster.TryReadApplication(out var roster, out _))
                    entry = catalog.ResolvePackage(roster!.DefaultFighter.PackageId);
            }
            return entry?.Choose(entry.Definition.AlternateForm != null ? _preferredPlayerForm : (byte)0);
        }

        /// <summary>Menu card preference, retained when the selected fighter has only one form.</summary>
        public static byte PreferredFormOf(int playerNumber) => playerNumber == 1 ? _preferredPlayerForm : Seat(playerNumber).PreferredForm;
        /// <summary>The admitted starting form of the seat's actual choice.</summary>
        public static byte FormOf(int playerNumber) => playerNumber == 1 ? PlayerForm : Seat(playerNumber).Form;
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

        /// <summary>P2–P4 of a Solo match (ADR-0041). P1 is always the local player with Selection.</summary>
        public static readonly SoloSeat[] SoloSeats = new SoloSeat[3];

        static MatchConfig()
        {
            ResetSoloSeats();
        }

        public static ref SoloSeat Seat(int playerNumber)
        {
            if (playerNumber < 2 || playerNumber > 4)
                throw new System.ArgumentOutOfRangeException(nameof(playerNumber));
            ref var seat = ref SoloSeats[playerNumber - 2];
            if (seat.Kind != SeatKind.Open && seat.Choice == null && LocalFighterLibrary.Current.TryDefault(out var choice)) seat.SetFighter(choice!);
            return ref seat;
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
                if (!LocalFighterLibrary.Current.TryRefreshChoice(seat.Choice, out var choice)) return 0;
                seat.SetFighter(choice!);
                seat.Kind = SeatKind.LocalPlayer;
                seat.DeviceId = deviceId;
                return player;
            }
            return 0;
        }

        public static void LeaveSoloSeat(int playerNumber)
        {
            ref var seat = ref Seat(playerNumber);
            seat.Kind = LocalMode == LocalPlayMode.CharacterCourse || SoloOpponentCount > 1 ? SeatKind.Open : SeatKind.Cpu;
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
            SoloSeats[0] = new SoloSeat
            {
                Kind = SeatKind.Cpu, Difficulty = CpuDifficulty.Normal,
            };

        }

        public static bool TryResolveLocalChoices(LocalFighterLibrary library, out IReadOnlyList<FighterChoice> choices, out string? failure, string? playerPackageOverride = null)
        {
            var resolved = new List<(int Player, FighterChoice Choice)>();
            choices = System.Array.Empty<FighterChoice>();
            failure = null;
            FighterChoice? player;
            bool validPlayer;
            if (playerPackageOverride == null) validPlayer = library.TryRefreshChoice(_player?.Choice, out player);
            else
            {
                validPlayer = library.TryChoose(playerPackageOverride, library.HasForms(playerPackageOverride) ? _preferredPlayerForm : (byte)0, out player);
                if (!validPlayer)
                {
                    LocalFighterLibrary.ReportDiagnostic("[MatchConfig] Stored fighter cannot be resolved: " + playerPackageOverride + "; using the default fighter.");
                    validPlayer = library.TryDefault(out player);
                }
            }
            if (!validPlayer)
            { failure = "content.choice.unavailable: P1 package is unavailable."; return false; }
            resolved.Add((1, player!));
            for (int number = 2; number <= 4; number++)
            {
                var seat = SoloSeats[number - 2];
                bool occupied = Mode == GameMode.Training ? number == 2
                    : Mode == GameMode.Solo ? seat.Kind != SeatKind.Open
                    : Mode == GameMode.Arcade && LocalMode == LocalPlayMode.CharacterCourse && seat.Kind == SeatKind.LocalPlayer;
                if (!occupied) continue;
                if (!library.TryRefreshChoice(seat.Choice, out var choice))
                { failure = $"content.choice.unavailable: P{number} package is unavailable."; return false; }
                resolved.Add((number, choice!));
            }
            foreach (var selection in resolved)
                if (selection.Player == 1)
                { if (playerPackageOverride == null) SetPlayerFighter(selection.Choice); }
                else SoloSeats[selection.Player - 2].SetFighter(selection.Choice);
            choices = resolved.ConvertAll(selection => selection.Choice).AsReadOnly();
            return true;
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
        public static StageHazardMode StageHazards = StageHazardMode.On;
        public static uint StageHazardSeed;

        public static bool HasStageHazardFamily(ArenaDefinition arena)
            => SlopArena.Shared.StageHazards.StageHazardFamilies.IsAvailable(arena);
        public static Dictionary<ulong, SlopballCamp> Camps = new();
        public static Dictionary<ulong, byte> Colours = new();
        /// <summary>The model each player (1-4) wears over their fighter, presentation only (ADR-0065). Kept between matches.</summary>
        public static readonly Dictionary<int, SlopArena.Client.Workshop.ModelSwapChoice> ModelSwaps = new();

        public static void ClearActiveMatch()
        {
            Format = MatchFormat.Stock;
            FriendlyFire = FriendlyFireMode.Off;
            BallOnly = false;
            StageHazards = StageHazardMode.On;
            StageHazardSeed = 0;
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
            SetDefaultPlayerFighter();
            ResetSoloSeats();
            ArenaName = "training";
            IsHost = true;
            ServerIP = "127.0.0.1";
            ServerPort = 9876;
        }
    }
}
