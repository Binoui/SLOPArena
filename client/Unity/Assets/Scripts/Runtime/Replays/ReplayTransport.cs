using System;

namespace SlopArena.Client.Replays
{
    public enum ReplaySpeed { Quarter, Half, One, Two }
    public enum ReplayTransportState { Playing, Paused, Ended }

    /// <summary>
    /// The transport as a value. One tick runs per FixedUpdate while playing, and Time.timeScale, derived from this
    /// value in one place, sets the speed, so every Unity clock the presenters use scales together.
    /// </summary>
    public readonly struct ReplayTransport
    {
        public ReplayTransport(ReplayTransportState state, ReplaySpeed speed)
        {
            State = state;
            Speed = speed;
        }

        public ReplayTransportState State { get; }
        public ReplaySpeed Speed { get; }
        public float TimeScale => State == ReplayTransportState.Playing ? Scale(Speed) : 0f;

        public ReplayTransport With(ReplayTransportState state) => new(state, Speed);
        public ReplayTransport Faster() => new(State, Speed == ReplaySpeed.Two ? Speed : Speed + 1);
        public ReplayTransport Slower() => new(State, Speed == ReplaySpeed.Quarter ? Speed : Speed - 1);

        public static float Scale(ReplaySpeed speed) => speed switch
        {
            ReplaySpeed.Quarter => 0.25f,
            ReplaySpeed.Half => 0.5f,
            ReplaySpeed.One => 1f,
            ReplaySpeed.Two => 2f,
            _ => throw new ArgumentOutOfRangeException(nameof(speed)),
        };

        public static string Label(ReplaySpeed speed) => speed switch
        {
            ReplaySpeed.Quarter => "0.25×",
            ReplaySpeed.Half => "0.5×",
            ReplaySpeed.One => "1×",
            _ => "2×",
        };
    }
}
