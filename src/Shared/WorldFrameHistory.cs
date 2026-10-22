using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// The GameHost's presentation events and hits of the last
    /// <see cref="WorldFramePacket.EventHistoryTicks"/> ticks: each <see cref="WorldFramePacket"/>
    /// carries the complete record of those ticks. Events the frame wire cannot carry are dropped
    /// here (and counted), so building a frame from this history never throws.
    /// </summary>
    public sealed class WorldFrameHistory
    {
        private readonly List<TimelinePresentationEvent> _events = new();
        private readonly List<SpellResolver.HitResult> _hits = new();
        private readonly List<TimelinePresentationEvent> _frameEvents = new();
        private readonly List<SpellResolver.HitResult> _frameHits = new();
        private readonly List<CourseTickEvent> _courses = new();
        private readonly List<CourseTickEvent> _frameCourses = new();
        private readonly List<SlopballTickEvent> _slopball = new();
        private readonly List<SlopballTickEvent> _frameSlopball = new();

        /// <summary>Events refused because the frame wire cannot encode them (cosmetic only).</summary>
        public int DroppedEvents { get; private set; }

        /// <summary>Add what the simulation produced at <paramref name="tick"/>.</summary>
        public void Record(uint tick, IReadOnlyList<TimelinePresentationEvent> events, IReadOnlyList<SpellResolver.HitResult> hits,
            IReadOnlyList<SlopballEvent>? slopballEvents = null, IReadOnlyList<CourseTickEvent>? courseEvents = null)
        {
            uint from = WorldFramePacket.EventsFrom(tick);
            _courses.RemoveAll(e => e.MatchTick < from || e.MatchTick > tick);
            if (courseEvents != null) _courses.AddRange(courseEvents);
            _events.RemoveAll(e => e.MatchTick < from || e.MatchTick > tick);
            _hits.RemoveAll(h => h.MatchTick < from || h.MatchTick > tick);
            _slopball.RemoveAll(e => e.MatchTick < from || e.MatchTick > tick);
            if (slopballEvents != null)
                foreach (var item in slopballEvents)
                    _slopball.Add(new SlopballTickEvent(tick, item));
            foreach (var evt in events)
            {
                if (evt.MatchTick < from || evt.MatchTick > tick)
                    continue;
                if (WorldFramePacket.CanCarry(evt))
                    _events.Add(evt);
                else
                    DroppedEvents++;
            }
            foreach (var hit in hits)
                if (hit.MatchTick >= from && hit.MatchTick <= tick)
                    _hits.Add(hit);
        }

        /// <summary>The events a frame of <paramref name="tick"/> carries (newest kept if over the
        /// frame limit); the list is reused by the next call.</summary>
        public IReadOnlyList<TimelinePresentationEvent> EventsForFrame(uint tick)
        {
            uint from = WorldFramePacket.EventsFrom(tick);
            _frameEvents.Clear();
            foreach (var evt in _events)
                if (evt.MatchTick >= from && evt.MatchTick <= tick)
                    _frameEvents.Add(evt);
            if (_frameEvents.Count > WorldFramePacket.MaxPresentationEvents)
                _frameEvents.RemoveRange(0, _frameEvents.Count - WorldFramePacket.MaxPresentationEvents);
            return _frameEvents;
        }

        /// <summary>The hits a frame of <paramref name="tick"/> carries (newest kept if over the
        /// frame limit); the list is reused by the next call.</summary>
        public IReadOnlyList<SpellResolver.HitResult> HitsForFrame(uint tick)
        {
            uint from = WorldFramePacket.EventsFrom(tick);
            _frameHits.Clear();
            foreach (var hit in _hits)
                if (hit.MatchTick >= from && hit.MatchTick <= tick)
                    _frameHits.Add(hit);
            if (_frameHits.Count > WorldFramePacket.MaxHits)
                _frameHits.RemoveRange(0, _frameHits.Count - WorldFramePacket.MaxHits);
            return _frameHits;
        }

        /// <summary>The Slopball events a frame of <paramref name="tick"/> carries (newest kept if over the
        /// frame limit); the list is reused by the next call.</summary>
        public IReadOnlyList<SlopballTickEvent> SlopballEventsForFrame(uint tick)
        {
            uint from = WorldFramePacket.EventsFrom(tick);
            _frameSlopball.Clear();
            foreach (var item in _slopball)
                if (item.MatchTick >= from && item.MatchTick <= tick)
                    _frameSlopball.Add(item);
            if (_frameSlopball.Count > WorldFramePacket.MaxSlopballEvents)
                _frameSlopball.RemoveRange(0, _frameSlopball.Count - WorldFramePacket.MaxSlopballEvents);
            return _frameSlopball;
        }

        public IReadOnlyList<CourseTickEvent> CourseEventsForFrame(uint tick)
        {
            _frameCourses.Clear();
            uint from = WorldFramePacket.EventsFrom(tick);
            foreach (var item in _courses)
                if (item.MatchTick >= from && item.MatchTick <= tick) _frameCourses.Add(item);
            if (_frameCourses.Count > WorldFramePacket.MaxCourseEvents)
                _frameCourses.RemoveRange(0, _frameCourses.Count - WorldFramePacket.MaxCourseEvents);
            return _frameCourses;
        }

        public void Clear()
        {
            _events.Clear();
            _hits.Clear();
            _slopball.Clear();
            _courses.Clear();
        }
    }
}
