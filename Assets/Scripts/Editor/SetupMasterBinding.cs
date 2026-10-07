using System;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-105] 씬에 이미 조립돼 있으나 인스펙터 참조가 None으로 비어 있는 핵심 UI 컨트롤러들의
    /// 매니저/텍스트 참조를 한 번에 재탐색·재바인딩하는 통합 핫픽스. 각 Setup*.cs(SetupScoutUI.cs/
    /// SetupLeagueUI.cs/SetupInGameUI.cs)가 최초 조립 시점에 놓쳤거나, 저장 누락/씬 리로드 등으로
    /// 유실된 참조를 대상으로 하며, 새 UI 오브젝트를 생성하지 않고 씬에 이미 존재하는 오브젝트만
    /// 이름으로 다시 찾아 연결한다(명령서 5항 - RectTransform 레이아웃 불변).
    /// </summary>
    public static class SetupMasterBinding
    {
        /// <summary>
        /// [TASK-KBO-176] TASK-168~176에서 추가/변경된 UI를 씬에 한 번에 반영하는 통합 메뉴(전부 idempotent - 몇 번 실행해도 안전).
        ///   1) SetupTemplates.SetupDualPortraitAndGradeFrameLayers - 카드 4단 레이어(TASK-168), 텍스트 Outline+Shadow
        ///      가독성(TASK-170, TASK-176 버그 수정), 카드 "SD n" 세트덱 스코어 표기(TASK-173/176).
        ///   2) SetupRosterUI.AutoConnectRosterUI - 주전/후보 구역 분리 + 카드 클릭 교체 팝업 + 세트덱 선택형 버프 패널(TASK-176).
        ///   3) AutoBindAllMissingReferences - 기존 핫픽스 재바인딩.
        /// 치어리더 시너지 표시(TASK-175)는 기존 텍스트(TeamSynergyUIController.cheerleaderText, CheerleaderSlotUI.gradeText)에
        /// 런타임 문구만 바뀐 것이라 별도 씬 조립이 필요 없다. 실행 후 반드시 씬을 저장(Ctrl+S)해야 디스크에 반영된다.
        /// </summary>
        /// <remarks>
        /// [TASK-KBO-177] 메뉴 이름을 TASK-168~177로 갱신하고 9:16 모바일 수정 4건을 추가했다. 실행 순서:
        ///   0) 화면 자체가 없을 때만 기본 조립(Scout Hub / 치어리더 관리) - 이미 있으면 다시 돌리지 않는다(구 Setup은 자식을
        ///      지우고 재조립하거나 구 행을 다시 만들어 TASK-177 레이아웃과 겹치기 때문).
        ///   1) 카드 템플릿(4단 레이어·가독성·SD) 2) 로스터(9:16 고정 28슬롯·빈 슬롯 배치·자동 편성·교체 팝업·버프 패널)
        ///   3) 로비 재화(볼/유니폼/티켓 텍스트 생성 + LeagueDashboardUIController 바인딩)
        ///   4) SetupMobileUI177 - 치어리더 관리 / 치어리더 영입 / 선수 영입(재화 바) 9:16 레이아웃 5) 핫픽스 재바인딩.
        /// 개별 구 메뉴(Auto-Connect Scout Hub 등)를 나중에 따로 실행했다면 이 메뉴를 한 번 더 실행하면 된다.
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-178] 메뉴 이름을 TASK-168~178로 갱신. SetupMobileUI177을 SetImage 레퍼런스 기반 SetupThemeUI178로 대체했다
        /// (로비 재화 바도 178이 직접 조립하므로 SetupLobbyCurrencyUI 호출은 뺐다). 핫픽스 재바인딩은 테마 적용 "전"에 돌려
        /// 이름 기반 재탐색이 새 로비 바인딩을 덮어쓰지 않게 했다(보관함 하위도 제외).
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-179] 메뉴 이름을 TASK-168~179로 갱신. 마지막 단계로 SetupCompyaMatchUI179.ApplyAll()을 실행한다 - Broadcast179
        /// 텍스처 임포트 설정, InGamePanel에 컴프야V26 1:1 경기 화면 9종(CompyaMatchView) 조립, 로비 하단 5탭 홈 전용 고정(LobbyOnlyNav).
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-180] 메뉴 이름을 TASK-168~180으로 갱신. 마지막 단계로 SetupTask180.ApplyAll()을 실행한다 - 치어리더 관리 화면
        /// 6인 역할 편성 3x2 그리드(CheerSquadPanel), 메인 홈(로비) 메인 홈.jpg 1:1 레이아웃 + 대구 삼성 라이온즈 파크 배경.
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-181] 메뉴 이름을 TASK-168~181로 갱신. 마지막 단계로 SetupTask181.ApplyAll()을 실행한다 - OnboardingManager/SaveManager
        /// 배선 + UIManager 온보딩(타이틀) 등록, 온보딩 3페이지(타이틀 / 10구단 + 닉네임 / 2024 골든글러브 4종 선물), 로비 5단 네이티브 레이아웃
        /// (TASK-178/180 캡처 배경·거대 로고·더미 버튼 레이아웃 삭제) + 신규 단장 튜토리얼, 라인업 [타자 라인업] / [투수 로스터] 탭 분리.
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-182] 메뉴 이름을 TASK-168~182로 갱신. 마지막 단계로 SetupTask182.ApplyAll()을 실행한다 - 컴프야V26 라인업(3탭 · 다이아몬드 ·
        /// 하단 세트덱 바 / 선수 액션 트레이), 상세 정보 패널 캔버스 최상위 이동, 세트덱 A/B 색, BroadcastUIManager 연결, 골든글러브 스카우트 배너.
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-183] 메뉴 이름을 TASK-168~183으로 갱신. 마지막 단계로 SetupTask183.ApplyAll()을 실행한다 - 선수 관리 허브 4대 성장
        /// ([훈련(특훈)] [한계 돌파] [각성] 개통 타일 + 성장 요약 텍스트 + 각성 재료 팝업 바인딩), 상세 정보 성장 요약 줄.
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-184] 메뉴 이름을 TASK-168~184로 갱신. 마지막 단계로 SetupTask184.ApplyAll()을 실행한다 - 하단 [선수 관리] = 성장 센터
        /// (GrowthCenterView: 강화/각성/한계 돌파/훈련·특훈 4탭 + 대상 변경 + 재료 선택), 라인업 [보관 선수] 탭 보유 리스트 필터/정렬 바.
        /// 배치 실행(RunBatchApplyLatestUI)은 끝에 SampleScene을 저장해 에디터를 열면 SampleScene이 바로 보이게 한다(SampleSceneGuard).
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-185] 메뉴 이름을 TASK-168~185로 갱신. 마지막 단계로 SetupTask185.ApplyAll()을 실행한다 - 라인업 직속 레거시 하단 바 고아
        /// (흰 게이지 막대 · 리스너 없는 [세트덱 버프 선택] · 미바인딩 [자동 교체]) 정리, 로비 [세트덱 &amp; 버프 선택] 타일 → 버프 팝업 직행,
        /// 스카우트 허브 3탭 + [특별 영입(골글·시그니처)] 섹션.
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-186] 메뉴 이름을 TASK-168~186으로 갱신. 마지막 단계로 SetupTask186.ApplyAll()을 실행한다 - 선수 상세정보 4단 카드 레이아웃
        /// (Detail186), 로비 · 경기 중계 · 라인업 · 스카우트 · 선수 관리 화면 가독성 패스(ReadableFontPass).
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-187] 메뉴 이름을 TASK-168~187로 갱신. 마지막 단계로 SetupTask187.ApplyAll()을 실행한다 - 치어리더 [응원단 성장](강화 · ★각성 · 도감).
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-188] 메뉴 이름을 TASK-168~188로 갱신. 마지막 단계로 SetupTask188.ApplyAll()을 실행한다 - 초상화 색인(같은 선수 다른 등급 폴백).
        /// </remarks>
        /// <remarks>
        /// [TASK-KBO-189] 메뉴 이름을 TASK-168~189로 갱신. 마지막 단계로 SetupTask189.ApplyAll()을 실행한다 - 스카우트 허브 [상점 · 교환소] 섹션 + 4번째 탭.
        /// </remarks>
        /// <remarks>
        /// [TASK-GM-01] 마지막 단계로 SetupTaskGM01.ApplyAll()을 실행한다 - 『스토브리그: 단장의 시간』 전환(로비 타일 · 진단 화면 · 치어리더 동선).
        /// </remarks>
        [MenuItem("KBO Manager/Setup/Apply Latest UI (TASK-168~193 + GM-01~06)")]
        public static void ApplyLatestUI()
        {
            if (UnityEngine.Object.FindAnyObjectByType<ScoutUIController>(FindObjectsInactive.Include) == null ||
                UnityEngine.Object.FindAnyObjectByType<CheerleaderShopUIController>(FindObjectsInactive.Include) == null)
            {
                SetupScoutHubUI.AutoConnectScoutHub();
            }
            if (UnityEngine.Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include) == null)
            {
                SetupCheerleaderUI.AutoCreateInventoryUI();
            }

            SetupTemplates.SetupDualPortraitAndGradeFrameLayers();
            SetupRosterUI.AutoConnectRosterUI();
            AutoBindAllMissingReferences();
            SetupThemeUI178.ApplyAll();
            SetupCompyaMatchUI179.ApplyAll();
            SetupTask180.ApplyAll();
            SetupTask181.ApplyAll();
            SetupTask182.ApplyAll();
            SetupTask183.ApplyAll();
            SetupTask184.ApplyAll();
            SetupTask185.ApplyAll();
            SetupTask186.ApplyAll();
            SetupTask187.ApplyAll();
            SetupTask188.ApplyAll();
            SetupTask189.ApplyAll();
            SetupTask190.ApplyAll(); // [TASK-KBO-190] SeasonRewardManager 배선 + 시즌 완주 오버레이
            SetupTask191.ApplyAll(); // [TASK-KBO-191] 전 화면 텍스트 정리(Bold 해제 · 계층 크기 · 자간) - 다른 Setup이 만든 글씨까지 덮도록 마지막
            SetupTask192.ApplyAll(); // [TASK-KBO-192] 일반 UI 글씨 복원(16~30pt · Best Fit 최소 15) · TASK-191 크기 1회 이전 집계
            SetupTask193.ApplyAll(); // [TASK-KBO-193] 스카우트 탭 명도 대비 · 섹션 텍스트 상한 · 스토브리그 · 리그 기록실
            SetupTaskGM01.ApplyAll(); // [TASK-GM-01] 단장 모드 전환 - 로비 타일 · 계약·연봉·팀워크 진단 · 치어리더 관리 동선 점검
            SetupTaskGM02.ApplyAll(); // [TASK-GM-02] 리그 플레이 실시간 144경기 대시보드
            SetupTaskGM03.ApplyAll(); // [TASK-GM-03] 한 경기 전력 비교 · 박스스코어 · WPA 화면
            SetupTaskGM04.ApplyAll(); // [TASK-GM-04] 시상식 & 포스트시즌 화면
            SetupTaskGM05.ApplyAll(); // [TASK-GM-05] 구단 치어리더 15인 풀 · 4~6인 엔트리 화면
            SetupTaskGM06.ApplyAll(); // [TASK-GM-06] 1920×1080 Landscape 전환 · OOTP 27 프런트 오피스 허브 · 기존 세로 화면 9:16 프레임
            SetupTaskGM07.ApplyAll(); // [TASK-GM-07] OOTP 레이아웃 6종(툴바 · 사이드바 · 감독 설정 · 일정 · 포스트시즌 트리) · 실시간 이닝 경기 화면 점검
            SetupTaskGM08.ApplyAll(); // [TASK-GM-08] 삼성 오디오 임포트 · GMAudioManager 배치 · FA 보상·보호명단 · AI 역제안 · 포스트시즌 KBO 리더 점검
            SetupTaskGM09.ApplyAll(); // [TASK-GM-09] 투타 밸런스 · FA 순환 · 글로벌 대회 일정 점검(씬 변경 없음)
            Debug.Log("[SetupMasterBinding] TASK-168~193 + TASK-GM-01~09 최신 UI 적용 완료 - 씬을 저장(Ctrl+S)하십시오.");
        }

        /// <summary>[TASK-KBO-176] 배치 실행용(`Unity.exe -batchmode -projectPath . -executeMethod
        /// KBOManager.EditorTools.SetupMasterBinding.RunBatchApplyLatestUI -quit`) - 씬을 열어 ApplyLatestUI() 후 저장한다.</summary>
        public static void RunBatchApplyLatestUI()
        {
            var scene = EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            ApplyLatestUI();
            bool saved = EditorSceneManager.SaveScene(scene);
            // [TASK-KBO-184] 마지막 단계 - SampleScene을 열린 상태로 저장해 둔다(에디터 재실행 시 Untitled 대신 SampleScene).
            saved &= SampleSceneGuard.EnsureOpen(save: true);
            Debug.Log(saved
                ? "[SetupMasterBinding] RunBatchApplyLatestUI 완료 - 씬 저장 성공."
                : "[SetupMasterBinding] RunBatchApplyLatestUI 완료했으나 씬 저장 실패.");
        }

        [MenuItem("KBO Manager/Setup/Auto-Bind All Missing References (HOTFIX)")]
        public static void AutoBindAllMissingReferences()
        {
            BindScoutUIController();
            BindLeagueDashboardUIController();
            BindInGameUIController();

            Debug.Log("[SetupMasterBinding] 전역 UI 컨트롤러 마스터 바인딩 완료.");
        }

        /// <summary>
        /// ScoutUIController.scoutManager 바인딩. 원문(ScoutUIController.cs 18행) 확인 결과 이 필드는
        /// SetupScoutUI.cs가 애초에 한 번도 바인딩한 적이 없었다(스카우트 UI 조립 당시 범위에 없었음) -
        /// "저장 누락"이 아니라 최초부터 누락된 배선이었다.
        /// </summary>
        private static void BindScoutUIController()
        {
            try
            {
                var controller = UnityEngine.Object.FindAnyObjectByType<ScoutUIController>(FindObjectsInactive.Include);
                if (controller == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 ScoutUIController를 찾지 못해 건너뜁니다.");
                    return;
                }

                var scoutManager = UnityEngine.Object.FindAnyObjectByType<ScoutManager>(FindObjectsInactive.Include);
                if (scoutManager == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 ScoutManager를 찾지 못해 ScoutUIController.scoutManager 바인딩을 건너뜁니다.");
                    return;
                }

                var serialized = new SerializedObject(controller);
                serialized.FindProperty("scoutManager").objectReferenceValue = scoutManager;
                serialized.ApplyModifiedProperties();

                MarkDirty(controller);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupMasterBinding] ScoutUIController 바인딩 중 예외 발생: {e}");
            }
        }

        /// <summary>
        /// LeagueDashboardUIController의 leagueManager + 4개 텍스트(seasonProgressText/nextMatchupText/
        /// teamOVRText/standingsRowTexts) 필드를 씬에 이미 존재하는 오브젝트에서 이름 기준으로 다시
        /// 찾아 강제 재바인딩한다(SetupLeagueUI.cs와 달리 새 오브젝트를 생성하지 않는 순수 재탐색).
        /// </summary>
        private static bool IsUnderLegacyHolder(Transform node)
        {
            for (var t = node; t != null; t = t.parent)
            {
                if (t.name.StartsWith("_Legacy", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void BindLeagueDashboardUIController()
        {
            try
            {
                var dashboard = UnityEngine.Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
                if (dashboard == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 LeagueDashboardUIController를 찾지 못해 건너뜁니다.");
                    return;
                }

                var serialized = new SerializedObject(dashboard);

                var leagueManager = UnityEngine.Object.FindAnyObjectByType<LeagueManager>(FindObjectsInactive.Include);
                if (leagueManager != null)
                {
                    serialized.FindProperty("leagueManager").objectReferenceValue = leagueManager;
                }
                else
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 LeagueManager를 찾지 못해 leagueManager 바인딩을 건너뜁니다.");
                }

                // [TASK-KBO-178] TASK-177/178 Setup이 구 오브젝트를 비활성 "_Legacy177/178" 보관함으로 옮기므로, 같은 이름의 구
                // 텍스트로 되돌려 바인딩하지 않도록 보관함 하위는 제외한다.
                var allTexts = dashboard.GetComponentsInChildren<Text>(true)
                    .Where(t => !IsUnderLegacyHolder(t.transform)).ToArray();

                BindNamedText(serialized, "seasonProgressText", allTexts, "SeasonProgressText");
                BindNamedText(serialized, "nextMatchupText", allTexts, "NextMatchupText");
                BindNamedText(serialized, "teamOVRText", allTexts, "TeamOVRText");

                var standingsRows = allTexts
                    .Where(t => t.name.StartsWith("StandingsRow", StringComparison.Ordinal))
                    .OrderBy(t => ExtractTrailingNumber(t.name))
                    .ToArray();

                if (standingsRows.Length > 0)
                {
                    var standingsProperty = serialized.FindProperty("standingsRowTexts");
                    standingsProperty.arraySize = standingsRows.Length;
                    for (int i = 0; i < standingsRows.Length; i++)
                    {
                        standingsProperty.GetArrayElementAtIndex(i).objectReferenceValue = standingsRows[i];
                    }
                }
                else
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 'StandingsRow##' 이름의 Text를 하나도 찾지 못해 standingsRowTexts 바인딩을 건너뜁니다.");
                }

                serialized.ApplyModifiedProperties();
                MarkDirty(dashboard);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupMasterBinding] LeagueDashboardUIController 바인딩 중 예외 발생: {e}");
            }
        }

        /// <summary>
        /// InGameUIController.cs 원문(28~62행) 확인 결과 이 컨트롤러에는 LeagueManager 참조 필드가
        /// 존재하지 않는다(경기 시뮬레이션 로직을 전혀 갖지 않는 순수 UI 브릿지 - 클래스 주석 11~13행
        /// 참고). 대신 이 컨트롤러가 실제로 갖는 매니저성 참조 2개(playBallController/
        /// matchRewardManager)만, 비어 있을 때만 재탐색해 채운다(이미 채워진 값은 덮어쓰지 않는다 -
        /// 씬에 동일 타입 오브젝트가 여러 개일 경우 의도치 않게 다른 참조로 바뀌는 것을 방지).
        /// </summary>
        private static void BindInGameUIController()
        {
            try
            {
                var controller = UnityEngine.Object.FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
                if (controller == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 InGameUIController를 찾지 못해 건너뜁니다.");
                    return;
                }

                var serialized = new SerializedObject(controller);

                var playBallProperty = serialized.FindProperty("playBallController");
                if (playBallProperty.objectReferenceValue == null)
                {
                    var playBallController = UnityEngine.Object.FindAnyObjectByType<PlayBallController>(FindObjectsInactive.Include);
                    if (playBallController != null)
                    {
                        playBallProperty.objectReferenceValue = playBallController;
                    }
                    else
                    {
                        Debug.LogWarning("[SetupMasterBinding] 씬에서 PlayBallController를 찾지 못해 InGameUIController.playBallController 바인딩을 건너뜁니다.");
                    }
                }

                var matchRewardProperty = serialized.FindProperty("matchRewardManager");
                if (matchRewardProperty.objectReferenceValue == null)
                {
                    var matchRewardManager = UnityEngine.Object.FindAnyObjectByType<MatchRewardManager>(FindObjectsInactive.Include);
                    if (matchRewardManager != null)
                    {
                        matchRewardProperty.objectReferenceValue = matchRewardManager;
                    }
                    else
                    {
                        Debug.LogWarning("[SetupMasterBinding] 씬에서 MatchRewardManager를 찾지 못해 InGameUIController.matchRewardManager 바인딩을 건너뜁니다.");
                    }
                }

                serialized.ApplyModifiedProperties();
                MarkDirty(controller);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupMasterBinding] InGameUIController 바인딩 중 예외 발생: {e}");
            }
        }

        private static void BindNamedText(SerializedObject serialized, string propertyName, Text[] pool, string objectName)
        {
            var match = pool.FirstOrDefault(t => t.name == objectName);
            if (match != null)
            {
                serialized.FindProperty(propertyName).objectReferenceValue = match;
            }
            else
            {
                Debug.LogWarning($"[SetupMasterBinding] '{objectName}' 이름의 Text를 찾지 못해 {propertyName} 바인딩을 건너뜁니다.");
            }
        }

        private static int ExtractTrailingNumber(string name)
        {
            var digits = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var value) ? value : int.MaxValue;
        }

        private static void MarkDirty(Component component)
        {
            EditorUtility.SetDirty(component);

            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
