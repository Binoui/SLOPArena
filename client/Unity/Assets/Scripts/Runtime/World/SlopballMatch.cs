using System;
using System.Security.Cryptography;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    public sealed class SlopballMatch : ArcadeBallMatch
    {
        public const string ArenaKey = "slopball";
        public const string SceneName = "Arcade_Slopball";
        protected override string StageKey => ArenaKey;
        private SlopballView _view;
        private ArcadeRecordKey _recordKey;
        private ArcadeRecordProvenance _provenance;
        private bool _wentLive;
        private int _countdownBeat = -1;
        public ArcadeRecordUpdate Result { get; private set; }
        public ulong ResultReturns { get; private set; }

        protected override void OnBallMatchReady(MatchContentEntry entry, ArenaDefinition arena)
        {
            _recordKey = new("slopball", arena.Arcade.ProfileId, entry.Identity.PackageId,
                ArcadeRecordCompatibility.Slopball(arena, entry));
            using var hash = SHA256.Create();
            string stageHash = BitConverter.ToString(hash.ComputeHash(ArenaBinaryFormat.Serialize(arena)))
                .Replace("-", "").ToLowerInvariant();
            _provenance = new(entry.Identity.Version, entry.Identity.CookedContentHash, entry.Identity.PackageHash, stageHash);
            _view = new SlopballView(_hudManager.Document.rootVisualElement, RestartAttempt, ReturnToArcade);
            Simulation.BeginSlopballAttempt(ArcadeRecordCompatibility.SlopballCountdownTicks);
            OnChallengeTick();
        }

        protected override void Update()
        {
            base.Update();
            if (_view != null)
            {
                _view.SetPaused(IsPaused);
                if (!IsPaused && Result != null)
                { _cameraMount?.SetMode(CameraMode.FreeCursor); _view.PumpResultInput(); }
            }
        }

        protected override void OnChallengeTick()
        {
            if (Simulation.Slopball is not SlopballState challenge) return;
            if (challenge.Phase == SlopballPhase.Countdown)
            {
                int beat = (challenge.CountdownTicks + 59) / 60;
                if (beat != _countdownBeat)
                { _countdownBeat = beat; _hudManager.ShowSoloCountdown(beat.ToString(), 1f); }
            }
            else if (!challenge.Ended)
            {
                if (!_wentLive)
                {
                    _wentLive = true;
                    _inputController.RequireReleaseBeforeHumanInput();
                    _aimHandler?.ResetPresentation();
                    _hudManager.ShowSoloCountdown("SLOP IT OUT", .8f);
                }
            }
            _view?.Present(challenge, Simulation.ArcadeBall.Value);
            if (challenge.Ended) CaptureResult(show: true);
        }

        private void CaptureResult(bool show)
        {
            if (Result != null || _recordKey == null || Simulation?.Slopball is not SlopballState challenge) return;
            ResultReturns = challenge.Returns;
            var store = ArcadeRecordsStore.Instance;
            Result = _wentLive ? store.Bank(_recordKey, ResultReturns, _provenance)
                : new ArcadeRecordUpdate(store.Book.Best(_recordKey), store.Book.Best(_recordKey) ?? 0, false);
            if (!show || _view == null) return;
            _inputController.RequireReleaseBeforeHumanInput();
            _aimHandler?.ResetPresentation();
            _showCrosshair = false;
            _cameraMount?.SetMode(CameraMode.FreeCursor);
            _view.ShowResult(ResultReturns, Result, challenge.Phase == SlopballPhase.Missed, store.Feedback);
        }

        protected override void OnAttemptStopped() => CaptureResult(show: false);
        protected override void LeaveMatch() => ReturnToArcade();
        private void ReturnToArcade()
        {
            Simulation?.StopSlopball(); CaptureResult(show: false);
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Arcade;
            FrontendController.Show(FrontendPage.ArcadeSelect);
        }

        protected override void OnDestroy()
        {
            Simulation?.StopSlopball(); CaptureResult(show: false);
            _view?.Dispose();
            base.OnDestroy();
        }

        protected override void DrawChallengeHUD(ArcadeBallState ball)
        {
            // The challenge and results use the existing HUD's UI Toolkit document.
        }
    }
}
