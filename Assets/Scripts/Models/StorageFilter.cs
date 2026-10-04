using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-184] 라인업 → [보관 선수] 탭의 보유 선수 리스트 필터/정렬(구 [선수 관리] 탭 InventoryPanel의 보유 카드 목록을 이 탭으로 옮겼다).
    /// 버튼을 누를 때마다 다음 값으로 순환한다(범위 · 구단 · 포지션 · 등급 · 정렬). 순수 데이터 - 단위 테스트 공용.
    /// </summary>
    [Serializable]
    public class StorageFilter
    {
        public enum ScopeKind { StorageOnly, AllOwned }
        public enum PositionGroup { All, Batter, Pitcher, Catcher, Infield, Outfield, DesignatedHitter, Starter, Bullpen }
        public enum SortKind { OvrDesc, GradeDesc, Growth, Name }

        public ScopeKind Scope = ScopeKind.StorageOnly;
        /// <summary>Team.None = 전체 구단.</summary>
        public Team Team = Team.None;
        public PositionGroup Position = PositionGroup.All;
        /// <summary>null = 전체 등급.</summary>
        public Grade? Grade;
        public SortKind Sort = SortKind.OvrDesc;

        private static readonly Team[] TeamCycle = (Team[])Enum.GetValues(typeof(Team));
        private static readonly Grade?[] GradeCycle =
        {
            null, Models.Grade.LIVE_NORMAL, Models.Grade.LIVE_EPIC, Models.Grade.ALLSTAR, Models.Grade.FRANCHISE, Models.Grade.TITLE_HOLDER,
            Models.Grade.GOLDEN_GLOVE, Models.Grade.SIGNATURE, Models.Grade.DYNASTY, Models.Grade.RETIRED_NUMBER,
        };

        public void CycleScope() => Scope = Scope == ScopeKind.StorageOnly ? ScopeKind.AllOwned : ScopeKind.StorageOnly;
        public void CycleTeam() => Team = TeamCycle[(Array.IndexOf(TeamCycle, Team) + 1) % TeamCycle.Length];
        public void CyclePosition() => Position = (PositionGroup)(((int)Position + 1) % Enum.GetValues(typeof(PositionGroup)).Length);
        public void CycleGrade() => Grade = GradeCycle[(Array.IndexOf(GradeCycle, Grade) + 1) % GradeCycle.Length];
        public void CycleSort() => Sort = (SortKind)(((int)Sort + 1) % Enum.GetValues(typeof(SortKind)).Length);

        public string ScopeLabel => Scope == ScopeKind.StorageOnly ? "보관 선수" : "전체 보유";
        public string TeamLabel => Team == Team.None ? "전체 구단" : Team.ToString();
        public string GradeLabel => Grade.HasValue ? CardGrowthRules.DisplayName(Grade.Value) : "전체 등급";
        public string PositionLabel => Position switch
        {
            PositionGroup.All => "전체 포지션",
            PositionGroup.Batter => "타자",
            PositionGroup.Pitcher => "투수",
            PositionGroup.Catcher => "포수",
            PositionGroup.Infield => "내야수",
            PositionGroup.Outfield => "외야수",
            PositionGroup.DesignatedHitter => "지명타자",
            PositionGroup.Starter => "선발",
            PositionGroup.Bullpen => "불펜",
            _ => Position.ToString()
        };
        public string SortLabel => Sort switch
        {
            SortKind.OvrDesc => "OVR 순",
            SortKind.GradeDesc => "등급 순",
            SortKind.Growth => "성장 순",
            SortKind.Name => "이름 순",
            _ => Sort.ToString()
        };

        public string Summary => $"{ScopeLabel} · {TeamLabel} · {PositionLabel} · {GradeLabel} · {SortLabel}";

        public bool MatchesPosition(Player p)
        {
            var t = p.Template;
            switch (Position)
            {
                case PositionGroup.All: return true;
                case PositionGroup.Batter: return !t.IsPitcher;
                case PositionGroup.Pitcher: return t.IsPitcher;
                case PositionGroup.Catcher: return !t.IsPitcher && t.BatterPosition == BatterPosition.Catcher;
                case PositionGroup.Infield:
                    return !t.IsPitcher && (t.BatterPosition == BatterPosition.FirstBase || t.BatterPosition == BatterPosition.SecondBase
                        || t.BatterPosition == BatterPosition.ThirdBase || t.BatterPosition == BatterPosition.ShortStop);
                case PositionGroup.Outfield:
                    return !t.IsPitcher && (t.BatterPosition == BatterPosition.LeftField || t.BatterPosition == BatterPosition.CenterField
                        || t.BatterPosition == BatterPosition.RightField);
                case PositionGroup.DesignatedHitter: return !t.IsPitcher && t.BatterPosition == BatterPosition.DesignatedHitter;
                case PositionGroup.Starter: return t.IsPitcher && t.PitcherRole == PitcherRole.StartingPitcher;
                case PositionGroup.Bullpen: return t.IsPitcher && t.PitcherRole != PitcherRole.StartingPitcher;
                default: return true;
            }
        }

        /// <summary>보유 카드(inventory) 중 필터를 통과한 목록을 정렬해 돌려준다. 동률은 선택 구단 우선 → OVR → 이름.</summary>
        public List<Player> Apply(IEnumerable<Player> inventory, IEnumerable<Player> roster, Team favoriteTeam)
        {
            var inRoster = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            var source = (inventory ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null);
            if (Scope == ScopeKind.StorageOnly) source = source.Where(p => !inRoster.Contains(p));
            else source = source.Concat(inRoster.Where(p => p?.Template != null)).Distinct();
            if (Team != Team.None) source = source.Where(p => p.Template.Team == Team);
            if (Grade.HasValue) source = source.Where(p => p.Template.Grade == Grade.Value);
            source = source.Where(MatchesPosition);

            IOrderedEnumerable<Player> ordered = Sort switch
            {
                SortKind.GradeDesc => source.OrderByDescending(p => CardGrowthRules.PowerRank(p.Template.Grade)).ThenByDescending(p => p.CalculateOVR(false)),
                SortKind.Growth => source.OrderByDescending(p => p.GetStatGrowth()).ThenByDescending(p => p.CalculateOVR(false)),
                SortKind.Name => source.OrderBy(p => p.Template.PlayerName, StringComparer.Ordinal).ThenByDescending(p => p.CalculateOVR(false)),
                _ => source.OrderByDescending(p => p.CalculateOVR(false)).ThenByDescending(p => p.Template.Team == favoriteTeam),
            };
            return ordered.ThenByDescending(p => p.Template.Team == favoriteTeam).ThenBy(p => p.Template.PlayerName, StringComparer.Ordinal).ToList();
        }
    }
}
