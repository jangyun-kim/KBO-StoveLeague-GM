using System.Collections.Generic;
using System.Linq;
using KBOManager.Broadcast.Data;
using KBOManager.Broadcast.Utils;
using UnityEngine;

namespace KBOManager.Broadcast.Managers
{
    /// <summary>
    /// players.csv / cards.csv / match_event_rates.csv를 읽어 딕셔너리로 들고 있는 싱글톤 DB.
    /// 데이터의 단일 진실 공급원(Source of Truth)은 CSV이며, 이 클래스는 그 CSV를 파싱해 메모리에
    /// 올려두는 역할만 한다 - 기획자가 CSV 값을 고치고 다시 임포트하면(Resources 갱신) 다음 로드부터
    /// 즉시 반영된다. 기존 PlayerTemplate(ScriptableObject) 계열 시스템과는 완전히 독립된 병행 경로다.
    /// </summary>
    public class DataManager : MonoBehaviour
    {
        public static DataManager Instance { get; private set; }

        [Tooltip("Resources 폴더 기준 상대 경로(확장자 제외). 기본값은 Assets/Resources/Data/*.csv")]
        [SerializeField] private string playersResourcePath = "Data/players";
        [SerializeField] private string cardsResourcePath = "Data/cards";
        [SerializeField] private string matchEventsResourcePath = "Data/match_event_rates";

        private readonly Dictionary<string, PlayerModel> players = new Dictionary<string, PlayerModel>();
        private readonly Dictionary<string, CardModel> cards = new Dictionary<string, CardModel>();
        private readonly Dictionary<string, MatchEventModel> matchEvents = new Dictionary<string, MatchEventModel>();

        public IReadOnlyDictionary<string, PlayerModel> Players => players;
        public IReadOnlyDictionary<string, CardModel> Cards => cards;
        public IReadOnlyDictionary<string, MatchEventModel> MatchEvents => matchEvents;

        public bool IsLoaded { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadAll();
        }

        /// <summary>세 CSV를 (재)로드한다. Awake에서 자동 호출되지만, 에디터 QA 도구 등에서
        /// 런타임 중 CSV를 갈아끼운 뒤 강제로 다시 부를 수 있도록 public으로 노출한다.</summary>
        public void LoadAll()
        {
            players.Clear();
            cards.Clear();
            matchEvents.Clear();

            LoadPlayers();
            LoadCards();
            LoadMatchEvents();

            IsLoaded = true;
        }

        private void LoadPlayers()
        {
            foreach (var row in LoadRows(playersResourcePath))
            {
                var model = new PlayerModel
                {
                    PlayerId = row.GetString("player_id"),
                    TeamId = row.GetString("team_id"),
                    Name = row.GetString("name"),
                    Year = row.GetInt("year"),
                    Position = row.GetString("position"),
                    PaOrIp = row.GetInt("pa_ip"),
                    ZContact = row.GetFloat("z_contact"),
                    ZEye = row.GetFloat("z_eye"),
                    ZPower = row.GetFloat("z_power"),
                    ZSpeed = row.GetFloat("z_speed"),
                    ZDef = row.GetFloat("z_def"),
                    ZStamina = row.GetFloat("z_stamina"),
                    Active = row.GetBool("active", true),
                };

                if (string.IsNullOrEmpty(model.PlayerId))
                {
                    Debug.LogWarning("[DataManager] player_id가 비어 있는 행을 건너뜁니다.");
                    continue;
                }

                players[model.PlayerId] = model; // 동일 player_id 중복 시 마지막 행으로 덮어씀
            }
        }

        private void LoadCards()
        {
            foreach (var row in LoadRows(cardsResourcePath))
            {
                var model = new CardModel
                {
                    CardId = row.GetString("card_id"),
                    PlayerId = row.GetString("player_id"),
                    GradeId = row.GetInt("grade_id"),
                    GradeName = row.GetString("grade_name"),
                    BaseOvr = row.GetInt("base_ovr"),
                    SetDeckBaseScore = row.GetInt("salary_cost"), // [TASK-KBO-173] Salary = 기본 세트덱 스코어
                    MaxEnhance = row.GetInt("max_enhance"),
                    MaxAwaken = row.GetInt("max_awaken"),
                    IsDroppable = row.GetBool("is_droppable", true),
                };

                if (string.IsNullOrEmpty(model.CardId))
                {
                    Debug.LogWarning("[DataManager] card_id가 비어 있는 행을 건너뜁니다.");
                    continue;
                }

                cards[model.CardId] = model;
            }
        }

        private void LoadMatchEvents()
        {
            foreach (var row in LoadRows(matchEventsResourcePath))
            {
                var model = new MatchEventModel
                {
                    EventId = row.GetString("event_id"),
                    EventName = row.GetString("event_name"),
                    Category = row.GetString("category"),
                    BaseWeight = row.GetFloat("base_weight"),
                    OvrDiffModifier = row.GetFloat("ovr_diff_modifier"),
                    Description = row.GetString("description"),
                };

                if (string.IsNullOrEmpty(model.EventId))
                {
                    Debug.LogWarning("[DataManager] event_id가 비어 있는 행을 건너뜁니다.");
                    continue;
                }

                matchEvents[model.EventId] = model;
            }
        }

        private static List<Dictionary<string, string>> LoadRows(string resourcePath)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                Debug.LogError($"[DataManager] CSV를 찾을 수 없습니다: Resources/{resourcePath}");
                return new List<Dictionary<string, string>>();
            }

            return CsvParser.ParseToRows(textAsset.text);
        }

        // ----- 조회 헬퍼 -----

        public PlayerModel GetPlayer(string playerId) =>
            !string.IsNullOrEmpty(playerId) && players.TryGetValue(playerId, out var model) ? model : null;

        public CardModel GetCard(string cardId) =>
            !string.IsNullOrEmpty(cardId) && cards.TryGetValue(cardId, out var model) ? model : null;

        public MatchEventModel GetMatchEvent(string eventId) =>
            !string.IsNullOrEmpty(eventId) && matchEvents.TryGetValue(eventId, out var model) ? model : null;

        /// <summary>특정 선수가 보유한 모든 등급의 카드를 반환한다(예: 구자욱 SEASON/LIVE_EPIC/GOLDEN_GLOVE).</summary>
        public IEnumerable<CardModel> GetCardsByPlayer(string playerId) =>
            cards.Values.Where(c => c.PlayerId == playerId);

        /// <summary>모든 타석 결과 이벤트 정의. BroadcastMatchEngine이 확률 테이블을 만들 때 순회한다.</summary>
        public IEnumerable<MatchEventModel> GetAllMatchEvents() => matchEvents.Values;
    }
}
