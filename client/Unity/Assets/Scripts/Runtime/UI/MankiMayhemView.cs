using System;
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed class MankiMayhemView : IDisposable
    {
        private readonly VisualElement _root, _scores;
        private readonly Label _round, _banner, _subtitle;
        private readonly Dictionary<ulong, Label> _rows = new();
        private readonly Dictionary<ulong, Label> _stamps = new();
        private readonly FriendlyFireMode _friendlyFire;
        private readonly Dictionary<ulong, MayhemEventKey> _scoreKeys = new();
        private readonly Dictionary<ulong, uint> _scoreUntil = new();
        private readonly Dictionary<ulong, string> _scoreText = new();
        private readonly IReadOnlyList<HUDManager.HudPlayer> _players;

        public MankiMayhemView(VisualElement hud, IReadOnlyList<HUDManager.HudPlayer> players, FriendlyFireMode friendlyFire = FriendlyFireMode.Shoves)
        {
            _players = players; _friendlyFire = friendlyFire;
            _root = new VisualElement { name = "mayhem-hud", pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.left = _root.style.right = _root.style.top = _root.style.bottom = 0;
            var stylesheet = Resources.Load<StyleSheet>("UI/MankiMayhem");
            if (stylesheet != null) _root.styleSheets.Add(stylesheet);
            var heading = new VisualElement(); heading.AddToClassList("mayhem-heading");
            heading.Add(Text("MANKI MAYHEM", "mayhem-brand"));
            _round = Text("", "mayhem-round"); heading.Add(_round); _root.Add(heading);
            _scores = new VisualElement(); _scores.AddToClassList("mayhem-scores"); _root.Add(_scores);
            foreach (var player in players)
            {
                var row = new VisualElement(); row.AddToClassList("mayhem-row");
                row.style.borderLeftColor = SeatColours.For(player.PlayerNumber);
                var portrait = new Image { image = Resources.Load<Texture2D>($"UI/Portraits/{player.Class}"), scaleMode = ScaleMode.ScaleToFit };
                portrait.AddToClassList("mayhem-portrait"); row.Add(portrait);
                var label = Text("", "mayhem-player"); row.Add(label); _rows.Add(player.EntityId, label);
                _scores.Add(row);
                var stamp = Text("", "mayhem-stamp"); stamp.style.display = DisplayStyle.None;
                _root.Add(stamp); _stamps.Add(player.EntityId, stamp);
            }
            var banner = new VisualElement(); banner.AddToClassList("mayhem-call");
            _banner = Text("SCATTER!", "mayhem-call-title"); banner.Add(_banner);
            _subtitle = Text("STICKY BOMBS · DON'T BE THE ONE HOLDING IT", "mayhem-call-line"); banner.Add(_subtitle);
            _root.Add(banner);
            hud.Add(_root);
        }

        private static Label Text(string text, string style)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(style); return label;
        }

        public void Events(IReadOnlyList<MankiMayhemEvent> events, IReadOnlyList<MankiMayhemEvent> retracted)
        {
            if (retracted != null)
                foreach (var e in retracted)
                    if (_scoreKeys.TryGetValue(e.EntityId, out var key) && key == e.Key)
                    { _scoreUntil.Remove(e.EntityId); _scoreKeys.Remove(e.EntityId); }
            foreach (var e in events)
                if (e.Kind == MankiMayhemEventKind.Score)
                {
                    _scoreKeys[e.EntityId] = e.Key;
                    _scoreUntil[e.EntityId] = e.MatchTick + 60;
                    _scoreText[e.EntityId] = $"+{e.Points}";
                }
        }

        public void Present(ServerSimulation sim, Func<ulong, Vector3> position, UnityEngine.Camera camera)
        {
            if (sim.MankiMayhem is not MankiMayhemState state) return;
            string card = CardTitle(state.Card);
            _banner.text = card;
            _subtitle.text = CardSubtitle(state.Card);
            _round.text = $"ROUND {state.RoundIndex + 1} / {state.RoundCount} · {card}\n{_friendlyFire.ToWire().ToUpperInvariant()} · SURVIVE TO SCORE";
            _banner.parent.style.display = state.Phase == MankiMayhemPhase.Call ? DisplayStyle.Flex : DisplayStyle.None;
            _scores.style.display = state.Ended ? DisplayStyle.None : DisplayStyle.Flex;
            var mayhemPlayers = sim.MayhemPlayers;
            int active = System.Linq.Enumerable.Count(mayhemPlayers, p => !p.Benched && !p.Withdrawn);
            foreach (var player in mayhemPlayers)
            {
                var identity = Find(player.EntityId);
                var row = _rows[player.EntityId];
                row.text = $"{identity.Label} · {MenuRoster.Name(identity.Class).ToUpperInvariant()}     {player.Score}\n"
                    + (player.Withdrawn ? "DISCONNECTED" : player.Benched ? "OUT · BOOTH · BACK NEXT ROUND" : $"{sim.GetState(player.EntityId).DamagePercent}% · ON DECK");
                row.parent.style.opacity = player.Benched ? .55f : 1f;
                string stamp = "";
                float fuse = -1f;
                foreach (var hb in sim.Resolver.GetActiveHitboxes())
                    if (hb.OwnerId == ServerSimulation.MayhemHostEntityId && hb.AttachedEntityId == player.EntityId)
                    {
                        stamp = $"STUCK! {Mathf.CeilToInt(hb.FuseTicksLeft / 60f)}…";
                        fuse = hb.FuseTicksLeft / (float)Math.Max(1, (int)hb.StickyFuseTicks);
                    }
                if (stamp.Length == 0 && _scoreUntil.TryGetValue(player.EntityId, out uint until) && sim.CurrentTick < until)
                    stamp = _scoreText[player.EntityId];
                var fighter = sim.GetState(player.EntityId);
                if (stamp.Length == 0 && !player.Benched && !fighter.IsGrounded && fighter.HitstunTicks > 0)
                    stamp = "LAUNCHED · RECOVER!";
                if (stamp.Length == 0 && !player.Benched && state.Phase is MankiMayhemPhase.Pattern or MankiMayhemPhase.Settle
                    && active == 1)
                    stamp = "LAST ONE STANDING!";
                var label = _stamps[player.EntityId];
                float blink = fuse >= 0f && !ClientSettingsService.Instance.ReducedFlashing
                    ? .5f + .5f * Mathf.Sin(sim.CurrentTick / 60f * Mathf.Lerp(26f, 8f, fuse)) : 0f;
                label.style.backgroundColor = Color.Lerp(new Color(1f, .76f, .15f), new Color(1f, .3f, .12f), blink);
                label.text = stamp;
                if (camera == null || stamp.Length == 0 || state.Ended) { label.style.display = DisplayStyle.None; continue; }
                var point = camera.WorldToViewportPoint(position(player.EntityId) + Vector3.up * 2f);
                label.style.display = point.z > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                label.style.left = Length.Percent(point.x * 100f);
                label.style.top = Length.Percent((1f - point.y) * 100f);
            }
        }

        public static string CardTitle(MankiMayhemCard card) => card == MankiMayhemCard.Jump ? "JUMP!"
            : card == MankiMayhemCard.BreakOut ? "BREAK OUT!" : "SCATTER!";

        public static string CardSubtitle(MankiMayhemCard card) => card == MankiMayhemCard.Jump ? "SHOCKWAVES · JUMP OVER THE RINGS"
            : card == MankiMayhemCard.BreakOut ? "SMASH A WALL · THE BOMB IS COMING" : "STICKY BOMBS · DON'T BE THE ONE HOLDING IT";

        public HUDManager.HudPlayer Find(ulong id)
        {
            foreach (var player in _players) if (player.EntityId == id) return player;
            throw new ArgumentException($"No Mayhem HUD fighter {id}.");
        }

        public void Dispose() => _root.RemoveFromHierarchy();
    }
}
