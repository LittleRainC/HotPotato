using System.Collections.Generic;

namespace Chardin
{
    public enum SeatPersonality
    {
        Unknown = 0,
        Player,
        Worm,
        Ash,
        Snake
    }

    public readonly struct BattleParticipantInfo
    {
        public readonly int Id;
        public readonly string DisplayName;
        public readonly bool IsPlayer;
        public readonly bool IsAlive;
        public readonly SeatPersonality Personality;

        public BattleParticipantInfo(int id, string displayName, bool isPlayer, bool isAlive,
            SeatPersonality personality = SeatPersonality.Unknown)
        {
            Id = id;
            DisplayName = displayName;
            IsPlayer = isPlayer;
            IsAlive = isAlive;
            Personality = personality;
        }
    }

    /// <summary>AI 决策时可见的公开局势（精确倒计时仅当自己持有）。</summary>
    public sealed class BattleSnapshot
    {
        public int SelfId;
        public int HolderId;
        public int SharedDefuseCharges;
        public int AliveCount;
        public int? HolderCountdown; // only set when SelfId == HolderId
        public float AppearanceRatio;
        public BombAppearanceTier AppearanceTier;
        /// <summary>传的方向：+1 顺时针，-1 逆时针（反弹手套会翻转）。</summary>
        public int PassDirection;
        public IReadOnlyList<BattleParticipantInfo> Participants;
    }

    public struct AiMove
    {
        public BombAction Action;
        /// <summary>目标在 clockwiseOrder 中的下标。</summary>
        public int TargetId;
    }
}
