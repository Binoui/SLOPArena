using SlopArena.Client.Animation;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>Confirmed impact audio selected by the admitted attacker catalog.</summary>
    public sealed class CombatSFX : MonoBehaviour
    {
        private AudioSource _source;
        private int _sequence;

        public void Play(ImpactTier tier, CharacterAnimationCatalog owner)
        {
            if (_source == null || owner?.PresentationProfile == null) return;
            AudioClip clip = owner.PresentationProfile.SelectImpact(tier, ref _sequence);
            if (clip != null) _source.PlayOneShot(clip);
        }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
            _source.volume = 0.8f;
        }
    }
}
