using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-068] 치어리더 가챠 대상 원본(카탈로그) 목록을 Resources/Data/cheerleaders.csv에서
    /// 읽어 메모리에 캐싱하는 Data-Driven 카탈로그. TASK-KBO-065의 하드코딩 버전을 대체한다.
    ///
    /// Initialize()를 GameManager.Awake()가 게임 시작 시 1회 호출한다(TASK-KBO-068). 이미 초기화된
    /// 뒤 다시 호출해도 재파싱하지 않는다. GetCheerleadersByGrade()도 방어적으로 지연 초기화를
    /// 시도하므로, 혹시 Initialize() 호출 전에 먼저 조회되어도 크래시하지 않는다.
    ///
    /// CSV 스키마(헤더): CatalogId,Name,Grade,ConditionBuff,EconomicBonusRate,ClutchMultiplier,
    /// SentimentDefense - 7개 컬럼. [TASK-KBO-069] 뒤 2개 컬럼(ClutchMultiplier/SentimentDefense)은
    /// TASK-KBO-068 당시 스키마 축소로 누락되어 모든 CSV 기반 치어리더가 중립값(1.0f/0)으로 통일되는
    /// 문제(docs/16_shop_and_gacha_policy.md 2절의 등급별 차등화 무효화, DCL-038)가 있었으나, 이번
    /// 작업에서 복원했다.
    ///
    /// 복잡한 외부 CSV 라이브러리를 쓰지 않고 string.Split(',') 수준의 단순 파서만 쓴다(명령서 5항) -
    /// 따라서 값에 콤마가 포함된 필드(따옴표 이스케이프 등)는 지원하지 않는다. 현재 카탈로그 데이터는
    /// 콤마를 포함하지 않으므로 문제가 없다.
    ///
    /// [TASK-KBO-175] 8·9번째 컬럼 Team/ActivePeriod를 읽는다(필수 7컬럼은 그대로 - 두 컬럼이 없는 행도 구단 정보
    /// 없이 로드된다). CatalogId = `{Team}_{활동기간 토큰}_CHR_{Name}_{Tier}`(예: KIA_2020-2021_CHR_이아영_LEGEND).
    /// TASK-171~174의 단일 연도 ID(`KIA_2020_CHR_이아영_LEGEND`)로 저장된 세이브는 Hydrate()가 같은 구단·이름·티어 +
    /// 그 연도를 포함하는 활동 기간의 새 카드로 매핑한다.
    /// </summary>
    public static class CheerleaderCatalog
    {
        private const string ResourcePath = "Data/cheerleaders";
        private const int ExpectedColumnCount = 7;
        private const int TeamColumn = 7;
        private const int ActivePeriodColumn = 8;
        private const string CatalogIdSeparator = "_CHR_";

        private static Dictionary<CheerleaderGrade, List<Cheerleader>> templatesByGrade;
        private static Dictionary<string, Cheerleader> templatesById;

        /// <summary>
        /// CSV를 읽어 등급별 템플릿 목록을 캐싱한다. 이미 초기화되어 있으면 아무 것도 하지 않는다
        /// (중복 로드 방지). Resources.Load가 실패하면(TextAsset을 찾지 못함) 빈 카탈로그로 초기화하고
        /// Debug.LogError만 남긴다 - 크래시하지 않는다. 이후 GetCheerleadersByGrade()는 빈 리스트를
        /// 반환하며, 그 폴백/에러 처리는 이미 CheerleaderGachaService.IssueCheerleader()(TASK-KBO-065,
        /// 이번 작업에서 수정하지 않음)가 담당한다.
        /// </summary>
        public static void Initialize()
        {
            if (templatesByGrade != null) return;

            templatesByGrade = new Dictionary<CheerleaderGrade, List<Cheerleader>>();
            templatesById = new Dictionary<string, Cheerleader>();

            var csvAsset = Resources.Load<TextAsset>(ResourcePath);
            if (csvAsset == null)
            {
                Debug.LogError($"[CheerleaderCatalog] '{ResourcePath}' TextAsset을 Resources에서 찾지 못했습니다. " +
                    "카탈로그가 빈 상태로 시작합니다(가챠 발급이 실패할 수 있습니다).");
                return;
            }

            ParseCsv(csvAsset.text);
        }

        /// <summary>\n(또는 \r\n) 기준으로 줄을 나누고 첫 줄(헤더)은 건너뛴다. 빈 줄이나 컬럼 수가
        /// 부족한 줄, 숫자/enum 파싱에 실패한 줄은 각각 Warning만 남기고 건너뛴다(명령서 6/7/9항 -
        /// 잘못된 한 줄 때문에 전체 로드가 죽지 않도록 방어).</summary>
        private static void ParseCsv(string csvText)
        {
            if (string.IsNullOrEmpty(csvText)) return;

            var lines = csvText.Replace("\r\n", "\n").Split('\n');
            int loadedCount = 0;

            for (int i = 1; i < lines.Length; i++) // 0번째 줄(헤더)은 스킵
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var columns = line.Split(',');
                if (columns.Length < ExpectedColumnCount)
                {
                    Debug.LogWarning($"[CheerleaderCatalog] {i + 1}번째 줄의 컬럼 수가 부족해 건너뜁니다({columns.Length}/{ExpectedColumnCount}): '{line}'");
                    continue;
                }

                try
                {
                    string catalogId = columns[0].Trim();
                    string name = columns[1].Trim();
                    var grade = (CheerleaderGrade)Enum.Parse(typeof(CheerleaderGrade), columns[2].Trim(), ignoreCase: true);
                    int conditionBuff = int.Parse(columns[3].Trim(), CultureInfo.InvariantCulture);
                    float economicBonusRate = float.Parse(columns[4].Trim(), CultureInfo.InvariantCulture);
                    float clutchMultiplier = float.Parse(columns[5].Trim(), CultureInfo.InvariantCulture);
                    int sentimentDefense = int.Parse(columns[6].Trim(), CultureInfo.InvariantCulture);
                    Team team = columns.Length > TeamColumn ? ParseTeam(columns[TeamColumn]) : Team.None;
                    string activePeriod = columns.Length > ActivePeriodColumn ? columns[ActivePeriodColumn].Trim() : null;

                    var template = new Cheerleader
                    {
                        CatalogId = catalogId,
                        Name = name,
                        Grade = grade,
                        ConditionBuff = conditionBuff,
                        EconomicBonusRate = economicBonusRate,
                        ClutchMultiplier = clutchMultiplier,
                        SentimentDefense = sentimentDefense,
                        Team = team,
                        ActivePeriod = string.IsNullOrEmpty(activePeriod) ? null : activePeriod,
                    };

                    if (templatesById.ContainsKey(catalogId))
                    {
                        Debug.LogWarning($"[CheerleaderCatalog] {i + 1}번째 줄의 CatalogId가 중복되어 건너뜁니다: '{catalogId}'");
                        continue;
                    }
                    templatesById[catalogId] = template;

                    if (!templatesByGrade.TryGetValue(grade, out var list))
                    {
                        list = new List<Cheerleader>();
                        templatesByGrade[grade] = list;
                    }

                    list.Add(template);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CheerleaderCatalog] {i + 1}번째 줄 파싱에 실패해 건너뜁니다: '{line}' ({ex.Message})");
                }
            }

            Debug.Log($"[CheerleaderCatalog] cheerleaders.csv에서 템플릿 {loadedCount}개를 로드했습니다.");
        }

        /// <summary>지정한 등급의 카탈로그 템플릿 목록을 반환한다(항상 새 리스트 - 내부 원본 보호).
        /// 해당 등급이 없으면 빈 리스트(null 아님). Initialize()가 아직 호출되지 않았다면 방어적으로
        /// 먼저 호출한다(호출 순서 실수로 인한 크래시 방지).</summary>
        public static List<Cheerleader> GetCheerleadersByGrade(CheerleaderGrade grade)
        {
            if (templatesByGrade == null) Initialize();

            return templatesByGrade.TryGetValue(grade, out var list)
                ? new List<Cheerleader>(list)
                : new List<Cheerleader>();
        }

        /// <summary>[TASK-KBO-175] CatalogId로 카탈로그 원본을 찾는다. 현행 ID에 없으면 TASK-171~174 단일 연도 ID로
        /// 해석해 같은 구단·이름·티어 + 그 연도를 포함하는 활동 기간의 카드를 찾는다. 못 찾으면 null.</summary>
        public static Cheerleader FindTemplate(string catalogId)
        {
            if (string.IsNullOrEmpty(catalogId)) return null;
            if (templatesById == null) Initialize();
            if (templatesById.TryGetValue(catalogId, out var exact)) return exact;

            if (!TryParseLegacyCatalogId(catalogId, out Team team, out int year, out string name, out string tier)) return null;

            Cheerleader fallback = null;
            int candidateCount = 0;
            foreach (var template in templatesById.Values)
            {
                if (template.Team != team || template.Name != name || TierToken(template.CatalogId) != tier) continue;
                if (CheerleaderActivePeriod.Contains(template.ActivePeriod, year)) return template;
                fallback = template;
                candidateCount++;
            }
            return candidateCount == 1 ? fallback : null;
        }

        /// <summary>
        /// [TASK-KBO-175] 세이브에서 복원한 치어리더 인스턴스에 현행 카탈로그의 CatalogId/Team/ActivePeriod를 채운다
        /// (구버전 세이브에는 Team/ActivePeriod 필드가 없고 CatalogId도 단일 연도 형식이다). 버프 수치·등급·InstanceId는
        /// 저장된 값을 그대로 둔다. 카탈로그로 식별할 수 없으면(예: 기준 개편으로 LEGEND에서 빠진 서현숙 LEGEND) ID와
        /// 수치는 보존하고, 구버전 ID 접두어에서 구단만 복원해 시너지 판정이 가능하게 한다.
        /// </summary>
        public static void Hydrate(Cheerleader instance)
        {
            if (instance == null || string.IsNullOrEmpty(instance.CatalogId)) return;

            var template = FindTemplate(instance.CatalogId);
            if (template != null)
            {
                instance.CatalogId = template.CatalogId;
                instance.Team = template.Team;
                instance.ActivePeriod = template.ActivePeriod;
                return;
            }

            if (instance.Team == Team.None && TryParseLegacyCatalogId(instance.CatalogId, out Team legacyTeam, out _, out _, out _))
            {
                instance.Team = legacyTeam;
            }
        }

        /// <summary>CSV Team 토큰("KIA"/"SAMSUNG"/"HANWHA" 등)을 Team enum으로(대소문자 무시). 해석 불가면 Team.None.</summary>
        private static Team ParseTeam(string token)
        {
            string trimmed = token?.Trim();
            return !string.IsNullOrEmpty(trimmed) && Enum.TryParse(trimmed, ignoreCase: true, out Team team) ? team : Team.None;
        }

        /// <summary>CatalogId 끝의 티어 토큰("LIVE"/"ICON"/"LEGEND").</summary>
        private static string TierToken(string catalogId)
        {
            int index = catalogId?.LastIndexOf('_') ?? -1;
            return index < 0 ? string.Empty : catalogId.Substring(index + 1);
        }

        /// <summary>TASK-171~174 단일 연도 ID `{Team}_{Year}_CHR_{Name}_{Tier}`를 해석한다(Year가 4자리 숫자일 때만).</summary>
        private static bool TryParseLegacyCatalogId(string catalogId, out Team team, out int year, out string name, out string tier)
        {
            team = Team.None;
            year = 0;
            name = null;
            tier = null;

            int separator = catalogId.IndexOf(CatalogIdSeparator, StringComparison.Ordinal);
            if (separator < 0) return false;

            var head = catalogId.Substring(0, separator).Split('_');
            string tail = catalogId.Substring(separator + CatalogIdSeparator.Length);
            int tierIndex = tail.LastIndexOf('_');
            if (head.Length != 2 || tierIndex <= 0 || head[1].Length != 4) return false;
            if (!int.TryParse(head[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out year)) return false;

            team = ParseTeam(head[0]);
            name = tail.Substring(0, tierIndex);
            tier = tail.Substring(tierIndex + 1);
            return team != Team.None;
        }
    }
}
