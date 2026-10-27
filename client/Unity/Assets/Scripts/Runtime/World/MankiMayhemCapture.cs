#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>Real offline inputs and frames for the first pass through the Mayhem deck.</summary>
    public sealed class MankiMayhemCapture : MonoBehaviour
    {
        public TrainingMatch Match;
        public string Output;
        public bool FullShow;
        public MankiHostPresenter Host;
        private string _reaction;
        private int _reactionTicks;
        public bool Done { get; private set; }
        private readonly HashSet<string> _shots = new();
        private readonly Queue<string> _pending = new();
        private readonly List<object> _events = new();
        private readonly Dictionary<string, int> _frames = new();
        private int _previousRate, _cleanupFrames;
        private float _previousFixed;
        private Texture2D _pixels;
        private readonly object _inputOwner = new();
        private bool _jumpSeen, _cageSeen, _cleaned, _tripped;
        private CharacterDefinition _definition;
        private ArenaDefinition _arena;

        private void OnEnable()
        {
            _previousRate = Time.captureFramerate; _previousFixed = Time.fixedDeltaTime;
            Time.captureFramerate = 60; Time.fixedDeltaTime = 1f / 60f;
            if (!Match.TryAcquireCaptureInputSequence(_inputOwner)) throw new InvalidOperationException("P1 capture input is already owned.");
            _definition = BuiltInContentResolver.Resolve(MatchConfig.PlayerClass).Definition;
            _arena = ArenaBinaryFormat.LoadFromFile(BakedContentPaths.ResolveArena(MatchConfig.ArenaName))
                ?? throw new InvalidOperationException("Mayhem capture requires its baked arena.");
            Match.CaptureSimulationTickCompleted += Tick;
            StartCoroutine(Capture());
        }

        private void Tick(uint tick)
        {
            var sim = Match.CaptureSimulation;
            var state = sim.MankiMayhem.Value;
            foreach (var e in sim.LastTickMayhemEvents)
                _events.Add(new { tick, round = e.RoundIndex, card = state.Card.ToString(), kind = e.Kind.ToString(), entity = e.EntityId.ToString(), e.OperationIndex, e.Points });
            if (FullShow)
            {
                if (state.Phase == MankiMayhemPhase.Call && state.PhaseTick >= 30) Shot("crt-" + state.Card.ToString().ToLowerInvariant());
            }
            var input = default(InputState);
            if (state.Card == MankiMayhemCard.Jump)
            {
                _jumpSeen = true;
                if (state.Phase == MankiMayhemPhase.Call) Shot("jump-call");
                uint first = sim.MayhemSlams.Count > 0 ? sim.MayhemSlams[0].Tick : 0;
                if (sim.CurrentTick < first + 125)
                {
                    new MayhemThreats(sim).TryAvoid(sim.GetState(MatchConfig.LocalEntityId), _definition, _arena, out input);
                    input.JumpHeld = input.Jump;
                }
                if (sim.GetState(MatchConfig.LocalEntityId).PY - _definition.CapsuleHeight * .5f > .5f && sim.Resolver.GetActiveHitboxes().Any(h => h.Shape == HitboxShape.Ring))
                    Shot("jump-wave-mid-jump");
                _tripped |= sim.LastTickHits.Any(h => h.OwnerEntityId == ServerSimulation.MayhemHostEntityId && h.TargetEntityId == MatchConfig.LocalEntityId);
                if (_tripped && sim.GetState(MatchConfig.LocalEntityId).HitstunTicks > 0) Shot("jump-tripped");
            }
            if (state.Card == MankiMayhemCard.BreakOut)
            {
                _cageSeen = true;
                if (state.Phase == MankiMayhemPhase.Call && state.PhaseTick >= 8) Shot("breakout-call-rising");
                new MayhemThreats(sim).TryAvoid(sim.GetState(MatchConfig.LocalEntityId), _definition, _arena, out input);
                if (sim.LastTickMayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.WallHit)) Shot("breakout-wall-cracking");
                var exit = sim.MayhemWalls.FirstOrDefault(w => w.CageEntityId == MatchConfig.LocalEntityId && w.HitBudget == MankiMayhemTuning.CrackedWallHits);
                if (exit.EntityId != 0 && sim.GetState(MatchConfig.LocalEntityId).PZ < exit.CageCenter.Z - MankiMayhemTuning.CageHalfSize - _definition.CapsuleRadius)
                    Shot("breakout-escaping");
                if (Match.CaptureLastTickPresentationEvents.Any(e => e.PresentationId == "mayhem.scatter.blast")) Shot("breakout-blast");
                if (sim.MayhemWalls.Count == 0 && state.Phase is MankiMayhemPhase.Score or MankiMayhemPhase.Break)
                { Shot("breakout-empty-cleanup"); _cleaned = true; }
            }
            if (FullShow)
            {
                if (state.RoundIndex == 0) input = new InputState { MoveX = 1f, Right = true };
                _reactionTicks = _reaction == Host.Reaction ? _reactionTicks + 1 : 0;
                _reaction = Host.Reaction;
                if (_reaction == "laugh" && _reactionTicks >= 12) Shot("ko-booth-laugh");
                if (_reaction == "hype" && _reactionTicks >= 25) Shot("last-standing-hype");
                if (sim.LastTickMayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.Slam)) Shot("host-slam");
                if (state.Card == MankiMayhemCard.BreakOut && sim.LastTickMayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.Launch)) Shot("host-lob");
            }
            Match.SetCaptureSequenceInput(_inputOwner, input);
        }

        private void Shot(string name) { if (_shots.Add(name)) _pending.Enqueue(name); }

        private IEnumerator Capture()
        {
            var end = new WaitForEndOfFrame();
            while (!Done)
            {
                yield return end;
                var sim = Match.CaptureSimulation; var state = sim.MankiMayhem.Value;
                if (FullShow && state.Ended && _cleanupFrames >= 30) Shot("show-finale");
                string card = state.Card == MankiMayhemCard.Jump ? "jump" : state.Card == MankiMayhemCard.BreakOut ? "breakout" : null;
                if (FullShow) card = "full-match";
                bool clip = FullShow ? Time.frameCount % 2 == 0 : card != null && state.RoundIndex < 3 && sim.CurrentTick % 2 == 0;
                if (clip || _pending.Count > 0)
                {
                    if (_pixels == null) _pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    _pixels.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                    while (_pending.Count > 0)
                    {
                        string name = _pending.Dequeue();
                        File.WriteAllBytes(Path.Combine(Output, name + ".png"), _pixels.EncodeToPNG());
                        _events.Add(new { shot = name, tick = sim.CurrentTick, round = state.RoundIndex, phase = state.Phase.ToString(),
                            hostReaction = Host?.Reaction, players = sim.MayhemPlayers, walls = sim.MayhemWalls,
                            rings = sim.Resolver.GetActiveHitboxes().Where(h => h.Shape == HitboxShape.Ring)
                                .Select(h => new { h.X, h.Y, h.Z, h.Radius }).ToArray() });
                    }
                    if (clip)
                    {
                        string folder = Path.Combine(Output, card); Directory.CreateDirectory(folder);
                        _frames.TryGetValue(card, out int frame); _frames[card] = frame + 1;
                        File.WriteAllBytes(Path.Combine(folder, $"{frame:0000}.jpg"), _pixels.EncodeToJPG(90));
                    }
                }
                if ((FullShow ? state.Ended : _jumpSeen && _cageSeen && _cleaned) && ++_cleanupFrames >= (FullShow ? 60 : 4))
                {
                    Done = true;
                    File.WriteAllText(Path.Combine(Output, "scenario.json"), JsonConvert.SerializeObject(new { shots = _shots, events = _events,
                        players = sim.MayhemPlayers, frames = _frames, tick = sim.CurrentTick }, Formatting.Indented));
                }
            }
        }

        private void OnDisable()
        {
            Match.CaptureSimulationTickCompleted -= Tick; Match.ReleaseCaptureInputSequence(_inputOwner);
            Time.captureFramerate = _previousRate; Time.fixedDeltaTime = _previousFixed;
            if (_pixels != null) Destroy(_pixels);
        }
    }
}
#endif
