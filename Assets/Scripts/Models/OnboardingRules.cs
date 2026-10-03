using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-181] 정식 온보딩 규칙(순수 로직 - OnboardingManager / 온보딩 화면 / 단위 테스트 공용).
    ///   1) 스타터 선수단 = 선택 구단의 2026 시즌 LIVE_NORMAL 카드 전원(cards_*.csv year=2026, grade=LIVE_NORMAL). 구단마다 32~35장
    ///      (타자 18~21 + 투수 13~16)이라 주전 9 + 후보 6 + 투수 13 = 28인 풀 로스터를 오토 라인업으로 즉시 편성하고 나머지는 보관한다.
    ///   2) 신규 단장 정착 지원 선물 = 2024 골든글러브 4종 중 택1(구자욱 '24 · 김도영 '24 · 하트 '24 · 로하스 '24). card_id는 기존
    ///      cards_*.csv의 실제 행을 그대로 쓴다(새 ID를 만들지 않음 - 세이브 호환).
    /// </summary>
    public static class OnboardingRules
    {
        public const int StarterSeasonYear = 2026;
        public const int NicknameMaxLength = 12;

        /// <summary>선물 카드 4종(표시 순서 고정). 모두 cards_*.csv에 이미 존재하는 2024 GOLDEN_GLOVE 행이다.</summary>
        public static readonly IReadOnlyList<string> GiftTemplateIds = new[]
        {
            "SAMSUNG_2024_PLY_004038_GG", // 구자욱 '24 - 삼성 라이온즈 외야수(RF)
            "KIA_2024_PLY_004368_GG",     // 김도영 '24 - KIA 타이거즈 3루수
            "NC_2024_PLY_004366_GG",      // 하트 '24 - NC 다이노스 선발투수(카일 하트)
            "KT_2024_PLY_004369_GG",      // 로하스 '24 - KT 위즈 외야수(멜 로하스 주니어)
        };

        public static bool IsGiftTemplateId(string templateId) => templateId != null && GiftTemplateIds.Contains(templateId);

        /// <summary>선택 구단의 2026 LIVE_NORMAL 카드 전원(TemplateId 순 - 결정적).</summary>
        public static List<PlayerTemplate> SelectStarterTemplates(IEnumerable<PlayerTemplate> templates, Team team)
        {
            if (team == Team.None) return new List<PlayerTemplate>();
            return (templates ?? Enumerable.Empty<PlayerTemplate>())
                .Where(t => t != null && t.Team == team && t.Grade == Grade.LIVE_NORMAL && t.SeasonYear == StarterSeasonYear)
                .GroupBy(t => t.TemplateId).Select(g => g.First())
                .OrderBy(t => t.TemplateId, System.StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>닉네임 정규화 - 앞뒤 공백 제거, 최대 12자, 비면 null(로비는 "단장"으로만 표기).</summary>
        public static string NormalizeNickname(string nickname)
        {
            var trimmed = (nickname ?? "").Trim();
            if (trimmed.Length == 0) return null;
            return trimmed.Length > NicknameMaxLength ? trimmed.Substring(0, NicknameMaxLength) : trimmed;
        }

        /// <summary>카드 표기명(예: "구자욱 '24").</summary>
        public static string CardTitle(PlayerTemplate template) =>
            template == null ? "" : template.SeasonYear > 0 ? $"{template.PlayerName} '{template.SeasonYear % 100:00}" : template.PlayerName;

        /// <summary>[TASK-KBO-182] 선물 선택 화면 안내 - 자동 편성이 선택 구단 선수를 최우선하므로 같은 구단 카드인지에 따라 결과가 다르다.</summary>
        public static string DescribeGiftChoice(Player gift, Team selectedTeam)
        {
            if (gift?.Template == null) return "";
            string title = CardTitle(gift.Template);
            return gift.Template.Team == selectedTeam
                ? $"{title} 선택 - 선택 구단 카드라 해당 포지션 최고 OVR이면 즉시 주전으로 편성됩니다."
                : $"{title} 선택 - 타 구단 카드는 자동 편성에서 선택 구단 선수 다음 순위입니다(골든글러브는 구단 무관 세트덱 합산). [보관 선수]에서 직접 투입할 수 있습니다.";
        }

        /// <summary>선물 카드가 오토 라인업에서 어디에 들어갔는지 안내 문구(튜토리얼 1단계 / 선물 수령 결과).</summary>
        public static string DescribeGiftPlacement(IReadOnlyList<Player> roster, Player gift)
        {
            if (gift?.Template == null) return "";
            string title = CardTitle(gift.Template);
            if (roster == null || !roster.Contains(gift))
            {
                return $"{title}는 보관함에 보관되었습니다 - 자동 편성은 선택 구단 선수를 우선합니다. [라인업] → [보관 선수]에서 직접 투입할 수 있습니다.";
            }

            if (gift.Template.IsPitcher)
            {
                var (starters, bullpen) = LineupView.BuildPitchers(roster);
                var slot = starters.Concat(bullpen).FirstOrDefault(e => e.Player == gift);
                return slot != null ? $"{title} → {slot.Header} 즉시 편성 완료" : $"{title} → 투수 로스터 편성 완료";
            }

            var lineupSlot = LineupView.BuildLineup(roster).FirstOrDefault(e => e.Player == gift);
            return lineupSlot != null
                ? $"{title} → {lineupSlot.BattingOrder}번 타자 · {RosterSlotLayout.PositionLabel(lineupSlot.Position)} 주전 즉시 편성 완료"
                : $"{title} → 후보 타자 편성 - [라인업]에서 주전과 교체할 수 있습니다";
        }
    }
}
