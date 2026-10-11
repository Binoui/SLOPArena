using UnityEngine;

namespace SlopArena.Client.World
{

    /// <summary>
    /// Opts an attached timeline effect into its attack's lifetime: interruption,
    /// replacement and owner disable/removal clear it; hitstop retains its remaining ticks.
    /// World-space contact bursts have an independent lifetime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackBoundPresentation : MonoBehaviour
    {
    }
}
