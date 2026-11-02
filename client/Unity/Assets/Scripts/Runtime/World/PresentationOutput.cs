using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The one gate for sound and controller output from presenters. A Replay seek runs its warmup ticks through
    /// the ordinary present code inside <see cref="MatchBase.SilenceOutput"/>, so warmup plays nothing at the
    /// source instead of being stopped afterwards. Live matches are never silent.
    /// </summary>
    public static class PresentationOutput
    {
        /// <summary>Outputs that played, for seek checks.</summary>
        public static int Played { get; private set; }
        /// <summary>Outputs a silent scope skipped, for seek checks.</summary>
        public static int Suppressed { get; private set; }

        /// <summary>True while a Replay warmup runs; delayed-sound queues drop what they would add.</summary>
        public static bool Silent => MatchBase.Active is { OutputSilent: true };

        /// <summary>Call where a sound or rumble would start; play only when it returns true.</summary>
        public static bool TryPlay()
        {
            if (MatchBase.Active is { OutputSilent: true })
            {
                Suppressed++;
                return false;
            }
            Played++;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => Played = Suppressed = 0;
    }
}
