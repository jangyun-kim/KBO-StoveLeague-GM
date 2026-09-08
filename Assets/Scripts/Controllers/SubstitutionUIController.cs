using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 하이라이트 개입("[개입]" 선택) 시 나타나는 교체 팝업을 관리한다. InterventionController가
    /// OnPinchHitterOpportunity/OnRelieverOpportunity로 넘겨주는 데이터를 화면에 그리고, 유저가 후보를
    /// 클릭하면 확인 절차를 거쳐 InterventionController.Confirm*Substitution을 호출한 뒤 팝업을 닫는다.
    /// 시뮬레이션/검증 로직은 전혀 갖지 않는다 - 여기서 하는 일은 오직 "후보를 보여주고 선택을 되돌려준다"뿐이다.
    /// </summary>
    public class SubstitutionUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InterventionController interventionController;

        [Header("Popup Root")]
        [Tooltip("교체 창 전체 루트. 평소엔 비활성 상태로 둔다.")]
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text currentPlayerText;

        [Header("Candidate List")]
        [Tooltip("후보 버튼들이 생성될 부모(보통 Scroll View의 Content).")]
        [SerializeField] private Transform listContainer;
        [Tooltip("후보 1명 = 버튼 1개. 자식 어딘가에 Text 컴포넌트가 있어야 이름/OVR이 표시된다.")]
        [SerializeField] private Button playerEntryPrefab;

        [Header("Confirm Dialog (선택 사항 - 비워두면 클릭 즉시 확정)")]
        [SerializeField] private GameObject confirmDialogRoot;
        [SerializeField] private Text confirmMessageText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        private readonly List<GameObject> spawnedEntries = new List<GameObject>();
        private bool isBatterMode;
        private Player selectedCandidate;

        private void Awake()
        {
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(HandleConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(HandleConfirmNo);

            ClosePopup();
        }

        private void OnEnable()
        {
            if (interventionController == null) return;
            interventionController.OnPinchHitterOpportunity += ShowPinchHitterList;
            interventionController.OnRelieverOpportunity += ShowRelieverList;
        }

        private void OnDisable()
        {
            if (interventionController == null) return;
            interventionController.OnPinchHitterOpportunity -= ShowPinchHitterList;
            interventionController.OnRelieverOpportunity -= ShowRelieverList;
        }

        /// <summary>대타 후보 목록 팝업을 연다. InterventionController.OnPinchHitterOpportunity에 그대로 연결된다.</summary>
        public void ShowPinchHitterList(Player currentBatter, List<Player> availableBench)
        {
            isBatterMode = true;
            if (titleText != null) titleText.text = "대타 기용";
            OpenPopupWithList(currentBatter, availableBench, currentLabel: "현재 타자");
        }

        /// <summary>구원 투수 후보 목록 팝업을 연다. InterventionController.OnRelieverOpportunity에 그대로 연결된다.</summary>
        public void ShowRelieverList(Player currentPitcher, List<Player> availableBullpen)
        {
            isBatterMode = false;
            if (titleText != null) titleText.text = "투수 교체";
            OpenPopupWithList(currentPitcher, availableBullpen, currentLabel: "현재 투수");
        }

        private void OpenPopupWithList(Player current, List<Player> candidates, string currentLabel)
        {
            ClearEntries();

            if (currentPlayerText != null)
            {
                currentPlayerText.text = $"{currentLabel}: {current?.Template?.PlayerName ?? "-"}";
            }

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    SpawnEntry(candidate);
                }
            }

            if (popupRoot != null) popupRoot.SetActive(true);
        }

        private void SpawnEntry(Player candidate)
        {
            if (playerEntryPrefab == null || listContainer == null) return;

            var button = Instantiate(playerEntryPrefab, listContainer);
            button.gameObject.SetActive(true);

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = candidate?.Template != null
                    ? $"{candidate.Template.PlayerName} (OVR {candidate.CalculateOVR(false)})"
                    : "알 수 없음";
            }

            button.onClick.AddListener(() => HandleCandidateClicked(candidate));
            spawnedEntries.Add(button.gameObject);
        }

        private void HandleCandidateClicked(Player candidate)
        {
            selectedCandidate = candidate;

            if (confirmDialogRoot != null && confirmMessageText != null)
            {
                string action = isBatterMode ? "대타로 기용" : "투수로 교체";
                confirmMessageText.text = $"{candidate?.Template?.PlayerName}(을)를 {action}하시겠습니까?";
                confirmDialogRoot.SetActive(true);
            }
            else
            {
                // 확인창 UI를 연결하지 않았으면 클릭 즉시 확정한다.
                ConfirmSelection();
            }
        }

        private void HandleConfirmYes()
        {
            if (confirmDialogRoot != null) confirmDialogRoot.SetActive(false);
            ConfirmSelection();
        }

        private void HandleConfirmNo()
        {
            if (confirmDialogRoot != null) confirmDialogRoot.SetActive(false);
            selectedCandidate = null;
        }

        private void ConfirmSelection()
        {
            if (selectedCandidate == null || interventionController == null)
            {
                ClosePopup();
                return;
            }

            if (isBatterMode)
            {
                interventionController.ConfirmBatterSubstitution(selectedCandidate);
            }
            else
            {
                interventionController.ConfirmPitcherSubstitution(selectedCandidate);
            }

            selectedCandidate = null;
            ClosePopup();
        }

        /// <summary>교체 없이 팝업을 닫고 개입 자체를 취소(스킵)한다. "취소/그냥 진행" 버튼에 연결한다.</summary>
        public void CancelAndSkip()
        {
            ClosePopup();
            interventionController?.SkipIntervention();
        }

        private void ClosePopup()
        {
            ClearEntries();
            if (popupRoot != null) popupRoot.SetActive(false);
            if (confirmDialogRoot != null) confirmDialogRoot.SetActive(false);
        }

        private void ClearEntries()
        {
            foreach (var entry in spawnedEntries)
            {
                if (entry != null) Destroy(entry);
            }
            spawnedEntries.Clear();
        }
    }
}
