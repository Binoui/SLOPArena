using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed class MankiMayhemView : IDisposable
    {
        private readonly VisualElement _root, _scores, _fuse, _chips;
        private readonly Label _round, _banner, _subtitle;
        private readonly Image _guest;
        private readonly Dictionary<ulong, Label> _rows = new();
        private readonly Dictionary<ulong, Label> _stamps = new();
        private readonly IReadOnlyList<HUDManager.HudPlayer> _players;
        private readonly FriendlyFireMode _friendlyFire;
        private ExtraVerb? _extra;
        private int _bindingRevision = -1;
        private bool _gamepad;
        private static readonly Dictionary<string, (string Title, string Line)> Copy = new()
        {
            ["mayhem.channel.dodge"] = ("DODGE!", "Stay out of the bomb rings."),
            ["mayhem.channel.jump"] = ("JUMP!", "Jump over the shockwaves."),
            ["mayhem.channel.break-out"] = ("BREAK OUT!", "Hit the cracked wall. Get out before the bomb!"),
            ["mayhem.channel.spot"] = ("SPOT!", "Be in the spotlight at the buzzer."),
            ["mayhem.channel.no-running"] = ("NO RUNNING!", "Move only when Bashie is not looking."),
            ["mayhem.channel.break-boards"] = ("BREAK THE BOARDS!", "Hit all your wooden boards."),
            ["mayhem.channel.prime-time"] = ("PRIME TIME!", "Break out AND dodge the bombs!")
        };
        public static string CardTitle(ChannelEntry channel) => channel == null ? "MANKI MAYHEM" : Copy[channel.CopyKey].Title;
        public static string CardSubtitle(ChannelEntry channel) => channel == null ? "Pass each channel to earn ratings." : Copy[channel.CopyKey].Line;
        public static string FailureText(MayhemFailure failure) => failure.Cause switch
        {
            FailCause.Fell => "FELL OFF", FailCause.StillCaged => "STILL CAGED", FailCause.OutsideSpot => "NO SPOT",
            FailCause.Caught => "WRITTEN UP", FailCause.BoardsRemain => "BOARDS STILL UP",
            _ => failure.Style == SplatStyle.Flattened ? "FLATTENED" : failure.Style == SplatStyle.Dizzy ? "DIZZY" : "TOASTED"
        };
        public static string WatchText(MayhemShowView view)
        {
            foreach (var w in view.Scene.Watches)
            {
                if (view.Tick >= w.StartTick && view.Tick < w.EndTick) return "WATCHING";
                if (view.Tick >= w.WarningTick && view.Tick < w.StartTick) return "TURNING! STOP!";
            }
            return "NOT LOOKING";
        }
        public MankiMayhemView(VisualElement hud, IReadOnlyList<HUDManager.HudPlayer> players, FriendlyFireMode friendlyFire = FriendlyFireMode.Shoves)
        {
            _players = players; _friendlyFire = friendlyFire;
            _root = new VisualElement { name = "mayhem-hud", pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.left = _root.style.right = _root.style.top = _root.style.bottom = 0;
            var sheet = Resources.Load<StyleSheet>("UI/MankiMayhem");
            if (sheet != null) _root.styleSheets.Add(sheet);
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
                var label = Text("", "mayhem-player"); row.Add(label); _rows.Add(player.EntityId, label); _scores.Add(row);
                var stamp=Text("","mayhem-stamp"); stamp.style.display=DisplayStyle.None; _root.Add(stamp); _stamps.Add(player.EntityId,stamp);
            }
            var banner = new VisualElement(); banner.AddToClassList("mayhem-call");
            banner.style.left = Length.Percent(27); banner.style.right = Length.Percent(27);
            _guest = new Image { scaleMode = ScaleMode.ScaleToFit }; _guest.style.height = 54; banner.Add(_guest);
            _banner = Text("", "mayhem-call-title"); banner.Add(_banner);
            _subtitle = Text("", "mayhem-call-line"); _subtitle.style.fontSize = 18; banner.Add(_subtitle);
            _chips = new VisualElement(); _chips.style.flexDirection = FlexDirection.Row; _chips.style.justifyContent = Justify.Center; banner.Add(_chips);
            _fuse = new VisualElement(); _fuse.style.height = 8; _fuse.style.backgroundColor = new Color(1f,.18f,.04f); banner.Add(_fuse);
            _root.Add(banner); hud.Add(_root);
        }
        private static Label Text(string text, string style)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(style); return label;
        }
        public void Events(IReadOnlyList<MayhemEvent> events, IReadOnlyList<MayhemEvent> retracted) { }
        public void Reset() { _extra = null; }
        public void Present(ServerSimulation sim, Func<ulong, Vector3> position, UnityEngine.Camera camera)
        {
            var view = sim.MayhemView; if (view == null) return;
            var c = view.Cursor;
            _banner.parent.style.backgroundColor = SlopArena.Client.World.MayhemChannelLooks.For(view.Channel).Accent;
            _round.text = $"ACT {c.Act + 1} / 3 · CHANNEL {(c.HasEpisode ? c.Episode + 1 : 0)} / {MayhemShowTuning.Episodes}\n{_friendlyFire.ToWire().ToUpperInvariant()}";
            if(c.Phase==MayhemPhase.Ended) _round.text="FINAL RATINGS";
            _banner.text = c.Phase == MayhemPhase.Intro ? "YOU'RE ON AIR!" : c.Phase == MayhemPhase.SpeedUp ? "SPEED UP!" : c.Phase == MayhemPhase.Ended ? "THAT'S THE SHOW!" : CardTitle(view.Channel);
            _subtitle.text = c.Phase == MayhemPhase.SpeedUp ? $"ACT {c.Act + 1} · FASTER CHANNELS!" : CardSubtitle(view.Channel);
            bool watch = view.Scene.Watches.Count > 0;
            if (watch) _subtitle.text = WatchText(view) + " · " + CardSubtitle(view.Channel);
            _subtitle.style.opacity = watch && WatchText(view)=="TURNING! STOP!" && !ClientSettingsService.Instance.ReducedFlashing ? .65f+.35f*Mathf.Sin(view.Tick*1.2f) : 1f;
            _subtitle.style.color = watch ? (WatchText(view) == "NOT LOOKING" ? new Color(0,.4f,.12f) : new Color(.7f,0,0)) : new Color(.12f,.06f,.07f);
            var guest = view.Channel?.Id == MayhemChannels.NoRunning ? CharacterClass.Bashie : view.Channel?.Id == MayhemChannels.BreakBoards ? CharacterClass.FightGuy : CharacterClass.None;
            _guest.image = guest == CharacterClass.None ? null : Resources.Load<Texture2D>($"UI/Portraits/{guest}");
            _guest.style.display = guest == CharacterClass.None ? DisplayStyle.None : DisplayStyle.Flex;
            _banner.parent.style.display = c.Phase == MayhemPhase.Ended ? DisplayStyle.None : DisplayStyle.Flex;
            _fuse.style.width = Length.Percent(c.Phase == MayhemPhase.Play ? 100f * (c.Deadline - Math.Min(view.Tick,c.Deadline)) / Math.Max(1u,c.Deadline-c.PlayStart) : 100);
            bool gamepad = SlopArena.Client.Input.HumanInputActions.LastUsedGamepad;
            int revision = SlopArena.Client.Input.HumanInputActions.BindingRevision;
            if (_extra != view.Controls.Extra || revision != _bindingRevision || gamepad != _gamepad)
            {
                _extra = view.Controls.Extra; _bindingRevision = revision; _gamepad = gamepad; _chips.Clear();
                _chips.Add(HUDManager.MayhemChip(gamepad ? "MoveStick" : "MoveUp", "Move")); _chips.Add(HUDManager.MayhemChip("Jump", "Jump"));
                _chips.Add(HUDManager.MayhemChip("Normal1", "Hit"));
                if (view.Controls.Extra != ExtraVerb.None) _chips.Add(HUDManager.MayhemChip(view.Controls.Extra == ExtraVerb.Specials ? "SlotA" : view.Controls.Extra == ExtraVerb.Beeline ? "Dash" : view.Controls.Extra.ToString(), view.Controls.Extra.ToString()));
            }
            _scores.style.display = c.Phase == MayhemPhase.Ended ? DisplayStyle.None : DisplayStyle.Flex;
            var occupied = new List<Rect>();
            foreach (var player in view.Contestants.OrderBy(p => p.Seat))
            {
                var identity = Find(player.EntityId);
                string status = player.Withdrawn ? "DISCONNECTED" : player.Verdict == MayhemVerdict.Passed ? "ON AIR!" : player.Failure is { } f ? "CANCELLED · " + FailureText(f) : player.ObjectiveMet ? "✓ READY" : "ON DECK";
                var stamp=_stamps[player.EntityId];
                stamp.text=player.Failure is {} failure ? "CANCELLED · "+FailureText(failure) : player.Verdict==MayhemVerdict.Passed?"ON AIR!":"";
                var point=camera==null?Vector3.zero:camera.WorldToViewportPoint(position(player.EntityId)+Vector3.up*2f);
                stamp.style.display=stamp.text.Length>0 && point.z>0 && c.Phase!=MayhemPhase.Ended?DisplayStyle.Flex:DisplayStyle.None;
                stamp.style.fontSize=16;
                bool visible = stamp.text.Length > 0 && point.z > 0 && c.Phase != MayhemPhase.Ended;
                if (visible)
                {
                    stamp.text = identity.Label + " · " + stamp.text;
                    float width = Mathf.Max(1, _root.resolvedStyle.width), height = Mathf.Max(1, _root.resolvedStyle.height);
                    const float stampWidth = 270, stampHeight = 34;
                    var rect = new Rect(Mathf.Clamp(point.x * width - stampWidth * .5f, 8, Mathf.Max(8, width - stampWidth - 8)),
                        Mathf.Clamp((1 - point.y) * height, 190, Mathf.Max(190, height - stampHeight - 8)), stampWidth, stampHeight);
                    for (int attempt = 0; attempt < 16; attempt++)
                    {
                        var below = occupied.Where(r => r.Overlaps(rect)).ToArray();
                        if (below.Length == 0) break;
                        rect.y = below.Min(r => r.y) - stampHeight - 6;
                    }
                    occupied.Add(rect);
                    stamp.style.translate = new Translate(0, 0);
                    stamp.style.width = stampWidth;
                    stamp.style.height = stampHeight;
                    stamp.style.left = rect.x; stamp.style.top = rect.y;
                }
                _rows[player.EntityId].text = $"{identity.Label} · {MenuRoster.Name(identity.Class).ToUpperInvariant()}   {player.Ratings} ★\n{status}";
                _rows[player.EntityId].style.color = player.Failure != null ? new Color(1,.4f,.25f) : Color.white;
            }
        }
        public HUDManager.HudPlayer Find(ulong id) => _players.First(p => p.EntityId == id);
        public void Dispose() => _root.RemoveFromHierarchy();
    }
}
