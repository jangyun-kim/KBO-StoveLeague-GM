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
    /// </summary>
    public class PlayerDatabase : MonoBehaviour
    {
        public static PlayerDatabase Instance { get; private set; }

        private const string ResourcePath = "Data/players";

        // 헤더: player_id,team_id,name,year,position,pa_ip,z_contact,z_eye,z_power,z_speed,z_def,z_stamina,active
        private const int ExpectedColumnCount = 13;

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
        /// [TASK-KBO-082 범위 제외, TASK-KBO-085에서도 계속 제외] cards.csv(등급/샐러리/강화 상한 등 카드
        /// 변형 데이터)는 조인하지 않는다 - 여기서 생성되는 템플릿의 Grade는 기본값(Grade.SEASON = 0)으로
        /// 남는다. z_contact 등 6종 Z-score를 BatterStats/PitcherStats(정수)로 변환하는 공식도 아직
        /// 확정되지 않아(명령서 5항이 이번 작업에서도 배제를 명시) 세부 스탯은 기본값(0)으로 남는다.
        /// </summary>
        private void Initialize()
        {
            if (isInitialized) return;
            isInitialized = true;

            var csvAsset = Resources.Load<TextAsset>(ResourcePath);
            if (csvAsset == null)
            {
                Debug.LogError($"[PlayerDatabase] '{ResourcePath}' TextAsset을 Resources에서 찾지 못했습니다. " +
                    "선수 데이터베이스가 빈 상태로 시작합니다(스카우트 발급이 실패할 수 있습니다).");
                return;
            }

            ParseCsv(csvAsset.text);
        }

        /// <summary>\n(또는 \r\n) 기준으로 줄을 나누고 첫 줄(헤더)은 건너뛴다. 컬럼 수가 부족한 줄이나
        /// 형변환에 실패한 줄은 그 한 줄만 Warning 후 건너뛰고 다음 줄을 계속 읽는다(명령서 6/7항 - 잘못된
        /// 한 줄 때문에 전체 로드가 죽지 않도록 방어).</summary>
        private void ParseCsv(string csvText)
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
                    // TemplateId/RealPlayerId 둘 다 player_id로 채운다 - cards.csv(카드 등급 변형)를 아직
                    // 조인하지 않는 이번 태스크 범위에서는 "선수 1명 = 카드 1장"이라 두 식별자가 같다.
                    template.TemplateId = playerId;
                    template.RealPlayerId = playerId;
                    template.PlayerName = name;
                    template.SeasonYear = year;
                    template.Team = team;
                    template.IsPitcher = isPitcher;

                    if (isPitcher)
                    {
                        template.PitcherRole = ParsePitcherRole(position);
                    }
                    else
                    {
                        template.BatterPosition = ParseBatterPosition(position);
                    }

                    templates[playerId] = template;
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlayerDatabase] {i + 1}번째 줄 파싱에 실패해 건너뜁니다: '{line}' ({ex.Message})");
                    continue;
                }
            }

            Debug.Log($"[PlayerDatabase] players.csv에서 템플릿 {loadedCount}개를 로드했습니다.");
        }

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

        /// <summary>TemplateId(=players.csv의 player_id)로 원본 템플릿을 조회한다. [TASK-KBO-085] 접근
        /// 시점에 아직 초기화되지 않았다면 지연 초기화한다.</summary>
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
