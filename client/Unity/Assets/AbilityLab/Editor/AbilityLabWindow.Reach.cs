using System;
using System.Globalization;
using System.Linq;
using System.Text;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    internal string CurrentReachAction => CurrentSharedAction();
    private Button? _reachMeasure;
    private Toggle? _reachShow;
    private Label? _reachSummary;
    private bool _reachBusy;
    private string _reachError = "";

    private void BindReach()
    {
        _reachMeasure = Required<Button>("reach-measure");
        _reachShow = Required<Toggle>("reach-show");
        _reachSummary = Required<Label>("reach-summary");
        _reachShow.RegisterValueChangedCallback(_ => Repaint());
        _reachMeasure.clicked += () =>
        {
            _reachMeasure.Blur();
            _reachBusy = true;
            RefreshReachPanel();
            _root.schedule.Execute(() =>
            {
                try { RunReachForCommand(CurrentSharedAction()); }
                catch (Exception ex) { _reachError = ex.Message; }
                finally { _reachBusy = false; RefreshReachPanel(); Repaint(); }
            });
        };
    }

    internal AbilityLabReachObservation RunReachForCommand(string action)
    {
        if (!CanonicalSlotProjection.TryGet(action, out var slot))
            throw new AbilityLabReachException("reach.action.invalid", "Choose a canonical move such as ground.1 or air.R.");
        try
        {
            var result = _workspace.MeasureReach(slot);
            _reachError = "";
            RefreshReachPanel();
            Repaint();
            return result;
        }
        catch (Exception ex)
        {
            _reachError = ex.Message;
            RefreshReachPanel();
            throw;
        }
    }

    private void RefreshReachPanel()
    {
        if (_reachSummary == null || _reachMeasure == null) return;
        string action = CurrentSharedAction();
        _reachMeasure.SetEnabled(!_reachBusy && !EditorApplication.isPlayingOrWillChangePlaymode &&
            _workspace.HasPackage && !_workspace.LiveDraftInvalid && !_workspace.PreviewPrerequisitesStale &&
            _workspace.Preview?.IsAvailable == true && CanonicalSlotProjection.TryGet(action, out _));
        if (!_workspace.ReachMatchesSelection(action))
        {
            _reachSummary.text = _reachError.Length > 0 ? _reachError : "Reach measures the selected live draft. No result for this move.";
            return;
        }
        var reach = _workspace.Reach!;
        var move = reach.Move;
        var text = new StringBuilder();
        text.AppendLine($"{move.Slot} · {move.Name} · Live draft" + (_workspace.IsReachCurrent(action) ? "" : " · STALE, run Reach again"));
        text.AppendLine($"{reach.ElapsedMs:0.0} ms · Touch at 1 m vs FightGuy: {move.ContactTicksAtProbe}/{move.ActiveTicks} active ticks");
        foreach (var target in reach.Document.Targets.Keys)
        {
            text.AppendLine(target + (target == "FightGuy" ? " □ magenta" : " × cyan"));
            foreach (var band in move.Reach.Where(b => b.Target == target))
                text.AppendLine($"  {band.BearingDegrees:0}°: " + (band.Intervals.Count == 0 ? "No connection in 0.30–4.50 m" :
                    string.Join(", ", band.Intervals.Select(i => $"{i.From:0.00}–{i.To:0.00} m"))));
            foreach (var vertical in move.Vertical.Where(v => v.Target == target))
                text.AppendLine("  Target feet height: " + (vertical.Lowest.HasValue ? $"{vertical.Lowest:0.00}–{vertical.Highest:0.00} m" : "No touch in sampled heights"));
        }
        text.AppendLine("Flags: " + (move.Flags.Count == 0 ? "None" : string.Join("; ", move.Flags)));
        text.AppendLine(move.Air ? "Aerial fixed-origin vertical reach. Panel only." :
            "Arc = target feet placements, fixed attacker, 0.25 m samples.\nFloor = simulation start distance, 0.05 m samples.");
        text.AppendLine("Geometry and touch use the first timeline stage; simulation reach can include later stages and movement.");
        if (move.ActiveHitboxes.Count == 0) text.AppendLine("No authored hitbox geometry in the measured stage.");
        if (_reachError.Length > 0) text.AppendLine(_reachError);
        _reachSummary.text = text.ToString().TrimEnd();
    }

    private void DrawReachOverlay()
    {
        if (Event.current.type != EventType.Repaint || _reachShow?.value != true || _lab == null ||
            _sceneRadiusEditing || !_workspace.ReachMatchesSelection(CurrentSharedAction())) return;
        var observation = _workspace.Reach!;
        if (observation.Move.Air) return;
        bool stale = !_workspace.IsReachCurrent(CurrentSharedAction());
        var matrix = Handles.matrix;
        var color = Handles.color;
        var depth = Handles.zTest;
        try
        {
            Handles.matrix = Matrix4x4.TRS(_lab.transform.position, Quaternion.Euler(0f, _lab.FacingYaw * Mathf.Rad2Deg, 0f), Vector3.one);
            Handles.zTest = CompareFunction.Always;
            Color Tint(string target) => stale ? new Color(.55f, .55f, .55f, .65f) :
                target == "FightGuy" ? new Color(1f, .25f, .75f, .9f) : new Color(.3f, .95f, 1f, .9f);
            foreach (var arc in observation.Move.Arc)
            {
                Handles.color = new Color(.55f, .55f, .55f, .22f);
                for (int r = 0; r < arc.Heights.Count; r++)
                for (int c = 0; c < arc.Distances.Count; c++)
                {
                    var point = new Vector3(0f, arc.Heights[r], arc.Distances[c]);
                    if (arc.Target == "FightGuy")
                    {
                        Handles.color = new Color(.55f, .55f, .55f, .22f);
                        Handles.DrawLine(point - Vector3.up * .025f, point + Vector3.up * .025f);
                    }
                    if (arc.Rows[r][c] != '#') continue;
                    Handles.color = Tint(arc.Target);
                    var up = Vector3.up * .09f;
                    var forward = Vector3.forward * .09f;
                    if (arc.Target == "FightGuy")
                        Handles.DrawPolyLine(point - up - forward, point + up - forward, point + up + forward,
                            point - up + forward, point - up - forward);
                    else
                    {
                        Handles.DrawLine(point - up - forward, point + up + forward);
                        Handles.DrawLine(point - up + forward, point + up - forward);
                    }
                }
                Handles.color = new Color(.65f, .65f, .65f, .5f);
                Handles.DrawLine(new Vector3(0, arc.Heights.Last(), 0), new Vector3(0, arc.Heights.First(), 0));
            }
            foreach (var band in observation.Move.Reach)
            {
                Handles.color = Tint(band.Target);
                var direction = Quaternion.Euler(0, band.BearingDegrees, 0) * Vector3.forward;
                var offset = Vector3.up * .02f + Vector3.right * (band.Target == "FightGuy" ? .04f : -.04f);
                foreach (var interval in band.Intervals)
                {
                    var start = offset + direction * interval.From;
                    var end = offset + direction * interval.To;
                    Handles.DrawAAPolyLine(band.BearingDegrees == 0f ? 4f : 2f, start, end);
                    Handles.DrawLine(start - Vector3.up * .04f, start + Vector3.up * .04f);
                    Handles.DrawLine(end - Vector3.up * .04f, end + Vector3.up * .04f);
                }
            }
            Handles.color = stale ? Color.gray : Color.white;
            Handles.Label(new Vector3(0, 3.2f, 0), stale ? "REACH STALE" : "Reach · □ FightGuy   × Manki\nTarget feet height / distance (m)");
        }
        finally { Handles.matrix = matrix; Handles.color = color; Handles.zTest = depth; }
    }
}
