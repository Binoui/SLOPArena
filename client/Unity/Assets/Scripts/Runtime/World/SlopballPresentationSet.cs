using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Slopball's generated feel: the mascot ball, its ground marker and the serve, hit, bounce,
    /// score and miss effects, plus the clip folder. Built by SlopballPresentationBuilder;
    /// <see cref="SlopballMatch"/> loads it from Resources. A court without it keeps the plain
    /// ball and silent cues. Presentation only: Shared owns serves, contacts and scoring.
    /// </summary>
    public sealed class SlopballPresentationSet : ScriptableObject
    {
        public const string ResourcesPath = "Slopball/SlopballPresentation";
        [Tooltip("A SlopballBall root built for BallRadius and scaled to the court's ball.")]
        public GameObject Ball;
        public float BallRadius = .65f;
        [Tooltip("Ring and contact shadow under the ball, built for BallRadius; children named Ring and Shadow.")]
        public GameObject GroundMarker;
        [Tooltip("One-shot puff and POP! at the sender's exit.")]
        public GameObject ServePuff;
        [Tooltip("One-shot burst at a weak hit's contact.")]
        public GameObject HitLight;
        [Tooltip("One-shot burst at a strong hit's contact.")]
        public GameObject HitHeavy;
        [Tooltip("One-shot dust ring where the ball bounces off the cage.")]
        public GameObject Bounce;
        [Tooltip("SLOP! splat on the sender floor when a return scores.")]
        public GameObject Score;
        [Tooltip("Red splat on the player floor when a ball lands there.")]
        public GameObject Miss;
        [Tooltip("Resources folder holding the slopball_* clips (tools/sfx/slopball_clips.py).")]
        public string SoundFolder = "Audio/SFX/Slopball";
        [Tooltip("Semitones above slopball_note for the 1st, 2nd, … return of a combo; it holds the top note after.")]
        public int[] NoteSemitones = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21 };
    }
}
