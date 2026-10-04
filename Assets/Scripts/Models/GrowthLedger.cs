using System.Collections.Generic;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-189] 초월 · 상점/교환소 · 선수 방출 · 3:1 포지션 재조합이 읽고 쓰는 보유 상태(GameManager 어댑터 / 테스트 더블 공용).
    /// IRecruitLedger(카드 · 포인트 · 트로피)에 성장 코인(방출 마일리지)과 성장 보조권 3종을 더한다.
    /// </summary>
    public interface IGrowthLedger : IRecruitLedger
    {
        /// <summary>성장 코인(방출 마일리지) - 선수 방출 · 리그 승리/우승으로 얻고 교환소에서 쓴다.</summary>
        int GrowthCoin { get; set; }
        /// <summary>범용 각성 보조권 - 같은 시즌 · 같은 포지션 재료가 없을 때 즉시 +1각(+10강 선행 · 9각까지).</summary>
        int AwakenTicket { get; set; }
        /// <summary>초월 핵심 대체권 - 초월 슬롯 1(같은 시즌 · 같은 선수 1장)을 대체한다.</summary>
        int TranscendTicket { get; set; }
        /// <summary>특훈권 - 훈련·특훈 1단계를 볼 대신 1장으로 진행한다.</summary>
        int TrainingTicket { get; set; }
    }

    /// <summary>[TASK-KBO-189] 메모리 원장(단위 테스트 · 검증 리포트용).</summary>
    public sealed class MemoryGrowthLedger : IGrowthLedger
    {
        public readonly List<Player> Cards = new List<Player>();
        public readonly List<Player> Lineup = new List<Player>();
        public IReadOnlyList<Player> Inventory => Cards;
        public IReadOnlyList<Player> Roster => Lineup;
        public Team FavoriteTeam { get; set; }
        public int Points { get; set; }
        public int Trophies { get; set; }
        public int GrowthCoin { get; set; }
        public int AwakenTicket { get; set; }
        public int TranscendTicket { get; set; }
        public int TrainingTicket { get; set; }
        public void RemoveCard(Player player) { Lineup.Remove(player); Cards.Remove(player); }
        public void AddCard(Player player) { if (player != null) Cards.Add(player); }
    }
}
