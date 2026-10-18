using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// A Character Course's generated look and sounds: target, break, checkpoint, beacon and
    /// finish prefabs plus the clip folder. <see cref="CharacterCourseMatch"/> loads it from
    /// Resources/Courses/&lt;stage key&gt;; a course without one keeps plain target orbs and
    /// silent cues. Presentation only: Shared owns hits, saves, falls and the clock.
    /// </summary>
    public sealed class CoursePresentationSet : ScriptableObject
    {
        public const string ResourcesFolder = "Courses";
        [Tooltip("Idle target (a CourseLantern root), built for TargetRadius and scaled to each target's radius.")]
        public GameObject Target;
        public float TargetRadius = .65f;
        [Tooltip("One-shot break effect at the target centre, scaled like the target.")]
        public GameObject Break;
        [Tooltip("A CourseCheckpointMarker, sized to each checkpoint and to the finish.")]
        public GameObject Marker;
        [Tooltip("A CourseBeacon raised over a missed target.")]
        public GameObject MissedBeacon;
        [Tooltip("A CourseBeacon raised at the finish once every target is broken.")]
        public GameObject FinishBeacon;
        [Tooltip("One-shot finish celebration at the fighter.")]
        public GameObject FinishBurst;
        [Tooltip("Resources folder holding course_pop, course_bell, course_chord, course_saved, course_missed, course_all_clear and course_finish.")]
        public string SoundFolder = "Audio/SFX/Course";
        [Tooltip("Semitones above course_bell for the first, second, … break of a section.")]
        public int[] NoteSemitones = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21 };
    }
}
