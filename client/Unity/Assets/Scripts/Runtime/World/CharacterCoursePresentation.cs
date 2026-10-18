using System;
using System.Collections.Generic;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The Character Course's feel: targets, breaks with a section melody, bonus targets that react to
    /// each hit, checkpoint frames, missed-target cues with light pillars, ALL CLEAR! and the finish. Sections are the target
    /// ranges between checkpoint prerequisites. It reads Shared course state and events after
    /// each tick and never feeds back: the missed cue repeats Shared's sensor test on the
    /// authoritative fighter position, only to explain why a checkpoint did not save.
    /// </summary>
    internal sealed class CharacterCoursePresentation : IDisposable
    {
        private const int Voices = 8;
        private const float CueCooldown = 2.5f;
        private const float FloorFallback = .85f;
        private readonly CoursePresentationSet _set;
        private readonly CharacterCourseDefinition _course;
        private readonly CharacterCourseView _view;
        private readonly HUDManager _hud;
        private readonly Func<UnityEngine.Camera> _camera;
        private readonly Transform _root;
        private readonly Transform[] _targets;
        private readonly CoursePresentationSet.TargetLook[] _looks;
        private readonly CourseBonusTarget[] _bonus;
        private readonly Dictionary<string, AudioClip> _lookClips = new();
        private readonly int[] _sectionOf, _sectionStart, _sectionEnd;
        private readonly bool[] _broken, _flagged, _inside, _lockedSeen;
        private readonly float[] _cueReady;
        private readonly CourseBeacon[] _beacons;
        private readonly CourseCheckpointMarker[] _markers;
        private readonly CourseCheckpointMarker _finishMarker;
        private readonly List<GameObject> _effects = new();
        private readonly List<(float at, AudioClip clip, float volume, float pitch)> _queued = new();
        private readonly List<Vector3> _flaggedPositions = new();
        private readonly AudioSource[] _voices = new AudioSource[Voices];
        private readonly AudioClip _pop, _bell, _chord, _saved, _missed, _gong, _finishGong;
        private readonly Material _orb, _orbCore;
        private readonly Dictionary<string, int> _played = new();
        private CourseBeacon _shrine;
        private Vector3? _lastPosition;
        private bool _insideFinish, _finishLocked, _cleared;
        private float _finishCueReady;

        public IReadOnlyList<Transform> Targets => _targets;

        /// <summary>For verification commands: the look, loaded clips, sounds played this attempt and live cues.</summary>
        public object Diagnostics
        {
            get
            {
                int clips = 0, beacons = 0;
                foreach (var clip in new[] { _pop, _bell, _chord, _saved, _missed, _gong, _finishGong }) if (clip != null) clips++;
                foreach (var clip in _lookClips.Values) if (clip != null) clips++;
                foreach (var beacon in _beacons) if (beacon != null) beacons++;
                var markers = new string[_markers.Length];
                for (int i = 0; i < markers.Length; i++) markers[i] = _markers[i] != null ? _markers[i].Current.ToString() : null;
                return new
                {
                    look = _set != null ? _set.name : "orbs", clips, played = new Dictionary<string, int>(_played), beacons,
                    shrine = _shrine != null, markers, finish = _finishMarker != null ? _finishMarker.Current.ToString() : null,
                };
            }
        }

        public CharacterCoursePresentation(CoursePresentationSet set, CharacterCourseDefinition course, Transform parent,
            CharacterCourseView view, HUDManager hud, Func<UnityEngine.Camera> camera)
        {
            _set = set; _course = course; _view = view; _hud = hud; _camera = camera;
            _root = new GameObject("Course Presentation").transform;
            _root.SetParent(parent, false);
            int count = course.Targets.Count;
            _broken = new bool[count]; _flagged = new bool[count]; _beacons = new CourseBeacon[count];
            _inside = new bool[course.Checkpoints.Count]; _lockedSeen = new bool[course.Checkpoints.Count];
            _cueReady = new float[course.Checkpoints.Count];

            // Sections end at each distinct checkpoint prerequisite; the last one ends at the finish.
            var ends = new List<int>();
            foreach (var checkpoint in course.Checkpoints)
                if (checkpoint.RequiredTargetCount > (ends.Count > 0 ? ends[^1] : 0) && checkpoint.RequiredTargetCount < count)
                    ends.Add(checkpoint.RequiredTargetCount);
            ends.Add(count);
            _sectionStart = new int[ends.Count]; _sectionEnd = ends.ToArray(); _sectionOf = new int[count];
            var sizes = new int[ends.Count];
            for (int s = 0, start = 0; s < ends.Count; start = ends[s], s++)
            {
                _sectionStart[s] = start; sizes[s] = ends[s] - start;
                for (int i = start; i < ends[s]; i++) _sectionOf[i] = s;
            }
            _view.ConfigurePips(sizes);

            _targets = new Transform[count];
            _looks = new CoursePresentationSet.TargetLook[count];
            _bonus = new CourseBonusTarget[count];
            if (set != null && set.Target != null) CreateLanterns();
            else (_orb, _orbCore) = CreateOrbs();
            _markers = new CourseCheckpointMarker[course.Checkpoints.Count];
            for (int i = 0; i < _markers.Length; i++)
                _markers[i] = CreateMarker("Checkpoint " + course.Checkpoints[i].Id, course.Checkpoints[i].Volume);
            _finishMarker = CreateMarker("Finish", course.Finish);

            string folder = set != null ? set.SoundFolder : null;
            AudioClip Load(string name) => string.IsNullOrEmpty(folder) ? null : Resources.Load<AudioClip>($"{folder}/{name}");
            _pop = Load("course_pop"); _bell = Load("course_bell"); _chord = Load("course_chord"); _saved = Load("course_saved");
            _missed = Load("course_missed"); _gong = Load("course_all_clear"); _finishGong = Load("course_finish");
            foreach (var look in _looks)
                foreach (string clip in new[] { look?.HitClip, look?.BreakClip })
                    if (!string.IsNullOrEmpty(clip) && !_lookClips.ContainsKey(clip)) _lookClips[clip] = Load(clip);
            var audio = new GameObject("Course Audio");
            audio.transform.SetParent(_root, false);
            var bus = ClientSettingsService.Instance.FindBus("SFX");
            for (int i = 0; i < Voices; i++)
            {
                var voice = audio.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 0f; voice.outputAudioMixerGroup = bus;
                _voices[i] = voice;
            }
        }

        private Transform Viewer => _camera()?.transform;

        private void CreateLanterns()
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _course.Targets[i];
                var look = _set.LookFor(target.Id);
                _looks[i] = look != null && look.Target != null ? look : null;
                var prefab = _looks[i]?.Target ?? _set.Target;
                float authored = _looks[i]?.TargetRadius ?? _set.TargetRadius;
                var view = Object.Instantiate(prefab, _root);
                view.name = "Course Target " + target.Id;
                view.transform.SetPositionAndRotation(Position(target.Position), prefab.transform.rotation);
                view.transform.localScale = Vector3.one * (target.Radius / Mathf.Max(.01f, authored));
                if (view.TryGetComponent<CourseLantern>(out var lantern)) lantern.Init(Viewer, i * 2.39996f);
                if (view.TryGetComponent<CourseBonusTarget>(out var bonus)) { bonus.Init(i * 2.39996f); _bonus[i] = bonus; }
                _targets[i] = view.transform;
            }
        }

        /// <summary>The plain gold orbs of a course without generated presentation.</summary>
        private (Material, Material) CreateOrbs()
        {
            var orb = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            orb.SetColor("_BaseColor", new Color(1, .75f, .15f));
            var core = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            core.SetColor("_BaseColor", new Color(.82f, .18f, .18f));
            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _course.Targets[i];
                var view = GameObject.CreatePrimitive(PrimitiveType.Sphere); view.name = "Course Target " + target.Id;
                view.transform.SetParent(_root, false); view.transform.position = Position(target.Position);
                view.transform.localScale = Vector3.one * target.Radius * 2f;
                Object.Destroy(view.GetComponent<Collider>()); view.GetComponent<Renderer>().sharedMaterial = orb;
                var inner = GameObject.CreatePrimitive(PrimitiveType.Sphere); inner.name = "Target Core";
                inner.transform.SetParent(view.transform, false); inner.transform.localScale = new(.72f, .72f, 1.02f);
                Object.Destroy(inner.GetComponent<Collider>()); inner.GetComponent<Renderer>().sharedMaterial = core;
                _targets[i] = view.transform;
            }
            return (orb, core);
        }

        private CourseCheckpointMarker CreateMarker(string name, ArcadeVolume volume)
        {
            if (_set == null || _set.Marker == null) return null;
            var instance = Object.Instantiate(_set.Marker, _root);
            instance.name = name;
            instance.transform.position = Floor(volume.Center, volume.HalfSize.Y);
            if (!instance.TryGetComponent<CourseCheckpointMarker>(out var marker)) return null;
            marker.Setup(HalfSize(volume));
            return marker;
        }

        /// <summary>A fresh attempt: every target back, no cues, effects or queued sounds.</summary>
        public void Reset()
        {
            foreach (var target in _targets) target.gameObject.SetActive(true);
            foreach (var bonus in _bonus) if (bonus != null) bonus.ResetDamage();
            for (int i = 0; i < _beacons.Length; i++)
            {
                if (_beacons[i] != null) Object.Destroy(_beacons[i].gameObject);
                _beacons[i] = null;
            }
            if (_shrine != null) Object.Destroy(_shrine.gameObject);
            _shrine = null;
            foreach (var effect in _effects) if (effect != null) Object.Destroy(effect);
            _effects.Clear(); _queued.Clear(); _played.Clear();
            foreach (var voice in _voices) voice.Stop();
            Array.Clear(_broken, 0, _broken.Length); Array.Clear(_flagged, 0, _flagged.Length);
            Array.Clear(_inside, 0, _inside.Length); Array.Clear(_lockedSeen, 0, _lockedSeen.Length);
            Array.Clear(_cueReady, 0, _cueReady.Length);
            for (int c = 0; c < _markers.Length; c++)
                _markers[c]?.Setup(HalfSize(_course.Checkpoints[c].Volume));
            _finishMarker?.Setup(HalfSize(_course.Finish));
            _lastPosition = null; _insideFinish = _finishLocked = _cleared = false; _finishCueReady = 0f;
            UISFX.ResetMusicLift();
            Present();
        }

        private static Vector2 HalfSize(ArcadeVolume volume) => new(volume.HalfSize.X, volume.HalfSize.Z);

        /// <summary>Consumes one completed Shared tick: its course events and the fighter's position.</summary>
        public void AfterTick(ServerSimulation simulation, in CharacterState fighter)
        {
            var state = simulation.Course.Value;
            for (int i = 0; i < _broken.Length; i++) _broken[i] = simulation.IsCourseTargetBroken(i);
            var events = simulation.LastTickCourseEvents;
            int pendingBreaks = 0;
            foreach (var e in events) if (e.Kind == CharacterCourseEventKind.TargetBroken) pendingBreaks++;
            bool respawned = false;
            var position = new Vector3(fighter.PX, fighter.PY, fighter.PZ);
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case CharacterCourseEventKind.TargetBroken:
                        pendingBreaks--;
                        Break(e.Index, state.BrokenTargets - pendingBreaks, state.TotalTargets);
                        break;
                    case CharacterCourseEventKind.TargetHit:
                        BonusHit(e.Index, simulation.CourseTargetHitsLeft(e.Index));
                        break;
                    case CharacterCourseEventKind.CheckpointSaved:
                        if (e.Index >= 0 && e.Index < _markers.Length) _markers[e.Index]?.Saved();
                        Play(_saved, .5f);
                        break;
                    case CharacterCourseEventKind.Respawned:
                        respawned = true;
                        break;
                    case CharacterCourseEventKind.Completed:
                        Finish(position);
                        break;
                }
            }
            if (state.Phase == CharacterCoursePhase.Running && !respawned && _lastPosition is Vector3 previous)
                DetectMissed(state, previous, position);
            _lastPosition = respawned ? null : position;
            Present(state);
        }

        private void Break(int index, int broken, int total)
        {
            var target = _course.Targets[index];
            var position = Position(target.Position);
            var look = _looks[index];
            float scale = _set != null ? target.Radius / Mathf.Max(.01f, look?.TargetRadius ?? _set.TargetRadius) : 1f;
            Spawn(look?.Break ?? _set?.Break, position, scale, look != null ? 4f : 3f);
            ReleaseBeacon(index);
            _flagged[index] = false;
            int section = _sectionOf[index], inSection = 0;
            for (int i = _sectionStart[section]; i < _sectionEnd[section]; i++) if (_broken[i]) inSection++;
            Play(LookClip(look?.BreakClip) ?? _pop, look != null ? .85f : .7f, UnityEngine.Random.Range(.96f, 1.04f));
            if (_set != null && _set.NoteSemitones.Length > 0)
                Play(_bell, .5f, Mathf.Pow(2f, _set.NoteSemitones[Mathf.Clamp(inSection - 1, 0, _set.NoteSemitones.Length - 1)] / 12f));
            _view.ShowCountPop(position + Vector3.up * target.Radius * 1.7f, $"{broken}/{total}");
            if (inSection < _sectionEnd[section] - _sectionStart[section]) return;
            if (broken >= total) { AllClear(); return; }
            Queue(_chord, .25f, .45f);
            int missing = 0, checkpoint = -1;
            for (int i = 0; i < _sectionStart[section]; i++) if (!_broken[i]) missing++;
            for (int c = 0; c < _course.Checkpoints.Count && checkpoint < 0; c++)
                if (_course.Checkpoints[c].RequiredTargetCount == _sectionEnd[section]) checkpoint = c;
            _view.ShowToast($"SECTION {section + 1} CLEAR",
                missing > 0 ? (missing == 1 ? "1 earlier target is still missing." : $"{missing} earlier targets are still missing.")
                    : checkpoint >= 0 ? $"Checkpoint {checkpoint + 1} is lit: it will save." : "Head for the finish.", false);
        }

        /// <summary>A bonus target took a hit that did not break it: its next damage stage, a jolt and a rising thud.</summary>
        private void BonusHit(int index, int hitsLeft)
        {
            var target = _course.Targets[index];
            int taken = target.Hits - hitsLeft;
            _bonus[index]?.Hit(taken, target.Hits);
            var look = _looks[index];
            var position = Position(target.Position);
            float scale = _set != null ? target.Radius / Mathf.Max(.01f, look?.TargetRadius ?? _set.TargetRadius) : 1f;
            Spawn(look?.Hit, position, scale, 1.5f);
            Play(LookClip(look?.HitClip) ?? _pop, .55f, Mathf.Pow(2f, (taken - 1) / 12f));
            _view.ShowCountPop(position + Vector3.up * target.Radius * 1.4f, $"{taken}/{target.Hits}", "course-count-pop--hit");
        }

        private AudioClip LookClip(string name)
            => !string.IsNullOrEmpty(name) && _lookClips.TryGetValue(name, out var clip) ? clip : null;

        private void AllClear()
        {
            _cleared = true;
            Queue(_gong, .3f, .85f);
            _hud?.ShowSoloStamp("ALL CLEAR!", 1.6f);
            UISFX.LiftMusic();
            if (_set != null && _set.FinishBeacon != null && _shrine == null)
            {
                var instance = Object.Instantiate(_set.FinishBeacon, Floor(_course.Finish.Center, _course.Finish.HalfSize.Y), Quaternion.identity, _root);
                instance.name = "Shrine Pillar";
                _shrine = instance.TryGetComponent<CourseBeacon>(out var shrine) ? shrine : null;
                _shrine?.Init(Viewer);
            }
        }

        private void Finish(Vector3 position)
        {
            Spawn(_set?.FinishBurst, position, 1f, 6f);
            Play(_finishGong, .8f);
            _shrine?.Release();
            _shrine = null;
        }

        private void DetectMissed(CharacterCourseState state, Vector3 previous, Vector3 position)
        {
            float now = Time.time;
            for (int c = 0; c < _inside.Length; c++)
            {
                var checkpoint = _course.Checkpoints[c];
                bool inside = Crosses(checkpoint.Volume, previous, position);
                if (inside && !_inside[c] && c > state.SavedCheckpoint && now >= _cueReady[c])
                {
                    int missing = Flag(checkpoint.RequiredTargetCount);
                    if (missing > 0)
                    {
                        _lockedSeen[c] = true; _cueReady[c] = now + CueCooldown;
                        Play(_missed, .65f);
                        _view.ShowToast(missing == 1 ? "1 TARGET MISSED" : $"{missing} TARGETS MISSED", FollowLine(missing), true);
                        _view.FlashCheckpointNotSaved();
                    }
                }
                _inside[c] = inside;
            }
            bool finish = Crosses(_course.Finish, previous, position);
            if (finish && !_insideFinish && state.BrokenTargets < state.TotalTargets && now >= _finishCueReady)
            {
                int missing = Flag(_broken.Length);
                _finishLocked = true; _finishCueReady = now + CueCooldown;
                Play(_missed, .65f);
                _view.ShowToast(missing == 1 ? "1 TARGET LEFT" : $"{missing} TARGETS LEFT", FollowLine(missing), true);
            }
            _insideFinish = finish;
        }

        private static string FollowLine(int missing) => missing == 1 ? "Follow the red light back to it." : "Follow the red lights back to them.";

        /// <summary>Raises a pillar over each unbroken target in the first <paramref name="required"/>; returns how many.</summary>
        private int Flag(int required)
        {
            int missing = 0;
            for (int i = 0; i < required && i < _broken.Length; i++)
            {
                if (_broken[i]) continue;
                missing++;
                _flagged[i] = true;
                if (_beacons[i] != null || _set == null || _set.MissedBeacon == null) continue;
                var instance = Object.Instantiate(_set.MissedBeacon, _targets[i].position, Quaternion.identity, _root);
                instance.name = "Missed Beacon " + _course.Targets[i].Id;
                _beacons[i] = instance.TryGetComponent<CourseBeacon>(out var beacon) ? beacon : null;
                _beacons[i]?.Init(Viewer);
            }
            return missing;
        }

        private void ReleaseBeacon(int index)
        {
            _beacons[index]?.Release();
            _beacons[index] = null;
        }

        private void Present(CharacterCourseState? state = null)
        {
            int saved = state?.SavedCheckpoint ?? -1;
            bool ended = state?.Ended ?? false;
            for (int c = 0; c < _markers.Length; c++)
            {
                if (_markers[c] == null) continue;
                bool eligible = true;
                for (int i = 0; i < _course.Checkpoints[c].RequiredTargetCount; i++) eligible &= _broken[i];
                _markers[c].Show(c <= saved || ended ? CourseCheckpointMarker.Look.Hidden
                    : eligible ? CourseCheckpointMarker.Look.Armed
                    : _lockedSeen[c] ? CourseCheckpointMarker.Look.Locked : CourseCheckpointMarker.Look.Hidden);
            }
            _finishMarker?.Show(_finishLocked && !_cleared && !ended ? CourseCheckpointMarker.Look.Locked : CourseCheckpointMarker.Look.Hidden);
            _view.PresentPips(_broken, _flagged);
        }

        /// <summary>Per frame: delayed sounds and the edge markers toward missed targets.</summary>
        public void Update()
        {
            for (int i = _queued.Count - 1; i >= 0; i--)
            {
                if (Time.time < _queued[i].at) continue;
                var (_, clip, volume, pitch) = _queued[i];
                _queued.RemoveAt(i);
                Play(clip, volume, pitch);
            }
            _flaggedPositions.Clear();
            for (int i = 0; i < _flagged.Length; i++)
                if (_flagged[i] && !_broken[i]) _flaggedPositions.Add(_targets[i].position);
            _view.PresentEdgeMarkers(_camera(), _flaggedPositions);
        }

        private void Spawn(GameObject prefab, Vector3 position, float scale, float seconds)
        {
            if (prefab == null) return;
            var instance = Object.Instantiate(prefab, position, Quaternion.identity, _root);
            instance.transform.localScale = Vector3.one * scale;
            Object.Destroy(instance, seconds);
            _effects.RemoveAll(x => x == null);
            _effects.Add(instance);
        }

        private void Queue(AudioClip clip, float delay, float volume, float pitch = 1f)
        {
            if (clip != null) _queued.Add((Time.time + delay, clip, volume, pitch));
        }

        private void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null) return;
            AudioSource voice = null;
            foreach (var candidate in _voices) if (!candidate.isPlaying) { voice = candidate; break; }
            if (voice == null)
            {
                voice = _voices[0];
                foreach (var candidate in _voices) if (candidate.time > voice.time) voice = candidate;
            }
            voice.clip = clip; voice.volume = volume; voice.pitch = pitch;
            voice.Play();
            _played.TryGetValue(clip.name, out int count);
            _played[clip.name] = count + 1;
        }

        private static Vector3 Position(ArcadePoint point) => new(point.X, point.Y, point.Z);

        /// <summary>The floor under a sensor volume, or a hip-height guess when no stage floor is there.</summary>
        private static Vector3 Floor(ArcadePoint center, float halfHeight)
        {
            float fallback = center.Y - FloorFallback;
            float y = PresentationFloor.TrySample(center.X, center.Z, out float floor)
                && floor <= center.Y + halfHeight && floor >= center.Y - halfHeight - .5f ? floor : fallback;
            return new Vector3(center.X, y, center.Z);
        }

        /// <summary>Shared's swept sensor test (ArcadeVolume.Crosses), repeated for presentation only.</summary>
        private static bool Crosses(ArcadeVolume volume, Vector3 start, Vector3 end)
        {
            float enter = 0f, exit = 1f;
            return Axis(start.x, end.x, volume.Center.X, volume.HalfSize.X, ref enter, ref exit)
                && Axis(start.y, end.y, volume.Center.Y, volume.HalfSize.Y, ref enter, ref exit)
                && Axis(start.z, end.z, volume.Center.Z, volume.HalfSize.Z, ref enter, ref exit);
        }

        private static bool Axis(float start, float end, float center, float half, ref float enter, ref float exit)
        {
            float delta = end - start;
            if (Mathf.Abs(delta) < .000001f) return start >= center - half && start <= center + half;
            float a = (center - half - start) / delta, b = (center + half - start) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        public void Dispose()
        {
            UISFX.ResetMusicLift();
            if (_root != null) Object.Destroy(_root.gameObject);
            if (_orb != null) Object.Destroy(_orb);
            if (_orbCore != null) Object.Destroy(_orbCore);
        }
    }
}
