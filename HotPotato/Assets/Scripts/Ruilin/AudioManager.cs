using System.Collections;
using System.Collections.Generic;
using Chardin;
using UnityEngine;
using UnityEngine.UI;

namespace Ruilin
{
    /// <summary>
    /// 跨场景音频：按文件 stem 从 Resources/Audio 加载（与 T0_Audio_Import.csv 对应）。
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public const string PrefBgm = "audio.bgm";
        public const string PrefSfx = "audio.sfx";

        public static class Clip
        {
            public const string Tick = "amb_tick_01_loop";
            public const string Pass = "sfx_act_pass_01";
            public const string Stuff = "sfx_act_stuff_01";
            public const string Slip = "sfx_act_slip_01";
            public const string Snuff = "sfx_act_snuff_01";
            public const string Explode = "sfx_bomb_explode_01";
            public const string Item = "sfx_item_01";
            public const string UiClick = "ui_btn_click_01";
            public const string SnakeDeath = "vo_snake_death_01";
            public const string WormDeath = "vo_worm_death_01";
            public const string AshDeath = "vo_ash_death_01";
            public const string Nailong = "vo_nailong_01";
            public const string BgmMenu = "bgm_menu_01_loop";
            public const string BgmBattle = "bgm_battle_01_loop";
            public const string BgmVictory = "bgm_victory_01_loop";
            public const string BgmLose = "bgm_result_lose_01";
        }

        static readonly float[] TickPitches =
        {
            1.00f, // Safe
            1.18f, // Warning
            1.38f, // Danger
            1.62f  // Critical
        };

        const float BattleBgmGain = 0.55f;
        const float TickCrossfadeSeconds = 0.12f;
        const float DeathVoDelay = 0.1f;

        static AudioManager _instance;

        [SerializeField] AudioSource bgmSource;
        [SerializeField] AudioSource ambSource;
        [SerializeField] AudioSource sfxSource;
        [SerializeField, Range(0f, 1f)] float bgmVolume = 1f;
        [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;

        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(32);
        string _currentBgmStem;
        float _bgmGain = 1f;
        BombAppearanceTier _tickTier = BombAppearanceTier.Safe;
        Coroutine _tickPitchRoutine;
        Coroutine _deathVoRoutine;

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
                ApplyBgmVolume();
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
                ApplySfxVolume();
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

            if (ambSource == null)
            {
                var ambGo = new GameObject("AMB");
                ambGo.transform.SetParent(transform, false);
                ambSource = ambGo.AddComponent<AudioSource>();
                ambSource.playOnAwake = false;
                ambSource.loop = true;
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
            ApplyBgmVolume();
            ApplySfxVolume();
        }

        void ApplyBgmVolume()
        {
            if (bgmSource != null)
                bgmSource.volume = bgmVolume * _bgmGain;
        }

        void ApplySfxVolume()
        {
            if (sfxSource != null)
                sfxSource.volume = sfxVolume;
            if (ambSource != null)
                ambSource.volume = sfxVolume;
        }

        AudioClip GetClip(string stem)
        {
            if (string.IsNullOrEmpty(stem))
                return null;
            if (_clips.TryGetValue(stem, out AudioClip cached) && cached != null)
                return cached;

            AudioClip clip = Resources.Load<AudioClip>("Audio/" + stem);
            if (clip == null)
                Debug.LogWarning("[Audio] Missing Resources/Audio/" + stem);
            _clips[stem] = clip;
            return clip;
        }

        public void PlaySfx(string stem, float pitch = 1f)
        {
            AudioClip clip = GetClip(stem);
            if (clip == null || sfxSource == null)
                return;
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        public void PlayUiClick() => PlaySfx(Clip.UiClick);

        public void PlayItemUse() => PlaySfx(Clip.Item);

        public void BindButtonClick(Button button)
        {
            if (button == null)
                return;
            button.onClick.AddListener(PlayUiClick);
        }

        public void PlayMenuBgm() => PlayLoopingBgm(Clip.BgmMenu, gain: 1f);

        public void PlayBattleBgm() => PlayLoopingBgm(Clip.BgmBattle, gain: BattleBgmGain);

        public void PlayVictoryBgm() => PlayLoopingBgm(Clip.BgmVictory, gain: 1f);

        /// <summary>失败 stinger：播完停，不循环。</summary>
        public void PlayLoseStinger()
        {
            AudioClip clip = GetClip(Clip.BgmLose);
            if (bgmSource == null || clip == null)
                return;

            StopTick();
            _currentBgmStem = Clip.BgmLose;
            _bgmGain = 1f;
            bgmSource.Stop();
            bgmSource.clip = clip;
            bgmSource.loop = false;
            ApplyBgmVolume();
            bgmSource.Play();
        }

        public void StopBgm()
        {
            _currentBgmStem = null;
            if (bgmSource != null)
                bgmSource.Stop();
        }

        void PlayLoopingBgm(string stem, float gain)
        {
            AudioClip clip = GetClip(stem);
            if (bgmSource == null || clip == null)
                return;

            _bgmGain = gain;
            if (_currentBgmStem == stem && bgmSource.isPlaying && bgmSource.loop)
            {
                ApplyBgmVolume();
                return;
            }

            _currentBgmStem = stem;
            bgmSource.clip = clip;
            bgmSource.loop = true;
            ApplyBgmVolume();
            if (!bgmSource.isPlaying)
                bgmSource.Play();
            else
            {
                bgmSource.Stop();
                bgmSource.Play();
            }
        }

        public void SyncBombTick(BombAppearanceTier tier, bool armed)
        {
            if (!armed)
            {
                StopTick();
                return;
            }

            AudioClip clip = GetClip(Clip.Tick);
            if (ambSource == null || clip == null)
                return;

            if (ambSource.clip != clip)
                ambSource.clip = clip;

            float targetPitch = TickPitches[Mathf.Clamp((int)tier, 0, TickPitches.Length - 1)];
            if (!ambSource.isPlaying)
            {
                ambSource.pitch = targetPitch;
                ApplySfxVolume();
                ambSource.Play();
                _tickTier = tier;
                return;
            }

            if (_tickTier == tier)
                return;

            _tickTier = tier;
            if (_tickPitchRoutine != null)
                StopCoroutine(_tickPitchRoutine);
            _tickPitchRoutine = StartCoroutine(CrossfadeTickPitch(targetPitch));
        }

        IEnumerator CrossfadeTickPitch(float targetPitch)
        {
            float startPitch = ambSource.pitch;
            float startVol = ambSource.volume;
            float t = 0f;
            while (t < TickCrossfadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / TickCrossfadeSeconds);
                // 短淡出再淡入感觉的简化：中间略压音量，同时插值 pitch
                float duck = 1f - 0.35f * Mathf.Sin(u * Mathf.PI);
                ambSource.pitch = Mathf.Lerp(startPitch, targetPitch, u);
                ambSource.volume = startVol * duck;
                yield return null;
            }

            ambSource.pitch = targetPitch;
            ApplySfxVolume();
            _tickPitchRoutine = null;
        }

        public void StopTick()
        {
            if (_tickPitchRoutine != null)
            {
                StopCoroutine(_tickPitchRoutine);
                _tickPitchRoutine = null;
            }

            if (ambSource != null && ambSource.isPlaying)
                ambSource.Stop();
        }

        public void PlayExplosion()
        {
            PlaySfx(Clip.Explode);
        }

        public void PlayDeathVoDelayed(SeatPersonality personality)
        {
            string stem = DeathVoStem(personality);
            if (stem == null)
                return;
            if (_deathVoRoutine != null)
                StopCoroutine(_deathVoRoutine);
            _deathVoRoutine = StartCoroutine(PlayDeathVoRoutine(stem));
        }

        public void PlayDeathVoDelayed(string displayName)
        {
            string stem = DeathVoStem(displayName);
            if (stem == null)
                return;
            if (_deathVoRoutine != null)
                StopCoroutine(_deathVoRoutine);
            _deathVoRoutine = StartCoroutine(PlayDeathVoRoutine(stem));
        }

        IEnumerator PlayDeathVoRoutine(string stem)
        {
            yield return new WaitForSecondsRealtime(DeathVoDelay);
            PlaySfx(stem);
            _deathVoRoutine = null;
        }

        static string DeathVoStem(SeatPersonality personality)
        {
            switch (personality)
            {
                case SeatPersonality.Snake: return Clip.SnakeDeath;
                case SeatPersonality.Worm: return Clip.WormDeath;
                case SeatPersonality.Ash: return Clip.AshDeath;
                default: return null;
            }
        }

        static string DeathVoStem(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return null;
            string n = displayName.ToLowerInvariant();
            if (n.Contains("snake")) return Clip.SnakeDeath;
            if (n.Contains("worm")) return Clip.WormDeath;
            if (n.Contains("ash")) return Clip.AshDeath;
            return null;
        }

        public void PlayNailong() => PlaySfx(Clip.Nailong);

        public void PlayActionSfx(BombAction action, bool slipped)
        {
            if (slipped)
            {
                PlaySfx(Clip.Slip);
                return;
            }

            switch (action)
            {
                case BombAction.Pass:
                    PlaySfx(Clip.Pass);
                    break;
                case BombAction.Shove:
                    PlaySfx(Clip.Stuff);
                    break;
                case BombAction.Defuse:
                    PlaySfx(Clip.Snuff);
                    break;
            }
        }

        // 兼容旧调用
        public void SetBgmClip(AudioClip clip, bool play = true)
        {
            if (bgmSource == null)
                return;
            bgmSource.clip = clip;
            bgmSource.loop = true;
            _currentBgmStem = clip != null ? clip.name : null;
            _bgmGain = 1f;
            ApplyBgmVolume();
            if (play && clip != null)
                bgmSource.Play();
            else
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
