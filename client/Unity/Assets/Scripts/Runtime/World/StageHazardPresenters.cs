using System;
using SlopArena.Shared.StageHazards;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// One stage-hazard family's On-only presentation (ADR-0062 decision 11). It is built only when
    /// the match has a hazard session, owns everything it draws under <see cref="Root"/>, and is fed
    /// the hazard view after every simulation tick. It reads the view and absolute ticks only.
    /// </summary>
    public interface IStageHazardPresenter : IDisposable
    {
        /// <summary>The presentation root; the match also hosts stage-owned hit visuals under it.</summary>
        Transform Root { get; }
        void Present(StageHazardView view);
    }

    public static class StageHazardPresenters
    {
        /// <summary>The name every family's root carries; smoke checks look for it to prove Off builds nothing.</summary>
        public const string RootName = "Stage Hazards (presentation only)";

        /// <summary>
        /// A family's root under the match, pinned to the world origin at unit scale. Hazard art is placed in
        /// simulation (world) space, and the match object need not sit at the origin (Local Brawl's does not).
        /// </summary>
        public static Transform CreateRoot(Transform parent)
        {
            var root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lossy = parent != null ? parent.lossyScale : Vector3.one;
            root.localScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);
            return root;
        }

        /// <summary>The family's presenter, chosen by the view's ambient id, or null for a family with no look yet.</summary>
        public static IStageHazardPresenter Create(StageHazardView view, Transform parent) => view?.AmbientPose.Id switch
        {
            "kraken" => new KrakenHazardPresentation(parent),
            "bass_drop" => new BassDropPresentation(parent),
            _ => null,
        };
    }
}
