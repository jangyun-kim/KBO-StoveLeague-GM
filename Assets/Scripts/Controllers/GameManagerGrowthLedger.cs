using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;

namespace KBOManager.Controllers
{
    /// <summary>[TASK-KBO-189] GameManager → IGrowthLedger 어댑터(포인트 = 볼(GameGold), 트로피 = Trophy, 성장 코인 · 보조권 3종).</summary>
    public sealed class GameManagerGrowthLedger : IGrowthLedger
    {
        private readonly GameManager gm;
        public GameManagerGrowthLedger(GameManager gameManager) { gm = gameManager; }
        public IReadOnlyList<Player> Inventory => gm.Inventory;
        public IReadOnlyList<Player> Roster => gm.Roster;
        public Team FavoriteTeam => gm.FavoriteTeam;
        public int Points { get => gm.GameGold; set => gm.GameGold = value; }
        public int Trophies { get => gm.Trophy; set => gm.Trophy = value; }
        public int GrowthCoin { get => gm.GrowthCoin; set => gm.GrowthCoin = value; }
        public int AwakenTicket { get => gm.AwakenTicket; set => gm.AwakenTicket = value; }
        public int TranscendTicket { get => gm.TranscendTicket; set => gm.TranscendTicket = value; }
        public int TrainingTicket { get => gm.TrainingTicket; set => gm.TrainingTicket = value; }
        public int SkillChangeTicket { get => gm.SkillChangeTicket; set => gm.SkillChangeTicket = value; } // [TASK-KBO-190]
        public int PremiumSkillChangeTicket { get => gm.PremiumSkillChangeTicket; set => gm.PremiumSkillChangeTicket = value; }
        public void RemoveCard(Player player) => gm.RemovePlayerFromInventory(player);
        public void AddCard(Player player) => gm.AddPlayerToInventory(player);
    }
}
