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
            ["mayhem.channel.get-him"] = ("GET HIM!", "Land two hits on Manki. Dodge his wake-up!"),
            ["mayhem.channel.quick-draw"] = ("QUICK DRAW!", "Wait for DRAW! Then hit a normal!"),
            ["mayhem.channel.hot-potato"] = ("HOT POTATO!", "Bump to pass the bomb. Do not hold it at the bang!"),
            ["mayhem.channel.block"] = ("BLOCK!", "Hold Shield when the barrage hits!"),
            ["mayhem.pair.smile-jump"] = ("Pose + jump", ""),
            ["mayhem.twist.get-him.slam"] = ("slam", "Land your hits, then jump his slam!"),
            ["mayhem.twist.get-him.tantrum"] = ("tantrum", "Land your hits, then run from his bombs!"),
            ["mayhem.twist.quick-draw.showdown"] = ("showdown", ""),
            ["mayhem.twist.quick-draw.fake-out"] = ("fake-out", "Only DRAW! counts. Read the whole word!"),
            ["mayhem.twist.quick-draw.duel"] = ("duel", "Wait for DRAW! The slowest drawer loses too!"),
            ["mayhem.twist.hot-potato.one-potato"] = ("one potato", ""),
            ["mayhem.twist.hot-potato.two-potatoes"] = ("two potatoes", "Two bombs. Pass yours to an empty hand!"),
            ["mayhem.twist.block.barrage"] = ("barrage", ""),
            ["mayhem.twist.block.waves"] = ("waves", "Hold Shield for EVERY blast!"),
            ["mayhem.twist.block.block-or-jump"] = ("block or jump", "Shield the bombs. Jump the rings!"),
            ["mayhem.draw.draw"] = ("DRAW!", ""),
            ["mayhem.draw.drawer"] = ("DRAWER!", ""),
            ["mayhem.draw.drawl"] = ("DRAWL!", ""),
            ["mayhem.channel.dodge"] = ("DODGE!", "Stay out of the bomb rings."),
            ["mayhem.channel.jump"] = ("JUMP!", "Jump over the shockwaves."),
            ["mayhem.channel.break-out"] = ("BREAK OUT!", "Hit the cracked wall. Get out before the bomb!"),
            ["mayhem.channel.spot"] = ("SPOT!", "Be in the spotlight at the buzzer."),
            ["mayhem.channel.no-running"] = ("NO RUNNING!", "Move only when Bashie is not looking."),
            ["mayhem.channel.break-boards"] = ("BREAK THE BOARDS!", "Hit all your wooden boards."),
            ["mayhem.channel.sumo"] = ("SUMO!", "Stay inside the shrinking ring!"),
            ["mayhem.channel.smile"] = ("SMILE!", "Be in the frame at the flash!"),
            ["mayhem.channel.glitch"] = ("GLITCH!", "Green panels vanish. Move or jump!"),
            ["mayhem.channel.bonk"] = ("BONK!", "Hit a Manki head before it ducks!"),
            ["mayhem.twist.sumo.shrink"] = ("shrink", ""),
            ["mayhem.twist.sumo.drift"] = ("drift", "Follow the moving ring!"),
            ["mayhem.twist.smile.squeeze"] = ("squeeze", ""),
            ["mayhem.twist.smile.pan"] = ("pan", "Follow the frame. Pose at the flash!"),
            ["mayhem.twist.glitch.scatter"] = ("scatter", ""),
            ["mayhem.twist.glitch.sweep"] = ("sweep", "Find safe panels or jump!"),
            ["mayhem.twist.glitch.checker"] = ("checker", ""),
            ["mayhem.twist.bonk.whack"] = ("whack", ""),
            ["mayhem.twist.bonk.decoys"] = ("decoys", "Hit heads. Avoid the lit fuses!"),
            ["mayhem.channel.prime-time"] = ("PRIME TIME!", "Two channels. One verdict."),
            ["mayhem.twist.dodge.aimed"] = ("aimed", ""),
            ["mayhem.twist.dodge.bomb-rain"] = ("bomb rain", ""),
            ["mayhem.twist.dodge.big-one"] = ("big one", ""),
            ["mayhem.twist.jump.rings"] = ("rings", ""),
            ["mayhem.twist.jump.crossfire"] = ("crossfire", ""),
            ["mayhem.twist.jump.tall-wave"] = ("tall wave", "Jump TWICE over the tall wave!"),
            ["mayhem.twist.break-out.cracked-wall"] = ("cracked wall", ""),
            ["mayhem.twist.break-out.any-wall"] = ("any wall", "Hit any wall. Get out before the bomb!"),
            ["mayhem.twist.break-out.double-cage"] = ("double cage", "Break through BOTH cages!"),
            ["mayhem.twist.spot.still"] = ("still", ""),
            ["mayhem.twist.spot.drifting"] = ("drifting", ""),
            ["mayhem.twist.spot.musical"] = ("musical", "Only the LAST spotlight counts!"),
            ["mayhem.twist.no-running.looks"] = ("looks", ""),
            ["mayhem.twist.no-running.fake-out"] = ("fake-out", ""),
            ["mayhem.twist.no-running.rapid"] = ("rapid", ""),
            ["mayhem.twist.break-boards.your-stack"] = ("your stack", ""),
            ["mayhem.twist.break-boards.scattered"] = ("scattered", ""),
            ["mayhem.pair.break-out-dodge"] = ("Escape + dodge", ""),
            ["mayhem.pair.spot-jump"] = ("Hold spots + jump", ""),
        };
        private static readonly IReadOnlyDictionary<string, string> Guests = new Dictionary<string, string>
        {
            ["mayhem.channel.no-running"] = "bashie",
            ["mayhem.channel.break-boards"] = "fightguy",
            ["mayhem.channel.bonk"] = "bonk",
            ["mayhem.channel.smile"] = "nyx",
            ["mayhem.channel.glitch"] = "wibou",
        };
        public static string GuestFor(ChannelEntry channel)
            => channel != null && Guests.TryGetValue(channel.CopyKey, out var guest) ? guest : null;
        private readonly Dictionary<string, FighterUiArt> _guestArt = new();
        public Texture2D GuestPortrait(ChannelEntry channel)
        {
            string id = GuestFor(channel);
            return id != null && _guestArt.TryGetValue(id, out var art) ? art.Portrait() : null;
        }
        public static void ValidateCopy()
        {
            var keys = MayhemChannels.All.SelectMany(c => new[] { c.CopyKey }.Concat(c.Twists.Select(t => t.CopyKey)))
                .Concat(PrimePairs.All.Select(p => p.CopyKey))
                .Concat(new[] { "mayhem.draw.draw", "mayhem.draw.drawer", "mayhem.draw.drawl" }).ToArray();
            foreach (var key in keys)
                if (!Copy.TryGetValue(key, out var row) || string.IsNullOrWhiteSpace(row.Title))
                    throw new InvalidOperationException("Missing Mayhem copy for " + key);
            foreach (var channel in MayhemChannels.All)
                if (string.IsNullOrWhiteSpace(Copy[channel.CopyKey].Line))
                    throw new InvalidOperationException("Missing Mayhem subtitle for " + channel.CopyKey);
            if (keys.Length != Copy.Count) throw new InvalidOperationException("Mayhem copy has an unregistered key.");
        }
        public static string CardTitle(ChannelEntry channel) => channel == null ? "MANKI MAYHEM" : Copy[channel.CopyKey].Title;
        public static string CardSubtitle(ChannelEntry channel) => channel == null ? "Pass each channel to earn ratings." : Copy[channel.CopyKey].Line;
        public static string TwistLine(MayhemShowView view) => view.Airing is SoloAiring solo ? Copy[solo.TwistCopyKey].Title : "";
        public static string CardLine(MayhemShowView view)
        {
            if (view.Airing is RemixAiring remix)
                return Copy[remix.PairCopyKey].Title + "\n"
                    + CardTitle(MayhemChannels.Find(remix.Goal.Channel)) + " " + Copy[remix.GoalTwistCopyKey].Title + "\n"
                    + CardTitle(MayhemChannels.Find(remix.Hazard.Channel)) + " " + Copy[remix.HazardTwistCopyKey].Title;
            if (view.Airing is not SoloAiring solo) return CardSubtitle(view.Channel);
            var row = Copy[solo.TwistCopyKey];
            return row.Title + "\n" + (row.Line.Length > 0 ? row.Line : CardSubtitle(view.Channel));
        }
        public static string DrawWord(MayhemShowView view)
        {
            foreach (var cue in view.Scene.Watches)
                if ((cue.Kind == MayhemWatchKind.Draw || cue.Kind == MayhemWatchKind.FakeDraw)
                    && view.Tick >= cue.StartTick && view.Tick < cue.EndTick)
                    return Copy[cue.CopyKey].Title;
            return "WAIT...";
        }
        public static float FakeTurn(MayhemShowView view)
        {
            foreach (var cue in view.Scene.Watches)
                if (cue.Kind == MayhemWatchKind.FakeLook && view.Tick >= cue.WarningTick && view.Tick < cue.EndTick)
                    return Mathf.Sin(Mathf.PI * (view.Tick - cue.WarningTick) / Mathf.Max(1f, cue.EndTick - cue.WarningTick));
            return 0;
        }
        public static string FailureText(MayhemFailure failure) => failure.Cause switch
        {
            FailCause.Fell => "FELL OFF", FailCause.StillCaged => "STILL CAGED", FailCause.OutsideSpot => "NO SPOT",
            FailCause.OutOfRing => "OUT OF THE RING", FailCause.OutOfFrame => "OUT OF FRAME",
            FailCause.HeGotUp => "HE GOT UP", FailCause.Misfire => "MISFIRE", FailCause.TooSlow => "TOO SLOW",
            FailCause.Glitched => "GLITCHED", FailCause.NoBonk => "NO BONK",
            FailCause.Caught => "WRITTEN UP", FailCause.BoardsRemain => "BOARDS STILL UP",
            _ => failure.Style == SplatStyle.Flattened ? "FLATTENED" : failure.Style == SplatStyle.Dizzy ? "DIZZY" : "TOASTED"
        };
        public static string WatchText(MayhemShowView view)
        {
            foreach (var w in view.Scene.Watches)
            {
                if (w.Kind != MayhemWatchKind.Look) continue;
                if (view.Tick >= w.StartTick && view.Tick < w.EndTick) return "WATCHING";
                if (view.Tick >= w.WarningTick && view.Tick < w.StartTick) return "TURNING! STOP!";
            }
            return "NOT LOOKING";
        }
        public MankiMayhemView(VisualElement hud, IReadOnlyList<HUDManager.HudPlayer> players, MatchContentCatalog catalog, FriendlyFireMode friendlyFire = FriendlyFireMode.Shoves)
        {
            foreach (string id in Guests.Values)
            {
                var entry = catalog?.ResolvePackage(id);
                if (entry != null) _guestArt[id] = FighterPresentation.Resolve(entry).Art;
            }

            ValidateCopy();
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
                var portrait = new Image { image = player.Art.Portrait(), scaleMode = ScaleMode.ScaleToFit };
                portrait.AddToClassList("mayhem-portrait"); row.Add(portrait);
                var label = Text("", "mayhem-player"); row.Add(label); _rows.Add(player.EntityId, label); _scores.Add(row);
                var stamp=Text("","mayhem-stamp"); stamp.style.display=DisplayStyle.None; _root.Add(stamp); _stamps.Add(player.EntityId,stamp);
            }
            var banner = new VisualElement(); banner.AddToClassList("mayhem-call");
            banner.style.left = Length.Percent(27); banner.style.right = Length.Percent(27);
            _root.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var inset = evt.newRect.width < 600 ? new StyleLength(12) : new StyleLength(Length.Percent(27));
                banner.style.left = banner.style.right = inset;
            });
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
            _subtitle.text = c.Phase == MayhemPhase.SpeedUp ? $"ACT {c.Act + 1} · FASTER CHANNELS!" : CardLine(view);
            bool draw = view.Channel?.Id == MayhemChannels.QuickDraw && c.Phase == MayhemPhase.Play;
            if (draw) _banner.text = DrawWord(view);
            _banner.style.color = draw ? (DrawWord(view) == "DRAW!" ? new Color(0, .38f, .08f) : new Color(.55f, .12f, 0)) : new Color(.12f, .06f, .07f);
            bool watch = view.Scene.Watches.Any(w => w.Kind == MayhemWatchKind.Look || w.Kind == MayhemWatchKind.FakeLook);
            if (watch) _subtitle.text = WatchText(view) + " · " + CardLine(view);
            _subtitle.style.opacity = watch && WatchText(view)=="TURNING! STOP!" && !ClientSettingsService.Instance.ReducedFlashing ? .65f+.35f*Mathf.Sin(view.Tick*1.2f) : 1f;
            _subtitle.style.color = watch ? (WatchText(view) == "NOT LOOKING" ? new Color(0,.4f,.12f) : new Color(.7f,0,0)) : new Color(.12f,.06f,.07f);
            var guest = GuestFor(view.Channel);
            _guest.image = GuestPortrait(view.Channel);
            _guest.style.display = guest == null ? DisplayStyle.None : DisplayStyle.Flex;
            _banner.parent.style.display = c.Phase == MayhemPhase.Ended ? DisplayStyle.None : DisplayStyle.Flex;
            _fuse.style.width = Length.Percent(c.Phase == MayhemPhase.Play ? 100f * (c.Deadline - Math.Min(view.Tick,c.Deadline)) / Math.Max(1u,c.Deadline-c.PlayStart) : 100);
            bool gamepad = SlopArena.Client.Input.HumanInputActions.LastUsedGamepad;
            int revision = SlopArena.Client.Input.HumanInputActions.BindingRevision;
            var extra = view.Airing?.Extra ?? ExtraVerb.None;
            if (_extra != extra || revision != _bindingRevision || gamepad != _gamepad)
            {
                _extra = extra; _bindingRevision = revision; _gamepad = gamepad; _chips.Clear();
                _chips.Add(HUDManager.MayhemChip(gamepad ? "MoveStick" : "MoveUp", "Move")); _chips.Add(HUDManager.MayhemChip("Jump", "Jump"));
                _chips.Add(HUDManager.MayhemChip("Normal1", "Hit"));
                if (extra != ExtraVerb.None) _chips.Add(HUDManager.MayhemChip(extra == ExtraVerb.Specials ? "SlotA" : extra == ExtraVerb.Beeline ? "Dash" : extra.ToString(), extra.ToString()));
            }
            _scores.style.display = c.Phase == MayhemPhase.Ended ? DisplayStyle.None : DisplayStyle.Flex;
            var occupied = new List<Rect>();
            foreach (var player in view.Contestants.OrderBy(p => p.Seat))
            {
                var identity = Find(player.EntityId);
                string status = player.Withdrawn ? "DISCONNECTED" : player.Verdict == MayhemVerdict.Passed ? "ON AIR!" : player.Failure is { } f ? "CANCELLED · " + FailureText(f) : player.ObjectiveMet ? "✓ READY" : "ON DECK";
                foreach (var progress in view.Scene.HitProgress)
                    if (progress.Seat == player.Seat) status += $" · HITS {progress.Hits}/{progress.Quota}";
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
                _rows[player.EntityId].text = $"{identity.Label} · {identity.Art.Name().ToUpperInvariant()}   {player.Ratings} ★\n{status}";
                _rows[player.EntityId].style.color = player.Failure != null ? new Color(1,.4f,.25f) : Color.white;
            }
        }
        public HUDManager.HudPlayer Find(ulong id) => _players.First(p => p.EntityId == id);
        public void Dispose() => _root.RemoveFromHierarchy();
    }
}
