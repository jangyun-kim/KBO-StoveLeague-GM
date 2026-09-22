using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// [TASK-KBO-082/085] 전체 선수 원본 템플릿을 보관하는 싱글톤 DB. docs/18_player_schema_policy.md /
    /// docs/11_data_dictionary.md(SSOT 확정, DCL-051)에 따라 players.csv를 SSOT로 삼는다 - 에디터에서
    /// PlayerTemplate .asset을 미리 구워 Inspector에 드래그하던 방식을 버리고, 게임 시작 시 CSV를 파싱해
    /// ScriptableObject.CreateInstance()로 런타임에만 존재하는 인스턴스를 생성해 메모리에 캐싱한다
    /// (cheerleaders.csv -&gt; CheerleaderCatalog.Initialize()와 동일한 패턴).
    ///
    /// [TASK-KBO-085] 다른 컴포넌트(GameManager 등)가 초기화 타이밍을 책임지지 않아도 되도록 완전히
    /// 자급자족한다 - 자신의 Awake()에서 스스로 Initialize()를 호출하고, 혹시 그보다 먼저 외부에서
    /// AllTemplates/GetTemplateById()가 호출되더라도(같은 GameObject에 붙은 다른 컴포넌트의 Awake()가
    /// 먼저 실행되는 등 Unity 컴포넌트 실행 순서 불확실성) 지연 초기화로 안전하게 채운다.
    /// 실제 유저 소유 카드(Models.Player 인스턴스)는 CreatePlayerInstance()로 템플릿을 참조해 발급한다.
    ///
    /// [TASK-KBO-153] DCL-057/DCL-124가 공지해 온 "cards.csv 미조인" 갭을 해소했다 - players.csv(선수
    /// 물리 데이터 SSOT)는 그대로 두고, cards.csv(카드별 등급 변형)를 추가로 파싱해 조인한다.
    /// players.csv 한 줄당 만들어지던 "기본 템플릿"을 그대로 두는 대신, cards.csv에 그 player_id로
    /// 등록된 카드가 있으면 그 카드 각각(등급별로 별도 row)을 `TemplateId=card_id`인 새 PlayerTemplate으로
    /// 대체 발급한다(물리 데이터는 기본 템플릿에서 복제) - 이제 `TemplateId`가 "카드 고유 ID"라는
    /// PlayerTemplate.cs 최초 설계 의도(TASK-082 주석 "예: GG_KOOJASOOK_2024")를 실제로 만족한다.
    /// cards.csv에 아직 등록되지 않은 선수는 안전 마이그레이션으로 기존 방식(TemplateId=player_id,
    /// Grade=LIVE_NORMAL 기본값) 그대로 남긴다 - 명령서 6항 "NullReference 없이 하위 호환"을 이렇게
    /// 만족한다(카드 CSV 자체가 하나도 없어도 완전히 예전과 동일하게 동작한다).
    ///
    /// [TASK-KBO-154] 카드 데이터가 단일 cards.csv 한 장에서 구단별 cards_{TEAM}.csv 10장으로
    /// 분할됐다(`GenerateKBODatabase.py`가 생성, 1986~2026년 방대한 카드 풀). 조인 로직은
    /// `Resources.LoadAll&lt;TextAsset&gt;("Data")`로 "Data" 폴더의 모든 TextAsset을 가져와 이름이
    /// `cards_`로 시작하는 파일만 순회하며 `ParseCardsCsv()`를 반복 호출·누적하는 방식으로
    /// 개편했다(파일 목록을 하드코딩하지 않아 구단이 늘어도 코드 수정이 필요 없다). 카드 CSV
    /// 스키마에도 `year`(카드가 실제로 발급된 시즌 연도) 컬럼이 10번째로 추가되어, 같은 선수의
    /// 카드라도 연도별로 `PlayerTemplate.SeasonYear`가 달라진다.
    /// </summary>
    public class PlayerDatabase : MonoBehaviour
    {
        public static PlayerDatabase Instance { get; private set; }

        private const string ResourcePath = "Data/players";

        // 헤더: player_id,team_id,name,year,position,pa_ip,z_contact,z_eye,z_power,z_speed,z_def,z_stamina,active,
        // z_stuff,z_control,z_movement ([TASK-KBO-088] 기존 13컬럼 뒤에 투수 전용 Z-score 3종을 추가한 16컬럼)
        private const int ExpectedColumnCount = 16;

        // [TASK-KBO-153] 헤더: card_id,player_id,grade_id,grade_name,base_ovr,salary_cost,max_enhance,
        // max_awaken,is_droppable,year (10컬럼 - year는 TASK-KBO-154에서 추가됨). 이번 조인은
        // card_id(카드 고유 ID)/player_id(FK)/grade/year만 대상으로 삼는다 - base_ovr/salary_cost/
        // max_enhance/max_awaken/is_droppable은 강화·샐러리 시스템(UpgradeManager/
        // Player.CalculateSalaryCost)과 얽혀 있어 "기존 강화 시스템 1mm도 건드리지 말 것"(TASK-153
        // 명령서 5항)의 범위를 넘어선다 - 이번엔 파싱하지 않고 후속 과제로 남긴다.
        //
        // [TASK-KBO-154] 카드 데이터가 단일 cards.csv에서 구단별 cards_{TEAM}.csv 10개로 분할됐다
        // (GenerateKBODatabase.py가 생성). `Resources.LoadAll&lt;TextAsset&gt;(CardsResourceFolder)`로
        // "Data" 폴더의 모든 TextAsset을 가져온 뒤 이름이 `CardsFilePrefix`로 시작하는 것만 골라
        // 순회 파싱한다 - players.csv/cheerleaders.csv 등 같은 폴더의 다른 CSV는 이름이 그
        // 접두사로 시작하지 않아 자동으로 제외된다.
        private const string CardsResourceFolder = "Data";
        private const string CardsFilePrefix = "cards_";
        private const int ExpectedCardColumnCount = 10;

        /// <summary>
        /// [TASK-KBO-085/086] CSV의 team_id(예: "TEM_001")를 Team enum으로 매핑하는 표. team_id 값이
        /// Team enum 이름(Doosan/LG/... 등)과 형식이 달라 Enum.TryParse로는 매핑할 수 없다는 사실이
        /// TASK-082/docs/18_player_schema_policy.md 2-1절에서 이미 확인되어 하드코딩 매핑 딕셔너리로
        /// 대체했다. 키가 없으면 Team.None으로 안전하게 폴백한다(TryGetValue 사용 -
        /// KeyNotFoundException 없음, TASK-085 명령서 9항).
        ///
        /// [TASK-KBO-086] docs/11_data_dictionary.md 2절에 공식 문서화된 KBO 10개 구단 전체 매핑표로
        /// 교체했다(TASK-085 당시의 3개 구단 임시 매핑을 대체 - 그 값이 이 문서와 서로 달라 DCL-055가
        /// [결정 필요]로 남겼던 불일치를 이 공식 매핑표로 해소한다). Team enum(Assets/Scripts/Models/
        /// Types.cs)에는 10개 구단이 전부 정의되어 있어(TASK-086 정적 확인 완료) Team.None 예외 매핑이
        /// 필요한 누락 구단은 없었다.
        /// </summary>
        private static readonly Dictionary<string, Team> TeamIdMapping = new Dictionary<string, Team>
        {
            { "TEM_001", Team.KIA },
            { "TEM_002", Team.Samsung },
            { "TEM_003", Team.LG },
            { "TEM_004", Team.Doosan },
            { "TEM_005", Team.KT },
            { "TEM_006", Team.SSG },
            { "TEM_007", Team.Lotte },
            { "TEM_008", Team.Hanwha },
            { "TEM_009", Team.NC },
            { "TEM_010", Team.Kiwoom },
        };

        private readonly Dictionary<string, PlayerTemplate> templates = new Dictionary<string, PlayerTemplate>();

        /// <summary>[TASK-KBO-085] Initialize()가 이미 실행됐는지(성공/실패 무관) 나타내는 플래그. 파싱
        /// 결과가 0건이어도(CSV가 비었거나 전부 실패) 재시도하지 않도록 templates.Count 대신 이 플래그로
        /// 판단한다(명령서 7항 - 다중 호출 방어).</summary>
        private bool isInitialized;

        /// <summary>DB에 등록된 전체 선수 템플릿 (읽기 전용 뷰). 기존 IReadOnlyList&lt;PlayerTemplate&gt; 반환
        /// 타입을 그대로 유지해 LeagueManager/OnboardingManager/ScoutManager 등 기존 소비자를 건드리지 않는다.
        /// [TASK-KBO-085] 접근 시점에 아직 초기화되지 않았다면 지연 초기화한다.</summary>
        public IReadOnlyList<PlayerTemplate> AllTemplates
        {
            get
            {
                EnsureInitialized();
                return templates.Values.ToList();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // [TASK-KBO-085] GameManager 등 외부 컴포넌트의 초기화 순서에 더 이상 의존하지 않는다 -
            // 스스로 여기서 곧바로 초기화한다.
            Initialize();
        }

        /// <summary>[TASK-KBO-085] AllTemplates/GetTemplateById가 Awake()보다 먼저 호출되는 예외적인
        /// 경우(예: 다른 컴포넌트가 이 오브젝트보다 먼저 Awake에서 접근)에도 크래시 없이 즉시 초기화한다.</summary>
        private void EnsureInitialized()
        {
            if (!isInitialized) Initialize();
        }

        /// <summary>
        /// players.csv를 읽어 PlayerTemplate 인스턴스를 런타임에 생성해 캐싱한다. isInitialized 플래그로
        /// 중복 실행을 막는다(성공/실패와 무관하게 1회만 시도, CheerleaderCatalog와 동일한 관례).
        /// Resources.Load가 실패하면(TextAsset을 찾지 못함) 빈 캐시로 남기고 크래시 없이 Debug.LogError만
        /// 남긴다. [TASK-KBO-085] GameManager 등 외부에서 더 이상 직접 호출하지 않으므로 private로 좁혔다 -
        /// Awake()의 자체 호출과 EnsureInitialized()의 지연 초기화 호출만 이 메서드에 접근한다.
        ///
        /// [TASK-KBO-153] cards.csv 조인이 이제 여기서 이루어진다(아래 ParseCardsCsv() 참고) - players.csv
        /// 파싱(ParsePlayersCsv())은 "기본 템플릿"만 만들 뿐, 최종 `templates`에 무엇이 들어갈지는
        /// cards.csv 조인 결과에 달렸다. [TASK-KBO-088] z_contact 등 9종 Z-score → BatterStats/
        /// PitcherStats(정수) 변환은 ParsePlayersCsv() 내부에서 ConvertZScoreToStat()으로 실제
        /// 구현되었다(docs/11_data_dictionary.md D절 확정 공식).
        /// </summary>
        private void Initialize()
        {
            if (isInitialized) return;
            isInitialized = true;

            var playersCsvAsset = Resources.Load<TextAsset>(ResourcePath);
            if (playersCsvAsset == null)
            {
                Debug.LogError($"[PlayerDatabase] '{ResourcePath}' TextAsset을 Resources에서 찾지 못했습니다. " +
                    "선수 데이터베이스가 빈 상태로 시작합니다(스카우트 발급이 실패할 수 있습니다).");
                return;
            }

            var baseTemplates = ParsePlayersCsv(playersCsvAsset.text);

            // [TASK-KBO-154, 명령서 6항 - 안전 마이그레이션] cards_*.csv가 한 장도 없어도(리소스 미존재)
            // 예전과 완전히 동일하게 동작해야 한다 - 카드 조인을 건너뛰고 기본 템플릿을 전부 그대로
            // 등록한다(부재는 NullReference가 아니라 Warning으로만 남긴다).
            var cardsCsvAssets = Resources.LoadAll<TextAsset>(CardsResourceFolder)
                .Where(a => a != null && a.name.StartsWith(CardsFilePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase) // 결정론적 로드 순서(로그 재현성)
                .ToArray();

            var playerIdsWithCards = new HashSet<string>();
            foreach (var asset in cardsCsvAssets)
            {
                playerIdsWithCards.UnionWith(ParseCardsCsv(asset.text, baseTemplates));
            }

            if (cardsCsvAssets.Length == 0)
            {
                Debug.LogWarning($"[PlayerDatabase] '{CardsResourceFolder}' 폴더에서 '{CardsFilePrefix}*' 카드 " +
                    "CSV를 하나도 찾지 못해 카드별 등급 조인 없이 선수당 기본(LIVE_NORMAL) 템플릿 1장씩만 등록합니다.");
            }

            int fallbackCount = 0;
            foreach (var pair in baseTemplates)
            {
                if (playerIdsWithCards.Contains(pair.Key)) continue; // 카드 조인으로 이미 대체된 선수는 제외

                templates[pair.Key] = pair.Value; // TemplateId=player_id, Grade=LIVE_NORMAL 기본값 그대로 유지
                fallbackCount++;
            }

            if (fallbackCount > 0)
            {
                Debug.Log($"[PlayerDatabase] cards_*.csv에 등록되지 않은 선수 {fallbackCount}명은 기본(LIVE_NORMAL) " +
                    "템플릿으로 폴백 등록했습니다.");
            }

            Debug.Log($"[PlayerDatabase] 카드 CSV {cardsCsvAssets.Length}개 파일 병합 파싱 완료. " +
                $"최종 템플릿 {templates.Count}개 등록 완료 " +
                $"(선수 {baseTemplates.Count}명 중 카드 조인 {playerIdsWithCards.Count}명 / 폴백 {fallbackCount}명).");
        }

        /// <summary>\n(또는 \r\n) 기준으로 줄을 나누고 첫 줄(헤더)은 건너뛴다. 컬럼 수가 부족한 줄이나
        /// 형변환에 실패한 줄은 그 한 줄만 Warning 후 건너뛰고 다음 줄을 계속 읽는다(명령서 6/7항 - 잘못된
        /// 한 줄 때문에 전체 로드가 죽지 않도록 방어).
        /// [TASK-KBO-153] 반환값은 `templates`에 바로 쓰지 않는 "기본 템플릿" 딕셔너리다(player_id 키) -
        /// 이 시점의 Grade는 항상 LIVE_NORMAL 기본값이며, 최종 등록 여부/Grade 재정의는 Initialize()가
        /// cards.csv 조인 결과를 보고 결정한다.</summary>
        private Dictionary<string, PlayerTemplate> ParsePlayersCsv(string csvText)
        {
            var baseTemplates = new Dictionary<string, PlayerTemplate>();
            if (string.IsNullOrEmpty(csvText)) return baseTemplates;

            var lines = csvText.Replace("\r\n", "\n").Split('\n');
            int loadedCount = 0;

            for (int i = 1; i < lines.Length; i++) // 0번째 줄(헤더)은 스킵
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var columns = line.Split(',');
                if (columns.Length < ExpectedColumnCount)
                {
                    Debug.LogWarning($"[PlayerDatabase] {i + 1}번째 줄의 컬럼 수가 부족해 건너뜁니다({columns.Length}/{ExpectedColumnCount}): '{line}'");
                    continue;
                }

                try
                {
                    // 실제 players.csv 헤더 순서(player_id,team_id,name,year,position,...) 기준 인덱스다.
                    // 명령서(TASK-KBO-085) 6항 예시는 team_id/position을 columns[2]/columns[3]으로 들었으나,
                    // 실제 헤더에서는 각각 columns[1]/columns[4]다(docs/18_player_schema_policy.md에서 이미
                    // 확인된 실제 스키마) - 잘못된 인덱스를 그대로 쓰면 name/year를 team_id/position으로
                    // 오독하게 되어, 검증된 실제 컬럼 순서를 그대로 따랐다.
                    string playerId = columns[0].Trim();
                    string teamId = columns[1].Trim();
                    string name = columns[2].Trim();
                    int year = int.Parse(columns[3].Trim(), CultureInfo.InvariantCulture);
                    string position = columns[4].Trim();

                    Team team = TeamIdMapping.TryGetValue(teamId, out var mappedTeam) ? mappedTeam : Team.None;

                    bool isPitcher = IsPitcherPosition(position);

                    var template = ScriptableObject.CreateInstance<PlayerTemplate>();
                    // [TASK-KBO-153] 이 시점의 TemplateId/RealPlayerId는 둘 다 player_id다 - 이 템플릿은
                    // 아직 "기본(물리 데이터) 템플릿"일 뿐이다. cards.csv에 이 player_id로 등록된 카드가
                    // 있으면 Initialize()가 ParseCardsCsv() 결과로 이 항목을 등급별 카드 템플릿(들)로
                    // 대체하고, 없는 선수만 이 기본 템플릿이 그대로 최종 등록된다(폴백).
                    template.TemplateId = playerId;
                    template.RealPlayerId = playerId;
                    template.PlayerName = name;
                    template.SeasonYear = year;
                    template.Team = team;
                    template.IsPitcher = isPitcher;
                    // [TASK-KBO-155] SEASON 삭제 이전에는 이 필드를 비워 둬도 C# enum 기본값(정수 0)이
                    // 곧 SEASON이라 우연히 맞았다 - 이제 0번 값에 대응하는 명명된 등급이 없으므로, 카드
                    // 폴백(cards_*.csv 미등록 선수)이 실제로 유효한 등급을 갖도록 명시적으로 대입한다.
                    template.Grade = Grade.LIVE_NORMAL;

                    // [TASK-KBO-088] docs/11_data_dictionary.md D절 확정 공식을 적용해 6종 공통 Z-score +
                    // 투수 전용 3종(z_stuff/z_control/z_movement, columns[13..15])을 1~100 정수 스탯으로
                    // 환산한다. z_speed는 PM 확정(명령서 3-3항)에 따라 타자는 Speed, 투수는 Velocity로
                    // 다형성 매핑한다. 개별 컬럼 파싱 실패(빈 값/포맷 오류)는 ParseZScore가 0.0f로 안전하게
                    // 폴백해 한 컬럼 오류로 행 전체가 스킵되지 않는다(명령서 7항).
                    float zContact = ParseZScore(columns[6]);
                    float zEye = ParseZScore(columns[7]);
                    float zPower = ParseZScore(columns[8]);
                    float zSpeed = ParseZScore(columns[9]);
                    float zDef = ParseZScore(columns[10]);
                    float zStamina = ParseZScore(columns[11]);
                    float zStuff = ParseZScore(columns[13]);
                    float zControl = ParseZScore(columns[14]);
                    float zMovement = ParseZScore(columns[15]);

                    if (isPitcher)
                    {
                        template.PitcherRole = ParsePitcherRole(position);
                        template.PitcherStats = new PitcherStats(
                            ConvertZScoreToStat(zStuff),
                            ConvertZScoreToStat(zSpeed), // 투수: z_speed → 구속(Velocity)
                            ConvertZScoreToStat(zMovement),
                            ConvertZScoreToStat(zControl),
                            ConvertZScoreToStat(zStamina));
                    }
                    else
                    {
                        template.BatterPosition = ParseBatterPosition(position);
                        template.BatterStats = new BatterStats(
                            ConvertZScoreToStat(zPower),
                            ConvertZScoreToStat(zContact),
                            ConvertZScoreToStat(zEye),
                            ConvertZScoreToStat(zSpeed), // 타자: z_speed → 주력(Speed)
                            ConvertZScoreToStat(zDef));
                    }

                    baseTemplates[playerId] = template;
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlayerDatabase] {i + 1}번째 줄 파싱에 실패해 건너뜁니다: '{line}' ({ex.Message})");
                    continue;
                }
            }

            Debug.Log($"[PlayerDatabase] players.csv에서 기본 템플릿 {loadedCount}개를 로드했습니다.");
            return baseTemplates;
        }

        /// <summary>[TASK-KBO-153, TASK-KBO-154] cards_{TEAM}.csv 한 장을 파싱해 player_id별로 등급
        /// 변형 카드를 만들어 `templates`에 직접 등록한다. 헤더: card_id,player_id,grade_id,grade_name,
        /// base_ovr,salary_cost,max_enhance,max_awaken,is_droppable,year(10컬럼, TASK-154에서 year
        /// 추가) - 이번 조인에는 card_id/player_id/grade_name(또는 grade_id)/year만 쓴다. 여러 파일에서
        /// 반복 호출되므로(Initialize() 참고) `templates`/`playerIdsWithCards`는 매 호출마다 누적된다.
        /// 반환값은 "이 파일에서 카드가 최소 1장이라도 등록된 player_id 집합"이다 - Initialize()가 전체
        /// 파일의 반환값을 합집합한 뒤, 그 안에 없는 player_id만 기본(LIVE_NORMAL) 템플릿으로 폴백 등록한다.
        /// 알 수 없는 player_id를 참조하는 카드 행이나 형변환 실패 행은 그 한 줄만 Warning 후 건너뛴다
        /// (players.csv와 동일한 방어 관례, 명령서 6/7항).</summary>
        private HashSet<string> ParseCardsCsv(string csvText, Dictionary<string, PlayerTemplate> baseTemplates)
        {
            var playerIdsWithCards = new HashSet<string>();
            if (string.IsNullOrEmpty(csvText)) return playerIdsWithCards;

            var lines = csvText.Replace("\r\n", "\n").Split('\n');
            int loadedCount = 0;

            for (int i = 1; i < lines.Length; i++) // 0번째 줄(헤더)은 스킵
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var columns = line.Split(',');
                if (columns.Length < ExpectedCardColumnCount)
                {
                    Debug.LogWarning($"[PlayerDatabase] cards_*.csv {i + 1}번째 줄의 컬럼 수가 부족해 건너뜁니다" +
                        $"({columns.Length}/{ExpectedCardColumnCount}): '{line}'");
                    continue;
                }

                try
                {
                    string cardId = columns[0].Trim();
                    string playerId = columns[1].Trim();
                    string gradeIdRaw = columns[2].Trim();
                    string gradeName = columns[3].Trim();

                    if (!baseTemplates.TryGetValue(playerId, out var baseTemplate))
                    {
                        Debug.LogWarning($"[PlayerDatabase] cards_*.csv {i + 1}번째 줄이 존재하지 않는 " +
                            $"player_id '{playerId}'를 참조해 건너뜁니다: '{line}'");
                        continue;
                    }

                    // grade_name(문자열)을 우선 시도하고, 실패하면 grade_id(정수, Grade enum과 동일한
                    // 서열 - 04_card_grade_policy.md/TASK-032-IMPLEMENT)로 폴백, 그마저 실패하면
                    // [TASK-KBO-155] LIVE_NORMAL(SEASON 삭제로 새로운 기본/floor 등급이 됨).
                    Grade grade;
                    if (!Enum.TryParse(gradeName, out grade))
                    {
                        if (int.TryParse(gradeIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gradeIdInt) &&
                            Enum.IsDefined(typeof(Grade), gradeIdInt))
                        {
                            grade = (Grade)gradeIdInt;
                        }
                        else
                        {
                            Debug.LogWarning($"[PlayerDatabase] cards_*.csv {i + 1}번째 줄의 등급 값을 해석하지 " +
                                $"못해 LIVE_NORMAL로 대체합니다(grade_id='{gradeIdRaw}', grade_name='{gradeName}'): '{line}'");
                            grade = Grade.LIVE_NORMAL;
                        }
                    }

                    // [TASK-KBO-154] year(10번째 컬럼, index 9) - 이 카드가 실제로 발급된 시즌 연도.
                    // 파싱에 실패해도 그 값만 기본 템플릿의 SeasonYear로 안전하게 폴백한다(행 전체를
                    // 스킵하지 않음 - 명령서 7항).
                    int year = int.TryParse(columns[9].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear)
                        ? parsedYear
                        : baseTemplate.SeasonYear;

                    var cardTemplate = CloneBaseTemplate(baseTemplate);
                    cardTemplate.TemplateId = cardId;      // [TASK-KBO-153] 이제부터 TemplateId = 카드 고유 ID
                    cardTemplate.RealPlayerId = playerId;  // 동일 선수 판정(각성 재료 등)은 그대로 player_id 기준
                    cardTemplate.Grade = grade;
                    cardTemplate.SeasonYear = year;        // [TASK-KBO-154] 카드별 실제 연도로 재정의

                    templates[cardId] = cardTemplate;
                    playerIdsWithCards.Add(playerId);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlayerDatabase] cards_*.csv {i + 1}번째 줄 파싱에 실패해 건너뜁니다: " +
                        $"'{line}' ({ex.Message})");
                    continue;
                }
            }

            Debug.Log($"[PlayerDatabase] cards_*.csv 파일 1개에서 카드 템플릿 {loadedCount}개를 로드했습니다 " +
                $"(선수 {playerIdsWithCards.Count}명 커버).");
            return playerIdsWithCards;
        }

        /// <summary>[TASK-KBO-153] 기본 템플릿(물리 데이터)의 필드를 새 ScriptableObject 인스턴스로
        /// 복제한다. Identity(TemplateId/RealPlayerId)와 Grade는 호출부(ParseCardsCsv)가 카드별로 다시
        /// 채울 것이므로 여기서는 복제하지 않는다.</summary>
        private static PlayerTemplate CloneBaseTemplate(PlayerTemplate source)
        {
            var clone = ScriptableObject.CreateInstance<PlayerTemplate>();
            clone.PlayerName = source.PlayerName;
            clone.SeasonYear = source.SeasonYear;
            clone.Team = source.Team;
            clone.IsPitcher = source.IsPitcher;
            clone.BatterPosition = source.BatterPosition;
            clone.PitcherRole = source.PitcherRole;
            clone.BatterStats = source.BatterStats;
            clone.PitcherStats = source.PitcherStats;
            clone.Cost = source.Cost;
            clone.PresetSkillTier = source.PresetSkillTier;
            clone.PresetSkillName = source.PresetSkillName;
            return clone;
        }

        /// <summary>[TASK-KBO-088] docs/11_data_dictionary.md D절 확정 공식. 평균(Z=0)을 50점에, 표준편차
        /// 1당 ±15점에 대응시키고 1~100 범위로 clamp한다.</summary>
        private static int ConvertZScoreToStat(float zScore) =>
            Mathf.Clamp(Mathf.RoundToInt(zScore * 15f + 50f), 1, 100);

        /// <summary>[TASK-KBO-088] 명령서 7항 - Z-score 컬럼 하나가 비어 있거나 포맷이 잘못돼도 행 전체가
        /// 스킵되지 않도록 예외 대신 0.0f로 안전하게 폴백한다.</summary>
        private static float ParseZScore(string raw) =>
            float.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;

        private static bool IsPitcherPosition(string position) =>
            position == "SP" || position == "RP" || position == "CP";

        /// <summary>players.csv의 position 컬럼은 투수를 SP/RP/CP 3종으로만 구분한다 - PitcherRole(5종:
        /// 선발/승리조/추격조/롱릴리프/마무리)만큼 세분화되어 있지 않아, RP는 잠정적으로
        /// WinningReliever로 매핑한다(docs/18_player_schema_policy.md 4-2절 [TBD] - 불펜 세부 롤 구분은
        /// CSV 스키마 확장이 필요).</summary>
        private static PitcherRole ParsePitcherRole(string position) => position switch
        {
            "SP" => PitcherRole.StartingPitcher,
            "CP" => PitcherRole.Closer,
            _ => PitcherRole.WinningReliever,
        };

        /// <summary>표준 야구 포지션 코드(docs/11_data_dictionary.md 3-A절과 동일한 값 체계)를
        /// BatterPosition enum으로 매핑한다. 알 수 없는 코드는 지명타자로 안전하게 폴백한다.</summary>
        private static BatterPosition ParseBatterPosition(string position) => position switch
        {
            "C" => BatterPosition.Catcher,
            "1B" => BatterPosition.FirstBase,
            "2B" => BatterPosition.SecondBase,
            "3B" => BatterPosition.ThirdBase,
            "SS" => BatterPosition.ShortStop,
            "LF" => BatterPosition.LeftField,
            "CF" => BatterPosition.CenterField,
            "RF" => BatterPosition.RightField,
            "DH" => BatterPosition.DesignatedHitter,
            _ => BatterPosition.DesignatedHitter,
        };

        /// <summary>TemplateId로 원본 템플릿을 조회한다. [TASK-KBO-153] cards.csv에 등록된 선수는
        /// TemplateId가 카드 고유 ID(cards.csv의 card_id, 예: "CRD_0001")이고, 아직 등록되지 않은 선수는
        /// 기존 방식대로 player_id다(안전 마이그레이션 폴백). [TASK-KBO-085] 접근 시점에 아직
        /// 초기화되지 않았다면 지연 초기화한다.</summary>
        public PlayerTemplate GetTemplateById(string templateId)
        {
            EnsureInitialized();
            return templates.TryGetValue(templateId, out var template) ? template : null;
        }

        /// <summary>
        /// 템플릿을 참조하는 새 카드 인스턴스를 발급한다. (스카우트/뽑기 등에서 사용)
        /// </summary>
        public Player CreatePlayerInstance(string templateId)
        {
            var template = GetTemplateById(templateId);
            if (template == null) return null;

            return new Player(System.Guid.NewGuid().ToString(), template);
        }
    }
}
