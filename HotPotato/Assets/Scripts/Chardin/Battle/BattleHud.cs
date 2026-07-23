using System;
using UnityEngine;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>战斗 UI：动作按钮、播报、心、拆线次数。</summary>
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField] Button btnPass;
        [SerializeField] Button btnShove;
        [SerializeField] Button btnDefuse;
        [SerializeField] Text defuseBadgeText;
        [SerializeField] Text broadcastText;
        [SerializeField] Text heartsText;
        [SerializeField] Text opponentNameText;

        public event Action<BombAction> ActionClicked;

        public void BindFromHierarchy(Transform battleRoot)
        {
            var canvas = battleRoot.Find("Canvas");
            if (canvas == null)
            {
                Debug.LogError("[BattleHud] Canvas not found under Battle_2P");
                return;
            }

            broadcastText = FindText(canvas, "BroadcastBoard/Text");
            opponentNameText = FindText(canvas, "OpponentName/Text");
            heartsText = FindText(canvas, "BottomBar/Hearts/Text");

            var actions = canvas.Find("BottomBar/ActionButtons");
            if (actions != null)
            {
                btnDefuse = actions.Find("BtnDefuse")?.GetComponent<Button>();
                btnPass = actions.Find("BtnPass")?.GetComponent<Button>();
                btnShove = actions.Find("BtnShove")?.GetComponent<Button>();
                defuseBadgeText = FindText(actions, "BtnDefuse/Badge/Text");
            }

            WireButtons();
        }

        void WireButtons()
        {
            if (btnPass != null)
            {
                btnPass.onClick.RemoveAllListeners();
                btnPass.onClick.AddListener(() => ActionClicked?.Invoke(BombAction.Pass));
            }
            if (btnShove != null)
            {
                btnShove.onClick.RemoveAllListeners();
                btnShove.onClick.AddListener(() => ActionClicked?.Invoke(BombAction.Shove));
            }
            if (btnDefuse != null)
            {
                btnDefuse.onClick.RemoveAllListeners();
                btnDefuse.onClick.AddListener(() => ActionClicked?.Invoke(BombAction.Defuse));
            }
        }

        public void SetOpponentName(string name)
        {
            if (opponentNameText != null)
                opponentNameText.text = name;
        }

        public void SetBroadcast(string message)
        {
            if (broadcastText != null)
                broadcastText.text = message;
        }

        public void SetHearts(int hearts)
        {
            if (heartsText != null)
                heartsText.text = new string('♥', Mathf.Max(0, hearts));
        }

        public void SetDefuseCharges(int charges)
        {
            if (defuseBadgeText != null)
                defuseBadgeText.text = "×" + charges;
        }

        public void SetActionsInteractable(bool interactable, bool defuseAvailable)
        {
            if (btnPass != null) btnPass.interactable = interactable;
            if (btnShove != null) btnShove.interactable = interactable;
            if (btnDefuse != null) btnDefuse.interactable = interactable && defuseAvailable;
        }

        static Text FindText(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null ? t.GetComponent<Text>() : null;
        }
    }
}
