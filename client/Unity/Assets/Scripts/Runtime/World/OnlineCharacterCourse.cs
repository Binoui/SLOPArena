using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.World
{
    /// <summary>Course presentation in PvPMatch; all progress and outcomes come from Shared.</summary>
    internal sealed class OnlineCharacterCourse : IDisposable
    {
        private readonly CharacterCourseView _view;
        private readonly CharacterCoursePresentation _presentation;
        private readonly CharacterCourseDefinition _definition;
        private readonly Func<UnityEngine.Camera> _camera;
        private readonly VisualElement _board;
        private readonly Dictionary<ulong, (Label label, VisualElement[] pips)> _rows = new();
        private readonly List<CharacterCourseEvent> _events = new();

        public OnlineCharacterCourse(Transform parent, in ArenaDefinition arena, HUDManager hud, Func<UnityEngine.Camera> camera)
        {
            _camera = camera; _definition = arena.Arcade.Course;
            var root = hud.Document.rootVisualElement;
            _view = new CharacterCourseView(root, CharacterCourseMatch.Course(arena.Name), () => { }, () => { }, online: true);
            _presentation = new CharacterCoursePresentation(Resources.Load<CoursePresentationSet>(
                $"{CoursePresentationSet.ResourcesFolder}/{arena.Name}"), _definition, parent, _view, hud, camera);
            _board = new VisualElement { name = "course-online-roster", pickingMode = PickingMode.Ignore };
            _board.AddToClassList("course-online-roster");
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            if (style != null) _board.styleSheets.Add(style);
            foreach (var player in ClientSession.MatchRoster ?? Array.Empty<LobbyPlayerInfo>())
            {
                var row = new VisualElement(); row.AddToClassList("course-online-player");
                var label = new Label { enableRichText = false }; row.Add(label);
                var group = new VisualElement(); group.AddToClassList("course-pips"); row.Add(group);
                var pips = new VisualElement[_definition.Targets.Count];
                for (int i = 0; i < pips.Length; i++)
                { pips[i] = new VisualElement(); pips[i].AddToClassList("course-pip"); group.Add(pips[i]); }
                row.style.borderLeftColor = CourseColours.For(player.Colour);
                _board.Add(row); _rows[(ulong)player.EntityId] = (label, pips);
            }
            _view.AttachOnlineRoster(_board);
        }

        public byte PickTarget(ServerSimulation simulation, UnityEngine.Camera camera, CharacterState owner, PlayerRenderer[] rivals)
        {
            if (camera == null || simulation.IsEntityInactive(owner.EntityId)) return 0;
            byte selected = 0; float best = float.MaxValue;
            void Consider(Transform target, byte alias)
            {
                if (target == null) return;
                var p = target.position; var screen = camera.WorldToViewportPoint(p);
                float dx = p.x - owner.PX, dz = p.z - owner.PZ;
                float score = (screen.x - .5f) * (screen.x - .5f) + (screen.y - .5f) * (screen.y - .5f);
                if (screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1 || dx * dx + dz * dz > 400 || score >= best) return;
                best = score; selected = alias;
            }
            for (int i = 0; i < _presentation.Targets.Count; i++)
                if (!simulation.IsCourseTargetBroken(owner.EntityId, i)) Consider(_presentation.Targets[i], simulation.CourseTargetInputAlias(i));
            if (simulation.OnlineCourse is { Cooperative: false })
                foreach (var rival in rivals)
                    if (rival != null && !simulation.IsEntityInactive(rival.EntityId)) Consider(rival.transform, (byte)rival.EntityId);
            return selected;
        }

        public bool TryTarget(ServerSimulation simulation, CharacterState owner, out Transform target, out float radius)
        {
            target = null; radius = 0;
            for (int i = 0; i < _presentation.Targets.Count; i++)
                if (owner.TargetEntityId == simulation.CourseTargetEntityId(owner.EntityId, i) && !simulation.IsCourseTargetBroken(owner.EntityId, i))
                { target = _presentation.Targets[i]; radius = _definition.Targets[i].Radius; return true; }
            return false;
        }

        public void AfterUpdate(ServerSimulation simulation, IReadOnlyList<CourseTickEvent> events,
            IReadOnlyList<CourseTickEvent> retracted, bool paused)
        {
            ulong self = MatchConfig.LocalEntityId;
            if (simulation.CourseFor(self) is not CharacterCourseState local || simulation.OnlineCourse is not OnlineCourseState run) return;
            if (retracted.Any(e => e.EntityId == 0 || e.EntityId == self))
            { _presentation.Reset(); _view.ResetForRetry(); }
            _events.Clear();
            foreach (var item in events)
                if (item.EntityId == 0 || item.EntityId == self || run.Cooperative && item.Event.Kind == CharacterCourseEventKind.Completed)
                    _events.Add(item.Event);
            _presentation.AfterTick(simulation, simulation.GetState(self), _events);
            for (int i = 0; i < _presentation.Targets.Count; i++)
            {
                var target = _presentation.Targets[i];
                target.gameObject.SetActive(!simulation.IsCourseTargetBroken(self, i));
                if (target.TryGetComponent<CourseBonusTarget>(out var bonus))
                    bonus.PresentDamage(_definition.Targets[i].Hits - simulation.CourseTargetHitsLeft(self, i), _definition.Targets[i].Hits);
            }
            _view.Present(local, paused, null, false, null);
            string status = run.Ended ? "COURSE OVER"
                : run.FinishWindowTicksLeft > 0 ? $"{(run.FinishWindowTicksLeft + 59) / 60}s LEFT TO FINISH"
                : run.Cooperative ? "TEAM UP: CLEAR EVERY TARGET, THEN REACH THE FINISH" : "CLEAR YOUR TARGETS AND REACH THE FINISH FIRST";
            if (local.Phase == CharacterCoursePhase.Completed && !run.Ended) status = "FINISHED · " + status;
            _view.PresentOnlineStatus(status, run.Cooperative ? "CO-OP · SHARED TARGETS" : "RACE · PERSONAL TARGETS · RIVALS CAN HIT YOU");
            foreach (var player in simulation.CoursePlayers)
                if (_rows.TryGetValue(player.EntityId, out var row))
                {
                    var name = ClientSession.MatchRoster?.FirstOrDefault(p => (ulong)p.EntityId == player.EntityId)?.Name ?? "P" + player.EntityId;
                    string progress = player.Place != 0 ? $"#{player.Place} · {CharacterCourseView.FormatTime(player.Course.ElapsedTicks)}"
                        : player.Withdrawn ? "DISCONNECTED" : $"{player.Course.BrokenTargets}/{player.Course.TotalTargets}";
                    row.label.text = name + " · " + progress;
                    for (int i = 0; i < row.pips.Length; i++)
                        row.pips[i].EnableInClassList("course-pip--broken", simulation.IsCourseTargetBroken(player.EntityId, i));
                }
            _board.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
        }
        public void Update()
        { _presentation.Update(); _view.Tick(Time.unscaledDeltaTime, _camera(), false); }
        public void Dispose()
        { _board.RemoveFromHierarchy(); _presentation.Dispose(); _view.Dispose(); }
    }
}
