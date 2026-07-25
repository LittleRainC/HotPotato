using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ruilin
{
    /// <summary>
    /// Start 主菜单：Start / Settings / Credits + 两个弹窗。
    /// </summary>
    public sealed class StartMenuController : MonoBehaviour
    {
        [Header("Main Buttons")]
        [SerializeField] Button startButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button creditsButton;

        [Header("Settings Popup")]
        [SerializeField] GameObject settingsRoot;
        [SerializeField] Slider bgmSlider;
        [SerializeField] Slider sfxSlider;
        [SerializeField] Button settingsCloseButton;

        [Header("Credits Popup")]
        [SerializeField] GameObject creditsRoot;
        [SerializeField] Button creditsCloseButton;

        [SerializeField] string firstLevelSceneName = "Level1";

        void Awake()
        {
            AudioManager.Ensure();
            BindOrFind();
            Wire();
            HidePopups();
            SyncSlidersFromAudio();
        }

        void BindOrFind()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
                return;

            Transform root = canvas.transform;
            if (startButton == null)
            {
                var t = root.Find("MenuButtons/BtnStart") ?? root.Find("Button");
                if (t != null) startButton = t.GetComponent<Button>();
            }

            if (settingsButton == null)
            {
                var t = root.Find("MenuButtons/BtnSettings");
                if (t != null) settingsButton = t.GetComponent<Button>();
            }

            if (creditsButton == null)
            {
                var t = root.Find("MenuButtons/BtnCredits");
                if (t != null) creditsButton = t.GetComponent<Button>();
            }

            if (settingsRoot == null)
            {
                var t = root.Find("SettingsPopup");
                if (t != null) settingsRoot = t.gameObject;
            }

            if (creditsRoot == null)
            {
                var t = root.Find("CreditsPopup");
                if (t != null) creditsRoot = t.gameObject;
            }

            if (settingsRoot != null)
            {
                if (bgmSlider == null)
                {
                    var t = settingsRoot.transform.Find("Panel/BgmSlider");
                    if (t != null) bgmSlider = t.GetComponent<Slider>();
                }

                if (sfxSlider == null)
                {
                    var t = settingsRoot.transform.Find("Panel/SfxSlider");
                    if (t != null) sfxSlider = t.GetComponent<Slider>();
                }

                if (settingsCloseButton == null)
                {
                    var t = settingsRoot.transform.Find("Panel/BtnClose");
                    if (t != null) settingsCloseButton = t.GetComponent<Button>();
                }

                var settingsDim = settingsRoot.transform.Find("Dim");
                if (settingsDim != null)
                {
                    var dimBtn = settingsDim.GetComponent<Button>();
                    if (dimBtn != null)
                    {
                        dimBtn.onClick.RemoveAllListeners();
                        dimBtn.onClick.AddListener(CloseSettings);
                    }
                }
            }

            if (creditsRoot != null)
            {
                if (creditsCloseButton == null)
                {
                    var t = creditsRoot.transform.Find("Panel/BtnClose");
                    if (t != null) creditsCloseButton = t.GetComponent<Button>();
                }

                var creditsDim = creditsRoot.transform.Find("Dim");
                if (creditsDim != null)
                {
                    var dimBtn = creditsDim.GetComponent<Button>();
                    if (dimBtn != null)
                    {
                        dimBtn.onClick.RemoveAllListeners();
                        dimBtn.onClick.AddListener(CloseCredits);
                    }
                }
            }
        }

        void Wire()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(OnStartClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveAllListeners();
                settingsButton.onClick.AddListener(OpenSettings);
            }

            if (creditsButton != null)
            {
                creditsButton.onClick.RemoveAllListeners();
                creditsButton.onClick.AddListener(OpenCredits);
            }

            if (settingsCloseButton != null)
            {
                settingsCloseButton.onClick.RemoveAllListeners();
                settingsCloseButton.onClick.AddListener(CloseSettings);
            }

            if (creditsCloseButton != null)
            {
                creditsCloseButton.onClick.RemoveAllListeners();
                creditsCloseButton.onClick.AddListener(CloseCredits);
            }

            if (bgmSlider != null)
            {
                bgmSlider.onValueChanged.RemoveAllListeners();
                bgmSlider.onValueChanged.AddListener(v =>
                {
                    if (AudioManager.Instance != null)
                        AudioManager.Instance.BgmVolume = v;
                });
            }

            if (sfxSlider != null)
            {
                sfxSlider.onValueChanged.RemoveAllListeners();
                sfxSlider.onValueChanged.AddListener(v =>
                {
                    if (AudioManager.Instance != null)
                        AudioManager.Instance.SfxVolume = v;
                });
            }
        }

        void SyncSlidersFromAudio()
        {
            var audio = AudioManager.Instance;
            if (audio == null)
                return;
            if (bgmSlider != null)
                bgmSlider.SetValueWithoutNotify(audio.BgmVolume);
            if (sfxSlider != null)
                sfxSlider.SetValueWithoutNotify(audio.SfxVolume);
        }

        void HidePopups()
        {
            if (settingsRoot != null) settingsRoot.SetActive(false);
            if (creditsRoot != null) creditsRoot.SetActive(false);
        }

        public void OnStartClicked()
        {
            SceneManager.LoadScene(firstLevelSceneName);
        }

        public void OpenSettings()
        {
            if (creditsRoot != null) creditsRoot.SetActive(false);
            SyncSlidersFromAudio();
            if (settingsRoot != null) settingsRoot.SetActive(true);
        }

        public void CloseSettings()
        {
            if (settingsRoot != null) settingsRoot.SetActive(false);
        }

        public void OpenCredits()
        {
            if (settingsRoot != null) settingsRoot.SetActive(false);
            if (creditsRoot != null) creditsRoot.SetActive(true);
        }

        public void CloseCredits()
        {
            if (creditsRoot != null) creditsRoot.SetActive(false);
        }
    }
}
