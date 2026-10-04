using System;
using System.Collections.Generic;
using System.Globalization;

namespace KBOManager.Models
{
    /// <summary>
    /// 치어리더 등급. 선수 카드 등급(Types.cs의 Grade enum - GDD 문서/코드 주석에서는 종종
    /// "PlayerGrade"로도 불린다)과는 완전히 분리된 별도 체계이며, 서로 혼용하지 않는다.
    ///
    /// [TASK-KBO-127] PM(단장)이 확정한 5단계 정식 등급(성능 순서 낮음-&gt;높음: LIVE_NORMAL &lt;
    /// LIVE_EPIC &lt; ICON &lt; LEGEND &lt; SEASON_LIMITED)으로 갱신했다 - TASK-KBO-064가 반영했던
    /// docs/16_shop_and_gacha_policy.md 2절의 임시 제안(NORMAL/RARE/EPIC/LEGEND, 4단계)을 폐기한다.
    /// 기존 NONE=0/TEST=1은 절대 바꾸지 않고 값을 이어 붙였다 - TASK-KBO-057부터 Cheerleader가
    /// GameSaveData로 직렬화되는 대상이라(11_data_dictionary.md 7절), Grade enum이 JsonUtility 정수
    /// 직렬화 문제로 명시적 정수값을 고정했던 선례(DCL-006)와 동일한 주의가 필요하다 - 앞으로 등급을
    /// 더 추가할 때도 반드시 끝에만 이어 붙일 것. 개발 빌드이므로 구버전 등급(NORMAL=2 등)으로 저장된
    /// 세이브 데이터의 값 재해석(2=LIVE_NORMAL 등)에 따른 데이터 유실은 허용된다(명령서 7항).
    /// </summary>
    public enum CheerleaderGrade
    {
        NONE = 0,
        TEST = 1,
        LIVE_NORMAL = 2,
        LIVE_EPIC = 3,
        ICON = 4,
        LEGEND = 5,
        SEASON_LIMITED = 6,
    }

    /// <summary>
    /// 치어리더 1명의 데이터. [TASK-KBO-048] TASK-KBO-039에서 마련된 B+C 하이브리드 버프 인프라
    /// (Engine.TeamPowerModifiers의 ConditionBuff/ClutchMultiplier)에 실제 값을 채워 넣는 소스다.
    ///
    /// 이 클래스가 들고 있는 두 버프 필드는 "경기 조건부 적용 전력(MatchConditionModifier)"이다 -
    /// 즉 Player.CalculateOVR()/GameManager.CalculateTeamOVR()(영구·표시용 구단 OVR)에는 절대
    /// 반영되지 않으며, 오직 경기가 시작되는 순간에만 각 매니저의 BuildTeamPowerModifiers()류
    /// 헬퍼가 이 값을 읽어 Engine.TeamPowerModifiers에 실어 MatchEngine에 일회성으로 전달한다
    /// (15_team_power_policy.md 2절 "③ 경기 적용 전력" 참고). 선수 스탯 원본이나 저장 데이터를
    /// 오염시키는 경로가 아니다.
    /// </summary>
    [Serializable]
    public class Cheerleader
    {
        public string InstanceId;

        /// <summary>
        /// [TASK-KBO-064] 원본(카탈로그) 식별자 - "어떤 종류의 치어리더인가"를 나타낸다.
        /// InstanceId(발급된 개체 고유값, 세이브 null 판별 불변식에 쓰임 - 11_data_dictionary.md 9절)와
        /// 완전히 별개다. 같은 CatalogId를 가진 치어리더를 또 획득하면 GameManager.AddCheerleader()가
        /// 인벤토리에 중복 추가하지 않고 재화로 변환한다(docs/16_shop_and_gacha_policy.md 4절 A안).
        /// 아직 카탈로그(cheerleaders.csv 등)가 없는 구버전 더미 데이터는 비워 둘 수 있으며, 비어 있으면
        /// AddCheerleader()가 중복 검사를 건너뛴다.
        /// </summary>
        public string CatalogId;

        public string Name;
        public CheerleaderGrade Grade = CheerleaderGrade.NONE;

        /// <summary>
        /// [TASK-KBO-048] 유저 팀의 홈 경기에서만(BuildTeamPowerModifiers 계열 호출부가 판별) 기본
        /// 홈 어드밴티지(Engine.TeamPowerModifiers.HomeAdvantageConditionBuff, +2)에 가산되는
        /// 조건부 보정치다. Player.FinalOVR이나 GameManager.CalculateTeamOVR()(영구·표시용 구단
        /// OVR)에는 절대 반영되지 않는다 - 경기 시작 시점에만 MatchEngine으로 일회성 전달되는
        /// "경기 조건부 적용 전력(MatchConditionModifier)"이다.
        /// </summary>
        public int ConditionBuff;

        /// <summary>
        /// [TASK-KBO-048] 득점권 상황에서 타자 긍정 결과(볼넷/안타/2루타/3루타/홈런) 가중치에 곱해지는
        /// 배율(Engine.TeamPowerModifiers.ClutchMultiplier에 전달 - 적용 지점은
        /// 15_team_power_policy.md 6절 참고). 기본/중립값은 1.0f(효과 없음)다.
        ///
        /// 이 값 역시 선수 스탯이나 영구 OVR에는 전혀 반영되지 않는 일회성 경기 조건부 값이며,
        /// 호출부(GameManager.ResolveCheerleaderClutchMultiplier())가 MatchEngine에 전달하기
        /// 직전 반드시 0 이하/NaN/Infinity 등 비정상 값을 1.0f로 정규화(Sanitize)한다.
        ///
        /// [TBD] 코치 등 다른 시너지와 중첩될 때의 합산 방식이나 상한선은 아직 기획 확정 전이다.
        /// </summary>
        public float ClutchMultiplier = 1.0f;

        /// <summary>
        /// [TASK-KBO-049] B안(상시 경제 효과) - 유저 팀이 "홈 경기에서 승리"했을 때 경기 보상(재화)에
        /// 곱해지는 배율. 기본/중립값은 1.0f(효과 없음). 위 ConditionBuff/ClutchMultiplier(A/C안)와
        /// 달리 MatchEngine에는 전혀 전달되지 않는다 - 시뮬레이션이 아니라 "스토브리그 로비 연산"
        /// (경기 결산 단계)에서만 쓰이는 값이며, 적용 지점은 MatchRewardManager.GrantRewardForMatch()
        /// 참고(15_team_power_policy.md 7절).
        /// </summary>
        public float EconomicBonusRate = 1.0f;

        /// <summary>
        /// [TASK-KBO-049] B안(상시 멘탈 효과) - 유저 팀이 연패했을 때 팬심(GameManager.FanSentiment)
        /// 하락폭을 방어하는 수치. 기본/중립값은 0(방어 없음). MatchRewardManager가
        /// `Mathf.Max(0, 기본 하락치 - SentimentDefense)`로 적용해, 방어가 하락폭을 초과해도 팬심이
        /// 오히려 상승하는 일이 없도록 클램핑한다. 위 두 필드와 마찬가지로 MatchEngine에는 전달되지
        /// 않으며, 오직 경기 결산 단계에서만 쓰인다.
        /// </summary>
        public int SentimentDefense = 0;

        /// <summary>
        /// [TASK-KBO-175] 이 카드의 소속 구단 - 활동 기간(ActivePeriod) 동안 소속했던 구단이며, 세트덱 기준 구단이
        /// 이 구단일 때만 치어리더 경기 버프(시너지)가 발동한다(CheerleaderSynergy). 같은 사람이라도 구단 이력마다
        /// 다른 카드다(예: 이아영 2020~2021 = KIA, 2022~2023 = NC). Team.None은 구단 제약 없는 개발용/식별 불가 카드.
        /// </summary>
        public Team Team = Team.None;

        /// <summary>
        /// [TASK-KBO-175] 활동 기간 원문("2020~2021", "2026~", "2009~2011/2017~", "2024"). 선수 시즌 카드처럼 단일
        /// 대표 연도로 쪼개지 않는다 - 파싱/판정은 CheerleaderActivePeriod 참고.
        /// </summary>
        public string ActivePeriod;

        /// <summary>UI 표기용 "구단 활동기간"(예: "KIA 2020~2021"). 구단 정보가 없으면 빈 문자열.</summary>
        public string AffiliationLabel => Team == Team.None
            ? string.Empty
            : string.IsNullOrEmpty(ActivePeriod) ? Team.ToString() : $"{Team} {ActivePeriod}";

        /// <summary>[TASK-KBO-187] 응원단 강화 단계(+0~+10강, 포인트 소모). CheerGrowth 참고.</summary>
        public int ReinforceLevel;

        /// <summary>[TASK-KBO-187] ★ 각성 단계(★1~★5, 동일 인물 재료 +2★ / 동일 구단 재료 +1★). 구버전 세이브(0)는 ★1로 읽는다.</summary>
        public int StarLevel = 1;

        public Cheerleader() { }

        public Cheerleader(string instanceId, string name, CheerleaderGrade grade, int conditionBuff, float clutchMultiplier,
            float economicBonusRate = 1.0f, int sentimentDefense = 0, string catalogId = null,
            Team team = Team.None, string activePeriod = null)
        {
            InstanceId = instanceId;
            Name = name;
            Grade = grade;
            ConditionBuff = conditionBuff;
            ClutchMultiplier = clutchMultiplier;
            EconomicBonusRate = economicBonusRate;
            SentimentDefense = sentimentDefense;
            CatalogId = catalogId;
            Team = team;
            ActivePeriod = activePeriod;
        }
    }

    /// <summary>
    /// [TASK-KBO-178] 유저에게 보이는 치어리더 티어 표기. 내부 enum은 세이브 호환 때문에 LIVE_NORMAL/LIVE_EPIC을 유지하지만 화면·로그에는
    /// 둘 다 "LIVE"로 통일한다(치어리더 DB의 라이브 티어는 하나뿐). SEASON_LIMITED는 상품이 폐지됐으나 구 세이브 표시용으로 "LIMITED".
    /// </summary>
    public static class CheerleaderGradeLabels
    {
        public static string Display(this CheerleaderGrade grade) => grade switch
        {
            CheerleaderGrade.LIVE_NORMAL => "LIVE",
            CheerleaderGrade.LIVE_EPIC => "LIVE",
            CheerleaderGrade.ICON => "ICON",
            CheerleaderGrade.LEGEND => "LEGEND",
            CheerleaderGrade.SEASON_LIMITED => "LIMITED",
            CheerleaderGrade.TEST => "TEST",
            _ => "-"
        };
    }

    /// <summary>
    /// [TASK-KBO-175] 치어리더 활동 기간 문자열 규칙. "시작~끝"(종료), "시작~"(진행 중), "연도"(단일 시즌),
    /// 복수 구간은 "/"로 연결("2009~2011/2017~"). GenerateKBODatabase.py의 CHEER_ICON_LEGEND 표기와 같다.
    /// </summary>
    public static class CheerleaderActivePeriod
    {
        /// <summary>진행 중("시작~") 구간의 끝 연도로 쓰는 값.</summary>
        public const int OngoingEndYear = int.MaxValue;

        /// <summary>구간 목록으로 파싱한다. 해석할 수 없는 구간은 건너뛴다(빈 문자열이면 빈 목록).</summary>
        public static List<(int Start, int End)> Parse(string activePeriod)
        {
            var ranges = new List<(int Start, int End)>();
            if (string.IsNullOrWhiteSpace(activePeriod)) return ranges;

            foreach (var rawSegment in activePeriod.Split('/'))
            {
                string segment = rawSegment.Trim();
                int tilde = segment.IndexOf('~');
                string startText = tilde < 0 ? segment : segment.Substring(0, tilde);
                if (!int.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)) continue;

                int end = start;
                if (tilde >= 0)
                {
                    string endText = segment.Substring(tilde + 1);
                    if (endText.Length == 0) end = OngoingEndYear;
                    else if (!int.TryParse(endText, NumberStyles.Integer, CultureInfo.InvariantCulture, out end)) continue;
                }
                ranges.Add((start, end));
            }
            return ranges;
        }

        /// <summary>year가 활동 기간 안에 있는가.</summary>
        public static bool Contains(string activePeriod, int year)
        {
            foreach (var (start, end) in Parse(activePeriod))
            {
                if (year >= start && year <= end) return true;
            }
            return false;
        }

        /// <summary>CatalogId용 토큰 - "~"는 "-", 열린 끝은 "NOW", "/"는 "+"("2009~2011/2017~" -&gt; "2009-2011+2017-NOW").
        /// GenerateKBODatabase.py cheer_period_token()과 반드시 같은 규칙이어야 한다.</summary>
        public static string ToIdToken(string activePeriod)
        {
            if (string.IsNullOrWhiteSpace(activePeriod)) return string.Empty;

            var segments = new List<string>();
            foreach (var rawSegment in activePeriod.Split('/'))
            {
                string segment = rawSegment.Trim();
                int tilde = segment.IndexOf('~');
                if (tilde < 0)
                {
                    segments.Add(segment);
                    continue;
                }
                string end = segment.Substring(tilde + 1);
                segments.Add($"{segment.Substring(0, tilde)}-{(end.Length == 0 ? "NOW" : end)}");
            }
            return string.Join("+", segments);
        }
    }

    /// <summary>
    /// [TASK-KBO-175] 치어리더 구단 시너지 판정. 치어리더 카드는 활동 기간 동안 소속했던 구단(Cheerleader.Team)이
    /// 세트덱 기준 구단(SetDeckResult.DeckTeam)과 같을 때만 경기 버프(ConditionBuff/ClutchMultiplier)가 발동한다 -
    /// 이아영 KIA(2020~2021) 카드는 KIA 세트덱에서만, NC(2022~2023) 카드는 NC 세트덱에서만 발동한다.
    /// 경기 결산 효과(EconomicBonusRate/SentimentDefense)는 구단과 무관한 상시 효과라 이 판정을 받지 않는다.
    /// </summary>
    public static class CheerleaderSynergy
    {
        public static bool IsActive(Cheerleader cheerleader, Team deckTeam)
        {
            if (cheerleader == null) return false;
            // 구단 정보가 없는 카드(개발용 TEST/더미, 카탈로그로 식별할 수 없는 구버전 세이브)는 기존처럼 제약 없이 동작한다.
            if (cheerleader.Team == Team.None) return true;
            return cheerleader.Team == deckTeam;
        }
    }
}
