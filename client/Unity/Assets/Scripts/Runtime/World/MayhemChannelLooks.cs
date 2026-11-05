using System;
using System.Collections.Generic;
using SlopArena.Shared.Mayhem;
using UnityEngine;

namespace SlopArena.Client.World
{
    public enum MayhemFloorPattern { Checker, Stripes, Rings, Target }
    public enum MayhemFloorDecal { Target, Star, PoolRules }
    public enum MayhemBulbChase { Forward, Alternating, Outward }
    public enum MayhemDeckBeat { Call, Play, Pass, Fail, SpeedUp, Ended, Safe, Warning, Watching }

    public readonly struct ChannelLook
    {
        public readonly Color A, B, C, Accent, Spot;
        public readonly MayhemFloorPattern Pattern;
        public readonly MayhemFloorDecal Decal;
        public readonly MayhemBulbChase Chase;
        public ChannelLook(Color a, Color b, Color c, Color accent, Color spot,
            MayhemFloorPattern pattern, MayhemBulbChase chase, MayhemFloorDecal decal)
        { A = a; B = b; C = c; Accent = accent; Spot = spot; Pattern = pattern; Chase = chase; Decal = decal; }
    }

    public static class MayhemChannelLooks
    {
        private static Color Hex(uint rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        private static readonly IReadOnlyDictionary<string, ChannelLook> Looks = new Dictionary<string, ChannelLook>
        {
            ["mayhem.channel.dodge"] = new(Hex(0x334875), Hex(0xA55750), Hex(0x26344E), Hex(0xFFBB82), Hex(0xFFB55A), MayhemFloorPattern.Checker, MayhemBulbChase.Forward, MayhemFloorDecal.Target),
            ["mayhem.channel.jump"] = new(Hex(0x286569), Hex(0x375146), Hex(0x60867B), Hex(0x9FF4CA), Hex(0x75DDE5), MayhemFloorPattern.Rings, MayhemBulbChase.Outward, MayhemFloorDecal.Star),
            ["mayhem.channel.break-out"] = new(Hex(0x494859), Hex(0x8A6849), Hex(0x383443), Hex(0xFFD080), Hex(0xFFB55A), MayhemFloorPattern.Stripes, MayhemBulbChase.Alternating, MayhemFloorDecal.Star),
            ["mayhem.channel.spot"] = new(Hex(0x4A385C), Hex(0x685637), Hex(0x3E3449), Hex(0xEAC6FF), Hex(0xFFD77B), MayhemFloorPattern.Checker, MayhemBulbChase.Alternating, MayhemFloorDecal.Star),
            ["mayhem.channel.no-running"] = new(Hex(0x23676E), Hex(0x397D81), Hex(0x285B65), Hex(0x93E4DF), Hex(0x75DDE5), MayhemFloorPattern.Checker, MayhemBulbChase.Forward, MayhemFloorDecal.PoolRules),
            ["mayhem.channel.break-boards"] = new(Hex(0x79504B), Hex(0xAC8059), Hex(0x493D46), Hex(0xFFD49A), Hex(0xFFD08A), MayhemFloorPattern.Stripes, MayhemBulbChase.Forward, MayhemFloorDecal.Star),
            ["mayhem.channel.prime-time"] = new(Hex(0x704E87), Hex(0x96524F), Hex(0x433455), Hex(0xFFD36A), Hex(0xDB9AFF), MayhemFloorPattern.Target, MayhemBulbChase.Outward, MayhemFloorDecal.Star),
        };
        public static ChannelLook For(ChannelEntry channel) => Looks[channel?.CopyKey ?? "mayhem.channel.spot"];
        public static void Validate()
        {
            foreach (var channel in MayhemChannels.All)
                if (!Looks.ContainsKey(channel.CopyKey)) throw new InvalidOperationException("Missing Mayhem ChannelLook for " + channel.CopyKey);
            if (Looks.Count != MayhemChannels.All.Count) throw new InvalidOperationException("Mayhem look table has an unregistered channel.");
        }
        public static MayhemDeckBeat Beat(MayhemShowView view)
        {
            var phase = view.Cursor.Phase;
            if (phase == MayhemPhase.Play && view.Channel?.Id == MayhemChannels.NoRunning)
            {
                for (int i = 0; i < view.Scene.Watches.Count; i++)
                {
                    var watch = view.Scene.Watches[i];
                    if (view.Tick >= watch.StartTick && view.Tick < watch.EndTick) return MayhemDeckBeat.Watching;
                    if (view.Tick >= watch.WarningTick && view.Tick < watch.StartTick) return MayhemDeckBeat.Warning;
                }
                return MayhemDeckBeat.Safe;
            }
            if (phase == MayhemPhase.Verdict)
            {
                for (int i = 0; i < view.Contestants.Count; i++)
                    if (view.Contestants[i].Verdict == MayhemVerdict.Passed) return MayhemDeckBeat.Pass;
                return MayhemDeckBeat.Fail;
            }
            return phase switch
            {
                MayhemPhase.Play => MayhemDeckBeat.Play,
                MayhemPhase.SpeedUp => MayhemDeckBeat.SpeedUp,
                MayhemPhase.Ended => MayhemDeckBeat.Ended,
                _ => MayhemDeckBeat.Call,
            };
        }
    }
}
