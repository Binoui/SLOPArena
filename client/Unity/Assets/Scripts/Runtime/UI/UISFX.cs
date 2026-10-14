using UnityEngine;

namespace SlopArena.Client.UI
{
    /// <summary>Shared menu click feedback for UI Toolkit screens.</summary>
    public sealed class UISFX : MonoBehaviour
    {
        private static UISFX _instance;
        private AudioSource _source;
        private AudioSource _musicSource;
        private AudioSource _countdownSource;
        private AudioClip _countdownBeat;
        private AudioClip _countdownStart;
        private AudioClip[] _clicks;
        private AudioClip _menuTheme;
        private AudioClip[] _matchThemes;
        private bool _missingMenuThemeWarningLogged;

        public static void PlayClick()
        {
            EnsureInstance();
            if (_instance._clicks.Length == 0)
                return;

            AudioClip clip = _instance._clicks[Random.Range(0, _instance._clicks.Length)];
            _instance._source.PlayOneShot(clip);
        }

        public static void PlayCountdownBeat(string text)
        {
            EnsureInstance();
            bool start = text == "SLOP IT OUT";
            AudioClip clip = start ? _instance._countdownStart : _instance._countdownBeat;
            if (clip == null) return;
            _instance._countdownSource.pitch = text switch
            {
                "3" => 0.9f, "2" => 1.05f, "1" => 1.22f, "SLOP IT OUT" => 1f, _ => 0.72f
            };
            _instance._countdownSource.PlayOneShot(clip, start ? 0.65f : 1f);
        }

        public static void PlaySoloFinish()
        {
            EnsureInstance();
            if (_instance._countdownStart == null) return;
            _instance._countdownSource.pitch = 0.82f;
            _instance._countdownSource.PlayOneShot(_instance._countdownStart, 0.85f);
        }

        public static void PlayMenuMusic()
        {
            EnsureInstance();
            if (_instance._menuTheme == null)
            {
                if (!_instance._missingMenuThemeWarningLogged)
                {
                    Debug.LogWarning("[UISFX] Menu theme missing at Resources/Audio/Music/MainTheme.");
                    _instance._missingMenuThemeWarningLogged = true;
                }
                return;
            }

            if (_instance._musicSource.isPlaying && _instance._musicSource.clip == _instance._menuTheme)
                return;

            _instance._musicSource.clip = _instance._menuTheme;
            _instance._musicSource.Play();
        }

        public static void PlayMatchMusic()
        {
            EnsureInstance();
            if (_instance._matchThemes.Length == 0)
                return;

            AudioClip clip = _instance._matchThemes[Random.Range(0, _instance._matchThemes.Length)];
            _instance._musicSource.Stop();
            _instance._musicSource.clip = clip;
            _instance._musicSource.Play();
        }

        public static void StopMenuMusic()
        {
            if (_instance == null || _instance._musicSource == null)
                return;
            _instance._musicSource.Stop();
            _instance._musicSource.clip = null;
        }

        private static void EnsureInstance()
        {
            if (_instance != null)
                return;

            var host = new GameObject("UI SFX");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<UISFX>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("UI");
            _source.spatialBlend = 0f;
            _source.volume = 0.55f;

            _countdownSource = gameObject.AddComponent<AudioSource>();
            _countdownSource.playOnAwake = false;
            _countdownSource.spatialBlend = 0f;
            _countdownSource.volume = 0.55f;
            _countdownSource.outputAudioMixerGroup = _source.outputAudioMixerGroup;
            _countdownBeat = Resources.Load<AudioClip>("Audio/SFX/UI/Click/UI_Button_Click_1");
            _countdownStart = Resources.Load<AudioClip>("Audio/SFX/Bonk/impact_heavy");

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = 0.35f;
            _musicSource.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("Music");
            _menuTheme = Resources.Load<AudioClip>("Audio/Music/MainTheme");
            _matchThemes = new[]
            {
                Resources.Load<AudioClip>("Audio/Music/Teeth_in_the_Transmission"),
                Resources.Load<AudioClip>("Audio/Music/Iron_Tooth_Grin"),
                Resources.Load<AudioClip>("Audio/Music/After_the_Bell")
            };

            _clicks = new AudioClip[9];
            for (int i = 0; i < _clicks.Length; i++)
                _clicks[i] = Resources.Load<AudioClip>($"Audio/SFX/UI/Click/UI_Button_Click_{i + 1}");
        }
    }
}

