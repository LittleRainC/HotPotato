using UnityEngine;

namespace Ruilin
{
    /// <summary>
    /// 跨场景音频：BGM / SFX 音量（PlayerPrefs），可选 BGM clip。
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public const string PrefBgm = "audio.bgm";
        public const string PrefSfx = "audio.sfx";

        static AudioManager _instance;

        [SerializeField] AudioSource bgmSource;
        [SerializeField] AudioSource sfxSource;
        [SerializeField] AudioClip bgmClip;
        [SerializeField, Range(0f, 1f)] float bgmVolume = 1f;
        [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;

        public static AudioManager Instance
        {
            get
            {
                if (_instance == null)
                    Ensure();
                return _instance;
            }
        }

        public float BgmVolume
        {
            get => bgmVolume;
            set
            {
                bgmVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(PrefBgm, bgmVolume);
                PlayerPrefs.Save();
                if (bgmSource != null)
                    bgmSource.volume = bgmVolume;
            }
        }

        public float SfxVolume
        {
            get => sfxVolume;
            set
            {
                sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(PrefSfx, sfxVolume);
                PlayerPrefs.Save();
                if (sfxSource != null)
                    sfxSource.volume = sfxVolume;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            Ensure();
        }

        public static AudioManager Ensure()
        {
            if (_instance != null)
                return _instance;

            var existing = Object.FindObjectOfType<AudioManager>();
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AudioManager>();
            _instance.BuildSources();
            _instance.LoadVolumes();
            _instance.TryPlayBgm();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            BuildSources();
            LoadVolumes();
            TryPlayBgm();
        }

        void BuildSources()
        {
            if (bgmSource == null)
            {
                var bgmGo = new GameObject("BGM");
                bgmGo.transform.SetParent(transform, false);
                bgmSource = bgmGo.AddComponent<AudioSource>();
                bgmSource.playOnAwake = false;
                bgmSource.loop = true;
            }

            if (sfxSource == null)
            {
                var sfxGo = new GameObject("SFX");
                sfxGo.transform.SetParent(transform, false);
                sfxSource = sfxGo.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
                sfxSource.loop = false;
            }
        }

        void LoadVolumes()
        {
            bgmVolume = PlayerPrefs.GetFloat(PrefBgm, 1f);
            sfxVolume = PlayerPrefs.GetFloat(PrefSfx, 1f);
            if (bgmSource != null) bgmSource.volume = bgmVolume;
            if (sfxSource != null) sfxSource.volume = sfxVolume;
        }

        void TryPlayBgm()
        {
            if (bgmSource == null || bgmClip == null)
                return;
            if (bgmSource.clip != bgmClip)
                bgmSource.clip = bgmClip;
            if (!bgmSource.isPlaying)
                bgmSource.Play();
        }

        public void SetBgmClip(AudioClip clip, bool play = true)
        {
            bgmClip = clip;
            if (bgmSource == null)
                return;
            bgmSource.clip = clip;
            if (play && clip != null)
                bgmSource.Play();
            else if (clip == null)
                bgmSource.Stop();
        }

        public void PlaySfx(AudioClip clip, float pitch = 1f)
        {
            if (clip == null || sfxSource == null)
                return;
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, sfxVolume);
        }
    }
}
