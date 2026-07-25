using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ruilin
{
    public enum ItemId
    {
        Peek,
        WireCutter,
        SteadyHand,
        ReflectGlove,
        FateDie
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public ItemId Id;
        public string Name;
        public string Type;
        public string Description;
        public int UsesPerMatch;

        public bool IsActive => UsesPerMatch > 0;
    }

    [Serializable]
    public sealed class OwnedItem
    {
        public ItemId Id;
        public int RemainingUses;
    }

    public static class ItemCatalog
    {
        static readonly ItemDefinition[] AllItems =
        {
            new ItemDefinition
            {
                Id = ItemId.Peek,
                Name = "窥视",
                Type = "主动",
                Description = "每场2次：查看当前炸弹倒计时的精确数字。",
                UsesPerMatch = 2
            },
            new ItemDefinition
            {
                Id = ItemId.WireCutter,
                Name = "拆线钳",
                Type = "被动",
                Description = "每场拆线次数+2。",
                UsesPerMatch = 0
            },
            new ItemDefinition
            {
                Id = ItemId.SteadyHand,
                Name = "稳定之手",
                Type = "被动",
                Description = "玩家手滑率由40%降低到20%。",
                UsesPerMatch = 0
            },
            new ItemDefinition
            {
                Id = ItemId.ReflectGlove,
                Name = "反弹手套",
                Type = "主动",
                Description = "每场1次：接手瞬间把炸弹原路弹回，倒计时不变，并反转默认传递方向。",
                UsesPerMatch = 1
            },
            new ItemDefinition
            {
                Id = ItemId.FateDie,
                Name = "命运骰",
                Type = "主动",
                Description = "每场1次：掷D6，倒计时扣除点数后立即移交，可能当场引爆。",
                UsesPerMatch = 1
            }
        };

        public static IReadOnlyList<ItemDefinition> All => AllItems;

        public static ItemDefinition Get(ItemId id)
        {
            for (int i = 0; i < AllItems.Length; i++)
                if (AllItems[i].Id == id)
                    return AllItems[i];
            throw new ArgumentOutOfRangeException(nameof(id), id, null);
        }

        /// <summary>道具卡立绘：Resources/UI/Cards，编辑器可回退到 Art/美术素材/Cards。</summary>
        public static Sprite GetIcon(ItemId id)
        {
            string file = IconFileName(id);
            if (string.IsNullOrEmpty(file))
                return null;

            Sprite fromRes = Resources.Load<Sprite>("UI/Cards/" + file);
            if (fromRes != null)
                return fromRes;

#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/美术素材/Cards/" + file + ".png");
#else
            return null;
#endif
        }

        static string IconFileName(ItemId id)
        {
            switch (id)
            {
                case ItemId.Peek: return "ico_item_peek_01";
                case ItemId.WireCutter: return "ico_item_defusekit_01";
                case ItemId.SteadyHand: return "ico_item_stablehand_01";
                case ItemId.ReflectGlove: return "ico_item_glove_01";
                case ItemId.FateDie: return "ico_item_dice_01";
                default: return null;
            }
        }
    }

    /// <summary>
    /// 本次Run持有的道具。跨场景保留，最多两件；每场开始时恢复主动次数。
    /// </summary>
    public static class RunInventory
    {
        public const int Capacity = 2;
        const string SaveKey = "Ruilin.RunInventory.Items";
        static readonly List<OwnedItem> ItemsInternal = new List<OwnedItem>(Capacity);

        public static IReadOnlyList<OwnedItem> Items => ItemsInternal;
        public static event Action Changed;

        static RunInventory()
        {
            Load();
        }

        public static bool Contains(ItemId id)
        {
            return Find(id) != null;
        }

        public static bool TryAdd(ItemId id)
        {
            if (Contains(id) || ItemsInternal.Count >= Capacity)
                return false;

            ItemDefinition definition = ItemCatalog.Get(id);
            ItemsInternal.Add(new OwnedItem
            {
                Id = id,
                RemainingUses = definition.UsesPerMatch
            });
            Save();
            Changed?.Invoke();
            return true;
        }

        public static void ReplaceAt(int slot, ItemId id)
        {
            if (slot < 0 || slot >= ItemsInternal.Count)
                throw new ArgumentOutOfRangeException(nameof(slot));

            ItemDefinition definition = ItemCatalog.Get(id);
            ItemsInternal[slot] = new OwnedItem
            {
                Id = id,
                RemainingUses = definition.UsesPerMatch
            };
            Save();
            Changed?.Invoke();
        }

        public static void ResetUsesForMatch()
        {
            for (int i = 0; i < ItemsInternal.Count; i++)
                ItemsInternal[i].RemainingUses = ItemCatalog.Get(ItemsInternal[i].Id).UsesPerMatch;
            Changed?.Invoke();
        }

        public static bool TryConsume(ItemId id)
        {
            OwnedItem item = Find(id);
            if (item == null || item.RemainingUses <= 0)
                return false;
            item.RemainingUses--;
            Changed?.Invoke();
            return true;
        }

        public static int DefuseBonus => Contains(ItemId.WireCutter) ? 2 : 0;
        public static float PlayerSlipChance => Contains(ItemId.SteadyHand) ? 0.20f : 0.40f;

        public static void ClearRun()
        {
            ItemsInternal.Clear();
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>
        /// 跨关卡续跑标记：NextLevel / 本关重开前写入；新开 Play 未带标记则清空背包。
        /// 同时写内存 + PlayerPrefs，避免切场景时只靠 Prefs 丢失。
        /// </summary>
        const string ContinueKey = "Ruilin.RunContinue";
        static bool ContinueInMemory;

        public static void MarkRunContinuing()
        {
            ContinueInMemory = true;
            PlayerPrefs.SetInt(ContinueKey, 1);
            PlayerPrefs.Save();
        }

        /// <returns>true = 从上一关或本关重开续跑，不要清空背包。</returns>
        public static bool ConsumeRunContinuing()
        {
            bool continuing = ContinueInMemory || PlayerPrefs.GetInt(ContinueKey, 0) == 1;
            ContinueInMemory = false;
            if (PlayerPrefs.GetInt(ContinueKey, 0) == 1)
            {
                PlayerPrefs.SetInt(ContinueKey, 0);
                PlayerPrefs.Save();
            }
            return continuing;
        }

        /// <summary>新开一局（Start 菜单）时清续跑标记。</summary>
        public static void ClearContinueFlag()
        {
            ContinueInMemory = false;
            PlayerPrefs.DeleteKey(ContinueKey);
            PlayerPrefs.Save();
        }

        /// <summary>若内存背包为空则从 PlayerPrefs 再读一次（防切场景丢静态状态）。</summary>
        public static void EnsureLoadedFromPrefs()
        {
            if (ItemsInternal.Count > 0)
                return;
            Load();
            if (ItemsInternal.Count > 0)
                Changed?.Invoke();
        }

        static void Save()
        {
            var ids = new string[ItemsInternal.Count];
            for (int i = 0; i < ItemsInternal.Count; i++)
                ids[i] = ((int)ItemsInternal[i].Id).ToString();
            PlayerPrefs.SetString(SaveKey, string.Join(",", ids));
            PlayerPrefs.Save();
        }

        static void Load()
        {
            ItemsInternal.Clear();
            string saved = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrEmpty(saved))
                return;

            string[] ids = saved.Split(',');
            for (int i = 0; i < ids.Length && ItemsInternal.Count < Capacity; i++)
            {
                if (!int.TryParse(ids[i], out int raw) || !Enum.IsDefined(typeof(ItemId), raw))
                    continue;
                ItemId id = (ItemId)raw;
                if (Contains(id))
                    continue;
                ItemDefinition definition = ItemCatalog.Get(id);
                ItemsInternal.Add(new OwnedItem
                {
                    Id = id,
                    RemainingUses = definition.UsesPerMatch
                });
            }
        }

        static OwnedItem Find(ItemId id)
        {
            for (int i = 0; i < ItemsInternal.Count; i++)
                if (ItemsInternal[i].Id == id)
                    return ItemsInternal[i];
            return null;
        }
    }
}
