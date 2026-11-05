using System;
using SlopArena.Shared;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.Diagnostics
{
    public static class FeedbackContextCapture
    {
        public static FeedbackContext Observe()
        {
            var active = MatchBase.Active;
            var phase = active == null ? (FrontendController.IsFrontendActive && FrontendController.CurrentPage == FrontendPage.Results ? FeedbackPhase.Results : FeedbackPhase.Frontend) :
                !active.DrivesControllerFeedback ? FeedbackPhase.Replay : FeedbackPhase.Match;
            FeedbackMatchContext match = null;
            if (phase == FeedbackPhase.Match)
            {
                var online = active as PvPMatch;
                match = new FeedbackMatchContext(MatchConfig.Mode.ToString().ToLowerInvariant(),
                    MatchConfig.Format.ToString().ToLowerInvariant(), MatchConfig.ArenaName,
                    online != null ? MatchConfig.MatchId : null, MatchConfig.LocalEntityId,
                    online != null ? online.FeedbackLastObservedServerTick : null, active.DescribeFeedbackFighters());
            }
            return new FeedbackContext(Application.version, SimulationProtocol.Version, Application.platform.ToString(),
                SceneManager.GetActiveScene().name, phase, DateTimeOffset.UtcNow, match);
        }
    }
}
