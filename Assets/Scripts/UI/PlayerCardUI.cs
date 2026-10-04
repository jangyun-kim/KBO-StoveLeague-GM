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
    /// [TASK-KBO-152, 로딩 키 변경 + 사실 정정] 당시엔 `PlayerDatabase`가 cards.csv를 조인하지 않아
    /// `TemplateId`가 "선수 1명당 값 1개"였다(DCL-057) - 그래서 임시로 `TemplateId_CurrentStarType`
    /// 조합 키를 썼다("24년 라이브 구자욱"과 "24년 골든글러브 구자욱"을 `CurrentStarType`(NORMAL/GOLD)
    /// 차이로만 구분).
    ///
    /// [TASK-KBO-153, 사실 정정 - 위 조합 키를 대체] `PlayerDatabase`가 이제 cards.csv를 실제로
    /// 조인한다(PlayerDatabase.cs 클래스 요약 참고) - cards.csv에 등록된 선수는 `TemplateId` 자체가
    /// 카드 고유 ID(cards.csv의 card_id, 예: "CRD_0001")로 이미 등급별로 갈라져 있어, 더 이상
    /// `CurrentStarType`을 덧붙일 필요가 없다. 명령서는 `Player.CardId`(또는 "템플릿의 CardId 속성")를
    /// 쓰라고 지시했으나 그런 이름의 필드는 만들지 않았다 - `PlayerTemplate.TemplateId`가 이번 조인으로
    /// 정확히 그 역할(카드 고유 ID)을 맡게 되었으므로, 이름만 다른 필드를 하나 더 만드는 대신
    /// `Template.TemplateId`를 그대로 재사용했다(불필요한 중복 필드 방지). 폴더 규칙:
    /// `Assets/Resources/Portraits/{TemplateId}.png`(예: `CRD_0001.png`) - PM이 예시로 든
    /// "SS_24_KooJaWook_SIG" 형식과는 다르지만, cards.csv(SSOT)가 실제로 발급하는 card_id 형식을
    /// 그대로 따랐다. cards.csv에 아직 등록되지 않은 선수(현재 11명 중 2명만 등록됨, PlayerDatabase.cs
    /// 참고)는 폴백으로 `TemplateId=player_id`가 쓰여 여전히 등급 구분 없는 사진 1장을 공유한다 -
    /// cards.csv에 해당 선수의 카드 데이터를 추가하는 즉시(코드 수정 없이) 해소된다.
    ///
    /// [TASK-KBO-169, 경로 개편] 2만 6천 장 규모라 한 폴더에 다 몰아넣지 않고 `{TemplateId}`의 앞 두
    /// `_` 조각(Team/Year)으로 중첩 폴더를 나눈다 - 최종 경로는 `Assets/Resources/Portraits/{Team}/
    /// {Year}/{TemplateId}.png`(예: `Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_SIG.png`).
    /// `BuildPortraitResourcePath()` 참고.
    ///
    /// [TASK-KBO-170, 렌더링 검수] 실사 에셋으로 검증한 결과 두 가지 실제 결함을 발견해 고쳤다: (1)
    /// TASK-169가 로딩 코드만 중첩 경로로 바꾸고 기존/신규 초상화 파일은 옮기지 않아 `Resources.Load`가
    /// 전부 null을 반환하고 있었다(에셋을 실제 `{Team}/{Year}/` 하위로 이동 - 코드 변경 아님). (2)
    /// `SAMSUNG_2026_PLY_004038_SIG.png`처럼 Sprite Mode가 "Multiple"으로 임포트되고 서브 스프라이트명이
    /// 파일명과 다른 리소스는 `Resources.Load&lt;Sprite&gt;`가 타입 불일치로 조용히 null을 반환한다 -
    /// `LoadSprite()`가 `Resources.LoadAll&lt;Sprite&gt;` 폴백으로 이를 구제한다(아래 참고).
    ///
    /// [TASK-KBO-171] DYNASTY(왕조) 등급 전용 배경에 구단별 분기를 추가했다 - `BG_{code}_{TeamToken}`이
    /// 있으면 그것을, 없으면 기존처럼 `BG_{code}` 공통 배경을 쓴다. `LoadGradeBackground()` 참고.
    ///
    /// [TASK-KBO-172] 신설 등급 FRANCHISE(코드 `FRA`)를 지원한다 - `CardDesigns/BG_FRA`/`CardDesigns/Frame_FRA`를
    /// 기존 규칙 그대로 찾고, 배경 아트가 아직 없으면 방사형 그라디언트(중심 #FFE8D6 -> #B87352 -> 가장자리
    /// #3D2214, 브론즈 톤) 스프라이트를 코드로 1회 생성·캐시해 폴백 배경으로 쓴다(`GetProceduralFallbackBackground()`).
    /// </summary>
    public class PlayerCardUI : MonoBehaviour
    {
        /// <summary>[TASK-KBO-147, TASK-KBO-153, TASK-KBO-169 경로 개편] 초상화 리소스 폴더 루트.
        /// 실제 로딩 경로는 이 상수 하나가 아니라 `BuildPortraitResourcePath()`가 만드는
        /// "{이 상수}/{Team}/{Year}/{TemplateId}"(중첩 폴더) 전체다 - 클래스 요약 참고.</summary>
        public const string PortraitResourceFolder = "Portraits";

        /// <summary>[TASK-KBO-168] 등급별 전용 배경(BG_{code})/테두리(Frame_{code}) 이미지 폴더 규칙.
        /// {code}는 GetGradeCode(Grade)가 반환하는 등급 영문 코드(예: SIG, GG, DYN - cards_*.csv
        /// card_id 접미사와 동일한 표기를 그대로 재사용했다).</summary>
        public const string CardDesignResourceFolder = "CardDesigns";

        [Header("Text")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text teamText;
        [SerializeField] private Text positionText;
        [SerializeField] private Text ovrText;
        [Tooltip("[TASK-KBO-173] 개인 세트덱 스코어(= 카드 Salary) 표기. 비워두면 생략(기존 프리팹 호환).")]
        [SerializeField] private Text setDeckScoreText;
        private Color setDeckDefaultColor = Color.white;
        private bool setDeckColorCaptured;

        /// <summary>[TASK-KBO-187] 라인업 칸 전용 - 이 카드가 세트덱 총점(NP POINT)에 실제로 더하는 값(SetDeckEvaluator.CardScoreIn)을 표시한다.
        /// 0점(다른 구단 · 미합산)은 회색 "SD 0", 27인 슬롯 밖이면 "SD 0 제외". 라인업 카드 SD 합계 == 하단 총점.</summary>
        public void ShowLineupSetDeckScore(int score, bool excluded)
        {
            if (setDeckScoreText == null) return;
            if (!setDeckColorCaptured) { setDeckDefaultColor = setDeckScoreText.color; setDeckColorCaptured = true; }
            setDeckScoreText.text = excluded ? "SD 0 제외" : $"SD {score}";
            setDeckScoreText.color = score > 0 ? setDeckDefaultColor : new Color(0.6f, 0.62f, 0.66f);
        }

        [Header("Portrait (TASK-KBO-147 - Resources/Portraits/{TemplateId} 동적 로딩)")]
        [Tooltip("실제 선수 초상화를 표시할 Image. 비워두면 초상화 기능 자체를 생략한다(기존 카드 프리팹 " +
                 "호환 - 필드가 없어도 크래시하지 않음).")]
        [SerializeField] private Image portraitImage;
        [Tooltip("Resources/Portraits/{TemplateId}에 이미지가 없을 때 대신 표시할 기본 실루엣. " +
                 "비워두면 스프라이트가 비워진 채로 표시된다(투명/흰 박스 - 크래시는 아니지만 시각적으로 " +
                 "어색하므로 실제 사용 시 반드시 채워 넣을 것을 권장).")]
        [SerializeField] private Sprite fallbackPortraitSprite;
        [Tooltip("[TASK-KBO-168] 배경 인물(듀얼 샷) 이미지. Resources/Portraits/{TemplateId}_BG가 있을 " +
                 "때만 활성화되어 표시된다. 비워두면 듀얼 포트레이트 기능 자체를 생략한다(기존 카드 프리팹 " +
                 "호환 - 필드가 없어도 크래시하지 않음).")]
        [SerializeField] private Image portraitBGImage;

        [Header("Grade Visual")]
        [Tooltip("카드 배경 또는 테두리 이미지. 등급(StarType)에 따라 색이 바뀐다. [TASK-KBO-168] " +
                 "Resources/CardDesigns/BG_{등급코드}가 있으면 그 스프라이트로, 없으면 기존 색상 틴트로 표시된다.")]
        [SerializeField] private Image frameImage;
        [Tooltip("[TASK-KBO-168] 등급별 전용 테두리 오버레이. Resources/CardDesigns/Frame_{등급코드}가 " +
                 "있을 때만 활성화되어 표시된다. 비워두면 생략한다.")]
        [SerializeField] private Image frameOverlayImage;
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

        [Header("[TASK-KBO-188] 3단 레이아웃(헤더 · 일러스트 · 네임플레이트)")]
        [Tooltip("켜면 Setup()이 카드 자식을 상단 헤더(좌 OVR / 우 구단 로고·SD) · 중단 일러스트(하단 다크 비네팅) · " +
                 "하단 다크 네임플레이트(#0F172A, [각성/강화 배지] 포지션 이름'연도)로 재배치한다.")]
        [SerializeField] private bool applyLayout188 = true;

        public static readonly Color NamePlateColor = new Color32(0x0F, 0x17, 0x2A, 0xFF);
        public static readonly Color AwakenBadgeColor = new Color32(0xC9, 0x93, 0x1A, 0xFF);
        public static readonly Color TranscendBadgeColor = new Color32(0x6D, 0x28, 0xD9, 0xFF);
        public static readonly Color ReinforceBadgeColor = new Color(0.55f, 0.18f, 0.62f, 1f);

        /// <summary>[TASK-KBO-188] 네임플레이트 높이(카드 높이 비율)와 헤더 시작점.</summary>
        public const float NamePlateTop = 0.17f;
        public const float HeaderBottom = 0.86f;

        private RectTransform headerBar;
        private Image teamLogoImage;
        private Image vignetteImage;
        private RectTransform growthBadge;
        private Text growthBadgeText;
        private static Sprite vignetteSprite;

        /// <summary>테스트/검증용 - 현재 표시 중인 성장 배지 문구(숨김이면 빈 문자열).</summary>
        public string GrowthBadgeText => growthBadge != null && growthBadge.gameObject.activeSelf && growthBadgeText != null ? growthBadgeText.text : "";
        public bool PortraitVisible => portraitImage != null && portraitImage.enabled && portraitImage.sprite != null;
        public Sprite PortraitSprite => portraitImage != null ? portraitImage.sprite : null;
        public string NameLabel => nameText != null ? nameText.text : "";

        public Player BoundPlayer { get; private set; }
        public bool IsSelected { get; private set; }

        /// <summary>[TASK-KBO-168] frameImage에 원래(디자인 리소스 도입 전) 지정돼 있던 sprite를 1회만
        /// 캐싱해 둔다 - 카드 풀링으로 이 컴포넌트가 재사용될 때, 이전 선수는 CardDesigns 아트가 있었고
        /// 새 선수는 없는 경우에도 이전 아트가 stale하게 남지 않고 이 기본값으로 정확히 되돌아간다.</summary>
        private Sprite defaultFrameSprite;
        private bool defaultFrameSpriteCaptured;

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

            if (nameText != null) nameText.text = applyLayout188 ? CardDisplay.NamePlate(player) : player.Template.PlayerName;
            if (teamText != null) teamText.text = player.Template.Team.ToString();
            if (positionText != null) positionText.text = DescribePosition(player.Template);
            if (ovrText != null) ovrText.text = player.CalculateOVR(false).ToString();
            if (setDeckScoreText != null)
            {
                if (!setDeckColorCaptured) { setDeckDefaultColor = setDeckScoreText.color; setDeckColorCaptured = true; }
                setDeckScoreText.color = setDeckDefaultColor; // 풀 재사용 시 라인업 회색 표시가 남지 않게
                setDeckScoreText.text = $"SD {SetDeckEvaluator.GetBaseCardSetDeckScore(player)}"; // [TASK-KBO-173] Salary = 세트덱 스코어
            }

            var gradeColor = GetStarTypeColor(player.CurrentStarType, player.Template.Team);
            if (frameImage != null) frameImage.color = gradeColor;

            SetupStars(player.StarLevel, gradeColor);
            SetupStamina(player);
            SetupCondition(player.CurrentCondition);
            SetupPortrait(player);
            SetupGradeDesign(player.Template.Grade, player.Template.Team);
            if (applyLayout188) ApplyLayout188(player);
        }

        /// <summary>[TASK-KBO-147, TASK-KBO-153 키 변경, TASK-KBO-169 경로 개편] `Resources/Portraits/
        /// {Team}/{Year}/{TemplateId}`에서 초상화를 동적으로 불러와 `portraitImage`에 대입한다 - 2만
        /// 6천 장 규모의 이미지가 한 폴더에 몰리지 않도록 카드 ID 자체에서 구단/연도를 뽑아 중첩
        /// 폴더로 나눠 찾는다(`BuildPortraitResourcePath()` 참고). `TemplateId`는 `PlayerDatabase`의
        /// cards.csv 조인 결과로 카드 고유 ID(card_id, 예: "SAMSUNG_2024_PLY_004038_SIG")다(선수당
        /// 카드가 아직 없으면 player_id로 폴백 - 이 경우 중첩 경로가 실제 폴더와 안 맞을 수 있지만,
        /// 그래도 `Resources.Load`가 null을 반환할 뿐 크래시하지 않고 `fallbackPortraitSprite`로 안전하게
        /// 폴백한다). 없으면 `fallbackPortraitSprite`로 폴백한다 - 풀링 재사용 시 이전 선수의 초상화가
        /// 남아 있는 사고를 막기 위해 매번 두 경우 모두 명시적으로 대입한다.</summary>
        private void SetupPortrait(Player player)
        {
            string templateId = player.Template.TemplateId;
            string portraitPath = BuildPortraitResourcePath(templateId);

            if (portraitImage != null)
            {
                // [TASK-KBO-188] 카드 고유 초상화가 없으면 같은 선수의 다른 등급·연도 초상화로 폴백(PortraitResolver).
                Sprite portrait = portraitPath != null ? LoadSprite(portraitPath) : null;
                if (portrait == null)
                {
                    string fallback = PortraitResolver.Resolve(player.Template);
                    if (fallback != null && fallback != portraitPath) portrait = LoadSprite(fallback);
                }

                portraitImage.sprite = portrait != null ? portrait : fallbackPortraitSprite;
                portraitImage.enabled = portraitImage.sprite != null;
            }

            // [TASK-KBO-168, TASK-KBO-169 경로 개편] 배경 인물(듀얼 샷) - {TemplateId}_BG 이미지가 있을
            // 때만 켠다. 풀링 재사용 시 이전 선수의 투샷이 남지 않도록 없는 경우도 명시적으로 꺼준다
            // (portraitImage와 동일 관례).
            if (portraitBGImage != null)
            {
                Sprite portraitBG = portraitPath != null ? LoadSprite($"{portraitPath}_BG") : null;

                portraitBGImage.sprite = portraitBG;
                portraitBGImage.gameObject.SetActive(portraitBG != null);
            }
        }

        /// <summary>[TASK-KBO-170, 렌더링 검수] `Resources.Load&lt;Sprite&gt;(path)`는 텍스처의 Sprite Mode가
        /// "Single"일 때만 동작한다 - 원본이 포토샵 스프라이트시트 슬라이스 잔재로 "Multiple"로 임포트되고
        /// 서브 스프라이트 이름이 파일명과 다르면(실사 검증 중 `SAMSUNG_2026_PLY_004038_SIG.png`에서 실제로
        /// 발견 - 서브 스프라이트명이 "구자욱'24_signature_AWAY_0"), `Resources.Load&lt;Sprite&gt;`가 조용히
        /// null을 반환해 카드에 초상화가 영원히 뜨지 않는다(크래시는 없지만 풀백만 보임 - 원인 파악이 매우
        /// 어려운 버그). 이런 임포트 상태의 에셋도 안전하게 구제하기 위해, 1차 로드가 실패하면
        /// `Resources.LoadAll&lt;Sprite&gt;`로 해당 경로의 서브 스프라이트를 모두 훑어 첫 번째를 사용한다 -
        /// 아트 담당자가 매번 Sprite Mode를 Single로 재설정하지 않아도 되는 방어 코드다.</summary>
        private static Sprite LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;

            Sprite[] subSprites = Resources.LoadAll<Sprite>(path);
            return subSprites.Length > 0 ? subSprites[0] : null;
        }

        /// <summary>[TASK-KBO-169] card_id 형식 `{Team}_{Year}_{PlayerID}_{SeasonCode}`(예:
        /// "SAMSUNG_2024_PLY_004038_SIG")의 앞 두 `_` 구분 조각(Team/Year)만 뽑아 중첩 리소스 경로
        /// `Portraits/{Team}/{Year}/{templateId}`를 만든다. `PlayerID` 자체도 "PLY_004038"처럼
        /// `_`를 포함하지만 항상 3번째 조각부터 시작하므로 앞 두 조각만 취하면 된다. 형식이 다른
        /// (예: 카드 미등록 선수의 `player_id` 단독 폴백, "PLY_004038") 값은 조각 수가 2개뿐이라도
        /// 그대로 최선을 다해 경로를 구성한다 - 실제 폴더와 안 맞으면 Resources.Load가 null을
        /// 반환할 뿐이라 SetupPortrait()의 폴백 로직이 그대로 안전하게 처리한다.</summary>
        private static string BuildPortraitResourcePath(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;

            var parts = templateId.Split('_');
            if (parts.Length < 2) return null;

            string team = parts[0];
            string year = parts[1];
            return $"{PortraitResourceFolder}/{team}/{year}/{templateId}";
        }

        /// <summary>[TASK-KBO-168] 등급별 전용 배경/테두리를 Resources/CardDesigns/에서 불러온다.
        /// frameImage(배경)는 리소스가 없으면 sprite를 건드리지 않고(defaultFrameSprite로 복원) 기존
        /// 색상 틴트(Setup()이 이미 대입한 gradeColor)만으로 표시한다 - 디자인 리소스가 아직 없는
        /// 등급도 크래시나 시각적 결손 없이 예전과 동일하게 보인다. frameOverlayImage(테두리)는 원래
        /// 이번 작업으로 신설된 요소라 지킬 기본값이 없으므로 있으면 켜고 없으면 끈다.
        ///
        /// [TASK-KBO-171] DYNASTY(왕조) 등급은 구단마다 다른 왕조 서사를 갖고 있어("왕조" 서사가
        /// 구단 공통이 아니라 삼성/해태 등 구단별로 다르다) 등급 공통 배경 하나로는 맞지 않는다 -
        /// `BG_{code}_{TeamToken}`(예: `BG_DYN_SAMSUNG`)을 우선 찾고, 해당 구단 전용 아트가 아직
        /// 없으면(대부분의 팀이 그럴 것) `BG_{code}`(구단 무관 공통 왕조 배경)로 자연스럽게 폴백한다.
        /// `TeamToken`은 `Team.ToString().ToUpperInvariant()`로 만든다 - `GenerateKBODatabase.py`의
        /// `TEAMS` 토큰(card_id에 실제로 박히는 값, 예: "SAMSUNG"/"KIWOOM"/"LOTTE")과 대소문자까지
        /// 정확히 일치한다(다른 구단은 enum 이름 자체가 이미 대문자와 동일 - KIA/LG/KT/SSG/NC).</summary>
        private void SetupGradeDesign(Grade grade, Team team)
        {
            string code = GetGradeCode(grade);

            if (frameImage != null)
            {
                if (!defaultFrameSpriteCaptured)
                {
                    defaultFrameSprite = frameImage.sprite;
                    defaultFrameSpriteCaptured = true;
                }

                Sprite bg = LoadGradeBackground(code, grade, team) ?? GetProceduralFallbackBackground(grade);
                if (bg != null)
                {
                    frameImage.sprite = bg;
                    frameImage.color = Color.white; // 커스텀 아트가 등급 틴트로 물들지 않도록.
                }
                else
                {
                    frameImage.sprite = defaultFrameSprite; // 풀링 재사용 시 이전 등급의 아트가 남지 않도록 복원.
                }
            }

            if (frameOverlayImage != null)
            {
                Sprite frame = LoadSprite($"{CardDesignResourceFolder}/Frame_{code}");
                frameOverlayImage.sprite = frame;
                frameOverlayImage.gameObject.SetActive(frame != null);
            }
        }

        /// <summary>[TASK-KBO-171] `grade`가 DYNASTY일 때만 구단별 전용 배경(`BG_{code}_{TeamToken}`)을
        /// 우선 시도하고, 그 외 모든 등급은 기존과 동일하게 `BG_{code}` 공통 배경만 찾는다 - 왕조가 아닌
        /// 등급까지 `_{TeamToken}` 접미사를 찾아보면 불필요한 `Resources.Load` 실패 시도가 매 Setup()마다
        /// 늘어나므로, 실제로 구단별 분기가 필요한 DYNASTY에만 한정했다.</summary>
        private Sprite LoadGradeBackground(string code, Grade grade, Team team)
        {
            if (grade == Grade.DYNASTY)
            {
                string teamToken = team.ToString().ToUpperInvariant();
                Sprite teamBg = LoadSprite($"{CardDesignResourceFolder}/BG_{code}_{teamToken}");
                if (teamBg != null) return teamBg;
            }

            return LoadSprite($"{CardDesignResourceFolder}/BG_{code}");
        }

        /// <summary>[TASK-KBO-168] Grade -&gt; CardDesigns 리소스 파일명 코드. GenerateKBODatabase.py의
        /// GRADE_META[grade]["code"]/cards_*.csv card_id 접미사와 동일한 표기로 맞췄다(예: "..._SIG",
        /// "..._GG") - 아트 담당자가 카드 ID를 보고 그대로 대응되는 디자인 파일명을 유추할 수 있게 한다.</summary>
        private static string GetGradeCode(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => "LN",
            Grade.LIVE_EPIC => "EPIC",
            Grade.ALLSTAR => "AS",
            Grade.FRANCHISE => "FRA", // [TASK-KBO-172 신설]
            Grade.TITLE_HOLDER => "TH",
            Grade.RETIRED_NUMBER => "RN", // [TASK-KBO-174 확인] CardDesigns/BG_RN, Frame_RN - 아트 부재 시 StarType.BLACK 틴트
            Grade.GOLDEN_GLOVE => "GG",
            Grade.SIGNATURE => "SIG",
            Grade.DYNASTY => "DYN",
            _ => "LN",
        };

        /// <summary>[TASK-KBO-172] 전용 배경 아트(BG_{code})가 없는 등급 중 "지정 폴백 그라디언트"가 있는 등급의
        /// 방사형 그라디언트 3색(중심/중간/가장자리). 현재는 FRANCHISE만 지정돼 있다(명령서 STEP 3-1:
        /// Radial #FFE8D6 -> #B87352 -> #3D2214). 그 외 등급은 기존처럼 색상 틴트만 쓴다.</summary>
        private static bool TryGetFallbackGradient(Grade grade, out Color inner, out Color mid, out Color outer)
        {
            if (grade == Grade.FRANCHISE)
            {
                inner = new Color32(0xFF, 0xE8, 0xD6, 0xFF);
                mid = new Color32(0xB8, 0x73, 0x52, 0xFF);
                outer = new Color32(0x3D, 0x22, 0x14, 0xFF);
                return true;
            }

            inner = mid = outer = Color.white;
            return false;
        }

        private const int FallbackGradientSize = 256;
        private static readonly System.Collections.Generic.Dictionary<Grade, Sprite> ProceduralBackgroundCache =
            new System.Collections.Generic.Dictionary<Grade, Sprite>();

        /// <summary>[TASK-KBO-172] `TryGetFallbackGradient()`에 지정된 등급이면 방사형 그라디언트 스프라이트를
        /// 한 번만 만들어(정적 캐시) 반환하고, 아니면 null(기존 틴트 경로 유지). 중심(0) -> 중간(0.5) -> 가장자리(1)
        /// 두 구간을 선형 보간하며, 가장자리 거리는 사각형 모서리까지(√2 반경)를 1로 정규화한다.</summary>
        private static Sprite GetProceduralFallbackBackground(Grade grade)
        {
            if (ProceduralBackgroundCache.TryGetValue(grade, out var cached) && cached != null) return cached;
            if (!TryGetFallbackGradient(grade, out var inner, out var mid, out var outer)) return null;

            var texture = new Texture2D(FallbackGradientSize, FallbackGradientSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = $"ProceduralBG_{GetGradeCode(grade)}"
            };
            var pixels = new Color[FallbackGradientSize * FallbackGradientSize];
            float center = (FallbackGradientSize - 1) * 0.5f;
            float maxDistance = center * Mathf.Sqrt(2f);
            for (int y = 0; y < FallbackGradientSize; y++)
            {
                for (int x = 0; x < FallbackGradientSize; x++)
                {
                    float dx = x - center, dy = y - center;
                    float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / maxDistance);
                    pixels[y * FallbackGradientSize + x] = t < 0.5f
                        ? Color.Lerp(inner, mid, t / 0.5f)
                        : Color.Lerp(mid, outer, (t - 0.5f) / 0.5f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, FallbackGradientSize, FallbackGradientSize), new Vector2(0.5f, 0.5f));
            sprite.name = texture.name;
            ProceduralBackgroundCache[grade] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------ [TASK-KBO-188] 3단 레이아웃

        private static void Fit(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private RectTransform EnsureChild(string childName, bool withImage)
        {
            var found = transform.Find(childName) as RectTransform;
            if (found == null)
            {
                var go = new GameObject(childName, typeof(RectTransform));
                found = (RectTransform)go.transform;
                found.SetParent(transform, false);
            }
            if (withImage && !found.TryGetComponent<Image>(out _))
            {
                var img = found.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
            }
            return found;
        }

        private static void FitText(Text text, float x0, float y0, float x1, float y1, TextAnchor anchor, int maxSize)
        {
            if (text == null) return;
            Fit(text.rectTransform, x0, y0, x1, y1);
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = Mathf.Max(10, maxSize);
            text.raycastTarget = false;
        }

        private static Sprite VignetteSprite()
        {
            if (vignetteSprite != null) return vignetteSprite;
            const int h = 64;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CardVignette188" };
            for (int y = 0; y < h; y++)
            {
                float t = 1f - y / (float)(h - 1); // 아래(0) = 진하게
                tex.SetPixel(0, y, new Color(0.04f, 0.06f, 0.12f, Mathf.Pow(t, 1.6f) * 0.92f));
            }
            tex.Apply();
            vignetteSprite = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f));
            vignetteSprite.name = tex.name;
            return vignetteSprite;
        }

        /// <summary>
        /// [TASK-KBO-188] 상단 헤더(좌 OVR / 우 구단 로고 · SD) · 중단 일러스트(하단 다크 비네팅 - 배경 등급 글씨가 이름을 가리지 않게) ·
        /// 하단 다크 네임플레이트(#0F172A, [각성/강화 배지] {포지션} {선수명}'{연도}) 3단으로 자식을 재배치한다(풀 재사용 시 반복 호출 안전).
        /// 1각 이상이면 강화 "+N" 대신 각성 배지("N각" 골드 / "초월" 퍼플)를 보인다.
        /// </summary>
        private void ApplyLayout188(Player player)
        {
            Font font = nameText != null ? nameText.font : ovrText != null ? ovrText.font : null;

            // 중단 일러스트
            if (portraitBGImage != null) Fit(portraitBGImage.rectTransform, 0f, NamePlateTop, 1f, HeaderBottom);
            if (portraitImage != null)
            {
                Fit(portraitImage.rectTransform, 0.02f, NamePlateTop, 0.98f, HeaderBottom);
                portraitImage.preserveAspect = true;
                portraitImage.raycastTarget = false;
            }
            var vignette = EnsureChild("Vignette188", true);
            Fit(vignette, 0f, NamePlateTop, 1f, 0.5f);
            vignetteImage = vignette.GetComponent<Image>();
            vignetteImage.sprite = VignetteSprite();
            vignetteImage.type = Image.Type.Simple;
            vignetteImage.color = Color.white;

            // 별(일러스트 하단, 비네팅 위)
            if (starIcons != null && starIcons.Length > 0)
            {
                const float w = 0.075f, gap = 0.012f;
                float total = starIcons.Length * w + (starIcons.Length - 1) * gap;
                float x = 0.5f - total * 0.5f;
                foreach (var star in starIcons)
                {
                    if (star != null)
                    {
                        Fit(star.rectTransform, x, NamePlateTop + 0.01f, x + w, NamePlateTop + 0.06f);
                        star.preserveAspect = true;
                    }
                    x += w + gap;
                }
            }
            if (conditionIconImage != null) Fit(conditionIconImage.rectTransform, 0.86f, HeaderBottom - 0.075f, 0.97f, HeaderBottom - 0.01f);
            if (conditionArrowText != null) FitText(conditionArrowText, 0.86f, HeaderBottom - 0.075f, 0.97f, HeaderBottom - 0.01f, TextAnchor.MiddleCenter, 14);
            if (staminaBarRoot != null && staminaBarRoot.transform is RectTransform stamina) Fit(stamina, 0.03f, NamePlateTop + 0.065f, 0.97f, NamePlateTop + 0.085f);

            // 상단 헤더
            headerBar = EnsureChild("Header188", true);
            Fit(headerBar, 0f, HeaderBottom, 1f, 1f);
            headerBar.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.12f, 0.78f);
            FitText(ovrText, 0.04f, HeaderBottom + 0.005f, 0.5f, 0.995f, TextAnchor.MiddleLeft, 34);
            var logoRect = EnsureChild("TeamLogo188", true);
            Fit(logoRect, 0.52f, HeaderBottom + 0.015f, 0.68f, 0.985f);
            teamLogoImage = logoRect.GetComponent<Image>();
            TeamLogoSprites.Apply(teamLogoImage, player.Template.Team);
            bool hasLogo = teamLogoImage.sprite != null;
            teamLogoImage.enabled = hasLogo;
            if (teamText != null)
            {
                teamText.gameObject.SetActive(!hasLogo);
                FitText(teamText, 0.5f, HeaderBottom + 0.005f, 0.7f, 0.995f, TextAnchor.MiddleCenter, 14);
            }
            FitText(setDeckScoreText, 0.69f, HeaderBottom + 0.005f, 0.97f, 0.995f, TextAnchor.MiddleRight, 16);
            if (positionText != null) positionText.gameObject.SetActive(false); // 포지션은 네임플레이트로 이동

            // 하단 네임플레이트
            var plate = transform.Find("NameStrip") as RectTransform;
            if (plate == null) plate = EnsureChild("NameStrip", true);
            if (!plate.TryGetComponent<Image>(out var plateImage)) plateImage = plate.gameObject.AddComponent<Image>();
            Fit(plate, 0f, 0f, 1f, NamePlateTop);
            plateImage.color = NamePlateColor;
            plateImage.raycastTarget = false;

            string badge = CardGrowthRules.GrowthBadgeLabel(player);
            bool hasBadge = !string.IsNullOrEmpty(badge);
            growthBadge = EnsureChild("GrowthBadge188", true);
            growthBadge.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                bool awaken = CardGrowthRules.ShowsAwakenBadge(player.Template.Grade, player.AwakenLevel);
                bool transcend = CardGrowthRules.IsTranscended(player.Template.Grade, player.AwakenLevel);
                Fit(growthBadge, 0.03f, 0.025f, 0.27f, NamePlateTop - 0.025f);
                growthBadge.GetComponent<Image>().color = transcend ? TranscendBadgeColor : awaken ? AwakenBadgeColor : ReinforceBadgeColor;
                var textRect = growthBadge.Find("Text") as RectTransform;
                if (textRect == null)
                {
                    textRect = (RectTransform)new GameObject("Text", typeof(RectTransform)).transform;
                    textRect.SetParent(growthBadge, false);
                    textRect.gameObject.AddComponent<Text>();
                }
                growthBadgeText = textRect.GetComponent<Text>();
                if (font != null) growthBadgeText.font = font;
                growthBadgeText.fontStyle = FontStyle.Bold;
                growthBadgeText.text = badge;
                growthBadgeText.color = transcend ? new Color(1f, 0.86f, 0.35f) : Color.white;
                FitText(growthBadgeText, 0.04f, 0f, 0.96f, 1f, TextAnchor.MiddleCenter, 18);
            }
            FitText(nameText, hasBadge ? 0.29f : 0.04f, 0f, 0.97f, NamePlateTop, hasBadge ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, 20);

            // 그리기 순서: 일러스트 → 비네팅 → 테두리 → 별/컨디션/체력 → 헤더 → 네임플레이트 → 선택 표시
            int order = 0;
            void Order(Component c) { if (c != null) c.transform.SetSiblingIndex(order++); }
            Order(portraitBGImage); Order(portraitImage); Order(vignetteImage); Order(frameOverlayImage);
            if (starIcons != null) foreach (var star in starIcons) Order(star);
            Order(conditionIconImage); Order(conditionArrowText);
            if (staminaBarRoot != null) Order(staminaBarRoot.transform);
            Order(headerBar); Order(ovrText); Order(teamLogoImage); Order(teamText); Order(setDeckScoreText); Order(positionText);
            Order(plate); Order(growthBadge); Order(nameText);
            if (selectedOverlay != null) selectedOverlay.transform.SetAsLastSibling();
            if (checkmarkIcon != null) checkmarkIcon.transform.SetAsLastSibling();
        }


        /// <summary>빈 카드로 되돌린다 (풀링/재사용 시 사용).</summary>
        public void Clear()
        {
            BoundPlayer = null;
            if (nameText != null) nameText.text = "";
            if (teamText != null) teamText.text = "";
            if (positionText != null) positionText.text = "";
            if (ovrText != null) ovrText.text = "";
            if (setDeckScoreText != null) setDeckScoreText.text = "";
            if (frameImage != null)
            {
                frameImage.color = Color.white;
                // [TASK-KBO-168] 풀링 재사용 전 이전 등급의 CardDesigns 배경 아트가 남지 않도록 복원.
                if (defaultFrameSpriteCaptured) frameImage.sprite = defaultFrameSprite;
            }
            SetupStars(0, Color.white);
            if (staminaBarRoot != null) staminaBarRoot.SetActive(false);
            SetupCondition(PlayerCondition.Normal);
            SetSelected(false);

            if (portraitImage != null)
            {
                portraitImage.sprite = fallbackPortraitSprite;
                portraitImage.enabled = fallbackPortraitSprite != null;
            }

            // [TASK-KBO-168] 빈 카드는 듀얼 샷 배경/등급 테두리도 없어야 하므로 둘 다 끈다.
            if (portraitBGImage != null)
            {
                portraitBGImage.sprite = null;
                portraitBGImage.gameObject.SetActive(false);
            }

            if (frameOverlayImage != null)
            {
                frameOverlayImage.sprite = null;
                frameOverlayImage.gameObject.SetActive(false);
            }

            if (growthBadge != null) growthBadge.gameObject.SetActive(false);
            if (teamLogoImage != null) teamLogoImage.enabled = false;
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
            StarType.BRONZE => new Color32(0xB8, 0x73, 0x52, 0xFF), // [TASK-KBO-172 신설] 프랜차이즈 - 폴백 그라디언트 중간색과 동일
            StarType.GOLD => new Color(1f, 0.84f, 0f),            // 골든 글러브
            StarType.PLATINUM => new Color(0.90f, 0.92f, 0.95f), // 시그니처(플래티넘)
            StarType.TEAM_COLOR => GetTeamColor(team),            // 왕조
            StarType.BLACK => new Color(0.10f, 0.10f, 0.10f),     // [TASK-KBO-155 신설] 영구결번 - 실제 KBO 구단들의 영구결번 현수막 관례(검정 바탕)를 참고
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
