using UnityEngine;
using System.Collections.Generic;
using SlopArena.Shared;
namespace SlopArena.Client.Animation
{
    /// <summary>
    /// Maps animation clips to the names PlayerRenderer looks up via GetClipByName().
    ///
    /// Standard clips (idle/run/jump/fall/dash/hit/tumble/death) are named fields — every character
    /// always has these, and the generator wires them automatically.
    ///
    /// Ability clips are character-specific: wire them in the AbilityClips list using the exact
    /// AnimationNames strings from CookedSlotDefinition (e.g. "spell_q_start", "spell_r_loop").
    /// Adding a new character means creating a config asset and filling the list — no code changes.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterAnimConfig", menuName = "SLOPArena/Character Animation Config")]
    public class CharacterAnimationConfig : ScriptableObject
    {
        [System.Serializable]
        public struct AbilityClipEntry
        {
            public string Name;
            public AnimationClip Clip;
            public ExtrapolationMode Extrapolation;
        }

        // ── Standard clips (always present, generator-wired) ──

        [Header("Movement")]
        public AnimationClip Idle;
        public AnimationClip Run;
        public AnimationClip JumpUp;
        public AnimationClip JumpDown;
        public AnimationClip Tumble;
        public AnimationClip Fall;

        [Header("Combat")]
        public AnimationClip Dash;
        public AnimationClip HitSmall;
        public AnimationClip HitMedium;
        public AnimationClip HitHard;
        public AnimationClip Death;

        // ── Ability clips (character-specific, fill per character) ──

        [Header("Ability Clips")]
        [Tooltip("One entry per AnimationNames string in CookedSlotDefinition. Key must match exactly.")]
        public List<AbilityClipEntry> AbilityClips = new();

        // ── Lookup ──

        private Dictionary<string, AnimationClip> _abilityLookup;

        private void OnEnable() => BuildLookup();
        private void OnValidate() => BuildLookup();

        private void BuildLookup()
        {
            _abilityLookup = new Dictionary<string, AnimationClip>(AbilityClips?.Count ?? 0);
            if (AbilityClips == null) return;
            foreach (var entry in AbilityClips)
                if (!string.IsNullOrEmpty(entry.Name) && entry.Clip != null)
                    _abilityLookup[entry.Name] = entry.Clip;
        }

        /// <summary>
        /// Look up a clip by animation name (matches AnimationNames strings in CookedSlotDefinition).
        /// Checks standard clips first, then the ability dictionary.
        /// Returns null if not found.
        /// </summary>
        public AnimationClip GetClipByName(string name)
        {
            // Standard clips — checked first, zero allocation
            switch (name)
            {
                case "idle":        return Idle;
                case "run":         return Run;
                case "jump_up":     return JumpUp;
                case "jump_down":   return JumpDown;
                case "jump":        return JumpUp ?? JumpDown; // backward compat
                case "dash":        return Dash;
                case "fall":        return Fall;
                case "tumble":      return Tumble;
                case "hit_small":
                case "hit_light":   return HitSmall;
                case "hit_medium":  return HitMedium;
                case "hit_hard":    return HitHard;
                case "death":       return Death;
            }

            // Ability clips — character-specific dictionary
            if (_abilityLookup == null) BuildLookup();
            return _abilityLookup!.TryGetValue(name, out var clip) ? clip : null;
        }
    }
}
