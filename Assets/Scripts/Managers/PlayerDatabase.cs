using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 전체 선수 원본 템플릿을 보관하는 싱글톤 DB.
    /// 실제 유저 소유 카드(Player 인스턴스)는 CreatePlayerInstance()로 템플릿을 복제해서 발급한다.
    /// </summary>
    public class PlayerDatabase : MonoBehaviour
    {
        public static PlayerDatabase Instance { get; private set; }

        // 등급별 초기 성급 규칙 (GDD v3.0 기준)
        // LIVE: 1성 / ALLSTAR: 2성 / GOLDEN_GLOVE: 4성 / SIGNATURE: 6성(만성) 지급
        private static readonly Dictionary<Grade, int> InitialStarByGrade = new Dictionary<Grade, int>
        {
            { Grade.LIVE, 1 },
            { Grade.ALLSTAR, 2 },
            { Grade.GOLDEN_GLOVE, 4 },
            { Grade.SIGNATURE, Player.MaxNormalStarLevel },
        };

        // 등급별 기본 오버롤/코스트 시작값
        private static readonly Dictionary<Grade, (int overallStart, int cost)> GradeStatBase = new Dictionary<Grade, (int, int)>
        {
            { Grade.LIVE, (60, 3) },
            { Grade.ALLSTAR, (70, 5) },
            { Grade.GOLDEN_GLOVE, (80, 7) },
            { Grade.SIGNATURE, (90, 9) },
        };

        private static readonly Position[] AllPositions = (Position[])System.Enum.GetValues(typeof(Position));
        private static readonly Team[] AllTeams = ((Team[])System.Enum.GetValues(typeof(Team)))
            .Where(t => t != Team.None).ToArray();

        private static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임" };
        private static readonly string[] GivenNames = { "민준", "서준", "도윤", "시우", "하준", "주원", "지호", "건우", "현우", "우진" };

        [SerializeField] private List<Player> allTemplates = new List<Player>();

        /// <summary>DB에 등록된 전체 선수 템플릿 (읽기 전용 뷰).</summary>
        public IReadOnlyList<Player> AllTemplates => allTemplates;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeDB();
        }

        /// <summary>
        /// 더미 선수 템플릿을 생성해 메모리(및 인스펙터)에 로드한다.
        /// 등급(Grade)당 7명씩, 총 28명을 생성한다.
        /// </summary>
        public void InitializeDB()
        {
            allTemplates.Clear();

            int templateId = 1;
            var grades = new[] { Grade.LIVE, Grade.ALLSTAR, Grade.GOLDEN_GLOVE, Grade.SIGNATURE };
            const int playersPerGrade = 7;

            foreach (var grade in grades)
            {
                var (overallStart, cost) = GradeStatBase[grade];
                int initialStar = InitialStarByGrade[grade];

                for (int i = 0; i < playersPerGrade; i++)
                {
                    var position = AllPositions[(templateId - 1) % AllPositions.Length];
                    var team = AllTeams[(templateId - 1) % AllTeams.Length];
                    string name = Surnames[(templateId - 1) % Surnames.Length]
                                  + GivenNames[(templateId * 3) % GivenNames.Length];
                    int overall = overallStart + i; // 등급 내에서도 소폭 편차를 둠
                    int playerCost = cost + (i / 3);

                    var template = new Player(
                        playerId: $"TPL_{templateId:D3}",
                        templateId: templateId,
                        name: name,
                        position: position,
                        team: team,
                        grade: grade,
                        baseOverall: overall,
                        cost: playerCost,
                        starLevel: initialStar,
                        starType: StarType.NORMAL
                    );

                    allTemplates.Add(template);
                    templateId++;
                }
            }
        }

        /// <summary>TemplateId로 원본 템플릿을 조회한다.</summary>
        public Player GetTemplateById(int templateId)
        {
            return allTemplates.FirstOrDefault(p => p.TemplateId == templateId);
        }

        /// <summary>
        /// 템플릿을 복제해 유저가 실제로 소유할 수 있는 새 카드 인스턴스를 발급한다.
        /// (스카우트/뽑기 등에서 사용)
        /// </summary>
        public Player CreatePlayerInstance(int templateId)
        {
            var template = GetTemplateById(templateId);
            if (template == null) return null;

            return template.Clone(System.Guid.NewGuid().ToString());
        }
    }
}
