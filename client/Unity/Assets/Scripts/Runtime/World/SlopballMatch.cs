using System;
using System.Security.Cryptography;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.World
{
    public sealed class SlopballMatch : ArcadeBallMatch
    {
        public const string ArenaKey = "slopball";
        public const string SceneName = "Arcade_Slopball";
        protected override string StageKey => ArenaKey;
        private SlopballView _view;
        private SlopballCourtShow _court;
        private SlopballPresentation _presentation;
        private SlopballPresentationSet _set;
        private bool _setLoaded;
        private Button _paceButton;
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
        /// <summary>For verification commands: the feel pass's clips, played sounds, ball face and live effects.</summary>
        public object PresentationDiagnostics => _presentation?.Diagnostics;
        protected override Action PauseRetry => RestartAttempt;

        private SlopballPresentationSet Set
        {
            get
            {
                if (!_setLoaded) { _set = Resources.Load<SlopballPresentationSet>(SlopballPresentationSet.ResourcesPath); _setLoaded = true; }
                return _set;
            }
        }

        // Without the generated set, the court keeps the plain ball and ring.
        protected override bool CustomBallPresentation => Set != null && Set.Ball != null && Set.GroundMarker != null;
        protected override ArenaDefinition PrepareArena(ArenaDefinition arena) => SlopballPace.Apply(arena, SlopballPace.Current);

        private UnityEngine.Camera RenderCamera => _cameraMount?.RenderCamera ?? UnityEngine.Camera.main;

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
            _view.SetPace(SlopballPace.Label(SlopballPace.Current));
            if (CustomBallPresentation)
                _presentation = new SlopballPresentation(Set, transform, BallAnchor, arena, _launcher, _view, _hudManager, () => RenderCamera)
                    { PersonalBest = _personalBest };
            AddPaceButton();
            var bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            uint seed = BitConverter.ToUInt32(bytes, 0);
            Simulation.BeginSlopballAttempt(ArcadeRecordCompatibility.SlopballCountdownTicks, seed == 0 ? 1u : seed);
            OnChallengeTick();
        }

        /// <summary>The temporary pace trial (SlopballPace): a pause-menu button after Retry that cycles the pace and restarts.</summary>
        private void AddPaceButton()
        {
            var retry = _hudManager?.Document?.rootVisualElement?.Q<Button>("pause-retry");
            if (retry?.parent == null) return;
            _paceButton = new Button(CyclePace) { name = "pause-pace", text = PaceText() };
            _paceButton.AddToClassList("match-pause-action");
            retry.parent.Insert(retry.parent.IndexOf(retry) + 1, _paceButton);
        }

        private static string PaceText() => $"PACE: {SlopballPace.Label(SlopballPace.Current)}";

        private void CyclePace()
        {
            SlopballPace.Current = SlopballPace.Next(SlopballPace.Current);
            if (_paceButton != null) _paceButton.text = PaceText();
            _pauseMenu?.SetPaused(false);
            RestartAttempt();
        }

        protected override void Update()
        {
            base.Update();
            _view?.SetPaused(IsPaused);
            _view?.Tick(Time.unscaledDeltaTime, RenderCamera);
            _court?.Tick(Time.unscaledDeltaTime);
            _presentation?.Update(Time.deltaTime, Simulation);
        }

        protected override void OnChallengeTick()
        {
            if (Simulation.Slopball is not SlopballState challenge) return;
            // The presentation glides the sender to Shared's next serve origin; without it, jump there.
            if (_presentation == null && _launcher != null)
                _launcher.position = new Vector3(challenge.Shot.Spawn.X, _launcherHeight, challenge.Shot.Spawn.Z);
            if (SimulationAdvanced) _presentation?.AfterTick(Simulation);
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
            // An ended attempt is presented again without new ticks; its last events are already handled.
            if (SimulationAdvanced)
            {
                foreach (var item in Simulation.LastTickSlopballEvents)
                {
                    if (item.Kind == SlopballEventKind.Returned) _court?.Returned();
                    if (item.Kind == SlopballEventKind.Missed)
                    {
                        if (_presentation == null && item.Returns > 0)
                            _view?.ShowDropToast(item.Returns, Math.Max(_personalBest, challenge.BestStreak));
                        BankBest();
                    }
                }
            }
            _view?.Present(challenge, Simulation.ArcadeBall.Value, _personalBest, ArcadeRecordsStore.Instance.Feedback);
            _court?.Present(challenge.Returns);
            _court?.SetHeat(challenge.Heat);
            if (challenge.Ended) BankBest();
        }

        private void BankBest()
        {
            if (!_wentLive || _recordKey == null || Simulation?.Slopball is not SlopballState challenge
                || _banked && challenge.BestStreak <= _bankedBest) return;
            SavedBest = challenge.BestStreak;
            LastRecordUpdate = ArcadeRecordsStore.Instance.Bank(_recordKey, SavedBest, _provenance);
            _personalBest = LastRecordUpdate.PersonalBest; _bankedBest = SavedBest; _banked = true;
            if (_presentation != null) _presentation.PersonalBest = _personalBest;
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
            _presentation?.Dispose();
            _paceButton?.RemoveFromHierarchy();
            base.OnDestroy();
        }

        protected override void DrawChallengeHUD(ArcadeBallState ball)
        {
            // Ball play and the small combo counter use the normal HUD document.
        }
    }
}
