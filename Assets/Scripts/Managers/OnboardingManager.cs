using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// [TASK-KBO-181 전면 개편] 정식 온보딩(타이틀 → 구단 선택·닉네임 → 정착 지원 선물 → 튜토리얼)의 지급/편성 담당 싱글톤.
    ///
    /// 지급 규칙(OnboardingRules):
    ///   1) 선택 구단의 2026 LIVE_NORMAL 카드 전원(구단당 32~35장)을 무료 지급한다 - 구 스타터 팩(LIVE 26장 + 절차 생성 "프랜차이즈 스타" 2장)을
    ///      실존 2026 현역 풀 로스터로 대체했다. DB가 연결되지 않았거나 인원이 부족할 때만 부족분(타자 15 / 투수 13 미만)을 절차 생성 LIVE 카드로 채운다.
    ///   2) 정착 지원 선물 2024 골든글러브 4종 중 유저가 고른 1장을 지급한다.
    ///   3) RosterManager.AutoSetRoster()로 28인(주전 9 + 후보 6 + 투수 13)을 즉시 편성 - 선물 카드는 OVR이 해당 포지션 주전보다 높으면
    ///      그대로 주전(선발 로테이션)으로 들어가고, 결과는 LastGiftPlacementNote로 튜토리얼 1단계에 안내된다.
    ///   4) IsFirstLogin = false, TutorialCompleted = false(첫 로비 진입 시 3단계 가이드), 리그 초기화, 세이브, 로비 전환.
    /// OnboardingUIController는 "어떤 구단/닉네임/선물이 골라졌는지"만 전달하는 얇은 브릿지다.
    /// </summary>
    public class OnboardingManager : MonoBehaviour
    {
        public static OnboardingManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("선택 구단 2026 LIVE_NORMAL 카드와 선물 카드를 찾는다. 비우면 PlayerDatabase.Instance를 쓴다.")]
        [SerializeField] private PlayerDatabase playerDatabase;
        [Tooltip("지급 카드에 초기 스킬을 1개씩 붙인다. 비워두면 스킬 없이 지급한다.")]
        [SerializeField] private SkillDB skillDB;
        [Tooltip("지급 직후 오토 라인업에 사용할 RosterManager.")]
        [SerializeField] private RosterManager rosterManager;

        private static readonly BatterPosition[] AllBatterPositions = (BatterPosition[])Enum.GetValues(typeof(BatterPosition));
        private const int StarterCardStatLevel = 50; // 절차 생성(DB 부족분 보충) LIVE 카드 스탯 수준

        /// <summary>마지막 온보딩에서 선물 카드가 어디에 편성됐는지(튜토리얼 1단계 안내 문구).</summary>
        public string LastGiftPlacementNote { get; private set; } = "";

        /// <summary>마지막 온보딩에서 실제 DB 카드로 지급한 2026 LIVE_NORMAL 장수(절차 생성 보충분 제외).</summary>
        public int LastStarterCardCount { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private PlayerDatabase Database => playerDatabase != null ? playerDatabase : PlayerDatabase.Instance;

        /// <summary>정착 지원 선물 4종 미리보기 카드(OnboardingRules.GiftTemplateIds 순서). DB에 없는 카드는 null.</summary>
        public List<Player> CreateGiftPreviews()
        {
            var db = Database;
            return OnboardingRules.GiftTemplateIds.Select(id => db != null ? db.CreatePlayerInstance(id) : null).ToList();
        }

        /// <summary>[구 API 호환] 닉네임/선물 없이 구단만으로 온보딩을 마친다.</summary>
        public void CompleteOnboarding(Team team) => CompleteOnboarding(team, null, null);

        /// <summary>
        /// 온보딩 단일 진입점(선물 화면의 [선물 수령 및 단장 취임]). 이미 진행 중이던 커리어가 있으면([새 단장 부임]) 먼저 초기화한다.
        /// </summary>
        public void CompleteOnboarding(Team team, string nickname, string giftTemplateId)
        {
            var gm = GameManager.Instance;
            if (team == Team.None || gm == null) return;

            if (gm.Inventory.Count > 0 || gm.Roster.Count > 0 || !gm.IsFirstLogin) gm.ResetForNewManager();

            gm.FavoriteTeam = team;
            gm.ManagerNickname = OnboardingRules.NormalizeNickname(nickname) ?? "";

            foreach (var player in GenerateStarterPack(team)) gm.AddPlayerToInventory(player);

            Player gift = null;
            if (OnboardingRules.IsGiftTemplateId(giftTemplateId))
            {
                var template = Database != null ? Database.GetTemplateById(giftTemplateId) : null;
                if (template != null)
                {
                    gift = CreatePlayer(template, randomizeLiveStars: false);
                    gm.AddPlayerToInventory(gift);
                }
                else
                {
                    Debug.LogWarning($"[OnboardingManager] 선물 카드 '{giftTemplateId}'를 PlayerDatabase에서 찾지 못했습니다.");
                }
            }

            if (rosterManager != null)
            {
                gm.OverwriteRoster(rosterManager.AutoSetRoster(gm.Inventory.ToList(), team.ToString()));
            }
            else
            {
                Debug.LogWarning("[OnboardingManager] RosterManager가 연결되지 않아 보유 카드 앞에서부터 타자 15 / 투수 13을 편성합니다.");
                foreach (var player in gm.Inventory.ToList()) gm.AddPlayerToRoster(player);
            }

            LastGiftPlacementNote = OnboardingRules.DescribeGiftPlacement(gm.Roster, gift);
            gm.IsFirstLogin = false;
            gm.TutorialCompleted = false;

            // 새 게임 경로에서 리그(스케줄/AI 로스터/캘린더)를 만드는 곳은 여기뿐이다 - 로스터 편성이 끝난 뒤 호출해야 AI 목표 스탯이 맞는다.
            LeagueManager.Instance?.InitializeLeague(team);

            Debug.Log($"[OnboardingManager] 단장 취임: {team} '{gm.ManagerNickname}' - 2026 LIVE 지급 {LastStarterCardCount}장, " +
                $"보유 {gm.Inventory.Count}장, 1군 {gm.Roster.Count}명(완비={gm.IsRosterComplete()}), 선물: {LastGiftPlacementNote}");

            SaveManager.Instance?.TrySaveCareer();
            UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }

        /// <summary>선택 구단 2026 LIVE_NORMAL 전원 + (부족할 때만) 절차 생성 LIVE 보충분.</summary>
        private List<Player> GenerateStarterPack(Team team)
        {
            var db = Database;
            var real = db != null ? OnboardingRules.SelectStarterTemplates(db.AllTemplates, team) : new List<PlayerTemplate>();
            LastStarterCardCount = real.Count;
            if (real.Count == 0) Debug.LogWarning($"[OnboardingManager] {team} 2026 LIVE_NORMAL 카드를 찾지 못해 절차 생성 선수로 대체합니다.");

            var pack = real.Select(t => CreatePlayer(t, randomizeLiveStars: true)).ToList();

            // 타자 보충: 비어 있는 포지션부터, 그다음 무작위 포지션으로 15명까지.
            int batters = pack.Count(p => !p.Template.IsPitcher);
            foreach (var position in AllBatterPositions)
            {
                if (batters >= GameManager.RequiredBatterCount) break;
                if (pack.Any(p => !p.Template.IsPitcher && p.Template.BatterPosition == position)) continue;
                pack.Add(CreatePlayer(CreateProceduralLiveTemplate(team, false, position, null), true));
                batters++;
            }
            while (batters < GameManager.RequiredBatterCount)
            {
                var position = AllBatterPositions[UnityEngine.Random.Range(0, AllBatterPositions.Length)];
                pack.Add(CreatePlayer(CreateProceduralLiveTemplate(team, false, position, null), true));
                batters++;
            }

            // 투수 보충: 보직 쿼터가 모자란 보직부터 13명까지.
            int pitchers = pack.Count(p => p.Template.IsPitcher);
            foreach (var (role, count) in RosterSlotLayout.PitcherRoleQuota)
            {
                int missing = count - pack.Count(p => p.Template.IsPitcher && p.Template.PitcherRole == role);
                for (int i = 0; i < missing && pitchers < GameManager.RequiredPitcherCount; i++, pitchers++)
                {
                    pack.Add(CreatePlayer(CreateProceduralLiveTemplate(team, true, null, role), true));
                }
            }

            return pack;
        }

        private Player CreatePlayer(PlayerTemplate template, bool randomizeLiveStars)
        {
            var player = new Player(Guid.NewGuid().ToString(), template);
            if (randomizeLiveStars && template.Grade == Grade.LIVE_NORMAL)
            {
                // GDD 2절: LIVE_NORMAL은 1~3성 무작위 배정(ScoutManager.ApplyInitialGradeRule과 동일 규칙).
                player.CurrentStarType = StarType.NORMAL;
                player.StarLevel = UnityEngine.Random.Range(Player.MinStarLevel, 4);
            }

            if (skillDB != null)
            {
                var skill = skillDB.GetRandomSkill(template);
                if (skill != null) player.AcquiredSkillIds.Add(skill.SkillName);
            }

            return player;
        }

        /// <summary>DB 부족분 보충용 더미 LIVE_NORMAL 템플릿.</summary>
        private static PlayerTemplate CreateProceduralLiveTemplate(Team team, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole)
        {
            var template = ScriptableObject.CreateInstance<PlayerTemplate>();

            string roleLabel = isPitcher ? pitcherRole.ToString() : batterPosition.ToString();
            template.TemplateId = $"STARTER_{team}_{roleLabel}_{Guid.NewGuid():N}";
            template.RealPlayerId = template.TemplateId;
            template.PlayerName = $"{team} 신인 ({roleLabel})";
            template.Team = team;
            template.Grade = Grade.LIVE_NORMAL;
            template.SeasonYear = OnboardingRules.StarterSeasonYear;
            template.IsPitcher = isPitcher;
            template.Cost = 1;

            if (isPitcher)
            {
                template.PitcherRole = pitcherRole ?? PitcherRole.StartingPitcher;
                template.PitcherStats = StatProfiles.SpreadPitcher(StarterCardStatLevel, template.PitcherRole, template.TemplateId.GetHashCode());
            }
            else
            {
                template.BatterPosition = batterPosition ?? BatterPosition.DesignatedHitter;
                template.BatterStats = StatProfiles.SpreadBatter(StarterCardStatLevel, template.BatterPosition, template.TemplateId.GetHashCode());
            }

            return template;
        }
    }
}
