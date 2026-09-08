using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 최초 실행(GameManager.IsFirstLogin == true) 시 선호 구단을 고르게 하고, 그 구단의 LIVE 등급
    /// 1~3성 스타터 팩 28장(타자 15 + 투수 13)을 인벤토리에 지급한 뒤 자동으로 로스터를 채워 로비로
    /// 진입시키는 싱글톤. OnboardingUIController의 구단 버튼 클릭이 CompleteOnboarding(Team) 하나만
    /// 호출하면 "지급 -&gt; 편성 -&gt; 화면 전환"이 한 번에 끝나도록 브릿지 역할을 겸한다.
    ///
    /// 온보딩을 거치기 전에는 GameManager.Roster/Inventory가 항상 비어 있었다 - LeagueManager가 그
    /// 빈 로스터의 소유 구단(Team.None, InitializeLeague에 아직 전달된 적 없는 기본값)으로 시즌을
    /// 초기화하면 standings 딕셔너리에 Team.None 키가 없어(InitializeLeague가 Team.None은 건너뛰고
    /// 채우므로) KeyNotFoundException으로 죽는 경로가 있었다. 온보딩은 로비에 진입하는 모든 경로보다
    /// 먼저 강제 실행되므로(UIManager.Start()), 이 시점 이후로는 GameManager.FavoriteTeam이 항상
    /// 실제 KBO 구단이고 Roster/Inventory가 항상 28장으로 채워져 있다는 불변식이 성립한다.
    /// </summary>
    public class OnboardingManager : MonoBehaviour
    {
        public static OnboardingManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("구단별 실제 카드가 등록돼 있으면 우선 사용한다. 없으면 procedural(더미) 템플릿으로 대체한다.")]
        [SerializeField] private PlayerDatabase playerDatabase;
        [Tooltip("스타터 팩 카드에도 초기 스킬을 1개씩 붙이고 싶을 때 연결한다. 비워두면 스킬 없이 지급한다.")]
        [SerializeField] private SkillDB skillDB;
        [Tooltip("스타터 팩 지급 직후 오토 라인업에 사용할 RosterManager.")]
        [SerializeField] private RosterManager rosterManager;
        [Tooltip("오토 라인업 시 적용할 샐러리 캡. GameActionController의 기본값(300)과 동일하게 맞춰 둔다.")]
        [SerializeField] private int starterPackSalaryCap = 300;

        // RosterManager/LeagueManager와 동일한 28인 배분(타자 9선발+6벤치, 투수 5/2/4/1/1)을 그대로
        // 재사용한다 - 세 곳의 배분 규칙이 어긋나면 오토 라인업 결과가 팀마다 달라지는 버그가 생긴다.
        private static readonly BatterPosition[] AllBatterPositions = (BatterPosition[])Enum.GetValues(typeof(BatterPosition));
        private const int BenchBatterCount = 6;

        private static readonly (PitcherRole role, int count)[] PitcherRoleQuota =
        {
            (PitcherRole.StartingPitcher, 5),
            (PitcherRole.WinningReliever, 2),
            (PitcherRole.MopUpReliever, 4),
            (PitcherRole.LongReliever, 1),
            (PitcherRole.Closer, 1),
        };

        private const int StarterCardStatLevel = 50; // GDD 밸런스 미확정 - LIVE 등급다운 낮은 초기 스탯 임시값

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// OnboardingUIController의 구단 버튼 OnClick이 호출하는 단일 진입점.
        /// 1) FavoriteTeam 설정 2) LIVE 1~3성 스타터 팩 28장 생성 및 인벤토리 지급
        /// 3) RosterManager.AutoSetRoster()로 28인 로스터 자동 편성 4) IsFirstLogin = false
        /// 5) 로비 화면으로 전환, 순서로 한 번에 처리한다.
        /// </summary>
        public void CompleteOnboarding(Team team)
        {
            if (team == Team.None || GameManager.Instance == null) return;

            GameManager.Instance.FavoriteTeam = team;

            var starterPack = GenerateStarterPack(team);
            foreach (var player in starterPack)
            {
                GameManager.Instance.AddPlayerToInventory(player);
            }

            if (rosterManager != null)
            {
                var autoRoster = rosterManager.AutoSetRoster(GameManager.Instance.Inventory.ToList(), starterPackSalaryCap);
                GameManager.Instance.OverwriteRoster(autoRoster);
            }
            else
            {
                // RosterManager 미연결 시 폴백: 스타터 팩 28장이 곧 정확히 요구 로스터 크기이므로 그대로 편성한다.
                GameManager.Instance.OverwriteRoster(starterPack);
            }

            GameManager.Instance.IsFirstLogin = false;
            UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }

        /// <summary>team 소속 LIVE 등급 1~3성 카드 28장(타자 9선발+6벤치, 투수 5/2/4/1/1)을 생성한다.</summary>
        private List<Player> GenerateStarterPack(Team team)
        {
            var pack = new List<Player>(GameManager.RequiredRosterSize);

            foreach (var position in AllBatterPositions)
            {
                pack.Add(CreateStarterPlayer(team, false, position, null));
            }

            for (int i = 0; i < BenchBatterCount; i++)
            {
                var randomPosition = AllBatterPositions[UnityEngine.Random.Range(0, AllBatterPositions.Length)];
                pack.Add(CreateStarterPlayer(team, false, randomPosition, null));
            }

            foreach (var (role, count) in PitcherRoleQuota)
            {
                for (int i = 0; i < count; i++)
                {
                    pack.Add(CreateStarterPlayer(team, true, null, role));
                }
            }

            return pack;
        }

        private Player CreateStarterPlayer(Team team, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole)
        {
            var template = FindLiveTemplate(team, isPitcher, batterPosition, pitcherRole)
                ?? CreateProceduralLiveTemplate(team, isPitcher, batterPosition, pitcherRole);

            var player = new Player(Guid.NewGuid().ToString(), template);
            // GDD 2절: LIVE_NORMAL은 1~3성 무작위 배정 (ScoutManager.ApplyInitialGradeRule과 동일 규칙).
            player.CurrentStarType = StarType.NORMAL;
            player.StarLevel = UnityEngine.Random.Range(Player.MinStarLevel, 4);

            if (skillDB != null)
            {
                var skill = skillDB.GetRandomSkill(template);
                if (skill != null) player.AcquiredSkillIds.Add(skill.SkillName);
            }

            return player;
        }

        /// <summary>playerDatabase에 등록된 실제 카드 중 team 소속 LIVE_NORMAL 등급 첫 항목을 사용한다.</summary>
        private PlayerTemplate FindLiveTemplate(Team team, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole)
        {
            if (playerDatabase == null) return null;

            return playerDatabase.AllTemplates.FirstOrDefault(t =>
                t.Team == team && t.Grade == Grade.LIVE_NORMAL && t.IsPitcher == isPitcher &&
                (isPitcher ? t.PitcherRole == pitcherRole : t.BatterPosition == batterPosition));
        }

        /// <summary>실제 카드가 없을 때 즉석에서 만드는 더미 템플릿. LeagueManager.CreateProceduralTemplate과
        /// 동일한 패턴이나, AI가 아닌 유저 스타터 팩이므로 등급을 항상 LIVE_NORMAL로 고정한다.</summary>
        private PlayerTemplate CreateProceduralLiveTemplate(Team team, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole)
        {
            var template = ScriptableObject.CreateInstance<PlayerTemplate>();

            string roleLabel = isPitcher ? pitcherRole.ToString() : batterPosition.ToString();
            template.TemplateId = $"STARTER_{team}_{roleLabel}_{Guid.NewGuid():N}";
            template.RealPlayerId = template.TemplateId;
            template.PlayerName = $"{team} 신인 ({roleLabel})";
            template.Team = team;
            template.Grade = Grade.LIVE_NORMAL;
            template.IsPitcher = isPitcher;
            template.Cost = 1;

            if (isPitcher)
            {
                template.PitcherRole = pitcherRole ?? PitcherRole.StartingPitcher;
                template.PitcherStats = new PitcherStats(StarterCardStatLevel, StarterCardStatLevel, StarterCardStatLevel, StarterCardStatLevel);
            }
            else
            {
                template.BatterPosition = batterPosition ?? BatterPosition.DesignatedHitter;
                template.BatterStats = new BatterStats(StarterCardStatLevel, StarterCardStatLevel, StarterCardStatLevel);
            }

            return template;
        }
    }
}
