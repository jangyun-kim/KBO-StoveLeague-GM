using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// 카드 한 장의 시각 요소를 담당하는 순수 표시 컴포넌트. Setup(Player)을 호출하면 이름/구단/포지션/
    /// 최종 OVR/등급 색상/성급 별을 UGUI 요소에 매핑한다. 시뮬레이션·저장 로직은 전혀 갖지 않는다.
    ///
    /// [TASK-KBO-147] 실제 선수 초상화 연동 - `Resources.Load&lt;Sprite&gt;()`로 카드별 초상화를
    /// 동적으로 불러온다. 리소스가 없으면(카드 신설 초기 상태, 지금 세션 시점) `fallbackPortraitSprite`로
    /// 안전하게 폴백한다 - 풀링된 카드가 이전 선수의 초상화를 그대로 보여주는 사고를 막기 위해 Setup()이
    /// 매번 명시적으로 두 경우(발견/미발견) 모두 스프라이트를 다시 대입한다(캐시해 두지 않고 항상
    /// 재계산 - Resources.Load 자체가 내부적으로 캐싱하므로 매 호출 비용은 낮다).
    ///
    /// [TASK-KBO-152, 로딩 키 변경 + 사실 정정] 명령서는 `Player.CatalogId`(개별 카드 고유 ID)를 키로
    /// 쓰라고 지시했으나, `Player`/`PlayerTemplate` 어느 쪽에도 그런 필드가 없다(`CatalogId`는
    /// `Cheerleader`/`CheerleaderTemplate` 전용 필드로, 선수 카드와는 무관하다 - `CheerleaderGachaService.cs`
    /// 참고) - 명령서가 의도를 정확히 설명한 것은 맞다: 명령서 3항이 지적한 대로 `TemplateId` 하나만
    /// 쓰면 "24년 라이브 구자욱"과 "24년 골든글러브 구자욱"이 같은 사진을 쓰게 된다. 다만 그 원인은
    /// `PlayerDatabase`가 아직 cards.csv를 조인하지 않아 등급별로 별도 `PlayerTemplate`을 만들지 않기
    /// 때문이고(DCL-057에서 이미 확인된, 이번 작업 범위 밖의 데이터 모델 갭 - "데이터 연동 로직은 1mm도
    /// 건드리지 말 것"이라는 명령서 CRITICAL 경고에 따라 `PlayerDatabase.cs`/`ScoutManager.cs`는 손대지
    /// 않았다), `Player`에 새 `CatalogId` 필드를 만드는 것도 데이터 모델 변경이라 이번 파일(순수 UI
    /// 컴포넌트) 범위를 벗어난다. 대신 오늘 시점에도 실제로 카드 인스턴스마다 정확히 채워지는 값인
    /// `Player.CurrentStarType`(뽑기 시 등급에 따라 `ScoutManager.ApplyInitialGradeRule()`이 이미
    /// 개별 대입해 둔 값 - SEASON=NORMAL, GOLDEN_GLOVE=GOLD, SIGNATURE=PLATINUM 등)을 `TemplateId`와
    /// 조합해 키를 만든다 - `Resources.Load&lt;Sprite&gt;($"{PortraitResourceFolder}/
    /// {TemplateId}_{CurrentStarType}")`. 폴더 규칙: `Assets/Resources/Portraits/{TemplateId}_
    /// {CurrentStarType}.png`(예: `PLY_0001_PLATINUM.png`). 이렇게 하면 같은 선수라도 뽑은 등급이
    /// 다르면(=`CurrentStarType`이 다르면) 오늘 당장 다른 사진 경로로 로드된다 - `PlayerDatabase`가
    /// cards.csv를 조인해 등급별 `PlayerTemplate`을 실제로 분리하는 후속 작업이 끝나면(현재는 모든
    /// 카드가 `TemplateId`만으로 동일 선수를 가리킴) 그 시점부터는 `TemplateId` 자체도 등급별로 달라져
    /// 이 조합 키가 한층 더 정밀해진다 - 코드 수정이 필요 없는 전방 호환 설계다.
    /// </summary>
    public class PlayerCardUI : MonoBehaviour
    {
        /// <summary>[TASK-KBO-147] 초상화 리소스 폴더 규칙. PM이 이 폴더에
        /// "{TemplateId}.png"(예: "PLY_0001.png")만 넣으면 별도 코드 수정 없이 즉시 연동된다.</summary>
        public const string PortraitResourceFolder = "Portraits";

        [Header("Text")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text teamText;
        [SerializeField] private Text positionText;
        [SerializeField] private Text ovrText;

        [Header("Portrait (TASK-KBO-147 - Resources/Portraits/{TemplateId} 동적 로딩)")]
        [Tooltip("실제 선수 초상화를 표시할 Image. 비워두면 초상화 기능 자체를 생략한다(기존 카드 프리팹 " +
                 "호환 - 필드가 없어도 크래시하지 않음).")]
        [SerializeField] private Image portraitImage;
        [Tooltip("Resources/Portraits/{TemplateId}에 이미지가 없을 때 대신 표시할 기본 실루엣. " +
                 "비워두면 스프라이트가 비워진 채로 표시된다(투명/흰 박스 - 크래시는 아니지만 시각적으로 " +
                 "어색하므로 실제 사용 시 반드시 채워 넣을 것을 권장).")]
        [SerializeField] private Sprite fallbackPortraitSprite;

        [Header("Grade Visual")]
        [Tooltip("카드 배경 또는 테두리 이미지. 등급(StarType)에 따라 색이 바뀐다.")]
        [SerializeField] private Image frameImage;
        [Tooltip("1~6성을 표시할 별 아이콘 6개. 인덱스 0=1성 ... 5=6성.")]
        [SerializeField] private Image[] starIcons;
        [SerializeField] private Color inactiveStarColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        [Header("Selection Feedback (재료 선택 UI 등에서 사용)")]
        [Tooltip("선택 시 카드를 어둡게 덮는 Dim 오버레이. 평소 비활성 상태로 둔다.")]
        [SerializeField] private GameObject selectedOverlay;
        [Tooltip("선택 시 표시할 체크마크 아이콘. 평소 비활성 상태로 둔다.")]
        [SerializeField] private GameObject checkmarkIcon;

        [Header("Stamina Bar (카드 하단, Image.Type=Filled) - 투수 카드에서만 표시된다")]
        [SerializeField] private GameObject staminaBarRoot;
        [SerializeField] private Image staminaFillImage;
        [SerializeField] private Color staminaHighColor = new Color(0.3f, 0.85f, 0.3f); // 60% 이상 - 초록
        [SerializeField] private Color staminaMidColor = new Color(1f, 0.85f, 0.2f);    // 30~60% - 노랑
        [SerializeField] private Color staminaLowColor = new Color(0.9f, 0.25f, 0.25f); // 30% 미만 - 빨강

        [Header("Condition Icon (우측 상단, 타자/투수 공통)")]
        [Tooltip("색상으로 5단계를 구분한다(비워두면 생략).")]
        [SerializeField] private Image conditionIconImage;
        [Tooltip("↑(최상)/↗(호조)/↔(보통)/↘(저조)/↓(열악) 화살표 문자로 표시한다(비워두면 생략).")]
        [SerializeField] private Text conditionArrowText;
        [SerializeField] private Color conditionPoorColor = new Color(0.9f, 0.25f, 0.25f);
        [SerializeField] private Color conditionBelowAverageColor = new Color(1f, 0.6f, 0.2f);
        [SerializeField] private Color conditionNormalColor = new Color(0.75f, 0.75f, 0.75f);
        [SerializeField] private Color conditionGoodColor = new Color(0.4f, 0.75f, 1f);
        [SerializeField] private Color conditionExcellentColor = new Color(1f, 0.84f, 0f);

        public Player BoundPlayer { get; private set; }
        public bool IsSelected { get; private set; }

        /// <summary>카드에 표시할 선수를 지정한다. player나 Template이 없으면 카드를 비워 표시한다.
        /// 선택 표시는 항상 false로 초기화되므로, 필요하면 Setup() 이후에 SetSelected()를 다시 호출한다.</summary>
        public void Setup(Player player)
        {
            BoundPlayer = player;
            SetSelected(false);

            if (player?.Template == null)
            {
                Clear();
                return;
            }

            if (nameText != null) nameText.text = player.Template.PlayerName;
            if (teamText != null) teamText.text = player.Template.Team.ToString();
            if (positionText != null) positionText.text = DescribePosition(player.Template);
            if (ovrText != null) ovrText.text = player.CalculateOVR(false).ToString();

            var gradeColor = GetStarTypeColor(player.CurrentStarType, player.Template.Team);
            if (frameImage != null) frameImage.color = gradeColor;

            SetupStars(player.StarLevel, gradeColor);
            SetupStamina(player);
            SetupCondition(player.CurrentCondition);
            SetupPortrait(player);
        }

        /// <summary>[TASK-KBO-147, TASK-KBO-152 키 변경] `Resources/Portraits/{TemplateId}_
        /// {CurrentStarType}`에서 초상화를 동적으로 불러와 `portraitImage`에 대입한다 - 클래스 요약의
        /// "로딩 키 변경" 문단 참고(같은 선수라도 뽑힌 등급별로 다른 사진을 쓸 수 있도록 `CurrentStarType`을
        /// 조합했다). 없으면 `fallbackPortraitSprite`로 폴백한다 - 풀링 재사용 시 이전 선수의 초상화가
        /// 남아 있는 사고를 막기 위해 매번 두 경우 모두 명시적으로 대입한다.</summary>
        private void SetupPortrait(Player player)
        {
            if (portraitImage == null) return;

            string templateId = player.Template.TemplateId;
            Sprite portrait = !string.IsNullOrEmpty(templateId)
                ? Resources.Load<Sprite>($"{PortraitResourceFolder}/{templateId}_{player.CurrentStarType}")
                : null;

            portraitImage.sprite = portrait != null ? portrait : fallbackPortraitSprite;
            portraitImage.enabled = portraitImage.sprite != null;
        }

        /// <summary>빈 카드로 되돌린다 (풀링/재사용 시 사용).</summary>
        public void Clear()
        {
            BoundPlayer = null;
            if (nameText != null) nameText.text = "";
            if (teamText != null) teamText.text = "";
            if (positionText != null) positionText.text = "";
            if (ovrText != null) ovrText.text = "";
            if (frameImage != null) frameImage.color = Color.white;
            SetupStars(0, Color.white);
            if (staminaBarRoot != null) staminaBarRoot.SetActive(false);
            SetupCondition(PlayerCondition.Normal);
            SetSelected(false);

            if (portraitImage != null)
            {
                portraitImage.sprite = fallbackPortraitSprite;
                portraitImage.enabled = fallbackPortraitSprite != null;
            }
        }

        /// <summary>투수 카드에서만 체력 게이지를 켠다. fillAmount = CurrentStamina/MaxStamina, 색은
        /// 60% 이상 초록 -> 30~60% 노랑 -> 30% 미만(Player.LowStaminaThresholdPercent) 빨강.</summary>
        private void SetupStamina(Player player)
        {
            bool isPitcherCard = player.Template.IsPitcher && player.MaxStamina > 0;
            if (staminaBarRoot != null) staminaBarRoot.SetActive(isPitcherCard);
            if (!isPitcherCard || staminaFillImage == null) return;

            float ratio = Mathf.Clamp01((float)player.CurrentStamina / player.MaxStamina);
            staminaFillImage.fillAmount = ratio;
            staminaFillImage.color = ratio >= 0.6f
                ? staminaHighColor
                : ratio >= Player.LowStaminaThresholdPercent ? staminaMidColor : staminaLowColor;
        }

        private void SetupCondition(PlayerCondition condition)
        {
            var (color, arrow) = condition switch
            {
                PlayerCondition.Poor => (conditionPoorColor, "↓"),
                PlayerCondition.BelowAverage => (conditionBelowAverageColor, "↘"),
                PlayerCondition.Normal => (conditionNormalColor, "↔"),
                PlayerCondition.Good => (conditionGoodColor, "↗"),
                PlayerCondition.Excellent => (conditionExcellentColor, "↑"),
                _ => (conditionNormalColor, "↔"),
            };

            if (conditionIconImage != null) conditionIconImage.color = color;
            if (conditionArrowText != null) conditionArrowText.text = arrow;
        }

        /// <summary>재료 선택 UI 등에서 이 카드가 선택됐는지를 시각적으로 표시한다(Dim 오버레이 + 체크마크).</summary>
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectedOverlay != null) selectedOverlay.SetActive(selected);
            if (checkmarkIcon != null) checkmarkIcon.SetActive(selected);
        }

        private void SetupStars(int starLevel, Color activeColor)
        {
            if (starIcons == null) return;

            for (int i = 0; i < starIcons.Length; i++)
            {
                if (starIcons[i] == null) continue;
                starIcons[i].color = i < starLevel ? activeColor : inactiveStarColor;
            }
        }

        private static string DescribePosition(PlayerTemplate template)
        {
            return template.IsPitcher ? template.PitcherRole.ToString() : template.BatterPosition.ToString();
        }

        /// <summary>
        /// GDD 2절의 등급별 색상 규칙을 실제 UI 색으로 변환한다. Player.CurrentStarType이 이미
        /// (LIVE_NORMAL/EPIC=NORMAL, ALLSTAR=PURPLE, TITLE_HOLDER=SILVER, GOLDEN_GLOVE=GOLD,
        /// SIGNATURE=PLATINUM, DYNASTY=TEAM_COLOR) 등급→색상 매핑을 담고 있으므로 여기서는 그 값을
        /// 그대로 소비하기만 한다. DYNASTY(왕조)만 구단색(GetTeamColor)으로 별도 분기한다.
        /// </summary>
        private static Color GetStarTypeColor(StarType starType, Team team) => starType switch
        {
            StarType.NORMAL => new Color(0.75f, 0.75f, 0.75f),   // 라이브 일반/에픽 - 무채색
            StarType.PURPLE => new Color(0.58f, 0.29f, 0.93f),   // 올스타
            StarType.SILVER => new Color(0.78f, 0.80f, 0.84f),   // 타이틀 홀더
            StarType.GOLD => new Color(1f, 0.84f, 0f),            // 골든 글러브
            StarType.PLATINUM => new Color(0.90f, 0.92f, 0.95f), // 시그니처(플래티넘)
            StarType.TEAM_COLOR => GetTeamColor(team),            // 왕조
            _ => Color.white
        };

        /// <summary>KBO 10개 구단의 대표색(근사치). 실제 브랜드 가이드가 확정되면 이 표만 갱신하면 된다.</summary>
        private static Color GetTeamColor(Team team) => team switch
        {
            Team.Doosan => new Color(0.05f, 0.16f, 0.35f),
            Team.LG => new Color(0.77f, 0.02f, 0.15f),
            Team.KT => new Color(0.05f, 0.05f, 0.05f),
            Team.SSG => new Color(0.85f, 0.10f, 0.15f),
            Team.NC => new Color(0.10f, 0.11f, 0.35f),
            Team.Kiwoom => new Color(0.50f, 0.09f, 0.16f),
            Team.KIA => new Color(0.65f, 0.0f, 0.05f),
            Team.Samsung => new Color(0.0f, 0.32f, 0.70f),
            Team.Lotte => new Color(0.85f, 0.10f, 0.15f),
            Team.Hanwha => new Color(1f, 0.45f, 0.0f),
            _ => Color.gray
        };
    }
}
