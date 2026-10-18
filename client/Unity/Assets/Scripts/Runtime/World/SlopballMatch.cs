using System;
using System.Security.Cryptography;
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
        private SlopballCourtShow _court;
        private Transform _launcher;
        private float _launcherHeight;
        private ArcadeRecordKey _recordKey;
        private ArcadeRecordProvenance _provenance;
        private bool _wentLive;
        private int _countdownBeat = -1;
        private ulong _personalBest, _bankedBest;
        private bool _banked;
        public ArcadeRecordUpdate LastRecordUpdate { get; private set; }
        public ulong SavedBest { get; private set; }
        protected override Action PauseRetry => RestartAttempt;

        protected override void OnBallMatchReady(MatchContentEntry entry, ArenaDefinition arena)
        {
            _launcher = StageVisual != null ? StageVisual.Find("Ball Sender") : null;
            if (_launcher != null) _launcherHeight = _launcher.position.y;
            _court = SlopballCourtShow.Find(StageVisual);
            _recordKey = new("slopball", arena.Arcade.ProfileId, entry.Identity.PackageId,
                ArcadeRecordCompatibility.Slopball(arena, entry));
            using var hash = SHA256.Create();
            string stageHash = BitConverter.ToString(hash.ComputeHash(ArenaBinaryFormat.Serialize(arena)))
                .Replace("-", "").ToLowerInvariant();
            _provenance = new(entry.Identity.Version, entry.Identity.CookedContentHash, entry.Identity.PackageHash, stageHash);
            _personalBest = ArcadeRecordsStore.Instance.Book.Best(_recordKey) ?? 0;
            _view = new SlopballView(_hudManager.Document.rootVisualElement);
            var bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            uint seed = BitConverter.ToUInt32(bytes, 0);
            Simulation.BeginSlopballAttempt(ArcadeRecordCompatibility.SlopballCountdownTicks, seed == 0 ? 1u : seed);
            OnChallengeTick();
        }

        protected override void Update()
        {
            base.Update();
            _view?.SetPaused(IsPaused);
            _view?.Tick(Time.unscaledDeltaTime);
            _court?.Tick(Time.unscaledDeltaTime);
        }

        protected override void OnChallengeTick()
        {
            if (Simulation.Slopball is not SlopballState challenge) return;
            // The authored mesh follows Shared's next serve origin during the gap.
            if (_launcher != null)
                _launcher.position = new Vector3(challenge.Shot.Spawn.X, _launcherHeight, challenge.Shot.Spawn.Z);
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
            foreach (var item in Simulation.LastTickSlopballEvents)
            {
                if (item.Kind == SlopballEventKind.Returned) { _view?.ShowStreakToast(false); _court?.Returned(); }
                if (item.Kind == SlopballEventKind.Missed)
                {
                    if (item.Returns > 0) _view?.ShowStreakToast(true);
                    BankBest();
                }
            }
            _view?.Present(challenge, Simulation.ArcadeBall.Value, _personalBest, ArcadeRecordsStore.Instance.Feedback);
            _court?.Present(challenge.Returns);
            if (challenge.Ended) BankBest();
        }

        private void BankBest()
        {
            if (!_wentLive || _recordKey == null || Simulation?.Slopball is not SlopballState challenge
                || _banked && challenge.BestStreak <= _bankedBest) return;
            SavedBest = challenge.BestStreak;
            LastRecordUpdate = ArcadeRecordsStore.Instance.Bank(_recordKey, SavedBest, _provenance);
            _personalBest = LastRecordUpdate.PersonalBest; _bankedBest = SavedBest; _banked = true;
        }

        protected override void OnAttemptStopped() => BankBest();
        protected override void LeaveMatch() => ReturnToArcade();
        private void ReturnToArcade()
        {
            Simulation?.StopSlopball(); BankBest();
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Arcade;
            FrontendController.Show(FrontendPage.ArcadeSelect);
        }

        protected override void OnDestroy()
        {
            Simulation?.StopSlopball(); BankBest();
            _view?.Dispose();
            base.OnDestroy();
        }

        protected override void DrawChallengeHUD(ArcadeBallState ball)
        {
            // Ball play and the small combo counter use the normal HUD document.
        }
    }
}
