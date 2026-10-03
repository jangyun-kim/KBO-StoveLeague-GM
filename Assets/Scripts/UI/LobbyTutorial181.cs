using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-181] 신규 단장 튜토리얼(3단계, 건너뛰기 가능). 온보딩 직후(GameManager.TutorialCompleted == false) 첫 로비 진입 시 뜬다.
    ///   ① 하단 [라인업] 탭 강조 - 지급받은 2026 LIVE 선수단과 '24 골든글러브 정착 지원 선수 확인/배치(선물 편성 결과 안내).
    ///      [라인업 확인하기]를 누르면 라인업 화면으로 이동하고, 로비로 돌아오면 ②부터 이어진다.
    ///   ② 헤더 세트덱 스코어 강조 - 같은 구단(또는 골든글러브) 카드 27인 합산 스코어와 구간 버프 구조.
    ///   ③ [플레이 볼] 강조 - [플레이 볼 ⚾]을 누르면 튜토리얼을 마치고 첫 경기(경기 유형 선택)로 바로 들어간다.
    /// 오버레이는 반투명 막 + 강조 테두리(대상 RectTransform 화면 좌표를 매 프레임 추적) + 설명 카드로 구성된다.
    /// </summary>
    public class LobbyTutorial181 : MonoBehaviour
    {
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private RectTransform highlightFrame;
        [SerializeField] private RectTransform messagePanel;
        [SerializeField] private Text stepText;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button skipButton;

        [Header("강조 대상(① 라인업 탭 / ② 세트덱 스코어 / ③ 플레이 볼)")]
        [SerializeField] private RectTransform[] targets = new RectTransform[3];
        [SerializeField] private Button playBallButton;

        private static int resumeStep; // 라인업 화면을 다녀와도 이어서 진행(세션 내)
        private int step = -1;

        private void Awake()
        {
            if (nextButton != null) nextButton.onClick.AddListener(Next);
            if (skipButton != null) skipButton.onClick.AddListener(Complete);
        }

        private void OnEnable()
        {
            var gm = GameManager.Instance;
            bool pending = gm != null && !gm.IsFirstLogin && !gm.TutorialCompleted;
            if (pending) ShowStep(Mathf.Clamp(resumeStep, 0, 2));
            else Hide();
        }

        private void ShowStep(int index)
        {
            step = index;
            resumeStep = index;
            if (overlayRoot != null) overlayRoot.SetActive(true);

            var gm = GameManager.Instance;
            string team = gm != null ? CompyaUiKit.FullName(gm.FavoriteTeam) : "";
            if (stepText != null) stepText.text = $"신규 단장 가이드  {index + 1} / 3";

            switch (index)
            {
                case 0:
                    int live = gm != null ? gm.Inventory.Count(p => p?.Template != null && p.Template.Grade == Grade.LIVE_NORMAL) : 0;
                    string note = OnboardingManager.Instance != null ? OnboardingManager.Instance.LastGiftPlacementNote : "";
                    SetMessage("① 라인업 확인",
                        $"하단 <b>[라인업]</b> 탭에서 지급받은 <b>{team} 2026 LIVE 선수단 {live}명</b>과 '24 골든글러브 정착 지원 선수를 확인하고 배치해 보세요." +
                        (string.IsNullOrEmpty(note) ? "" : $"\n<color=#FFD54A>{note}</color>"),
                        "라인업 확인하기");
                    break;
                case 1:
                    var setDeck = gm != null
                        ? GameManager.EvaluateSetDeck(gm.Roster.ToList(), gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null, gm.SetDeckSelection)
                        : null;
                    SetMessage("② 세트덱 스코어",
                        $"라인업 27인(주전 9 · 후보 6 · 투수)의 <b>내 구단 카드</b>(골든글러브는 구단 무관) 스코어를 합산합니다. " +
                        $"현재 <color=#5FE3FF><b>{setDeck?.Score ?? 0}P</b></color> - 30P부터 구간(27단계)마다 버프가 누적되고" +
                        $"{SetDeckBuffTable.MinimumGoalScore}P(1차 목표) · {SetDeckBuffTable.FinalGoalScore}P(최종 목표)를 노리십시오. " +
                        "[라인업] 하단 [세트덱 버프 선택]에서 선택형 구간 옵션을 고를 수 있습니다.",
                        "다음");
                    break;
                default:
                    SetMessage("③ 첫 경기 시작",
                        "준비가 끝났습니다! <b>[플레이 볼 ⚾]</b>을 눌러 첫 경기를 바로 시작하십시오. 경기 유형(빠른 진행 / 하이라이트 / 풀 플레이)을 고를 수 있습니다.",
                        "플레이 볼 ⚾");
                    break;
            }
            LateUpdate();
        }

        private void SetMessage(string title, string body, string nextLabel)
        {
            if (titleText != null) titleText.text = title;
            if (bodyText != null) bodyText.text = body;
            CompyaUiKit.SetButtonText(nextButton, nextLabel);
        }

        private void Next()
        {
            switch (step)
            {
                case 0:
                    resumeStep = 1;
                    Hide();
                    UIManager.Instance?.ShowScreen(ScreenType.Roster);
                    break;
                case 1:
                    ShowStep(2);
                    break;
                default:
                    Complete();
                    if (playBallButton != null) playBallButton.onClick.Invoke();
                    break;
            }
        }

        private void Complete()
        {
            if (GameManager.Instance != null) GameManager.Instance.TutorialCompleted = true;
            resumeStep = 0;
            Hide();
            SaveManager.Instance?.TrySaveCareer();
        }

        private void Hide()
        {
            step = -1;
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        /// <summary>강조 테두리를 현재 단계 대상의 화면 영역에 맞추고, 설명 카드를 대상 반대편(위/아래)에 둔다.</summary>
        private void LateUpdate()
        {
            if (step < 0 || highlightFrame == null || overlayRoot == null || !overlayRoot.activeSelf) return;
            var target = targets != null && step < targets.Length ? targets[step] : null;
            highlightFrame.gameObject.SetActive(target != null);
            if (target == null) return;

            var parent = (RectTransform)highlightFrame.parent;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = parent.InverseTransformPoint(corners[0]);
            Vector2 max = parent.InverseTransformPoint(corners[2]);
            highlightFrame.anchorMin = highlightFrame.anchorMax = new Vector2(0.5f, 0.5f);
            highlightFrame.pivot = new Vector2(0.5f, 0.5f);
            highlightFrame.anchoredPosition = (min + max) * 0.5f - parent.rect.center;
            highlightFrame.sizeDelta = (max - min) + new Vector2(24f, 24f);

            if (messagePanel != null)
            {
                bool targetInLowerHalf = (min.y + max.y) * 0.5f < parent.rect.center.y;
                messagePanel.anchorMin = new Vector2(0.04f, targetInLowerHalf ? 0.56f : 0.12f);
                messagePanel.anchorMax = new Vector2(0.96f, targetInLowerHalf ? 0.84f : 0.4f);
            }
        }
    }
}
