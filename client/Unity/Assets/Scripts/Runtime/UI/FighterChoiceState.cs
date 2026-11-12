#nullable enable
using System;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.Client.UI
{
    [Serializable]
    public sealed class FighterChoiceState
    {
        public string PackageId = "";
        public byte Form;
        public byte? MenuForm;
        public FighterChoiceState() { }
        public FighterChoiceState(FighterChoice choice, byte? menuForm = null)
        { PackageId = choice.Revision.PackageId; Form = choice.StartingForm; MenuForm = menuForm; }
        public FighterChoice Restore()
        {
            var library = LocalFighterLibrary.Current;
            if (library.Contains(PackageId) && (Form == 0 || Form == 1 && library.HasForms(PackageId)))
                return library.Choose(PackageId, Form);
            LocalFighterLibrary.ReportDiagnostic("[FighterChoiceState] Unresolvable stored fighter '" + PackageId + "'; using the default fighter.");
            return library.Choose(ShippedRoster.ReadApplication().DefaultFighter.PackageId);
        }
    }

    [Serializable]
    public sealed class SoloSeatState
    {
        public SeatKind Kind;
        public FighterChoiceState? FighterPackage;
        public CpuDifficulty Difficulty;
        public int DeviceId;
        public static SoloSeatState Capture(SoloSeat seat) => new SoloSeatState
        { Kind = seat.Kind, FighterPackage = seat.Choice == null ? null : new FighterChoiceState(seat.Choice, seat.PreferredForm), Difficulty = seat.Difficulty, DeviceId = seat.DeviceId };
        public SoloSeat Restore()
        {
            var seat = new SoloSeat { Kind = Kind, Difficulty = Difficulty, DeviceId = DeviceId };
            if (FighterPackage != null || Kind != SeatKind.Open) seat.SetFighter(FighterPackage ?? new FighterChoiceState());
            return seat;
        }
    }
}
