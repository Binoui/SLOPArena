using SlopArena.Shared;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Clocks a Replay recorded for one fighter tick. Presenters use them in place of today's tuning, so an old
    /// move keeps its recorded timing; live play passes none. docs/systems/replay-file.md lists each field's reader.
    /// </summary>
    public readonly struct RecordedFighterClocks
    {
        public RecordedFighterClocks(AttackPose? pose, float flightElapsedTicks, float flightFuelFraction, int chargeTier, float chargeProgress)
        {
            Pose = pose;
            FlightElapsedTicks = flightElapsedTicks;
            FlightFuelFraction = flightFuelFraction;
            ChargeTier = chargeTier;
            ChargeProgress = chargeProgress;
        }

        public AttackPose? Pose { get; }
        public float FlightElapsedTicks { get; }
        public float FlightFuelFraction { get; }
        public int ChargeTier { get; }
        public float ChargeProgress { get; }
    }
}
