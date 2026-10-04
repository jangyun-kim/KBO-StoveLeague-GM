using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-188] 선수 카드 · 성장 센터 대상 목록 · 중계 타순표가 함께 쓰는 표시 문자열 SSOT.
    ///   - 포지션 약어(C/1B/…/DH, SP/RP/CP) · "이름'연도" · 등급 약칭(LIVE/EPIC/AS/…)
    ///   - 성장 배지: 미각성(0각)은 강화 "+N", 1각 이상은 각성 "N각"/"초월"(CardGrowthRules.GrowthBadgeLabel)
    /// </summary>
    public static class CardDisplay
    {
        public static string PositionShort(PlayerTemplate t)
        {
            if (t == null) return "";
            if (t.IsPitcher)
            {
                return t.PitcherRole switch
                {
                    PitcherRole.StartingPitcher => "SP",
                    PitcherRole.Closer => "CP",
                    _ => "RP"
                };
            }
            return t.BatterPosition switch
            {
                BatterPosition.Catcher => "C",
                BatterPosition.FirstBase => "1B",
                BatterPosition.SecondBase => "2B",
                BatterPosition.ThirdBase => "3B",
                BatterPosition.ShortStop => "SS",
                BatterPosition.LeftField => "LF",
                BatterPosition.CenterField => "CF",
                BatterPosition.RightField => "RF",
                _ => "DH"
            };
        }

        /// <summary>"구자욱'24" - 연도가 없으면 이름만.</summary>
        public static string NameWithYear(PlayerTemplate t)
        {
            if (t == null) return "";
            return t.SeasonYear > 0 ? $"{t.PlayerName}'{t.SeasonYear % 100:00}" : t.PlayerName;
        }

        public static string GradeShort(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => "LIVE",
            Grade.LIVE_EPIC => "EPIC",
            Grade.ALLSTAR => "AS",
            Grade.FRANCHISE => "FRA",
            Grade.TITLE_HOLDER => "TH",
            Grade.RETIRED_NUMBER => "RN",
            Grade.GOLDEN_GLOVE => "GG",
            Grade.SIGNATURE => "SIG",
            Grade.DYNASTY => "DYN",
            _ => grade.ToString()
        };

        /// <summary>하단 네임플레이트 본문: "{포지션} {선수명}'{연도}" (배지는 별도 칩).</summary>
        public static string NamePlate(Player p) =>
            p?.Template == null ? "" : $"{PositionShort(p.Template)} {NameWithYear(p.Template)}";

        /// <summary>성장 센터 [대상 선수 변경] 1번째 줄: "구자욱'24 (RF)".</summary>
        public static string TargetLine1(Player p) =>
            p?.Template == null ? "" : $"{NameWithYear(p.Template)} ({PositionShort(p.Template)})";

        /// <summary>성장 센터 [대상 선수 변경] 2번째 줄: "GG · 3각 · OVR 98" (미각성은 "+7강", 성장 없음은 "명함").</summary>
        public static string TargetLine2(Player p, int ovr)
        {
            if (p?.Template == null) return "";
            string growth = CardGrowthRules.ShowsAwakenBadge(p.Template.Grade, p.AwakenLevel)
                ? CardGrowthRules.GrowthBadgeLabel(p)
                : p.ReinforceLevel > 0 ? $"+{p.ReinforceLevel}강" : "명함";
            return $"{GradeShort(p.Template.Grade)} · {growth} · OVR {ovr}";
        }
    }
}
