using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Shared.Telemetry;

namespace SlopArena.SessionLogReport;

/// <summary>Aggregates of one or more practice session logs, from the player's point of view.</summary>
internal sealed class ReportModel
{
    public List<SessionSummary> Sessions = new();
    public OutcomeRow Overall = new() { Label = "All swings" };
    public List<OutcomeRow> ByDevice = new();
    public List<OutcomeRow> Moves = new();
    public List<OutcomeRow> AttackerHeight = new();
    public List<OutcomeRow> TargetMotion = new();
    public List<AngleRow> CameraAngles = new();
    public List<DeviceRow> Devices = new();
    public List<MissPoint> MissPoints = new();
    public List<string> Notes = new();
}

internal sealed class SessionSummary
{
    public string File = "";
    public string Started = "";
    public string Mode = "";
    public string Arena = "";
    public string Player = "";
    public string Opponents = "";
    public double Minutes;
    public double GamepadShare;
    public bool Truncated;
    public int Swings;
}

/// <summary>Outcomes of accepted player swings that had a target in range.</summary>
internal sealed class OutcomeRow
{
    public string Label = "";
    public int Presses;
    public int Accepted;
    public int Hits;
    public int Blocked;
    public int Whiffs;
    /// <summary>Accepted, but no hitbox was ever tested (interrupted, cancelled, or no hitbox).</summary>
    public int NoHitbox;
    /// <summary>Accepted, but no fighter was selected or within range when pressed.</summary>
    public int NoTarget;
    public int NearMisses;
    public List<float> WhiffGaps = new();
    public SortedDictionary<string, int> MissDirections = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> CameraAxes = new(StringComparer.Ordinal);

    public int Measured => Hits + Blocked + Whiffs;
    public double ContactRate => Measured == 0 ? 0 : (Hits + Blocked) / (double)Measured;
    public double? MedianWhiffGap => Median(WhiffGaps);

    internal static double? Median(List<float> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(x => x).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}

/// <summary>Contact rate by the angle between the camera's view and the line to the target.</summary>
internal sealed class AngleRow
{
    public string Label = "";
    public float MinDegrees, MaxDegrees;
    public int Contacts;
    public int Whiffs;
    public int DepthMisses;
    public int Measured => Contacts + Whiffs;
    public double ContactRate => Measured == 0 ? 0 : Contacts / (double)Measured;
}

internal sealed class DeviceRow
{
    public string Device = "";
    public double Minutes;
    public double OffscreenShare;
    public double EdgeShare;
    public double MouseDegreesPerSecond;
    public double StickDegreesPerSecond;
    public double StickSaturatedShare;
    /// <summary>Of the saturated-stick frames, the share where the opponent was off-screen or at the edge.</summary>
    public double SaturatedWhileTargetLostShare;
    public int HitsTaken;
    public int HitsTakenFromOffscreen;
}

internal readonly record struct MissPoint(float Forward, float Side, float Up, bool Gamepad, bool Air);

internal static class SessionAnalysis
{
    internal const float NearMissMeters = 0.5f;
    internal const float EdgeMargin = 0.1f;
    internal const float StickSaturated = 0.95f;
    internal const float VerticalMotionThreshold = 1f;
    internal static readonly (string Label, float Min, float Max)[] AngleBins =
    {
        ("0–15°", 0f, 15f), ("15–30°", 15f, 30f), ("30–60°", 30f, 60f), ("60–180°", 60f, 180.01f),
    };

    internal static ReportModel Analyze(IReadOnlyList<SessionLogData> sessions)
    {
        var model = new ReportModel();
        foreach (var bin in AngleBins)
            model.CameraAngles.Add(new AngleRow { Label = bin.Label, MinDegrees = bin.Min, MaxDegrees = bin.Max });
        var moves = new SortedDictionary<string, OutcomeRow>(StringComparer.Ordinal);
        var devices = new Dictionary<bool, OutcomeRow>
        {
            [false] = new OutcomeRow { Label = "Keyboard + mouse" },
            [true] = new OutcomeRow { Label = "Controller" },
        };
        var heights = new Dictionary<bool, OutcomeRow>
        {
            [false] = new OutcomeRow { Label = "Grounded attacks" },
            [true] = new OutcomeRow { Label = "Aerial attacks" },
        };
        var motions = new SortedDictionary<string, OutcomeRow>(StringComparer.Ordinal);
        var deviceTotals = new Dictionary<bool, DeviceAccumulator> { [false] = new(), [true] = new() };

        foreach (var session in sessions)
        {
            ulong player = PlayerId(session.Header);
            var frames = session.Frames.GroupBy(x => x.Tick).ToDictionary(x => x.Key, x => x.Last());
            model.Sessions.Add(Summarize(session, player));
            AccumulateFrames(session, player, deviceTotals);
            AccumulateHitsTaken(session, player, frames, deviceTotals);

            foreach (var swing in session.Swings)
            {
                if (swing.Attacker != player) continue;
                bool gamepad = frames.TryGetValue(swing.StartTick, out var pressFrame) && pressFrame.Gamepad;
                var rows = new List<OutcomeRow>
                {
                    model.Overall,
                    devices[gamepad],
                    heights[swing.Air],
                    Row(moves, MoveLabel(swing.ActiveSlot, swing.Air)),
                };
                if (swing.Accepted && swing.HasClosestApproach)
                    rows.Add(Row(motions, TargetMotion(swing)));
                var outcome = Classify(swing);
                foreach (var row in rows)
                    Add(row, swing, outcome, pressFrame);
                if (outcome == SwingOutcome.Whiff)
                {
                    model.MissPoints.Add(new MissPoint(swing.MissForward, swing.MissSide, swing.MissUp, gamepad, swing.Air));
                }
                if ((outcome is SwingOutcome.Hit or SwingOutcome.Blocked or SwingOutcome.Whiff) && pressFrame != null
                    && TryLineAngle(pressFrame, swing, out float angle))
                {
                    var bin = model.CameraAngles.First(x => angle >= x.MinDegrees && angle < x.MaxDegrees);
                    if (outcome == SwingOutcome.Whiff)
                    {
                        bin.Whiffs++;
                        if (CameraAxis(pressFrame, swing) == "depth") bin.DepthMisses++;
                    }
                    else bin.Contacts++;
                }
            }
        }

        model.ByDevice.AddRange(devices.Values.Where(x => x.Presses > 0));
        model.AttackerHeight.AddRange(heights.Values.Where(x => x.Presses > 0));
        model.Moves.AddRange(moves.Values);
        model.TargetMotion.AddRange(motions.Values);
        foreach (var (gamepad, totals) in deviceTotals)
            if (totals.Frames > 0)
                model.Devices.Add(totals.ToRow(gamepad ? "Controller" : "Keyboard + mouse"));
        if (sessions.Any(x => x.Truncated))
            model.Notes.Add("At least one session ended without its end record (crash or forced quit); its last unsettled swings are missing.");
        return model;
    }

    internal enum SwingOutcome { Rejected, NoTarget, NoHitbox, Hit, Blocked, Whiff }

    internal static SwingOutcome Classify(SwingRecord swing)
    {
        if (!swing.Accepted) return SwingOutcome.Rejected;
        if (swing.Connected) return SwingOutcome.Hit;
        if (swing.Blocked) return SwingOutcome.Blocked;
        if (swing.HitboxTicks == 0) return SwingOutcome.NoHitbox;
        if (swing.ReferenceTarget == 0 || !swing.HasClosestApproach) return SwingOutcome.NoTarget;
        return SwingOutcome.Whiff;
    }

    private static void Add(OutcomeRow row, SwingRecord swing, SwingOutcome outcome, SessionFrame? pressFrame)
    {
        row.Presses++;
        if (outcome == SwingOutcome.Rejected) return;
        row.Accepted++;
        switch (outcome)
        {
            case SwingOutcome.Hit: row.Hits++; break;
            case SwingOutcome.Blocked: row.Blocked++; break;
            case SwingOutcome.NoHitbox: row.NoHitbox++; break;
            case SwingOutcome.NoTarget: row.NoTarget++; break;
            case SwingOutcome.Whiff:
                row.Whiffs++;
                row.WhiffGaps.Add(swing.ClosestGap);
                if (swing.ClosestGap <= NearMissMeters) row.NearMisses++;
                Increment(row.MissDirections, MissDirection(swing));
                if (pressFrame != null) Increment(row.CameraAxes, CameraAxis(pressFrame, swing));
                break;
        }
    }

    /// <summary>
    /// Dominant attacker-frame axis of the closest-approach gap: <c>short</c> (target beyond the
    /// hitbox), <c>past</c> (target nearer or behind it), <c>under</c> (target above, the attack
    /// passed beneath), <c>over</c> (target below), <c>left</c>/<c>right</c> (target to that side).
    /// </summary>
    internal static string MissDirection(SwingRecord swing)
    {
        float forward = Math.Abs(swing.MissForward), side = Math.Abs(swing.MissSide), up = Math.Abs(swing.MissUp);
        if (forward >= side && forward >= up) return swing.MissForward >= 0f ? "short" : "past";
        if (up >= side) return swing.MissUp >= 0f ? "under" : "over";
        return swing.MissSide >= 0f ? "right" : "left";
    }

    /// <summary>Dominant camera axis of the world-space miss: depth (into the screen), lateral or vertical.</summary>
    internal static string CameraAxis(SessionFrame frame, SwingRecord swing)
    {
        var (fx, fz) = HorizontalForward(frame);
        float depth = Math.Abs(swing.MissWorldX * fx + swing.MissWorldZ * fz);
        float lateral = Math.Abs(swing.MissWorldX * fz - swing.MissWorldZ * fx);
        float vertical = Math.Abs(swing.MissWorldY);
        if (depth >= lateral && depth >= vertical) return "depth";
        return vertical >= lateral ? "vertical" : "lateral";
    }

    /// <summary>Angle, degrees 0–180, between the camera's horizontal view and attacker → target.</summary>
    internal static bool TryLineAngle(SessionFrame frame, SwingRecord swing, out float degrees)
    {
        degrees = 0f;
        if (!TryFighter(frame, swing.Attacker, out var attacker) || !TryFighter(frame, swing.ReferenceTarget, out var target))
            return false;
        float dx = target.PX - attacker.PX, dz = target.PZ - attacker.PZ;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (length < 0.05f) return false;
        var (fx, fz) = HorizontalForward(frame);
        float dot = (dx * fx + dz * fz) / length;
        degrees = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
        return true;
    }

    internal static string TargetMotion(SwingRecord swing)
    {
        if (swing.TargetGroundedAtClosest) return "Target grounded";
        if (swing.TargetVYAtClosest > VerticalMotionThreshold) return "Target rising";
        if (swing.TargetVYAtClosest < -VerticalMotionThreshold) return "Target falling";
        return "Target near apex";
    }

    internal static string MoveLabel(byte activeSlot, bool air)
    {
        string label = CanonicalSlotProjection.TryGet(activeSlot, air, out var address)
            ? address.InputLabel : "slot " + activeSlot;
        return (air ? "air." : "ground.") + label;
    }

    /// <summary>Viewport position (0–1, origin bottom-left) of a world point in the frame's camera.</summary>
    internal static bool TryViewport(SessionFrame frame, float x, float y, float z, out float u, out float v)
    {
        u = v = 0f;
        if (frame.VerticalFov <= 0f || frame.Aspect <= 0f) return false;
        Basis(frame, out var right, out var up, out var forward);
        float px = x - frame.CamX, py = y - frame.CamY, pz = z - frame.CamZ;
        float depth = px * forward.X + py * forward.Y + pz * forward.Z;
        if (depth <= 0.01f) return false;
        float tan = MathF.Tan(frame.VerticalFov * 0.5f * MathF.PI / 180f);
        float ndcX = (px * right.X + py * right.Y + pz * right.Z) / depth / (tan * frame.Aspect);
        float ndcY = (px * up.X + py * up.Y + pz * up.Z) / depth / tan;
        u = (ndcX + 1f) * 0.5f;
        v = (ndcY + 1f) * 0.5f;
        return true;
    }

    internal enum Visibility { Unknown, OnScreen, Edge, Offscreen }

    internal static Visibility Visible(SessionFrame frame, in SessionFighterFrame fighter)
    {
        if (frame.VerticalFov <= 0f) return Visibility.Unknown;
        if (!TryViewport(frame, fighter.PX, fighter.PY, fighter.PZ, out float u, out float v)) return Visibility.Offscreen;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return Visibility.Offscreen;
        return u < EdgeMargin || u > 1f - EdgeMargin || v < EdgeMargin || v > 1f - EdgeMargin
            ? Visibility.Edge : Visibility.OnScreen;
    }

    internal static ulong PlayerId(SessionHeader header)
        => header.Fighters.FirstOrDefault(x => x.Role == "player")?.EntityId ?? 1ul;

    /// <summary>The fighter the player is watching: their target if set, otherwise the nearest other fighter.</summary>
    internal static bool TryOpponent(SessionFrame frame, ulong player, out SessionFighterFrame opponent)
    {
        opponent = default;
        if (!TryFighter(frame, player, out var self)) return false;
        if (self.Target != 0 && TryFighter(frame, self.Target, out opponent)) return true;
        float best = float.MaxValue;
        bool found = false;
        foreach (var other in frame.Fighters)
        {
            if (other.Id == player) continue;
            float dx = other.PX - self.PX, dz = other.PZ - self.PZ;
            float distance = dx * dx + dz * dz;
            if (distance < best) { best = distance; opponent = other; found = true; }
        }
        return found;
    }

    private static bool TryFighter(SessionFrame frame, ulong id, out SessionFighterFrame fighter)
    {
        foreach (var candidate in frame.Fighters)
            if (candidate.Id == id) { fighter = candidate; return true; }
        fighter = default;
        return false;
    }

    private sealed class DeviceAccumulator
    {
        public int Frames, Offscreen, Edge, Saturated, SaturatedLost, HitsTaken, HitsTakenOffscreen;
        public double MouseDegrees, StickDegrees;

        public DeviceRow ToRow(string device) => new()
        {
            Device = device,
            Minutes = Frames / 3600.0,
            OffscreenShare = Share(Offscreen, Frames),
            EdgeShare = Share(Edge, Frames),
            MouseDegreesPerSecond = Frames == 0 ? 0 : MouseDegrees / (Frames / 60.0),
            StickDegreesPerSecond = Frames == 0 ? 0 : StickDegrees / (Frames / 60.0),
            StickSaturatedShare = Share(Saturated, Frames),
            SaturatedWhileTargetLostShare = Share(SaturatedLost, Saturated),
            HitsTaken = HitsTaken,
            HitsTakenFromOffscreen = HitsTakenOffscreen,
        };
    }

    private static void AccumulateFrames(SessionLogData session, ulong player, Dictionary<bool, DeviceAccumulator> totals)
    {
        foreach (var frame in session.Frames)
        {
            var device = totals[frame.Gamepad];
            device.Frames++;
            device.MouseDegrees += frame.MouseLookDegrees;
            device.StickDegrees += frame.StickLookDegrees;
            var visibility = TryOpponent(frame, player, out var opponent) ? Visible(frame, opponent) : Visibility.Unknown;
            if (visibility == Visibility.Offscreen) device.Offscreen++;
            if (visibility == Visibility.Edge) device.Edge++;
            if (frame.StickPeak >= StickSaturated)
            {
                device.Saturated++;
                if (visibility is Visibility.Offscreen or Visibility.Edge) device.SaturatedLost++;
            }
        }
    }

    /// <summary>Damaging hits on the player, and whether the attacker was off-screen when it pressed.</summary>
    private static void AccumulateHitsTaken(SessionLogData session, ulong player,
        Dictionary<int, SessionFrame> frames, Dictionary<bool, DeviceAccumulator> totals)
    {
        var swings = session.Swings.Where(x => x.Accepted && x.ActivationId != 0)
            .GroupBy(x => (x.Attacker, x.ActivationId)).ToDictionary(x => x.Key, x => x.First());
        foreach (var hit in session.Hits)
        {
            if (hit.Target != player || hit.Blocked || hit.Damage <= 0f) continue;
            int pressTick = swings.TryGetValue((hit.Attacker, hit.ActivationId), out var swing) ? swing.StartTick : hit.Tick;
            if (!frames.TryGetValue(pressTick, out var frame) && !frames.TryGetValue(hit.Tick, out frame)) continue;
            var device = totals[frame.Gamepad];
            device.HitsTaken++;
            if (TryFighter(frame, hit.Attacker, out var attacker) && Visible(frame, attacker) == Visibility.Offscreen)
                device.HitsTakenOffscreen++;
        }
    }

    private static SessionSummary Summarize(SessionLogData session, ulong player)
    {
        var header = session.Header;
        int frames = session.Frames.Count;
        return new SessionSummary
        {
            File = System.IO.Path.GetFileName(session.Source),
            Started = header.StartedUtc,
            Mode = header.Mode,
            Arena = header.Arena,
            Player = header.Fighters.FirstOrDefault(x => x.EntityId == player)?.Character ?? "?",
            Opponents = string.Join(", ", header.Fighters.Where(x => x.EntityId != player)
                .Select(x => x.CpuDifficulty == null ? x.Character : $"{x.Character} ({x.CpuDifficulty})")),
            Minutes = frames / 3600.0,
            GamepadShare = Share(session.Frames.Count(x => x.Gamepad), frames),
            Truncated = session.Truncated,
            Swings = session.Swings.Count(x => x.Attacker == player),
        };
    }

    private static (float X, float Z) HorizontalForward(SessionFrame frame)
    {
        Basis(frame, out _, out _, out var forward);
        float length = MathF.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
        return length < 0.0001f ? (0f, 1f) : (forward.X / length, forward.Z / length);
    }

    /// <summary>Camera axes from the frame quaternion (Unity convention: forward +Z, right +X, up +Y).</summary>
    private static void Basis(SessionFrame frame, out (float X, float Y, float Z) right,
        out (float X, float Y, float Z) up, out (float X, float Y, float Z) forward)
    {
        float x = frame.CamQX, y = frame.CamQY, z = frame.CamQZ, w = frame.CamQW;
        right = (1f - 2f * (y * y + z * z), 2f * (x * y + w * z), 2f * (x * z - w * y));
        up = (2f * (x * y - w * z), 1f - 2f * (x * x + z * z), 2f * (y * z + w * x));
        forward = (2f * (x * z + w * y), 2f * (y * z - w * x), 1f - 2f * (x * x + y * y));
    }

    private static OutcomeRow Row(IDictionary<string, OutcomeRow> rows, string label)
    {
        if (!rows.TryGetValue(label, out var row)) rows[label] = row = new OutcomeRow { Label = label };
        return row;
    }

    private static void Increment(IDictionary<string, int> counts, string key)
        => counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;

    private static double Share(int part, int whole) => whole == 0 ? 0 : part / (double)whole;
}
