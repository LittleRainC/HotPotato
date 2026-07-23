using System.Collections.Generic;

namespace Chardin
{
    public sealed class BattleParticipant
    {
        public int Id { get; }
        public string DisplayName { get; }
        public bool IsPlayer { get; }
        public bool IsAlive { get; set; } = true;
        public UnityEngine.Transform Seat { get; }

        public BattleParticipant(int id, string displayName, bool isPlayer, UnityEngine.Transform seat)
        {
            Id = id;
            DisplayName = displayName;
            IsPlayer = isPlayer;
            Seat = seat;
        }
    }

    public readonly struct BattleParticipantInfo
    {
        public readonly int Id;
        public readonly string DisplayName;
        public readonly bool IsPlayer;
        public readonly bool IsAlive;

        public BattleParticipantInfo(BattleParticipant p)
        {
            Id = p.Id;
            DisplayName = p.DisplayName;
            IsPlayer = p.IsPlayer;
            IsAlive = p.IsAlive;
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
        public IReadOnlyList<BattleParticipantInfo> Participants;
    }

    public struct AiMove
    {
        public BombAction Action;
        public int TargetId;
    }
}
