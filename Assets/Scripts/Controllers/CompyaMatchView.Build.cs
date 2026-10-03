using System.Collections.Generic;
using System.Linq;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-179] CompyaMatchView 화면 조립/갱신부. 모든 좌표는 레퍼런스 세로 캡처(1248x1972 px, 좌상단 원점)에서 잰 값이다
    /// (ReferenceImages/SetImage: 경기 플레이 유형 선택 / 경기 하이라이트 플레이_1·_2 / 경기 하이라이트 플레이 중 직접 플레이 선택 /
    /// 경기 종료 후 결과_1~3 / 경기 플레이 중 한 이닝 종료 후 중간 UI / 라팍_1~3 / 경기 플레이 메인 시뮬레이션 UI).
    /// 배경·그라운드·타석 뷰·구단 로고는 CropBroadcastAssets179.py가 레퍼런스에서 잘라 만든 Resources/Broadcast179 텍스처다.
    /// </summary>
    public partial class CompyaMatchView
    {
        // ---- 레퍼런스 팔레트
        private static readonly Color White = new Color(0.98f, 0.98f, 0.99f);
        private static readonly Color Ink = new Color(0.1f, 0.11f, 0.15f);
        private static readonly Color Navy = new Color(0.04f, 0.06f, 0.2f);
        private static readonly Color Muted = new Color(0.55f, 0.57f, 0.62f);
        private static readonly Color PanelGray = new Color(0.86f, 0.87f, 0.9f);
        private static readonly Color Cyan = new Color(0.33f, 0.83f, 0.97f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.3f);
        private static readonly Color BadgeBlue = new Color(0.16f, 0.36f, 0.84f);
        private static readonly Color BadgeRed = new Color(0.72f, 0.16f, 0.17f);
        private static readonly Color PurpleTop = new Color(0.62f, 0.33f, 1f);
        private static readonly Color PurpleBottom = new Color(0.4f, 0.13f, 0.88f);
        private static readonly Color BlueTop = new Color(0.22f, 0.45f, 0.98f);
        private static readonly Color BlueBottom = new Color(0.1f, 0.27f, 0.85f);
        private static readonly Color AccentRed = new Color(0.86f, 0.16f, 0.22f);

        private CompyaUiKit kit;
        private RectTransform root;
        private GameObject typePanel, relayPanel, splashPanel, choicePanel, directPanel, result1Panel, result2Panel, result3Panel, popupPanel;

        // ---- 유형 선택
        private readonly RectTransform[] typeCards = new RectTransform[3];
        private readonly Image[] typeFrames = new Image[3];
        private readonly Image[] typeUnderlines = new Image[3];
        private readonly RawImage[] typePhotos = new RawImage[3];
        private Text typeDescText, typeInfoText, typeBallText, typeSeasonText;

        // ---- 중계
        private RawImage awayRowBg, homeRowBg, awayRowLogo, homeRowLogo;
        private Text awayRowName, homeRowName;
        private Image awayBatMark, homeBatMark;
        private PolygonGraphic awayUserBadge, homeUserBadge;
        private readonly Text[] awayCells = new Text[12];
        private readonly Text[] homeCells = new Text[12];
        private readonly Text[] awayRheb = new Text[4];
        private readonly Text[] homeRheb = new Text[4];
        private ProgressWidget relayProgress;
        private RectTransform arcRect;
        private ArcLineGraphic arcGlow, arcCore;
        private RectTransform toastRoot;
        private Text toastText, toastValue;
        private PolygonGraphic awayBand, homeBand, awaySlash, homeSlash;
        private RawImage awayBigLogo, homeBigLogo;
        private Text awayPctText, homePctText, awayScoreText, homeScoreText, countText, outsText, inningText;
        private RectTransform gaugeAway, gaugeHome, gaugeMarker;
        private Image gaugeAwayImage, gaugeHomeImage;
        private InfoBox leftInfo, rightInfo;
        private readonly LineupRow[] leftRows = new LineupRow[9];
        private readonly LineupRow[] rightRows = new LineupRow[9];
        private Button pauseButton;

        // ---- 이닝 종료
        private Text splashTitle, splashAwayScore, splashHomeScore, splashAwayName, splashHomeName;
        private RawImage splashAwayLogo, splashHomeLogo;

        // ---- 승부처 선택
        private Text choiceTitle, choiceSubtitle, choiceLeftScore, choiceRightScore, choiceInning;
        private MiniCard choiceBatterCard, choicePitcherCard;
        private readonly Image[] choiceBases = new Image[4];
        private readonly Text[] choiceBalls = new Text[3];
        private readonly Text[] choiceStrikes = new Text[2];
        private readonly Text[] choiceOuts = new Text[2];
        private ProgressWidget choiceProgress;

        // ---- 직접 플레이
        private RawImage directAwayRow, directHomeRow;
        private Text directAwayName, directHomeName, directAwayScore, directHomeScore, directInning, directCount, directOuts;
        private readonly Image[] directBases = new Image[4];
        private MiniCard directBatterCard, directPitcherCard;
        private Text directOrderText, directBatterStats, directPitcherStats, directSubtitleText, directResultText;
        private readonly Text[] directBatterSkills = new Text[3];
        private readonly Text[] directPitcherSkills = new Text[3];
        private RectTransform directStamina;
        private RectTransform directBall;
        private Button directPlayButton;
        private readonly Button[] tacticButtons = new Button[4];
        private readonly MatchTactic[] tacticOptions = new MatchTactic[4];
        private Text directTacticHint;

        // ---- 결과 1
        private RawImage r1AwayRow, r1HomeRow, r1AwayLogo, r1HomeLogo, r1AwayWatermark, r1HomeWatermark;
        private Text r1AwayName, r1HomeName, r1AwayScore, r1HomeScore;
        private PolygonGraphic r1UserBadge;
        private readonly Text[] r1AwayCells = new Text[12];
        private readonly Text[] r1HomeCells = new Text[12];
        private readonly Text[] r1AwayRheb = new Text[4];
        private readonly Text[] r1HomeRheb = new Text[4];
        private Image r1AwayBlock, r1HomeBlock;
        private RawImage r1AwayVerdictBg, r1HomeVerdictBg;
        private Text r1AwayVerdict, r1HomeVerdict;
        private readonly StatBar[] r1Stats = new StatBar[6];
        private Image r1WinPanel, r1LosePanel, r1WinNameBar, r1LoseNameBar;
        private RawImage r1WinLogo, r1LoseLogo;
        private MiniCard r1WinCard, r1LoseCard;
        private Text r1WinTag, r1LoseTag, r1WinName, r1WinRecord, r1LoseName, r1LoseRecord;

        // ---- 결과 2
        private Text r2Title, r2NextTeam, r2NextPitcher, r2Ball;
        private readonly RoundRow[] r2Rows = new RoundRow[5];

        // ---- 결과 3
        private MiniCard r3MvpCard;
        private Text r3MvpName, r3AchieveText, r3DirectText;
        private readonly RewardTile[] r3Rewards = new RewardTile[3];
        private RectTransform r3BarFill;
        private readonly Text[] r3MilestoneState = new Text[3];
        private readonly GameObject[] r3MilestoneChecks = new GameObject[3];

        // ---- 팝업
        private Text popupTitle, popupBody;
        private ScrollRect popupScroll;

        private class MiniCard
        {
            public RectTransform Root;
            public RawImage Background, Portrait, Watermark, TeamLogo;
            public Text Ovr, Position, Stars, Name, GradeChip;
        }

        private class InfoBox
        {
            public RawImage HeaderBg;
            public Text HeaderText, NameText;
            public MiniCard Card;
            public readonly Text[] RowLabels = new Text[3];
            public readonly Text[] RowValues = new Text[3];
            public readonly Image[] Badges = new Image[6];
            public readonly Text[] BadgeTexts = new Text[6];
        }

        private class LineupRow
        {
            public PolygonGraphic Highlight;
            public Image Badge;
            public Text BadgeText, Position, Name, Average, Ovr;
            public Image OvrBox;
        }

        private class ProgressWidget
        {
            public Text Count;
            public RectTransform Fill;
            public float FillMinX, FillMaxX; // 정규화 앵커 x(빈 바 ~ 60개 달성)
            public GameObject[] Checks = new GameObject[3];
        }

        private class StatBar
        {
            public float CenterY;
            public RectTransform LeftBar, RightBar, LeftValueRect, RightValueRect;
            public Text LeftValue, RightValue;
        }

        private class RoundRow
        {
            public RawImage AwayLogo, HomeLogo;
            public Text AwayName, HomeName, AwayScore, HomeScore, Venue;
            public PolygonGraphic UserBadge;
        }

        private class RewardTile
        {
            public GameObject Root, EventTag;
            public Text Icon, Count;
        }

        // ================================================================== 조립

        /// <summary>화면 계층을 (다시) 만든다. 기존 Root179는 제거한다 - Setup(에디터)과 런타임 Awake가 같은 코드를 쓴다.</summary>
        public void Build()
        {
            var old = transform.Find(RootName);
            if (old != null)
            {
                old.name = "_" + RootName + "_old";
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }

            kit = new CompyaUiKit(boldFont, regularFont);
            root = CompyaUiKit.Fill(transform, RootName);
            root.SetAsLastSibling();

            typePanel = BuildTypeSelect();
            relayPanel = BuildRelay();
            splashPanel = BuildSplash();
            choicePanel = BuildChoice();
            directPanel = BuildDirect();
            result1Panel = BuildResult1();
            result2Panel = BuildResult2();
            result3Panel = BuildResult3();
            popupPanel = BuildPopup();
            HideAll();
        }

        private void HideAll()
        {
            foreach (var panel in new[] { typePanel, relayPanel, splashPanel, choicePanel, directPanel, result1Panel, result2Panel, result3Panel, popupPanel })
                if (panel != null) panel.SetActive(false);
        }

        private RectTransform NewScreen(string name, string background, Color fallback)
        {
            var rect = CompyaUiKit.Fill(root, name);
            var raw = CompyaUiKit.PictureOn(rect, background);
            raw.raycastTarget = true; // 아래 구 InGamePanel 클릭 차단
            if (raw.texture == null) raw.color = fallback;
            return rect;
        }

        private static void Slash(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
            => CompyaUiKit.Polygon(parent, name, x0, y0, x1, y1, color,
                new Vector2(0.55f, 1f), new Vector2(1f, 1f), new Vector2(0.45f, 0f), new Vector2(0f, 0f));

        /// <summary>노란 체크 표시(두 개의 회전 막대).</summary>
        private static GameObject Check(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var rect = CompyaUiKit.Place(parent, name, x0, y0, x1, y1);
            var shortBar = CompyaUiKit.Norm(rect, "Short", 0.12f, 0.38f, 0.5f, 0.56f);
            CompyaUiKit.Paint(shortBar, Gold);
            shortBar.localRotation = Quaternion.Euler(0f, 0f, -45f);
            var longBar = CompyaUiKit.Norm(rect, "Long", 0.3f, 0.45f, 1f, 0.63f);
            CompyaUiKit.Paint(longBar, Gold);
            longBar.localRotation = Quaternion.Euler(0f, 0f, 45f);
            return rect.gameObject;
        }

        private static Image Diamond(Transform parent, string name, float cx, float cy, float size, Color color)
        {
            var image = CompyaUiKit.Box(parent, name, cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f, color);
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            image.rectTransform.localScale = Vector3.one * 0.72f;
            return image;
        }

        // ------------------------------------------------------------------ 1. 경기 유형 선택(SELECT TYPE)

        private GameObject BuildTypeSelect()
        {
            var p = NewScreen("TypeSelect", "Broadcast179/bg_select_type", new Color(0.12f, 0.1f, 0.35f));
            Slash(p, "SlashBlue", 388, 360, 430, 412, new Color(0.3f, 0.65f, 1f));
            Slash(p, "SlashPink", 826, 335, 864, 386, new Color(0.95f, 0.25f, 0.6f));
            var title = kit.Label(p, "Title", "SELECT TYPE", 420, 330, 830, 412, 78, TextAnchor.MiddleCenter, White, true, true);
            CompyaUiKit.Shadow(title, new Color(0f, 0f, 0f, 0.4f));
            CompyaUiKit.Box(p, "Line", 445, 415, 808, 418, new Color(0.7f, 0.78f, 0.95f, 0.6f));
            kit.Label(p, "Sub", "플레이 방식 설정", 420, 425, 830, 482, 46, TextAnchor.MiddleCenter, Cyan, true);

            var labels = new[] { ("빠른 진행", "QUICK PLAY", "type_quick"), ("하이라이트", "HIGHLIGHT", "type_highlight"), ("풀 플레이", "FULL PLAY", "type_full") };
            var xs = new[] { (172f, 462f), (478f, 772f), (785f, 1075f) };
            for (int i = 0; i < 3; i++)
            {
                float x0 = xs[i].Item1, x1 = xs[i].Item2;
                typeFrames[i] = CompyaUiKit.Box(p, $"Frame{i}", x0 - 8, 529, x1 + 8, 1111, new Color(0.3f, 0.85f, 1f));
                var card = CompyaUiKit.Place(p, $"Card{i}", x0, 537, x1, 1103);
                typeCards[i] = card;
                CompyaUiKit.Paint(card, new Color(0.93f, 0.93f, 0.95f), true);
                int captured = i;
                card.gameObject.AddComponent<Button>().onClick.AddListener(() => SelectMode((PlayMode)captured));
                typePhotos[i] = CompyaUiKit.PictureOn(CompyaUiKit.Norm(card, "Photo", 0f, 0.27f, 1f, 1f), $"Broadcast179/{labels[i].Item3}");
                kit.Gradient(CompyaUiKit.Norm(card, "Fade", 0f, 0.2f, 1f, 0.5f), new Color(0.93f, 0.93f, 0.95f, 0f), new Color(0.93f, 0.93f, 0.95f, 1f), false);
                var name = kit.LabelOn(CompyaUiKit.Norm(card, "Name", 0.08f, 0.1f, 0.92f, 0.23f), labels[i].Item1, 52, TextAnchor.MiddleCenter, Ink, true, true);
                var sub = kit.LabelOn(CompyaUiKit.Norm(card, "Eng", 0.1f, 0.02f, 0.9f, 0.1f), labels[i].Item2, 26, TextAnchor.MiddleCenter, new Color(0.5f, 0.52f, 0.58f), true, true);
                CompyaUiKit.Outline(sub, new Color(1f, 1f, 1f, 0.8f), 1f);
                CompyaUiKit.Paint(CompyaUiKit.Norm(card, "SlashL", 0.05f, 0.12f, 0.09f, 0.2f), new Color(0.2f, 0.45f, 0.95f)).rectTransform.localRotation = Quaternion.Euler(0, 0, -20);
                CompyaUiKit.Paint(CompyaUiKit.Norm(card, "SlashR", 0.91f, 0.15f, 0.95f, 0.23f), new Color(0.9f, 0.25f, 0.6f)).rectTransform.localRotation = Quaternion.Euler(0, 0, -20);
                typeUnderlines[i] = CompyaUiKit.Paint(CompyaUiKit.Norm(card, "Underline", 0f, 0f, 1f, 0.016f), new Color(0.45f, 0.2f, 0.95f));
                name.raycastTarget = false;
            }

            CompyaUiKit.Box(p, "DescBox", 172, 1148, 1075, 1495, new Color(0.05f, 0.05f, 0.14f, 0.88f));
            typeDescText = kit.Label(p, "Desc", "", 205, 1172, 1045, 1268, 34, TextAnchor.MiddleCenter, Cyan);
            typeInfoText = kit.Label(p, "Info", "", 205, 1272, 1045, 1335, 36, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Box(p, "Divider", 205, 1395, 1045, 1397, new Color(1f, 1f, 1f, 0.15f));
            CompyaUiKit.Box(p, "BallPill", 445, 1418, 805, 1475, new Color(0.02f, 0.02f, 0.07f, 0.75f));
            typeBallText = kit.Label(p, "Ball", "", 445, 1418, 805, 1475, 38, TextAnchor.MiddleCenter, White, true);

            kit.GradientButton(p, "Start", "START", 355, 1557, 892, 1670, PurpleTop, PurpleBottom, White, 70).onClick.AddListener(OnStartPressed);

            kit.Label(p, "BonusTitle", "SEASON", 540, 1712, 710, 1748, 28, TextAnchor.MiddleCenter, Gold, true, true);
            CompyaUiKit.Box(p, "BonusBar", 395, 1755, 860, 1795, new Color(0.13f, 0.2f, 0.36f, 0.9f));
            typeSeasonText = kit.Label(p, "Bonus", "", 395, 1755, 860, 1795, 34, TextAnchor.MiddleCenter, White, true);

            kit.Button(p, "Back", "◀  뒤 로 가 기", 0, 1900, 1248, 1972, new Color(0.2f, 0.42f, 0.78f), White, 36).onClick.AddListener(OnBackPressed);
            return p.gameObject;
        }

        private void RefreshTypeSelect()
        {
            for (int i = 0; i < 3; i++)
            {
                bool selected = (int)mode == i;
                typeFrames[i].enabled = selected;
                typeUnderlines[i].enabled = selected;
                typePhotos[i].color = selected ? Color.white : new Color(0.62f, 0.62f, 0.66f);
                typeCards[i].localScale = selected ? new Vector3(1.03f, 1.03f, 1f) : Vector3.one;
            }

            switch (mode)
            {
                case PlayMode.Quick:
                    typeDescText.text = "빠른 진행은 중계 없이 경기 결과를 바로 확인합니다.\n결과 화면으로 즉시 이동합니다.";
                    break;
                case PlayMode.Highlight:
                    typeDescText.text = "하이라이트에서는 시뮬레이션과 직접 플레이를 번갈아\n플레이합니다. 직접 플레이 시점은 자동 선택됩니다.";
                    break;
                default:
                    typeDescText.text = "풀 플레이는 모든 타석을 중계로 시청하며,\n우리 팀 득점권 찬스마다 직접 플레이로 개입합니다.";
                    break;
            }

            var fixture = LeagueManager.Instance != null ? LeagueManager.Instance.PeekNextFixture() : null;
            typeInfoText.text = fixture != null
                ? $"{CompyaUiKit.ShortName(fixture.AwayTeam)} vs {CompyaUiKit.ShortName(fixture.HomeTeam)}  ·  {CompyaUiKit.Stadium(fixture.HomeTeam)}"
                : "진행할 예정 경기가 없습니다.";
            typeBallText.text = GameManager.Instance != null ? $"볼  {GameManager.Instance.GameGold:N0}" : "볼  -";
            int played = LeagueManager.Instance != null ? LeagueManager.Instance.PlayedGameCount : 0;
            typeSeasonText.text = $"{played}/{LeagueManager.TotalUserGames}";
        }

        // ------------------------------------------------------------------ 2. 메인 세로 중계(하이라이트 플레이_1/_2)

        private GameObject BuildRelay()
        {
            var p = CompyaUiKit.Fill(root, "Relay");
            CompyaUiKit.Paint(p, PanelGray, true);

            // 상단 부감도 그라운드 + 타구 궤적
            CompyaUiKit.Picture(p, "Ground", 0, 0, 1248, 850, "Broadcast179/ground_highangle");
            arcRect = CompyaUiKit.Place(p, "Trajectory", 0, 0, 1248, 850);
            arcGlow = arcRect.gameObject.AddComponent<ArcLineGraphic>();
            arcGlow.color = new Color(0.45f, 0.75f, 1f, 0.35f);
            arcGlow.Thickness = 14f;
            arcGlow.raycastTarget = false;
            arcCore = CompyaUiKit.Fill(arcRect, "Core").gameObject.AddComponent<ArcLineGraphic>();
            arcCore.color = new Color(0.22f, 0.56f, 1f);
            arcCore.Thickness = 5f;
            arcCore.raycastTarget = false;

            // 이벤트 토스트("삼진 | P 30")
            toastRoot = CompyaUiKit.Place(p, "Toast", 30, 318, 333, 370);
            CompyaUiKit.Paint(toastRoot, new Color(0.97f, 0.97f, 0.98f));
            CompyaUiKit.Paint(CompyaUiKit.Norm(toastRoot, "Accent", 0f, 0.15f, 0.02f, 0.85f), AccentRed);
            toastText = kit.LabelOn(CompyaUiKit.Norm(toastRoot, "Text", 0.05f, 0f, 0.6f, 1f), "", 36, TextAnchor.MiddleLeft, Ink, true);
            CompyaUiKit.Paint(CompyaUiKit.Norm(toastRoot, "Pill", 0.63f, 0.08f, 0.99f, 0.92f), new Color(0.08f, 0.13f, 0.4f));
            kit.LabelOn(CompyaUiKit.Norm(toastRoot, "P", 0.64f, 0.08f, 0.76f, 0.92f), "P", 28, TextAnchor.MiddleCenter, White, true);
            toastValue = kit.LabelOn(CompyaUiKit.Norm(toastRoot, "Value", 0.76f, 0.08f, 0.98f, 0.92f), "", 34, TextAnchor.MiddleCenter, White, true);

            BuildScorebug(p);
            relayProgress = BuildProgress(p, 160, 243);

            // 승률 브릿지 바 + 좌우 구단 사선 띠/로고 + 사다리꼴 카운트 박스
            kit.GradientBox(p, "PctShade", 0, 845, 1248, 895, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.6f), false);
            kit.GradientBox(p, "ScoreBar", 0, 895, 1248, 1005, new Color(0.22f, 0.23f, 0.26f), new Color(0.05f, 0.05f, 0.07f), false);
            awayBand = CompyaUiKit.Polygon(p, "AwayBand", 0, 600, 250, 1005, Color.blue,
                new Vector2(0f, 1f), new Vector2(0.16f, 1f), new Vector2(0.86f, 0f), new Vector2(0f, 0f));
            homeBand = CompyaUiKit.Polygon(p, "HomeBand", 998, 600, 1248, 1005, Color.red,
                new Vector2(0.84f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0.14f, 0f));
            awayBigLogo = CompyaUiKit.Logo(p, "AwayLogo", 0, 788, 245, 995);
            homeBigLogo = CompyaUiKit.Logo(p, "HomeLogo", 1003, 788, 1248, 995);

            awayPctText = kit.Label(p, "AwayPct", "50%", 320, 845, 460, 895, 48, TextAnchor.MiddleRight, White, true);
            homePctText = kit.Label(p, "HomePct", "50%", 790, 845, 930, 895, 48, TextAnchor.MiddleLeft, White, true);
            var gauge = CompyaUiKit.Place(p, "Gauge", 470, 858, 778, 880);
            CompyaUiKit.Paint(gauge, new Color(0.1f, 0.1f, 0.13f));
            gaugeAway = CompyaUiKit.Norm(gauge, "Away", 0f, 0f, 0.5f, 1f);
            gaugeAwayImage = CompyaUiKit.Paint(gaugeAway, Color.blue);
            gaugeHome = CompyaUiKit.Norm(gauge, "Home", 0.5f, 0f, 1f, 1f);
            gaugeHomeImage = CompyaUiKit.Paint(gaugeHome, Color.red);
            gaugeMarker = CompyaUiKit.Norm(gauge, "Marker", 0.495f, -0.25f, 0.505f, 1.25f);
            CompyaUiKit.Paint(gaugeMarker, new Color(0.45f, 0.95f, 1f));

            awayScoreText = kit.Label(p, "AwayScore", "0", 340, 895, 480, 1005, 128, TextAnchor.MiddleCenter, White, true);
            homeScoreText = kit.Label(p, "HomeScore", "0", 768, 895, 908, 1005, 128, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Polygon(p, "CountBox", 478, 898, 772, 955, new Color(0.11f, 0.11f, 0.13f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.93f, 0f), new Vector2(0.07f, 0f));
            countText = kit.Label(p, "Count", "0-0", 500, 898, 618, 955, 46, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Box(p, "CountDivider", 621, 908, 624, 946, new Color(0.62f, 0.62f, 0.66f));
            outsText = kit.Label(p, "Outs", "0 OUTS", 630, 898, 765, 955, 46, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Polygon(p, "InningBox", 518, 955, 732, 1005, new Color(0.87f, 0.88f, 0.9f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.92f, 0f), new Vector2(0.08f, 0f));
            inningText = kit.Label(p, "Inning", "1회초", 518, 955, 732, 1005, 42, TextAnchor.MiddleCenter, Ink);
            awaySlash = CompyaUiKit.Polygon(p, "AwaySlash", 476, 898, 552, 1005, new Color(1f, 0.66f, 0.12f),
                new Vector2(0f, 1f), new Vector2(0.12f, 1f), new Vector2(1f, 0f), new Vector2(0.86f, 0f));
            homeSlash = CompyaUiKit.Polygon(p, "HomeSlash", 698, 898, 774, 1005, new Color(1f, 0.66f, 0.12f),
                new Vector2(0.88f, 1f), new Vector2(1f, 1f), new Vector2(0.14f, 0f), new Vector2(0f, 0f));

            // 하단 2분할 + 타순
            CompyaUiKit.Box(p, "LowerBg", 0, 1005, 1248, 1890, PanelGray);
            CompyaUiKit.Box(p, "LeftPanel", 10, 1015, 612, 1888, White);
            CompyaUiKit.Box(p, "RightPanel", 636, 1015, 1238, 1888, White);
            CompyaUiKit.Box(p, "Divider", 612, 1005, 636, 1890, new Color(0.08f, 0.08f, 0.1f));
            leftInfo = BuildInfoBox(p, "LeftInfo", 0f);
            rightInfo = BuildInfoBox(p, "RightInfo", 630f);
            for (int i = 0; i < 9; i++)
            {
                leftRows[i] = BuildLineupRow(p, $"LeftRow{i + 1}", 0f, 1357 + i * 63);
                rightRows[i] = BuildLineupRow(p, $"RightRow{i + 1}", 630f, 1357 + i * 63);
            }

            CompyaUiKit.Box(p, "BottomBar", 0, 1890, 1248, 1972, new Color(0.6f, 0.62f, 0.67f));
            kit.Button(p, "SkipButton", "▶▶", 845, 1897, 1035, 1967, new Color(0.22f, 0.25f, 0.32f), White, 40).onClick.AddListener(OnSkipPressed);
            pauseButton = kit.Button(p, "PauseButton", "II", 1045, 1897, 1235, 1967, new Color(0.22f, 0.25f, 0.32f), White, 52);
            pauseButton.onClick.AddListener(OnPausePressed);
            return p.gameObject;
        }

        private void BuildScorebug(RectTransform p)
        {
            awayRowBg = kit.GradientBox(p, "AwayRow", 160, 110, 460, 172, Color.blue, Color.blue, true);
            homeRowBg = kit.GradientBox(p, "HomeRow", 160, 175, 460, 235, Color.red, Color.red, true);
            awayBatMark = CompyaUiKit.Box(p, "AwayBatMark", 160, 112, 167, 170, new Color(1f, 0.66f, 0.12f));
            homeBatMark = CompyaUiKit.Box(p, "HomeBatMark", 160, 177, 167, 233, new Color(1f, 0.66f, 0.12f));
            awayRowLogo = CompyaUiKit.Logo(p, "AwayRowLogo", 175, 114, 240, 168);
            homeRowLogo = CompyaUiKit.Logo(p, "HomeRowLogo", 175, 179, 240, 231);
            awayRowName = kit.Label(p, "AwayRowName", "", 252, 110, 455, 172, 40, TextAnchor.MiddleLeft, White, true);
            homeRowName = kit.Label(p, "HomeRowName", "", 252, 175, 455, 235, 40, TextAnchor.MiddleLeft, White, true);
            awayUserBadge = UserBadge(p, "AwayUserBadge", 168, 102, 198, 140);
            homeUserBadge = UserBadge(p, "HomeUserBadge", 168, 167, 198, 205);

            CompyaUiKit.Box(p, "AwayLine", 462, 112, 922, 160, new Color(0.96f, 0.96f, 0.97f));
            CompyaUiKit.Box(p, "InningStrip", 462, 160, 922, 187, new Color(0.11f, 0.12f, 0.15f));
            CompyaUiKit.Box(p, "HomeLine", 462, 187, 922, 235, new Color(0.96f, 0.96f, 0.97f));
            for (int i = 0; i < 12; i++)
            {
                float x0 = 462 + i * 38.33f, x1 = x0 + 38.33f;
                kit.Label(p, $"No{i + 1}", (i + 1).ToString(), x0, 160, x1, 187, 22, TextAnchor.MiddleCenter, new Color(0.72f, 0.73f, 0.78f));
                awayCells[i] = kit.Label(p, $"A{i + 1}", "", x0, 112, x1, 160, 42, TextAnchor.MiddleCenter, Ink, true);
                homeCells[i] = kit.Label(p, $"H{i + 1}", "", x0, 187, x1, 235, 42, TextAnchor.MiddleCenter, Ink, true);
            }
            CompyaUiKit.Box(p, "Rheb", 922, 110, 1090, 237, new Color(0.04f, 0.04f, 0.06f));
            var heads = new[] { "R", "H", "E", "B" };
            for (int i = 0; i < 4; i++)
            {
                float x0 = 922 + i * 42f, x1 = x0 + 42f;
                kit.Label(p, $"Head{heads[i]}", heads[i], x0, 160, x1, 187, 22, TextAnchor.MiddleCenter, i == 0 ? Gold : new Color(0.72f, 0.73f, 0.78f), true);
                awayRheb[i] = kit.Label(p, $"AR{i}", "0", x0, 112, x1, 160, 46, TextAnchor.MiddleCenter, White, true);
                homeRheb[i] = kit.Label(p, $"HR{i}", "0", x0, 187, x1, 235, 46, TextAnchor.MiddleCenter, White, true);
            }
        }

        private PolygonGraphic UserBadge(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var badge = CompyaUiKit.Polygon(parent, name, x0, y0, x1, y1, AccentRed,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.25f), new Vector2(0.5f, 0f), new Vector2(0f, 0.25f));
            kit.LabelOn(CompyaUiKit.Norm(badge.rectTransform, "P", 0f, 0.2f, 1f, 1f), "P", (y1 - y0) * 0.62f, TextAnchor.MiddleCenter, White, true);
            return badge;
        }

        /// <summary>반복과제 진행 바("N개 달성" 보라 탭 + 흰 트랙 + 0/35/60 체크).</summary>
        private ProgressWidget BuildProgress(Transform p, float x, float y)
        {
            var w = new ProgressWidget();
            CompyaUiKit.Box(p, "ProgressTrack", x, y, x + 915, y + 57, new Color(0.95f, 0.95f, 0.97f));
            CompyaUiKit.Box(p, "ProgressLine", x + 162, y + 15, x + 910, y + 23, new Color(0.12f, 0.13f, 0.18f));
            var fillRect = CompyaUiKit.Place(p, "ProgressFill", x + 162, y + 13, x + 910, y + 25);
            kit.Gradient(fillRect, new Color(0.5f, 0.3f, 0.95f), new Color(0.35f, 0.85f, 0.98f), true);
            w.Fill = fillRect;
            w.FillMinX = (x + 162) / CompyaUiKit.RefWidth;
            w.FillMaxX = (x + 910) / CompyaUiKit.RefWidth;
            CompyaUiKit.Polygon(p, "ProgressTab", x, y, x + 168, y + 43, new Color(0.3f, 0.18f, 0.72f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.94f, 0f), new Vector2(0f, 0f));
            w.Count = kit.Label(p, "ProgressCount", "", x + 8, y, x + 160, y + 43, 34, TextAnchor.MiddleCenter, White, true);
            var marks = new[] { (x + 80f, "0"), (x + 528f, "35"), (x + 893f, "60") };
            for (int i = 0; i < 3; i++)
            {
                float cx = marks[i].Item1;
                if (i > 0)
                {
                    CompyaUiKit.Box(p, $"Mark{marks[i].Item2}", cx - 27, y + 37, cx + 27, y + 62, new Color(0.08f, 0.13f, 0.4f));
                    kit.Label(p, $"MarkText{marks[i].Item2}", marks[i].Item2, cx - 27, y + 37, cx + 27, y + 62, 26, TextAnchor.MiddleCenter, White, true);
                }
                w.Checks[i] = Check(p, $"Check{i}", cx - 30, y + 14, cx + 32, y + 64);
            }
            return w;
        }

        private static void SetProgress(ProgressWidget w, int count)
        {
            w.Count.text = $"<color=#7CFF9C>{count}</color>개 달성";
            w.Fill.anchorMax = new Vector2(Mathf.Lerp(w.FillMinX, w.FillMaxX, Mathf.Clamp01(count / 60f)), w.Fill.anchorMax.y);
            w.Checks[0].SetActive(true);
            w.Checks[1].SetActive(count >= 35);
            w.Checks[2].SetActive(count >= 60);
        }

        private InfoBox BuildInfoBox(Transform p, string name, float dx)
        {
            var box = new InfoBox();
            var header = CompyaUiKit.Place(p, name + "Header", 14 + dx, 1027, 605 + dx, 1082);
            box.HeaderBg = kit.Gradient(header, Color.gray, Color.gray, true);
            box.HeaderText = kit.Label(p, name + "HeaderText", "", 24 + dx, 1027, 600 + dx, 1082, 40, TextAnchor.MiddleLeft, White, true);
            CompyaUiKit.Box(p, name + "Border", 14 + dx, 1095, 604 + dx, 1306, new Color(0.74f, 0.76f, 0.81f));
            CompyaUiKit.Box(p, name + "Inner", 16 + dx, 1097, 602 + dx, 1304, White);
            box.Card = BuildCard(p, name + "Card", 18 + dx, 1100, 190 + dx, 1302, false);
            CompyaUiKit.Box(p, name + "NameBar", 200 + dx, 1100, 596 + dx, 1141, new Color(0.31f, 0.33f, 0.39f));
            box.NameText = kit.Label(p, name + "Name", "", 205 + dx, 1100, 592 + dx, 1141, 36, TextAnchor.MiddleCenter, White, true);
            for (int r = 0; r < 3; r++)
            {
                float y0 = 1146 + r * 50f;
                box.RowLabels[r] = kit.Label(p, $"{name}RowLabel{r}", "", 207 + dx, y0, 360 + dx, y0 + 48, 34, TextAnchor.MiddleLeft, Ink);
                box.RowValues[r] = kit.Label(p, $"{name}RowValue{r}", "", 360 + dx, y0, 596 + dx, y0 + 48, 54, TextAnchor.MiddleRight, Ink, true);
            }
            for (int b = 0; b < 6; b++)
            {
                int row = b / 3, col = b % 3;
                float x0 = 330 + col * 90f + dx, y0 = 1198 + row * 52f;
                box.Badges[b] = CompyaUiKit.Box(p, $"{name}Badge{b}", x0, y0, x0 + 85, y0 + 45, BadgeBlue);
                box.BadgeTexts[b] = kit.Label(p, $"{name}BadgeText{b}", "", x0, y0, x0 + 85, y0 + 45, 28, TextAnchor.MiddleCenter, White, true);
            }
            return box;
        }

        private LineupRow BuildLineupRow(Transform p, string name, float dx, float cy)
        {
            var row = new LineupRow();
            row.Highlight = CompyaUiKit.Polygon(p, name + "Highlight", 4 + dx, cy - 27, 612 + dx, cy + 25, Color.white,
                new Vector2(0f, 0.5f), new Vector2(0.035f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0.035f, 0f));
            row.Highlight.SetVerticalGradient(new Color(0.42f, 0.38f, 1f), new Color(0.2f, 0.22f, 0.8f));
            row.Badge = CompyaUiKit.Box(p, name + "Badge", 20 + dx, cy - 17, 290 + dx, cy + 17, BadgeBlue);
            row.BadgeText = kit.Label(p, name + "BadgeText", "", 20 + dx, cy - 19, 290 + dx, cy + 19, 30, TextAnchor.MiddleCenter, White, true);
            row.Position = kit.Label(p, name + "Pos", "", 300 + dx, cy - 25, 348 + dx, cy + 25, 34, TextAnchor.MiddleLeft, Muted);
            row.Name = kit.Label(p, name + "Name", "", 348 + dx, cy - 25, 494 + dx, cy + 25, 36, TextAnchor.MiddleLeft, Ink);
            row.Average = kit.Label(p, name + "Avg", "", 490 + dx, cy - 25, 548 + dx, cy + 25, 32, TextAnchor.MiddleRight, Ink);
            row.OvrBox = CompyaUiKit.Box(p, name + "OvrBox", 557 + dx, cy - 22, 604 + dx, cy + 22, new Color(0.19f, 0.23f, 0.37f));
            row.Ovr = kit.Label(p, name + "Ovr", "", 557 + dx, cy - 22, 604 + dx, cy + 22, 36, TextAnchor.MiddleCenter, White, true);
            return row;
        }

        /// <summary>미니 선수 카드(OVR 큰 숫자 좌상단 · 포지션 · 별 · 구단 로고 · 등급 칩 · 초상화). 레퍼런스 AT BAT/ON THE MOUND 카드.</summary>
        private MiniCard BuildCard(Transform parent, string name, float x0, float y0, float x1, float y1, bool nameStrip)
        {
            float h = y1 - y0;
            var c = new MiniCard { Root = CompyaUiKit.Place(parent, name, x0, y0, x1, y1) };
            c.Background = kit.Gradient(CompyaUiKit.Fill(c.Root, "Bg"), Color.gray, Color.black, false);
            c.Watermark = LogoIn(c.Root, "Watermark", 0.1f, 0.12f, 0.9f, 0.7f);
            c.Portrait = CompyaUiKit.Fill(c.Root, "Portrait").gameObject.AddComponent<RawImage>();
            c.Portrait.raycastTarget = false;
            c.Portrait.color = new Color(0f, 0f, 0f, 0f);
            kit.Gradient(CompyaUiKit.Norm(c.Root, "Shade", 0f, 0.62f, 1f, 1f), new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f), false);
            c.Ovr = kit.LabelOn(CompyaUiKit.Norm(c.Root, "Ovr", 0.04f, 0.7f, 0.6f, 0.92f), "", h * 0.22f, TextAnchor.UpperLeft, White, true);
            CompyaUiKit.Outline(c.Ovr, new Color(0f, 0f, 0f, 0.6f), 1.5f);
            c.Position = kit.LabelOn(CompyaUiKit.Norm(c.Root, "Pos", 0.05f, 0.57f, 0.5f, 0.71f), "", h * 0.1f, TextAnchor.UpperLeft, new Color(0.55f, 1f, 0.75f), true);
            CompyaUiKit.Outline(c.Position, new Color(0f, 0f, 0f, 0.7f), 1.2f);
            c.Stars = kit.LabelOn(CompyaUiKit.Norm(c.Root, "Stars", 0.25f, 0.9f, 0.98f, 1f), "", h * 0.075f, TextAnchor.MiddleCenter, Gold, true);
            c.TeamLogo = LogoIn(c.Root, "TeamLogo", 0.66f, 0.7f, 0.97f, 0.88f);
            c.GradeChip = kit.LabelOn(CompyaUiKit.Norm(c.Root, "Grade", 0.04f, nameStrip ? 0.15f : 0.03f, 0.6f, nameStrip ? 0.23f : 0.12f), "", h * 0.06f, TextAnchor.MiddleLeft, White, true, true);
            CompyaUiKit.Outline(c.GradeChip, new Color(0f, 0f, 0f, 0.7f), 1f);
            if (nameStrip)
            {
                CompyaUiKit.Paint(CompyaUiKit.Norm(c.Root, "NameStrip", 0f, 0f, 1f, 0.14f), new Color(0f, 0f, 0f, 0.62f));
                c.Name = kit.LabelOn(CompyaUiKit.Norm(c.Root, "Name", 0.04f, 0f, 0.96f, 0.14f), "", h * 0.075f, TextAnchor.MiddleRight, White, true);
            }
            return c;
        }

        private static RawImage LogoIn(RectTransform parent, string name, float x0, float y0, float x1, float y1)
        {
            var rect = CompyaUiKit.Norm(parent, name, x0, y0, x1, y1);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            return raw;
        }

        private void FillCard(MiniCard c, Player player)
        {
            if (c == null) return;
            if (player?.Template == null)
            {
                c.Root.gameObject.SetActive(false);
                return;
            }
            c.Root.gameObject.SetActive(true);
            var template = player.Template;
            var (top, bottom) = GradeColors(template.Grade, template.Team);
            c.Background.texture = kit.GradientTexture(top, bottom, false);
            c.Ovr.text = Ovr(player).ToString();
            c.Position.text = PositionLabel(player);
            c.Stars.text = new string('★', Mathf.Clamp(player.StarLevel, 1, 6));
            c.Stars.color = StarColor(player.CurrentStarType);
            c.GradeChip.text = GradeShort(template.Grade);
            if (c.Name != null) c.Name.text = DisplayName(player);
            CompyaUiKit.SetLogo(c.TeamLogo, template.Team);
            CompyaUiKit.SetLogo(c.Watermark, template.Team, 0.22f);

            var portrait = LoadPortrait(template.TemplateId);
            c.Portrait.texture = portrait;
            c.Portrait.color = portrait != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            c.Watermark.enabled = portrait == null;
        }

        /// <summary>PlayerCardUI와 같은 규칙(Portraits/{Team}/{Year}/{TemplateId}) - 없으면 null.</summary>
        private static Texture2D LoadPortrait(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;
            var parts = templateId.Split('_');
            if (parts.Length >= 2)
            {
                var nested = Resources.Load<Texture2D>($"Portraits/{parts[0]}/{parts[1]}/{templateId}");
                if (nested != null) return nested;
            }
            return Resources.Load<Texture2D>($"Portraits/{templateId}");
        }

        private static (Color, Color) GradeColors(Grade grade, Team team)
        {
            switch (grade)
            {
                case Grade.LIVE_EPIC: return (new Color(0.38f, 0.4f, 0.62f), new Color(0.13f, 0.14f, 0.28f));
                case Grade.ALLSTAR: return (new Color(0.58f, 0.36f, 0.9f), new Color(0.22f, 0.1f, 0.42f));
                case Grade.FRANCHISE: return (new Color(0.75f, 0.5f, 0.3f), new Color(0.32f, 0.18f, 0.1f));
                case Grade.TITLE_HOLDER: return (new Color(0.82f, 0.84f, 0.88f), new Color(0.38f, 0.4f, 0.46f));
                case Grade.RETIRED_NUMBER: return (new Color(0.3f, 0.3f, 0.33f), new Color(0.04f, 0.04f, 0.05f));
                case Grade.GOLDEN_GLOVE: return (new Color(0.95f, 0.78f, 0.35f), new Color(0.45f, 0.3f, 0.08f));
                case Grade.SIGNATURE: return (new Color(0.86f, 0.9f, 0.98f), new Color(0.35f, 0.42f, 0.6f));
                case Grade.DYNASTY:
                    var tc = CompyaUiKit.TeamColor(team);
                    return (Color.Lerp(tc, Color.white, 0.35f), CompyaUiKit.Darken(tc, 0.45f));
                default: return (new Color(0.42f, 0.45f, 0.52f), new Color(0.12f, 0.13f, 0.17f));
            }
        }

        private static Color StarColor(StarType type)
        {
            switch (type)
            {
                case StarType.PURPLE: return new Color(0.85f, 0.45f, 1f);
                case StarType.SILVER: return new Color(0.9f, 0.92f, 0.96f);
                case StarType.GOLD: return Gold;
                case StarType.PLATINUM: return new Color(0.75f, 0.92f, 1f);
                case StarType.BRONZE: return new Color(0.95f, 0.6f, 0.35f);
                case StarType.BLACK: return new Color(0.9f, 0.9f, 0.9f);
                case StarType.TEAM_COLOR: return new Color(1f, 0.4f, 0.4f);
                default: return new Color(0.45f, 0.7f, 1f);
            }
        }

        private static string GradeShort(Grade grade)
        {
            switch (grade)
            {
                case Grade.LIVE_NORMAL: return "LIVE";
                case Grade.LIVE_EPIC: return "LIVE EPIC";
                case Grade.ALLSTAR: return "ALL STAR";
                case Grade.FRANCHISE: return "FRANCHISE";
                case Grade.TITLE_HOLDER: return "TITLE HOLDER";
                case Grade.RETIRED_NUMBER: return "RETIRED";
                case Grade.GOLDEN_GLOVE: return "GOLDEN GLOVE";
                case Grade.SIGNATURE: return "SIGNATURE";
                case Grade.DYNASTY: return "DYNASTY";
                default: return grade.ToString();
            }
        }

        // ---- 중계 갱신

        private void ApplyTeamsToRelay()
        {
            var awayColor = CompyaUiKit.TeamColor(awayTeam);
            var homeColor = CompyaUiKit.TeamColor(homeTeam);
            awayRowBg.texture = kit.GradientTexture(Color.Lerp(awayColor, Color.white, 0.12f), CompyaUiKit.Darken(awayColor, 0.85f), true);
            homeRowBg.texture = kit.GradientTexture(Color.Lerp(homeColor, Color.white, 0.12f), CompyaUiKit.Darken(homeColor, 0.85f), true);
            awayRowName.text = CompyaUiKit.ShortName(awayTeam);
            homeRowName.text = CompyaUiKit.ShortName(homeTeam);
            CompyaUiKit.SetLogo(awayRowLogo, awayTeam);
            CompyaUiKit.SetLogo(homeRowLogo, homeTeam);
            CompyaUiKit.SetLogo(awayBigLogo, awayTeam);
            CompyaUiKit.SetLogo(homeBigLogo, homeTeam);
            awayBand.color = awayColor;
            homeBand.color = homeColor;
            gaugeAwayImage.color = Color.Lerp(awayColor, new Color(0.15f, 0.35f, 0.95f), 0.25f);
            gaugeHomeImage.color = Color.Lerp(homeColor, new Color(0.65f, 0.12f, 0.2f), 0.25f);
            var user = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            awayUserBadge.gameObject.SetActive(awayTeam == user);
            homeUserBadge.gameObject.SetActive(homeTeam == user);
        }

        private void RefreshRelay(CompyaGameTracker t, int index)
        {
            for (int i = 0; i < 12; i++)
            {
                awayCells[i].text = i < t.Away.Runs.Count ? t.Away.Runs[i].ToString() : "";
                homeCells[i].text = i < t.Home.Runs.Count ? t.Home.Runs[i].ToString() : "";
            }
            SetRheb(awayRheb, t.Away);
            SetRheb(homeRheb, t.Home);

            var next = PeekNextAtBat(index);
            bool top = next != null ? next.IsTopHalf : t.IsTopHalf;
            awayBatMark.enabled = top;
            homeBatMark.enabled = !top;
            awaySlash.enabled = top;
            homeSlash.enabled = !top;

            awayScoreText.text = t.AwayScore.ToString();
            homeScoreText.text = t.HomeScore.ToString();
            float pAway = AwayWinProbability(t);
            int awayPct = Mathf.RoundToInt(pAway * 100f);
            awayPctText.text = $"{awayPct}%";
            homePctText.text = $"{100 - awayPct}%";
            gaugeAway.anchorMax = new Vector2(pAway, 1f);
            gaugeHome.anchorMin = new Vector2(pAway, 0f);
            gaugeMarker.anchorMin = new Vector2(pAway - 0.006f, -0.25f);
            gaugeMarker.anchorMax = new Vector2(pAway + 0.006f, 1.25f);

            countText.text = $"{t.Balls}-{Mathf.Min(t.Strikes, 2)}";
            outsText.text = $"{Mathf.Min(t.Outs, 3)} <size={Mathf.RoundToInt(30 * 0.9f)}>OUTS</size>";
            int inning = next != null ? next.Inning : t.Inning;
            inningText.text = InningLabel(inning, top);

            var batter = next?.Batter ?? t.LastAtBat?.Batter;
            var pitcher = next?.Pitcher ?? t.LastAtBat?.Pitcher;
            FillAtBat(top ? leftInfo : rightInfo, batter, t);
            FillMound(top ? rightInfo : leftInfo, pitcher, t);
            FillLineup(leftRows, awayOrder, t, top ? batter : null);
            FillLineup(rightRows, homeOrder, t, top ? null : batter);
            SetProgress(relayProgress, AchievementCount(t));
        }

        private static void SetRheb(Text[] cells, CompyaGameTracker.TeamLine line)
        {
            cells[0].text = line.R.ToString();
            cells[1].text = line.H.ToString();
            cells[2].text = "0";
            cells[3].text = line.B.ToString();
        }

        private void FillAtBat(InfoBox box, Player batter, CompyaGameTracker t)
        {
            box.HeaderBg.texture = kit.GradientTexture(new Color(0.78f, 0.18f, 0.2f), new Color(0.62f, 0.62f, 0.68f), true);
            box.HeaderText.text = "AT BAT";
            FillCard(box.Card, batter);
            box.NameText.text = batter != null ? $"{PositionLabel(batter)} {DisplayName(batter)}" : "";
            var line = t.BatOf(batter);
            box.RowLabels[0].text = "타율";
            box.RowValues[0].text = CompyaGameTracker.FormatAverage(line.Average);
            for (int r = 1; r < 3; r++) { box.RowLabels[r].text = ""; box.RowValues[r].text = ""; }

            var recent = line.Badges.Skip(Mathf.Max(0, line.Badges.Count - 6)).ToList();
            for (int b = 0; b < 6; b++)
            {
                bool on = b < recent.Count;
                box.Badges[b].enabled = on;
                box.BadgeTexts[b].text = on ? recent[b].Label : "";
                if (on) box.Badges[b].color = recent[b].Positive ? BadgeBlue : BadgeRed;
            }
        }

        private void FillMound(InfoBox box, Player pitcher, CompyaGameTracker t)
        {
            box.HeaderBg.texture = kit.GradientTexture(new Color(0.33f, 0.35f, 0.42f), new Color(0.63f, 0.65f, 0.71f), true);
            box.HeaderText.text = "ON THE MOUND";
            FillCard(box.Card, pitcher);
            box.NameText.text = pitcher != null ? $"{PositionLabel(pitcher)} {DisplayName(pitcher)}" : "";
            var line = t.PitchOf(pitcher);
            box.RowLabels[0].text = "이닝";
            box.RowValues[0].text = CompyaGameTracker.FormatInnings(line.Outs);
            box.RowLabels[1].text = "투구수";
            box.RowValues[1].text = line.Pitches.ToString();
            box.RowLabels[2].text = "탈삼진";
            box.RowValues[2].text = line.Strikeouts.ToString();
            for (int b = 0; b < 6; b++) { box.Badges[b].enabled = false; box.BadgeTexts[b].text = ""; }
        }

        private void FillLineup(LineupRow[] rows, List<Player> order, CompyaGameTracker t, Player current)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var player = i < order.Count ? order[i] : null;
                bool highlighted = player != null && player == current;
                row.Highlight.enabled = highlighted;
                var textColor = highlighted ? White : Ink;
                row.Position.text = player != null ? PositionLabel(player) : "";
                row.Position.color = highlighted ? White : Muted;
                row.Name.text = player != null ? DisplayName(player) : "";
                row.Name.color = textColor;
                var line = t.BatOf(player);
                row.Average.text = player != null ? CompyaGameTracker.FormatAverage(line.Average) : "";
                row.Average.color = textColor;
                row.Ovr.text = player != null ? Ovr(player).ToString() : "";
                row.OvrBox.enabled = player != null;

                bool hasResult = player != null && line.Badges.Count > 0 && !highlighted;
                row.Badge.enabled = hasResult;
                row.BadgeText.text = hasResult ? line.Badges[line.Badges.Count - 1].Label : "";
                if (hasResult) row.Badge.color = line.Badges[line.Badges.Count - 1].Positive ? BadgeBlue : BadgeRed;
            }
        }

        private void ShowToast(PlayEvent evt, CompyaGameTracker t)
        {
            toastRoot.gameObject.SetActive(true);
            toastText.text = evt.Tactic == MatchTactic.Bunt && evt.Result == AtBatResult.Groundout ? "희생번트" : CompyaGameTracker.ResultLabel(evt.Result);
            toastValue.text = t.PitchOf(evt.Pitcher).Pitches.ToString();
        }

        // ---- 타구 궤적(홈 -> 낙구 지점 파란 포물선 + 바운드)

        private static readonly Vector2 HomePlate = new Vector2(622f, 730f);
        private static readonly Vector2 LeftFoulEnd = new Vector2(0f, 440f);
        private static readonly Vector2 CenterFence = new Vector2(622f, 330f);
        private static readonly Vector2 RightFoulEnd = new Vector2(1248f, 440f);

        /// <summary>그라운드 텍스처(1248x850) 기준 필드 좌표: angle -45(3루 파울라인) ~ 45(1루 파울라인), dist 1 = 펜스.</summary>
        private static Vector2 FieldPoint(float angle, float dist)
        {
            var center = CenterFence - HomePlate;
            var v = angle < 0f
                ? Vector2.Lerp(center, LeftFoulEnd - HomePlate, -angle / 45f)
                : Vector2.Lerp(center, RightFoulEnd - HomePlate, angle / 45f);
            return HomePlate + v * dist;
        }

        private void ClearArc()
        {
            if (arcRoutine != null) StopCoroutine(arcRoutine);
            arcRoutine = null;
            arcGlow.Clear();
            arcCore.Clear();
        }

        private void DrawTrajectory(PlayEvent evt, int index)
        {
            ClearArc();
            if (evt.Result == AtBatResult.Strikeout || evt.Result == AtBatResult.Walk) return;

            var rng = new System.Random(index * 7919 + 13);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Side() => rng.NextDouble() < 0.5 ? -1f : 1f;

            float angle, dist, height, bounce;
            switch (evt.Result)
            {
                case AtBatResult.Groundout: angle = Range(-40, 40); dist = Range(0.28f, 0.45f); height = 14; bounce = 0f; break;
                case AtBatResult.Single: angle = Range(-38, 38); dist = Range(0.55f, 0.72f); height = 70; bounce = 0.08f; break;
                case AtBatResult.Double: angle = Side() * Range(18, 42); dist = Range(0.82f, 0.95f); height = 120; bounce = 0.05f; break;
                case AtBatResult.Triple: angle = Side() * Range(24, 43); dist = Range(0.9f, 0.98f); height = 140; bounce = 0.03f; break;
                case AtBatResult.HomeRun: angle = Range(-35, 35); dist = Range(1.12f, 1.3f); height = 260; bounce = 0f; break;
                default: angle = Range(-40, 40); dist = Range(0.6f, 0.92f); height = 200; bounce = 0f; break; // Flyout
            }

            var pts = new List<Vector2>();
            var land = FieldPoint(angle, dist);
            if (evt.Result == AtBatResult.Groundout)
            {
                var mid = FieldPoint(angle, dist * 0.55f);
                AppendArc(pts, HomePlate, mid, height);
                AppendArc(pts, mid, land, height * 0.6f);
            }
            else
            {
                AppendArc(pts, HomePlate, land, height);
                if (bounce > 0f) AppendArc(pts, land, FieldPoint(angle, Mathf.Min(1f, dist + bounce)), 18f);
            }

            var size = arcRect.rect.size;
            for (int i = 0; i < pts.Count; i++)
                pts[i] = new Vector2((pts[i].x / 1248f - 0.5f) * size.x, (0.5f - pts[i].y / 850f) * size.y);
            arcGlow.SetPoints(pts);
            arcCore.SetPoints(pts);
            arcRoutine = StartCoroutine(AnimateArc());
        }

        private static void AppendArc(List<Vector2> pts, Vector2 from, Vector2 to, float height)
        {
            var control = (from + to) * 0.5f + new Vector2(0f, -height * 2f); // 레퍼런스 px는 y가 아래로 증가
            const int steps = 24;
            for (int i = pts.Count == 0 ? 0 : 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                pts.Add((1 - t) * (1 - t) * from + 2 * (1 - t) * t * control + t * t * to);
            }
        }

        private System.Collections.IEnumerator AnimateArc()
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
            {
                arcGlow.Progress = t;
                arcCore.Progress = t;
                yield return null;
            }
            arcGlow.Progress = 1f;
            arcCore.Progress = 1f;
            arcRoutine = null;
        }

        // ------------------------------------------------------------------ 3. 이닝 종료 중간 화면

        private GameObject BuildSplash()
        {
            var p = NewScreen("InningSplash", "Broadcast179/bg_round_tiles", new Color(0.1f, 0.1f, 0.12f));
            splashTitle = kit.Label(p, "Title", "ROUND 1", 324, 500, 924, 620, 96, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Box(p, "Underline", 474, 632, 774, 639, new Color(0.17f, 0.3f, 0.72f));
            splashAwayLogo = CompyaUiKit.Logo(p, "AwayLogo", 150, 700, 560, 1020);
            splashHomeLogo = CompyaUiKit.Logo(p, "HomeLogo", 688, 700, 1098, 1020);
            splashAwayScore = kit.Label(p, "AwayScore", "0", 330, 1040, 590, 1300, 230, TextAnchor.MiddleCenter, White, true);
            kit.Label(p, "Colon", ":", 590, 1060, 658, 1270, 150, TextAnchor.MiddleCenter, White, true);
            splashHomeScore = kit.Label(p, "HomeScore", "0", 658, 1040, 918, 1300, 230, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Shadow(splashAwayScore, new Color(0.05f, 0.08f, 0.3f, 0.9f), 6f);
            CompyaUiKit.Shadow(splashHomeScore, new Color(0.05f, 0.08f, 0.3f, 0.9f), 6f);
            splashAwayName = kit.Label(p, "AwayName", "", 280, 1305, 640, 1360, 36, TextAnchor.MiddleCenter, White);
            splashHomeName = kit.Label(p, "HomeName", "", 608, 1305, 968, 1360, 36, TextAnchor.MiddleCenter, White);
            return p.gameObject;
        }

        private void FillSplash(CompyaGameTracker t, int inning)
        {
            splashTitle.text = $"ROUND {inning}";
            CompyaUiKit.SetLogo(splashAwayLogo, awayTeam);
            CompyaUiKit.SetLogo(splashHomeLogo, homeTeam);
            splashAwayScore.text = t.AwayScore.ToString();
            splashHomeScore.text = t.HomeScore.ToString();
            splashAwayName.text = CompyaUiKit.FullName(awayTeam);
            splashHomeName.text = CompyaUiKit.FullName(homeTeam);
        }

        // ------------------------------------------------------------------ 4. 승부처 직접 플레이 선택

        private GameObject BuildChoice()
        {
            var p = NewScreen("DirectChoice", "Broadcast179/bg_highlight_navy", new Color(0.04f, 0.07f, 0.2f));
            Slash(p, "SlashBlue", 388, 430, 432, 490, new Color(0.3f, 0.65f, 1f));
            Slash(p, "SlashPink", 818, 400, 862, 460, new Color(0.95f, 0.25f, 0.6f));
            choiceTitle = kit.Label(p, "Title", "하이라이트", 440, 385, 810, 470, 86, TextAnchor.MiddleCenter, White, true, true);
            CompyaUiKit.Shadow(choiceTitle, new Color(0f, 0f, 0f, 0.5f));
            choiceSubtitle = kit.Label(p, "Sub", "HIGHLIGHT", 500, 465, 750, 505, 34, TextAnchor.MiddleCenter, new Color(0.78f, 0.8f, 0.86f), true, true);
            CompyaUiKit.Outline(choiceSubtitle, new Color(0f, 0f, 0f, 0.7f), 1.5f);

            choiceBatterCard = BuildCard(p, "BatterCard", 160, 693, 430, 1117, true);
            UserBadge(p, "UserBadge", 435, 693, 477, 750);
            choicePitcherCard = BuildCard(p, "PitcherCard", 820, 693, 1090, 1117, true);
            choiceLeftScore = kit.Label(p, "LeftScore", "0", 455, 830, 575, 970, 116, TextAnchor.MiddleCenter, White, true);
            kit.Label(p, "VS", "VS", 585, 860, 665, 940, 46, TextAnchor.MiddleCenter, White, true);
            choiceRightScore = kit.Label(p, "RightScore", "0", 675, 830, 795, 970, 116, TextAnchor.MiddleCenter, White, true);

            choiceInning = kit.Label(p, "Inning", "1회초", 410, 1200, 660, 1300, 92, TextAnchor.MiddleCenter, White, true);
            choiceBases[2] = Diamond(p, "Base2", 752, 1228, 62, Gold);
            choiceBases[3] = Diamond(p, "Base3", 707, 1268, 62, Gold);
            choiceBases[1] = Diamond(p, "Base1", 797, 1268, 62, Gold);

            CompyaUiKit.Box(p, "CountPill", 385, 1318, 862, 1382, new Color(0.2f, 0.2f, 0.5f));
            kit.Label(p, "B", "B", 402, 1318, 450, 1382, 50, TextAnchor.MiddleCenter, White, true);
            for (int i = 0; i < 3; i++) choiceBalls[i] = kit.Label(p, $"Ball{i}", "●", 460 + i * 34, 1318, 494 + i * 34, 1382, 40, TextAnchor.MiddleCenter, Color.white);
            kit.Label(p, "S", "S", 578, 1318, 626, 1382, 50, TextAnchor.MiddleCenter, White, true);
            for (int i = 0; i < 2; i++) choiceStrikes[i] = kit.Label(p, $"Strike{i}", "●", 632 + i * 34, 1318, 666 + i * 34, 1382, 40, TextAnchor.MiddleCenter, Color.white);
            kit.Label(p, "O", "O", 712, 1318, 760, 1382, 50, TextAnchor.MiddleCenter, White, true);
            for (int i = 0; i < 2; i++) choiceOuts[i] = kit.Label(p, $"Out{i}", "●", 768 + i * 34, 1318, 802 + i * 34, 1382, 40, TextAnchor.MiddleCenter, Color.white);

            CompyaUiKit.Box(p, "Help", 1035, 1665, 1090, 1722, new Color(0.62f, 0.63f, 0.67f));
            kit.Label(p, "HelpText", "?", 1035, 1665, 1090, 1722, 44, TextAnchor.MiddleCenter, White, true);
            choiceProgress = BuildProgress(p, 160, 1740);
            kit.GradientButton(p, "Skip", "건너 뛰기", 160, 1815, 570, 1935, new Color(0.64f, 0.65f, 0.68f), new Color(0.44f, 0.45f, 0.49f), new Color(0.93f, 0.93f, 0.95f), 52).onClick.AddListener(OnChoiceSkip);
            kit.GradientButton(p, "PlayBall", "PLAY BALL", 590, 1815, 1088, 1935, PurpleTop, PurpleBottom, White, 62, true).onClick.AddListener(OnChoicePlay);
            return p.gameObject;
        }

        private void FillChoice(PlayEvent evt, CompyaGameTracker t)
        {
            // [TASK-KBO-180] 왼쪽 = 우리 선수(공격이면 타자, 수비면 투수), 점수도 우리 팀 기준.
            bool offense = IsUserBatting(evt.IsTopHalf);
            choiceTitle.text = mode == PlayMode.Full ? "풀 플레이" : "하이라이트";
            choiceSubtitle.text = (mode == PlayMode.Full ? "FULL PLAY" : "HIGHLIGHT") + (offense ? " · 공격 찬스" : " · 수비 위기");
            FillCard(choiceBatterCard, offense ? evt.Batter : evt.Pitcher);
            FillCard(choicePitcherCard, offense ? evt.Pitcher : evt.Batter);
            bool userAway = LeagueManager.Instance != null && awayTeam == LeagueManager.Instance.UserTeam;
            int userScore = userAway ? t.AwayScore : t.HomeScore;
            int opponentScore = userAway ? t.HomeScore : t.AwayScore;
            choiceLeftScore.text = userScore.ToString();
            choiceRightScore.text = opponentScore.ToString();
            choiceInning.text = InningLabel(evt.Inning, evt.IsTopHalf);
            var dim = new Color(0.33f, 0.33f, 0.62f);
            for (int b = 1; b <= 3; b++) choiceBases[b].color = t.Bases[b] ? Gold : dim;
            var off = new Color(0.1f, 0.1f, 0.27f);
            foreach (var dot in choiceBalls) dot.color = off;
            foreach (var dot in choiceStrikes) dot.color = off;
            for (int i = 0; i < 2; i++) choiceOuts[i].color = i < t.Outs ? new Color(1f, 0.32f, 0.3f) : off;
            SetProgress(choiceProgress, AchievementCount(t));
        }

        // ------------------------------------------------------------------ 5. 직접 플레이 타석 HUD(대구 삼성 라이온즈 파크)

        private GameObject BuildDirect()
        {
            var p = CompyaUiKit.Fill(root, "DirectPlay");
            CompyaUiKit.Paint(p, new Color(0.05f, 0.05f, 0.07f), true);
            var view = CompyaUiKit.Place(p, "View", 0, 0, 1248, 1338);
            CompyaUiKit.PictureOn(view, "Broadcast179/batter_view");

            // 스트라이크 존(타석 뷰 텍스처의 베이크된 존 프레임 위치와 일치: x 0.338~0.578, y 0.339~0.642)
            var zone = CompyaUiKit.Norm(view, "StrikeZone", 0.338f, 1f - 0.642f, 0.578f, 1f - 0.339f);
            CompyaUiKit.Paint(zone, new Color(0.95f, 0.22f, 0.15f, 0.16f));
            foreach (var (x0, y0, x1, y1) in new[] { (0f, 0f, 1f, 0.012f), (0f, 0.988f, 1f, 1f), (0f, 0f, 0.016f, 1f), (0.984f, 0f, 1f, 1f) })
                CompyaUiKit.Paint(CompyaUiKit.Norm(zone, "Edge", x0, y0, x1, y1), White);
            foreach (var g in new[] { 1f / 3f, 2f / 3f })
            {
                CompyaUiKit.Paint(CompyaUiKit.Norm(zone, "GridV", g - 0.004f, 0f, g + 0.004f, 1f), new Color(1f, 1f, 1f, 0.3f));
                CompyaUiKit.Paint(CompyaUiKit.Norm(zone, "GridH", 0f, g - 0.003f, 1f, g + 0.003f), new Color(1f, 1f, 1f, 0.3f));
            }
            directBall = CompyaUiKit.Norm(view, "Ball", 0.5f, 0.5f, 0.5f, 0.5f);
            directBall.sizeDelta = new Vector2(46f, 46f);
            kit.LabelOn(directBall, "●", 60, TextAnchor.MiddleCenter, White, true);
            directBall.gameObject.SetActive(false);

            // 좌상단 스코어버그(라팍 캡처 배치 - 이닝 / 원정·홈 행 / 다이아몬드 / 카운트)
            CompyaUiKit.Box(p, "Scorebug", 12, 40, 702, 190, new Color(0.07f, 0.08f, 0.1f, 0.93f));
            directInning = kit.Label(p, "Inning", "1\n▲", 12, 40, 70, 190, 40, TextAnchor.MiddleCenter, White, true);
            directAwayRow = kit.GradientBox(p, "AwayRow", 70, 45, 420, 113, Color.red, Color.red, true);
            directHomeRow = kit.GradientBox(p, "HomeRow", 70, 118, 420, 186, Color.blue, Color.blue, true);
            directAwayName = kit.Label(p, "AwayName", "", 92, 45, 415, 113, 40, TextAnchor.MiddleLeft, White, true);
            directHomeName = kit.Label(p, "HomeName", "", 92, 118, 415, 186, 40, TextAnchor.MiddleLeft, White, true);
            CompyaUiKit.Box(p, "AwayScoreBox", 420, 45, 500, 113, White);
            CompyaUiKit.Box(p, "HomeScoreBox", 420, 118, 500, 186, White);
            directAwayScore = kit.Label(p, "AwayScore", "0", 420, 45, 500, 113, 56, TextAnchor.MiddleCenter, Ink, true);
            directHomeScore = kit.Label(p, "HomeScore", "0", 420, 118, 500, 186, 56, TextAnchor.MiddleCenter, Ink, true);
            directBases[2] = Diamond(p, "Base2", 557, 82, 38, Gold);
            directBases[3] = Diamond(p, "Base3", 530, 112, 38, Gold);
            directBases[1] = Diamond(p, "Base1", 584, 112, 38, Gold);
            directCount = kit.Label(p, "Count", "0 - 0", 612, 45, 700, 113, 46, TextAnchor.MiddleCenter, White, true);
            directOuts = kit.Label(p, "Outs", "0 OUTS", 612, 118, 700, 186, 34, TextAnchor.MiddleCenter, White, true);

            // 자막 + 결과 배너(타석 뷰 위). [TASK-KBO-180] 작전 안내 줄(선택 작전 효과).
            directTacticHint = kit.Label(p, "TacticHint", "", 110, 1212, 1138, 1268, 30, TextAnchor.MiddleCenter, Gold, true);
            CompyaUiKit.Outline(directTacticHint, new Color(0f, 0f, 0f, 0.8f), 2f);
            CompyaUiKit.Box(p, "SubtitleBar", 110, 1272, 1138, 1330, new Color(0.08f, 0.08f, 0.1f, 0.85f));
            directSubtitleText = kit.Label(p, "Subtitle", "", 130, 1272, 1120, 1330, 32, TextAnchor.MiddleLeft, White);
            directResultText = kit.Label(p, "ResultBanner", "", 74, 520, 1174, 760, 130, TextAnchor.MiddleCenter, Gold, true, true);
            CompyaUiKit.Outline(directResultText, new Color(0.05f, 0.05f, 0.1f, 0.95f), 4f);
            directResultText.gameObject.SetActive(false);

            // 하단: 타자 카드/스킬/기록 | 투수 카드/스킬/기록 | 작전 버튼 + PLAY BALL
            directBatterCard = BuildCard(p, "BatterCard", 20, 1355, 270, 1730, true);
            CompyaUiKit.Box(p, "OrderChip", 20, 1690, 190, 1728, new Color(0.12f, 0.2f, 0.5f));
            directOrderText = kit.Label(p, "Order", "", 20, 1690, 190, 1728, 30, TextAnchor.MiddleCenter, White, true);
            directPitcherCard = BuildCard(p, "PitcherCard", 978, 1355, 1228, 1730, true);
            for (int i = 0; i < 3; i++)
            {
                float y0 = 1365 + i * 105f;
                CompyaUiKit.Box(p, $"BatterSkill{i}", 285, y0, 610, y0 + 92, new Color(0.18f, 0.15f, 0.32f));
                directBatterSkills[i] = kit.Label(p, $"BatterSkillText{i}", "", 295, y0, 600, y0 + 92, 34, TextAnchor.MiddleCenter, White, true);
                CompyaUiKit.Box(p, $"PitcherSkill{i}", 638, y0, 963, y0 + 92, new Color(0.12f, 0.2f, 0.32f));
                directPitcherSkills[i] = kit.Label(p, $"PitcherSkillText{i}", "", 648, y0, 953, y0 + 92, 34, TextAnchor.MiddleCenter, White, true);
            }
            CompyaUiKit.Box(p, "BatterStatsBar", 20, 1742, 610, 1802, new Color(0.08f, 0.09f, 0.12f));
            directBatterStats = kit.Label(p, "BatterStats", "", 32, 1742, 600, 1802, 32, TextAnchor.MiddleLeft, White, true);
            CompyaUiKit.Box(p, "PitcherStatsBar", 638, 1742, 1228, 1802, new Color(0.08f, 0.09f, 0.12f));
            directPitcherStats = kit.Label(p, "PitcherStats", "", 650, 1742, 1215, 1802, 32, TextAnchor.MiddleLeft, White, true);
            var stamina = CompyaUiKit.Place(p, "StaminaTrack", 638, 1706, 963, 1728);
            CompyaUiKit.Paint(stamina, new Color(0.15f, 0.15f, 0.18f));
            directStamina = CompyaUiKit.Norm(stamina, "Fill", 0f, 0f, 1f, 1f);
            kit.Gradient(directStamina, new Color(0.9f, 0.2f, 0.15f), new Color(0.3f, 0.85f, 0.35f), true);

            // [TASK-KBO-180] 작전 버튼 4개(공격: 강공/컨택/번트/도루, 수비: 정면 승부/투수 교체/고의사구/일반) - 실제 판정에 반영된다.
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                float x0 = 20 + i * 158f;
                tacticButtons[i] = kit.Button(p, $"Tactic{i}", "", x0, 1830, x0 + 150, 1945, new Color(0.18f, 0.19f, 0.23f), White, 34);
                tacticButtons[i].onClick.AddListener(() => SelectTactic(index));
            }
            directPlayButton = kit.GradientButton(p, "PlayBall", "PLAY BALL", 665, 1830, 1228, 1945,
                new Color(0.82f, 0.36f, 1f), new Color(0.6f, 0.14f, 0.95f), White, 64, true);
            directPlayButton.onClick.AddListener(OnDirectPlayBall);
            return p.gameObject;
        }

        private void FillDirect(PlayEvent evt, CompyaGameTracker t)
        {
            directResultText.gameObject.SetActive(false);
            directBall.gameObject.SetActive(false);
            directPlayButton.interactable = true;
            ConfigureTacticButtons(evt, t);

            var awayColor = CompyaUiKit.TeamColor(awayTeam);
            var homeColor = CompyaUiKit.TeamColor(homeTeam);
            directAwayRow.texture = kit.GradientTexture(awayColor, CompyaUiKit.Darken(awayColor, 0.8f), true);
            directHomeRow.texture = kit.GradientTexture(homeColor, CompyaUiKit.Darken(homeColor, 0.8f), true);
            directAwayName.text = CompyaUiKit.ShortName(awayTeam);
            directHomeName.text = CompyaUiKit.ShortName(homeTeam);
            directAwayScore.text = t.AwayScore.ToString();
            directHomeScore.text = t.HomeScore.ToString();
            if (evt == null) return;

            directInning.text = $"{evt.Inning}\n{(evt.IsTopHalf ? "▲" : "▼")}";
            directCount.text = "0 - 0";
            directOuts.text = $"{t.Outs} <size={Mathf.RoundToInt(26 * 0.9f)}>OUTS</size>";
            var dim = new Color(0.3f, 0.3f, 0.36f);
            for (int b = 1; b <= 3; b++) directBases[b].color = t.Bases[b] ? Gold : dim;

            FillCard(directBatterCard, evt.Batter);
            FillCard(directPitcherCard, evt.Pitcher);
            var order = evt.IsTopHalf ? awayOrder : homeOrder;
            int slot = order.IndexOf(evt.Batter);
            directOrderText.text = slot >= 0 ? $"{slot + 1}번 타자" : "대타";
            FillSkills(directBatterSkills, evt.Batter);
            FillSkills(directPitcherSkills, evt.Pitcher);

            var today = t.BatOf(evt.Batter);
            var season = SeasonStatManager.Instance != null && evt.Batter != null ? SeasonStatManager.Instance.GetBatterStats(evt.Batter) : null;
            directBatterStats.text = season != null
                ? $"<color=#6FA8FF>오늘</color> {today.AtBats}타수 {today.Hits}안타  <color=#6FA8FF>타율</color> {CompyaGameTracker.FormatAverage(season.BattingAverage)}  <color=#6FA8FF>홈런</color> {season.HomeRuns}"
                : $"<color=#6FA8FF>오늘</color> {today.AtBats}타수 {today.Hits}안타  <color=#6FA8FF>타율</color> {CompyaGameTracker.FormatAverage(today.Average)}";

            var pitchLine = t.PitchOf(evt.Pitcher);
            var pitcherSeason = SeasonStatManager.Instance != null && evt.Pitcher != null ? SeasonStatManager.Instance.GetPitcherStats(evt.Pitcher) : null;
            string record = pitcherSeason != null ? $"<color=#6FA8FF>승패</color> {pitcherSeason.Wins}-{pitcherSeason.Losses}  <color=#6FA8FF>ERA</color> {pitcherSeason.EarnedRunAverage:0.00}  " : "";
            directPitcherStats.text = $"{record}<color=#6FA8FF>투구</color> {pitchLine.Pitches}  <color=#6FA8FF>K</color> {pitchLine.Strikeouts}";
            directStamina.anchorMax = new Vector2(Mathf.Clamp01(1f - pitchLine.Pitches / 110f), 1f);

            string name = evt.Batter?.Template != null ? evt.Batter.Template.PlayerName : "타자";
            directSubtitleText.text = today.AtBats + today.Walks == 0
                ? $"{name} 선수, 오늘 첫 타석입니다. 승부처에서 직접 플레이합니다."
                : $"{name} 선수입니다. 오늘 {today.AtBats}타수 {today.Hits}안타를 기록하고 있습니다.";
        }

        /// <summary>[TASK-KBO-180] 공격(우리 팀 타석)/수비(상대 타석) 작전 버튼 구성. 상황상 불가능한 작전(주자 없는 번트 등)은 비활성.</summary>
        private void ConfigureTacticButtons(PlayEvent evt, CompyaGameTracker t)
        {
            bool offense = evt != null && IsUserBatting(evt.IsTopHalf);
            var options = offense
                ? new[] { MatchTactic.PowerSwing, MatchTactic.ContactSwing, MatchTactic.Bunt, MatchTactic.Steal }
                : new[] { MatchTactic.FullForce, MatchTactic.PitchingChange, MatchTactic.IntentionalWalk, MatchTactic.None };
            for (int i = 0; i < 4; i++)
            {
                tacticOptions[i] = options[i];
                bool available = options[i] switch
                {
                    MatchTactic.Bunt => t.Bases[1] || t.Bases[2] || t.Bases[3] ? t.Outs < 2 : false,
                    MatchTactic.Steal => t.Bases[1] && !t.Bases[2] && t.Outs < 2,
                    _ => true,
                };
                tacticButtons[i].interactable = available;
                CompyaUiKit.SetButtonText(tacticButtons[i], MatchEngine.TacticLabel(options[i]));
            }
            selectedTactic = offense ? MatchTactic.PowerSwing : MatchTactic.FullForce;
            if (!offense) selectedTactic = MatchTactic.FullForce;
            RefreshTacticButtons();
        }

        private void SelectTactic(int index)
        {
            if (!tacticButtons[index].interactable) return;
            selectedTactic = tacticOptions[index];
            RefreshTacticButtons();
        }

        private void RefreshTacticButtons()
        {
            for (int i = 0; i < 4; i++)
            {
                var image = tacticButtons[i].targetGraphic as Image;
                if (image != null) image.color = tacticOptions[i] == selectedTactic ? new Color(0.55f, 0.25f, 0.95f) : new Color(0.18f, 0.19f, 0.23f);
            }
            directTacticHint.text = TacticHint(selectedTactic);
        }

        private static string TacticHint(MatchTactic tactic)
        {
            switch (tactic)
            {
                case MatchTactic.PowerSwing: return "강공: 파워 +8 / 정확 -3 / 선구 -4 (장타 노림)";
                case MatchTactic.ContactSwing: return "컨택: 정확 +6 / 선구 +2 / 파워 -6 (출루 노림)";
                case MatchTactic.Bunt: return "번트: 희생번트 위주 - 성공 시 모든 주자 한 루씩 진루";
                case MatchTactic.Steal: return "도루: 타석 전 1루 주자 2루 도루(성공률 약 72%)";
                case MatchTactic.FullForce: return "정면 승부: 구위 +5 / 구속 +4 / 제구 -3";
                case MatchTactic.PitchingChange: return "투수 교체: 가용 불펜 중 최적 투수 즉시 등판";
                case MatchTactic.IntentionalWalk: return "고의사구: 타자를 1루로 내보냅니다";
                default: return "일반: 작전 없이 승부";
            }
        }

        private static void FillSkills(Text[] slots, Player player)
        {
            var skills = player?.AcquiredSkillIds ?? new List<string>();
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].text = i < skills.Count ? skills[i] : "";
                slots[i].transform.parent.Find(slots[i].name.Replace("Text", ""))?.gameObject.SetActive(i < skills.Count);
            }
        }

        // ------------------------------------------------------------------ 6. 결과 1(박스 스코어 + 경기 결과 막대 + 승/패 투수)

        private GameObject BuildResult1()
        {
            var p = NewScreen("Result1", "Broadcast179/bg_result_gray", new Color(0.7f, 0.72f, 0.76f));

            r1AwayRow = kit.GradientBox(p, "AwayRow", 130, 105, 332, 190, Color.blue, Color.blue, true);
            r1HomeRow = kit.GradientBox(p, "HomeRow", 130, 192, 332, 278, Color.red, Color.red, true);
            r1AwayLogo = CompyaUiKit.Logo(p, "AwayLogo", 140, 112, 212, 184);
            r1HomeLogo = CompyaUiKit.Logo(p, "HomeLogo", 140, 199, 212, 271);
            r1AwayName = kit.Label(p, "AwayName", "", 214, 105, 330, 190, 46, TextAnchor.MiddleCenter, White, true);
            r1HomeName = kit.Label(p, "HomeName", "", 214, 192, 330, 278, 46, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Box(p, "AwayLine", 332, 105, 893, 170, White);
            CompyaUiKit.Box(p, "NumberStrip", 332, 170, 893, 212, new Color(0.11f, 0.12f, 0.15f));
            CompyaUiKit.Box(p, "HomeLine", 332, 212, 893, 278, White);
            for (int i = 0; i < 12; i++)
            {
                float x0 = 332 + i * 46.75f, x1 = x0 + 46.75f;
                kit.Label(p, $"No{i + 1}", (i + 1).ToString(), x0, 170, x1, 212, 26, TextAnchor.MiddleCenter, new Color(0.72f, 0.73f, 0.78f));
                r1AwayCells[i] = kit.Label(p, $"A{i + 1}", "", x0, 105, x1, 170, 54, TextAnchor.MiddleCenter, Ink, true);
                r1HomeCells[i] = kit.Label(p, $"H{i + 1}", "", x0, 212, x1, 278, 54, TextAnchor.MiddleCenter, Ink, true);
            }
            CompyaUiKit.Box(p, "Rheb", 893, 105, 1115, 278, new Color(0.04f, 0.04f, 0.05f));
            var heads = new[] { "R", "H", "E", "B" };
            for (int i = 0; i < 4; i++)
            {
                float x0 = 895 + i * 55f, x1 = x0 + 55f;
                kit.Label(p, $"Head{heads[i]}", heads[i], x0, 170, x1, 212, 26, TextAnchor.MiddleCenter, i == 0 ? Gold : new Color(0.72f, 0.73f, 0.78f), true);
                r1AwayRheb[i] = kit.Label(p, $"AR{i}", "0", x0, 105, x1, 170, 56, TextAnchor.MiddleCenter, White, true);
                r1HomeRheb[i] = kit.Label(p, $"HR{i}", "0", x0, 212, x1, 278, 56, TextAnchor.MiddleCenter, White, true);
            }

            // AWAY / HOME 큰 점수 카드 + 구단 컬러 블록 + WIN/LOSE
            CompyaUiKit.Box(p, "AwayCard", 160, 280, 583, 690, new Color(0.96f, 0.96f, 0.97f));
            CompyaUiKit.Box(p, "HomeCard", 663, 280, 1087, 690, new Color(0.96f, 0.96f, 0.97f));
            kit.Label(p, "AwayLabel", "AWAY", 160, 285, 583, 335, 36, TextAnchor.MiddleCenter, new Color(0.45f, 0.47f, 0.52f), true);
            kit.Label(p, "HomeLabel", "HOME", 663, 285, 1087, 335, 36, TextAnchor.MiddleCenter, new Color(0.45f, 0.47f, 0.52f), true);
            r1AwayScore = kit.Label(p, "AwayScore", "0", 160, 335, 583, 690, 300, TextAnchor.MiddleCenter, Navy, true);
            r1HomeScore = kit.Label(p, "HomeScore", "0", 663, 335, 1087, 690, 300, TextAnchor.MiddleCenter, Navy, true);
            r1UserBadge = UserBadge(p, "UserBadge", 172, 262, 212, 322);
            CompyaUiKit.Box(p, "Dash", 605, 508, 642, 520, new Color(0.55f, 0.57f, 0.62f));
            r1AwayBlock = CompyaUiKit.Box(p, "AwayBlock", 160, 690, 583, 808, Color.blue);
            r1HomeBlock = CompyaUiKit.Box(p, "HomeBlock", 663, 690, 1087, 808, Color.red);
            r1AwayWatermark = CompyaUiKit.Logo(p, "AwayWatermark", 250, 692, 495, 806);
            r1HomeWatermark = CompyaUiKit.Logo(p, "HomeWatermark", 752, 692, 997, 806);
            r1AwayVerdictBg = kit.GradientBox(p, "AwayVerdictBg", 158, 825, 588, 912, BlueTop, BlueBottom, false);
            r1HomeVerdictBg = kit.GradientBox(p, "HomeVerdictBg", 661, 825, 1090, 912, BlueTop, BlueBottom, false);
            r1AwayVerdict = kit.Label(p, "AwayVerdict", "WIN", 158, 825, 588, 912, 72, TextAnchor.MiddleCenter, White, true, true);
            r1HomeVerdict = kit.Label(p, "HomeVerdict", "LOSE", 661, 825, 1090, 912, 72, TextAnchor.MiddleCenter, White, true, true);
            CompyaUiKit.Shadow(r1AwayVerdict, new Color(0f, 0f, 0f, 0.35f));
            CompyaUiKit.Shadow(r1HomeVerdict, new Color(0f, 0f, 0f, 0.35f));

            // 경기 결과 막대 6종
            CompyaUiKit.Box(p, "StatsHeader", 160, 945, 1087, 1015, new Color(0.13f, 0.15f, 0.22f));
            kit.Label(p, "StatsTitle", "경기 결과", 160, 945, 1087, 1015, 40, TextAnchor.MiddleCenter, new Color(0.86f, 0.87f, 0.9f), true);
            CompyaUiKit.Box(p, "StatsBody", 160, 1015, 1087, 1407, White);
            var names = new[] { "안타", "홈런", "도루", "삼진", "병살", "실책" };
            var ys = new[] { 1067f, 1125f, 1183f, 1240f, 1298f, 1355f };
            for (int i = 0; i < 6; i++)
            {
                float cy = ys[i];
                kit.Label(p, $"StatName{i}", names[i], 570, cy - 30, 680, cy + 30, 40, TextAnchor.MiddleCenter, new Color(0.4f, 0.42f, 0.48f));
                var bar = new StatBar { CenterY = cy };
                bar.LeftBar = CompyaUiKit.Place(p, $"LeftBar{i}", 400, cy - 9, 562, cy + 9);
                CompyaUiKit.Paint(bar.LeftBar, new Color(0.36f, 0.68f, 0.98f));
                bar.RightBar = CompyaUiKit.Place(p, $"RightBar{i}", 686, cy - 9, 800, cy + 9);
                CompyaUiKit.Paint(bar.RightBar, new Color(0.6f, 0.63f, 0.7f));
                bar.LeftValueRect = CompyaUiKit.Place(p, $"LeftValue{i}", 300, cy - 32, 390, cy + 32);
                bar.LeftValue = kit.LabelOn(bar.LeftValueRect, "0", 56, TextAnchor.MiddleRight, new Color(0.17f, 0.4f, 0.85f), true);
                bar.RightValueRect = CompyaUiKit.Place(p, $"RightValue{i}", 810, cy - 32, 900, cy + 32);
                bar.RightValue = kit.LabelOn(bar.RightValueRect, "0", 56, TextAnchor.MiddleLeft, new Color(0.55f, 0.58f, 0.65f), true);
                r1Stats[i] = bar;
            }

            // 승리/패전 투수
            r1WinPanel = CompyaUiKit.Box(p, "WinPanel", 160, 1407, 623, 1825, Color.blue);
            r1LosePanel = CompyaUiKit.Box(p, "LosePanel", 623, 1407, 1087, 1825, new Color(0.37f, 0.39f, 0.46f));
            r1WinLogo = CompyaUiKit.Logo(p, "WinLogo", 190, 1420, 595, 1690);
            r1LoseLogo = CompyaUiKit.Logo(p, "LoseLogo", 653, 1420, 1058, 1690);
            r1WinCard = BuildCard(p, "WinCard", 300, 1425, 486, 1688, false);
            r1LoseCard = BuildCard(p, "LoseCard", 762, 1425, 948, 1688, false);
            CompyaUiKit.Polygon(p, "WinTag", 160, 1695, 335, 1750, AccentRed,
                new Vector2(0f, 1f), new Vector2(0.86f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f));
            r1WinTag = kit.Label(p, "WinTagText", "승리 투수", 168, 1695, 320, 1750, 40, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Polygon(p, "LoseTag", 912, 1695, 1087, 1750, new Color(0.27f, 0.28f, 0.32f),
                new Vector2(0.14f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f));
            r1LoseTag = kit.Label(p, "LoseTagText", "패전 투수", 928, 1695, 1080, 1750, 40, TextAnchor.MiddleCenter, White, true);
            r1WinNameBar = CompyaUiKit.Box(p, "WinNameBar", 160, 1750, 623, 1825, Color.blue);
            r1LoseNameBar = CompyaUiKit.Box(p, "LoseNameBar", 623, 1750, 1087, 1825, new Color(0.3f, 0.31f, 0.36f));
            r1WinName = kit.Label(p, "WinName", "", 175, 1750, 430, 1825, 48, TextAnchor.MiddleLeft, White, true);
            r1WinRecord = kit.Label(p, "WinRecord", "", 420, 1750, 612, 1825, 46, TextAnchor.MiddleRight, White, true);
            r1LoseRecord = kit.Label(p, "LoseRecord", "", 638, 1750, 820, 1825, 46, TextAnchor.MiddleLeft, White, true);
            r1LoseName = kit.Label(p, "LoseName", "", 810, 1750, 1075, 1825, 48, TextAnchor.MiddleRight, White, true);

            kit.Button(p, "Record", "경기 기록", 165, 1838, 410, 1940, new Color(0.36f, 0.38f, 0.43f), White, 42).onClick.AddListener(() => ShowPopup(true));
            kit.Button(p, "Timeline", "타임 라인", 432, 1838, 678, 1940, new Color(0.36f, 0.38f, 0.43f), White, 42).onClick.AddListener(() => ShowPopup(false));
            kit.GradientButton(p, "Next", "다음", 697, 1838, 1083, 1940, BlueTop, BlueBottom, White, 46).onClick.AddListener(OnResult1Next);
            return p.gameObject;
        }

        private void FillResult1()
        {
            var t = finalTracker ?? new CompyaGameTracker();
            var awayColor = CompyaUiKit.TeamColor(awayTeam);
            var homeColor = CompyaUiKit.TeamColor(homeTeam);
            r1AwayRow.texture = kit.GradientTexture(Color.Lerp(awayColor, Color.white, 0.12f), CompyaUiKit.Darken(awayColor, 0.85f), true);
            r1HomeRow.texture = kit.GradientTexture(Color.Lerp(homeColor, Color.white, 0.12f), CompyaUiKit.Darken(homeColor, 0.85f), true);
            CompyaUiKit.SetLogo(r1AwayLogo, awayTeam);
            CompyaUiKit.SetLogo(r1HomeLogo, homeTeam);
            r1AwayName.text = CompyaUiKit.ShortName(awayTeam);
            r1HomeName.text = CompyaUiKit.ShortName(homeTeam);
            for (int i = 0; i < 12; i++)
            {
                r1AwayCells[i].text = i < t.Away.Runs.Count ? t.Away.Runs[i].ToString() : "";
                r1HomeCells[i].text = i < t.Home.Runs.Count ? t.Home.Runs[i].ToString()
                    : (i < t.Away.Runs.Count && t.GameOver ? "X" : "");
            }
            SetRheb(r1AwayRheb, t.Away);
            SetRheb(r1HomeRheb, t.Home);

            int awayRuns = lastResult != null ? lastResult.AwayTotalScore : t.AwayScore;
            int homeRuns = lastResult != null ? lastResult.HomeTotalScore : t.HomeScore;
            r1AwayScore.text = awayRuns.ToString();
            r1HomeScore.text = homeRuns.ToString();
            var user = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            r1UserBadge.gameObject.SetActive(user != Team.None);
            r1UserBadge.rectTransform.anchorMin = new Vector2((homeTeam == user ? 675f : 172f) / CompyaUiKit.RefWidth, r1UserBadge.rectTransform.anchorMin.y);
            r1UserBadge.rectTransform.anchorMax = new Vector2((homeTeam == user ? 715f : 212f) / CompyaUiKit.RefWidth, r1UserBadge.rectTransform.anchorMax.y);

            r1AwayBlock.color = Color.Lerp(awayColor, Color.white, 0.35f);
            r1HomeBlock.color = Color.Lerp(homeColor, Color.white, 0.35f);
            CompyaUiKit.SetLogo(r1AwayWatermark, awayTeam, 0.55f);
            CompyaUiKit.SetLogo(r1HomeWatermark, homeTeam, 0.55f);
            int verdict = awayRuns > homeRuns ? -1 : awayRuns < homeRuns ? 1 : 0;
            SetVerdict(r1AwayVerdictBg, r1AwayVerdict, verdict == -1, verdict == 0);
            SetVerdict(r1HomeVerdictBg, r1HomeVerdict, verdict == 1, verdict == 0);

            var pairs = new[] { (t.Away.H, t.Home.H), (t.Away.HR, t.Home.HR), (t.Away.SB, t.Home.SB), (t.Away.K, t.Home.K), (t.Away.DP, t.Home.DP), (0, 0) };
            for (int i = 0; i < 6; i++) SetStatBar(r1Stats[i], pairs[i].Item1, pairs[i].Item2);

            var winner = verdict == -1 ? awayTeam : verdict == 1 ? homeTeam : Team.None;
            var loser = verdict == -1 ? homeTeam : verdict == 1 ? awayTeam : Team.None;
            bool draw = verdict == 0;
            var winColor = CompyaUiKit.TeamColor(draw ? awayTeam : winner);
            r1WinPanel.color = winColor;
            r1WinNameBar.color = CompyaUiKit.Darken(winColor, 0.82f);
            CompyaUiKit.SetLogo(r1WinLogo, draw ? awayTeam : winner, 0.25f);
            CompyaUiKit.SetLogo(r1LoseLogo, draw ? homeTeam : loser, 0.2f);
            var winP = draw ? t.AwayPitchers.FirstOrDefault() : t.WinningPitcher;
            var loseP = draw ? t.HomePitchers.FirstOrDefault() : t.LosingPitcher;
            FillCard(r1WinCard, winP);
            FillCard(r1LoseCard, loseP);
            r1WinTag.text = draw ? "선발 투수" : "승리 투수";
            r1LoseTag.text = draw ? "선발 투수" : "패전 투수";
            r1WinName.text = winP != null ? DisplayName(winP) : "-";
            r1LoseName.text = loseP != null ? DisplayName(loseP) : "-";
            r1WinRecord.text = winP != null ? $"<color=#0B1E6E>W-L</color> {PitcherRecord(winP, draw ? 0 : 1, 0)}" : "";
            r1LoseRecord.text = loseP != null ? PitcherRecord(loseP, 0, draw ? 0 : 1) : "";
        }

        private void SetVerdict(RawImage bg, Text label, bool win, bool draw)
        {
            label.text = draw ? "DRAW" : win ? "WIN" : "LOSE";
            label.fontStyle = win ? FontStyle.BoldAndItalic : FontStyle.Bold;
            label.color = win ? White : new Color(0.93f, 0.93f, 0.95f);
            bg.texture = win ? kit.GradientTexture(BlueTop, BlueBottom, false)
                : kit.GradientTexture(new Color(0.62f, 0.64f, 0.68f), new Color(0.44f, 0.46f, 0.5f), false);
        }

        private static void SetStatBar(StatBar bar, int left, int right)
        {
            const float maxLength = 340f;
            int max = Mathf.Max(1, Mathf.Max(left, right));
            float l = maxLength * left / max, r = maxLength * right / max;
            CompyaUiKit.SetBox(bar.LeftBar, 562 - l, bar.CenterY - 9, 562, bar.CenterY + 9);
            CompyaUiKit.SetBox(bar.RightBar, 686, bar.CenterY - 9, 686 + r, bar.CenterY + 9);
            bar.LeftBar.gameObject.SetActive(left > 0);
            bar.RightBar.gameObject.SetActive(right > 0);
            CompyaUiKit.SetBox(bar.LeftValueRect, 562 - l - 100, bar.CenterY - 32, 554 - l, bar.CenterY + 32);
            CompyaUiKit.SetBox(bar.RightValueRect, 694 + r, bar.CenterY - 32, 794 + r, bar.CenterY + 32);
            bar.LeftValue.text = left.ToString();
            bar.RightValue.text = right.ToString();
        }

        /// <summary>시즌 승-패(SeasonStatManager) - 없으면 이번 경기 기록.</summary>
        private static string PitcherRecord(Player pitcher, int gameWins, int gameLosses)
        {
            var season = SeasonStatManager.Instance != null ? SeasonStatManager.Instance.GetPitcherStats(pitcher) : null;
            if (season != null && season.Wins + season.Losses > 0) return $"{season.Wins}-{season.Losses}";
            return $"{gameWins}-{gameLosses}";
        }

        // ------------------------------------------------------------------ 7. 결과 2(라운드 결과 + NEXT MATCH)

        private GameObject BuildResult2()
        {
            var p = NewScreen("Result2", "Broadcast179/bg_result_gray", new Color(0.7f, 0.72f, 0.76f));
            CompyaUiKit.Box(p, "Header", 162, 342, 1086, 432, new Color(0.15f, 0.17f, 0.24f));
            r2Title = kit.Label(p, "Title", "", 162, 342, 1086, 432, 50, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Box(p, "SubHeader", 162, 432, 1086, 507, new Color(0.3f, 0.32f, 0.39f));
            kit.Label(p, "Away", "AWAY", 200, 432, 420, 507, 38, TextAnchor.MiddleCenter, new Color(0.86f, 0.87f, 0.9f));
            kit.Label(p, "Home", "HOME", 827, 432, 1047, 507, 38, TextAnchor.MiddleCenter, new Color(0.86f, 0.87f, 0.9f));
            for (int r = 0; r < 5; r++)
            {
                float y0 = 507 + r * 197f;
                CompyaUiKit.Box(p, $"Row{r}", 162, y0, 1086, y0 + 197, r % 2 == 0 ? White : new Color(0.95f, 0.96f, 0.98f));
                var row = new RoundRow
                {
                    AwayLogo = CompyaUiKit.Logo(p, $"AwayLogo{r}", 240, y0 + 14, 385, y0 + 124),
                    HomeLogo = CompyaUiKit.Logo(p, $"HomeLogo{r}", 865, y0 + 14, 1010, y0 + 124),
                    AwayName = kit.Label(p, $"AwayName{r}", "", 180, y0 + 128, 445, y0 + 182, 36, TextAnchor.MiddleCenter, new Color(0.25f, 0.26f, 0.3f)),
                    HomeName = kit.Label(p, $"HomeName{r}", "", 805, y0 + 128, 1070, y0 + 182, 36, TextAnchor.MiddleCenter, new Color(0.25f, 0.26f, 0.3f)),
                    AwayScore = kit.Label(p, $"AwayScore{r}", "", 424, y0 + 28, 564, y0 + 168, 104, TextAnchor.MiddleCenter, Navy, true),
                    HomeScore = kit.Label(p, $"HomeScore{r}", "", 682, y0 + 28, 822, y0 + 168, 104, TextAnchor.MiddleCenter, Navy, true),
                    Venue = kit.Label(p, $"Venue{r}", "", 430, y0 + 128, 818, y0 + 182, 30, TextAnchor.MiddleCenter, new Color(0.62f, 0.64f, 0.69f)),
                };
                CompyaUiKit.Box(p, $"Dash{r}", 610, y0 + 92, 637, y0 + 104, Navy);
                row.UserBadge = UserBadge(p, $"UserBadge{r}", 173, y0 + 10, 207, y0 + 60);
                r2Rows[r] = row;
            }
            CompyaUiKit.Box(p, "NextBar", 162, 1500, 1086, 1562, new Color(0.04f, 0.05f, 0.2f));
            CompyaUiKit.Box(p, "NextTick", 178, 1514, 184, 1548, AccentRed);
            kit.Label(p, "NextTitle", "NEXT MATCH", 195, 1500, 372, 1562, 42, TextAnchor.MiddleLeft, White, true, true);
            r2NextTeam = kit.Label(p, "NextTeam", "", 380, 1500, 650, 1562, 44, TextAnchor.MiddleLeft, White, true);
            kit.Label(p, "NextPitcherLabel", "선발 투수", 740, 1500, 895, 1562, 32, TextAnchor.MiddleRight, new Color(0.75f, 0.77f, 0.82f));
            r2NextPitcher = kit.Label(p, "NextPitcher", "", 900, 1500, 1075, 1562, 46, TextAnchor.MiddleRight, White, true);

            CompyaUiKit.Box(p, "BallPill", 160, 1700, 615, 1748, new Color(0.04f, 0.05f, 0.22f));
            r2Ball = kit.Label(p, "Ball", "", 160, 1700, 615, 1748, 40, TextAnchor.MiddleCenter, White, true);
            kit.GradientButton(p, "Again", "한 번 더 하기", 165, 1762, 610, 1865, new Color(0.97f, 0.97f, 0.99f), new Color(0.82f, 0.84f, 0.9f), new Color(0.15f, 0.16f, 0.2f), 50).onClick.AddListener(OnResult2Again);
            kit.GradientButton(p, "Confirm", "확인", 637, 1762, 1080, 1865, BlueTop, BlueBottom, White, 50).onClick.AddListener(OnResult2Confirm);
            return p.gameObject;
        }

        private void FillResult2()
        {
            var league = LeagueManager.Instance;
            var user = league != null ? league.UserTeam : Team.None;
            int gameNumber = league != null ? league.PlayedGameCount : 0;
            r2Title.text = $"정규시즌 <color=#FFD84A>{gameNumber}</color> 경기";

            // 1행: 방금 치른 우리 경기(실제 결과)
            int awayRuns = lastResult != null ? lastResult.AwayTotalScore : 0;
            int homeRuns = lastResult != null ? lastResult.HomeTotalScore : 0;
            SetRoundRow(r2Rows[0], awayTeam, homeTeam, awayRuns.ToString(), homeRuns.ToString(), awayRuns, homeRuns,
                CompyaUiKit.Stadium(homeTeam), awayTeam == user || homeTeam == user);

            // 2~5행: 리그 엔진은 우리 구단 경기만 시뮬레이션하므로(타 구장 경기 결과가 존재하지 않음) 가짜 점수를 만들지 않고,
            // 나머지 8개 구단을 현재 순위 순으로 짝지어 각 구단의 시즌 승수를 같은 자리에 표기한다(행 하단 = 홈 구장 · 순위).
            var others = league != null
                ? league.GetStandings().Select(s => s.Team).Where(team => team != awayTeam && team != homeTeam && team != Team.None).ToList()
                : new List<Team>();
            var standings = league != null ? league.GetStandings() : new List<TeamInfo>();
            for (int r = 1; r < 5; r++)
            {
                int a = (r - 1) * 2, h = a + 1;
                if (h >= others.Count) { SetRoundRow(r2Rows[r], Team.None, Team.None, "", "", 0, 0, "", false); continue; }
                var awayInfo = league.GetTeamInfo(others[a]);
                var homeInfo = league.GetTeamInfo(others[h]);
                int awayRank = standings.FindIndex(s => s.Team == others[a]) + 1;
                int homeRank = standings.FindIndex(s => s.Team == others[h]) + 1;
                SetRoundRow(r2Rows[r], others[a], others[h], $"{awayInfo?.Wins ?? 0}", $"{homeInfo?.Wins ?? 0}",
                    awayInfo?.Wins ?? 0, homeInfo?.Wins ?? 0, $"시즌 승수 · {awayRank}위 vs {homeRank}위", false);
            }

            var next = league != null ? league.PeekNextFixture() : null;
            if (next != null)
            {
                var opponent = next.HomeTeam == user ? next.AwayTeam : next.HomeTeam;
                int rank = standings.FindIndex(s => s.Team == opponent) + 1;
                r2NextTeam.text = $"<color=#5ED6F2>{CompyaUiKit.ShortName(opponent)}</color> {rank} 위";
                var roster = league.ResolveRosterForTeam(opponent);
                var starter = roster?.Where(pl => pl?.Template != null && pl.Template.IsPitcher && pl.Template.PitcherRole == PitcherRole.StartingPitcher)
                    .OrderByDescending(Ovr).FirstOrDefault();
                r2NextPitcher.text = starter != null ? DisplayName(starter) : "-";
            }
            else
            {
                r2NextTeam.text = "시즌 일정 종료";
                r2NextPitcher.text = "-";
            }
            r2Ball.text = GameManager.Instance != null ? $"볼  {GameManager.Instance.GameGold:N0}" : "볼  -";
        }

        private void SetRoundRow(RoundRow row, Team away, Team home, string awayText, string homeText, int awayValue, int homeValue, string venue, bool isUserGame)
        {
            CompyaUiKit.SetLogo(row.AwayLogo, away);
            CompyaUiKit.SetLogo(row.HomeLogo, home);
            row.AwayName.text = away == Team.None ? "" : CompyaUiKit.FullName(away);
            row.HomeName.text = home == Team.None ? "" : CompyaUiKit.FullName(home);
            row.AwayScore.text = awayText;
            row.HomeScore.text = homeText;
            var winRed = new Color(0.9f, 0.2f, 0.27f);
            row.AwayScore.color = awayValue > homeValue ? winRed : Navy;
            row.HomeScore.color = homeValue > awayValue ? winRed : Navy;
            int size = isUserGame ? Mathf.RoundToInt(104 * 0.9f) : Mathf.RoundToInt(80 * 0.9f); // 우리 경기 점수는 크게, 시즌 승수는 작게
            row.AwayScore.fontSize = row.HomeScore.fontSize = size;
            row.AwayScore.resizeTextMaxSize = row.HomeScore.resizeTextMaxSize = size;
            row.Venue.text = venue;
            row.UserBadge.gameObject.SetActive(isUserGame);
        }

        // ------------------------------------------------------------------ 8. 결과 3(TODAY'S MVP + 반복과제)

        private GameObject BuildResult3()
        {
            var p = NewScreen("Result3", "Broadcast179/bg_result_gray", new Color(0.7f, 0.72f, 0.76f));
            kit.GradientBox(p, "MvpBand", 0, 470, 1248, 1040, new Color(1f, 1f, 1f, 0.78f), new Color(0.92f, 0.92f, 0.95f, 0.92f), false);
            r3MvpCard = BuildCard(p, "MvpCard", 215, 400, 485, 1000, true);
            var todays = kit.Label(p, "Todays", "TODAY'S", 690, 478, 910, 562, 72, TextAnchor.MiddleRight, new Color(0.07f, 0.08f, 0.12f), true, true);
            var mvp = kit.Label(p, "Mvp", "MVP", 905, 445, 1095, 572, 116, TextAnchor.MiddleLeft, new Color(0.38f, 0.24f, 0.92f), true, true);
            CompyaUiKit.Shadow(todays, new Color(0f, 0f, 0f, 0.15f));
            CompyaUiKit.Shadow(mvp, new Color(0f, 0f, 0f, 0.15f));
            r3MvpName = kit.Label(p, "MvpName", "", 700, 560, 1085, 640, 58, TextAnchor.MiddleRight, new Color(0.2f, 0.21f, 0.25f));

            var xs = new[] { (500f, 665f), (685f, 853f), (872f, 1040f) };
            for (int i = 0; i < 3; i++)
            {
                var tile = new RewardTile();
                var tileRect = CompyaUiKit.Place(p, $"Reward{i}", xs[i].Item1, 655, xs[i].Item2, 920);
                CompyaUiKit.Shadow(CompyaUiKit.Paint(tileRect, White), new Color(0f, 0f, 0f, 0.18f), 4f);
                tile.Root = tileRect.gameObject;
                CompyaUiKit.Paint(CompyaUiKit.Norm(tileRect, "IconBg", 0.12f, 0.33f, 0.88f, 0.92f), new Color(0.88f, 0.9f, 0.95f));
                tile.Icon = kit.LabelOn(CompyaUiKit.Norm(tileRect, "Icon", 0.12f, 0.33f, 0.88f, 0.92f), "", 40, TextAnchor.MiddleCenter, new Color(0.15f, 0.2f, 0.45f), true);
                tile.Count = kit.LabelOn(CompyaUiKit.Norm(tileRect, "Count", 0.02f, 0.02f, 0.98f, 0.3f), "", 44, TextAnchor.MiddleCenter, Ink, true);
                var tag = CompyaUiKit.Norm(tileRect, "EventTag", 0.45f, 0.9f, 1.05f, 1.04f);
                CompyaUiKit.Paint(tag, new Color(0.45f, 0.22f, 0.95f));
                kit.LabelOn(CompyaUiKit.Fill(tag, "Text"), "EVENT", 26, TextAnchor.MiddleCenter, White, true, true);
                tile.EventTag = tag.gameObject;
                r3Rewards[i] = tile;
            }

            CompyaUiKit.Box(p, "Band", 0, 1040, 1248, 1135, new Color(0.97f, 0.97f, 0.98f));
            kit.Button(p, "History", "보상 내역", 160, 1055, 330, 1120, new Color(0.93f, 0.94f, 0.96f), new Color(0.2f, 0.21f, 0.25f), 32, false).onClick.AddListener(() => ShowPopup(false));
            Slash(p, "SlashBlue", 452, 1080, 478, 1122, new Color(0.3f, 0.65f, 1f));
            Slash(p, "SlashPink", 772, 1062, 798, 1104, new Color(0.95f, 0.25f, 0.6f));
            kit.Label(p, "Title", "반복과제 보상", 480, 1050, 770, 1125, 52, TextAnchor.MiddleCenter, new Color(0.1f, 0.11f, 0.15f));
            CompyaUiKit.Box(p, "Help", 1033, 1060, 1090, 1117, White);
            kit.Label(p, "HelpText", "?", 1033, 1060, 1090, 1117, 44, TextAnchor.MiddleCenter, Ink, true);

            CompyaUiKit.Box(p, "AchievePill", 928, 1160, 1082, 1210, new Color(0.08f, 0.12f, 0.12f));
            r3AchieveText = kit.Label(p, "Achieve", "", 928, 1160, 1082, 1210, 38, TextAnchor.MiddleCenter, White, true);
            CompyaUiKit.Box(p, "BarTrack", 240, 1220, 1005, 1236, new Color(0.75f, 0.76f, 0.8f));
            r3BarFill = CompyaUiKit.Place(p, "BarFill", 240, 1220, 1005, 1236);
            kit.Gradient(r3BarFill, new Color(0.45f, 0.25f, 0.95f), new Color(0.96f, 0.92f, 0.3f), true);
            var centers = new[] { 240f, 623f, 1005f };
            var marks = new[] { "0", "35", "60" };
            for (int i = 0; i < 3; i++)
            {
                float cx = centers[i];
                var tile = CompyaUiKit.Place(p, $"Milestone{i}", cx - 77, 1255, cx + 77, 1470);
                CompyaUiKit.Shadow(CompyaUiKit.Paint(tile, White), new Color(0f, 0f, 0f, 0.15f), 3f);
                CompyaUiKit.Box(p, $"MilestonePill{i}", cx - 45, 1268, cx + 45, 1310, new Color(0.25f, 0.26f, 0.3f));
                kit.Label(p, $"MilestoneText{i}", marks[i], cx - 45, 1268, cx + 45, 1310, 38, TextAnchor.MiddleCenter, White, true);
                CompyaUiKit.Box(p, $"MilestoneIcon{i}", cx - 34, 1318, cx + 34, 1412, new Color(0.25f, 0.26f, 0.3f));
                kit.Label(p, $"MilestoneIconText{i}", "★", cx - 34, 1318, cx + 34, 1412, 50, TextAnchor.MiddleCenter, Gold, true);
                r3MilestoneState[i] = kit.Label(p, $"MilestoneState{i}", "", cx - 75, 1415, cx + 75, 1468, 34, TextAnchor.MiddleCenter, Ink, true);
                r3MilestoneChecks[i] = Check(p, $"MilestoneCheck{i}", cx + 30, 1250, cx + 95, 1300);
            }
            kit.GradientBox(p, "DirectBar", 213, 1550, 1035, 1606, new Color(0.02f, 0.02f, 0.05f), new Color(0.38f, 0.16f, 0.88f), true);
            r3DirectText = kit.Label(p, "DirectText", "", 213, 1550, 1035, 1606, 42, TextAnchor.MiddleCenter, new Color(0.86f, 1f, 0.45f), true);
            kit.GradientButton(p, "Next", "다음", 355, 1785, 893, 1890, BlueTop, BlueBottom, White, 48).onClick.AddListener(OnResult3Next);
            return p.gameObject;
        }

        private void FillResult3()
        {
            var t = finalTracker ?? new CompyaGameTracker();
            var user = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            var mvp = PickMvp(t, user);
            FillCard(r3MvpCard, mvp);
            r3MvpName.text = mvp != null ? DisplayName(mvp) : "-";

            var tiles = new List<(string icon, string count, bool eventTag)>();
            if (lastReward != null)
            {
                tiles.Add(("영입권", $"×{lastReward.LiveNormalTicketGained:N0}", false));
                foreach (var group in lastReward.ItemsGained.Where(item => item?.Template != null).GroupBy(item => item.Template.DisplayName).Take(2))
                    tiles.Add((group.Key, $"×{group.Count()}", true));
            }
            for (int i = 0; i < 3; i++)
            {
                bool on = i < tiles.Count;
                r3Rewards[i].Root.SetActive(on);
                if (!on) continue;
                r3Rewards[i].Icon.text = tiles[i].icon;
                r3Rewards[i].Count.text = tiles[i].count;
                r3Rewards[i].EventTag.SetActive(tiles[i].eventTag);
            }

            int count = AchievementCount(t);
            r3AchieveText.text = $"<color=#7CFF9C>{count}</color>개 달성";
            r3BarFill.anchorMax = new Vector2(Mathf.Lerp(240f, 1005f, Mathf.Clamp01(count / 60f)) / CompyaUiKit.RefWidth, r3BarFill.anchorMax.y);
            var thresholds = new[] { 0, 35, 60 };
            for (int i = 0; i < 3; i++)
            {
                bool done = count >= thresholds[i];
                r3MilestoneChecks[i].SetActive(done);
                r3MilestoneState[i].text = done ? "달성" : "진행 중";
            }
            r3DirectText.text = directPlayCount > 0 ? $"직접 플레이 {directPlayCount}회 진행" : "이번 경기 직접 플레이 없음";
        }

        /// <summary>오늘의 MVP - 이긴 팀(무승부면 우리 팀)에서 타자(안타2·홈런3·볼넷1)와 투수(이닝1.2·탈삼진0.5·실점-1) 점수 최고.</summary>
        private Player PickMvp(CompyaGameTracker t, Team user)
        {
            bool awayWon = t.AwayScore > t.HomeScore, homeWon = t.HomeScore > t.AwayScore;
            bool useAway = awayWon || (!homeWon && awayTeam == user);
            var batters = useAway ? awayOrder : homeOrder;
            var pitchers = useAway ? t.AwayPitchers : t.HomePitchers;
            Player best = null;
            float bestScore = float.MinValue;
            foreach (var b in batters.Where(x => x != null))
            {
                var line = t.BatOf(b);
                float score = line.Hits * 2f + line.HomeRuns * 3f + line.Walks;
                if (score > bestScore) { bestScore = score; best = b; }
            }
            foreach (var pi in pitchers)
            {
                var line = t.PitchOf(pi);
                float score = line.Outs / 3f * 1.2f + line.Strikeouts * 0.5f - line.Runs;
                if (score > bestScore) { bestScore = score; best = pi; }
            }
            return best;
        }

        // ------------------------------------------------------------------ 9. 팝업(경기 기록 / 타임 라인)

        private GameObject BuildPopup()
        {
            var p = CompyaUiKit.Fill(root, "Popup");
            CompyaUiKit.Paint(p, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(p, "Panel", 80, 250, 1168, 1760, new Color(0.1f, 0.11f, 0.15f));
            popupTitle = kit.Label(p, "Title", "", 80, 250, 1168, 345, 50, TextAnchor.MiddleCenter, White, true);
            var viewport = CompyaUiKit.Place(p, "Viewport", 110, 355, 1138, 1640);
            viewport.gameObject.AddComponent<RectMask2D>();
            CompyaUiKit.Paint(viewport, new Color(1f, 1f, 1f, 0.02f), true);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            popupBody = kit.LabelOn(content, "", 32, TextAnchor.UpperLeft, new Color(0.9f, 0.91f, 0.94f));
            popupBody.resizeTextForBestFit = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            popupScroll = viewport.gameObject.AddComponent<ScrollRect>();
            popupScroll.content = content;
            popupScroll.horizontal = false;
            popupScroll.viewport = viewport;
            kit.GradientButton(p, "Close", "닫기", 424, 1665, 824, 1745, BlueTop, BlueBottom, White, 44).onClick.AddListener(() => popupPanel.SetActive(false));
            return p.gameObject;
        }

        private void ShowPopup(bool boxScore)
        {
            var t = finalTracker ?? new CompyaGameTracker();
            if (boxScore)
            {
                popupTitle.text = "경기 기록";
                var sb = new System.Text.StringBuilder();
                AppendBox(sb, awayTeam, awayOrder, t.AwayPitchers, t);
                sb.AppendLine();
                AppendBox(sb, homeTeam, homeOrder, t.HomePitchers, t);
                popupBody.text = sb.ToString();
            }
            else
            {
                popupTitle.text = "타임 라인";
                popupBody.text = events != null
                    ? string.Join("\n", events.Where(e => e.Type == PlayEventType.AtBatResult && !string.IsNullOrEmpty(e.LogMessage)).Select(e => e.LogMessage))
                    : "";
            }
            popupPanel.SetActive(true);
            popupScroll.verticalNormalizedPosition = 1f;
        }

        private static void AppendBox(System.Text.StringBuilder sb, Team team, List<Player> order, List<Player> pitchers, CompyaGameTracker t)
        {
            sb.AppendLine($"<b><color=#FFD84A>{CompyaUiKit.FullName(team)}</color></b>");
            sb.AppendLine("<color=#8C93A3>타자              타수  안타  홈런  볼넷  타율</color>");
            for (int i = 0; i < order.Count; i++)
            {
                var player = order[i];
                if (player == null) continue;
                var line = t.BatOf(player);
                sb.AppendLine($"{i + 1}. {PositionLabel(player),-3} {DisplayName(player),-9}  {line.AtBats,3}  {line.Hits,4}  {line.HomeRuns,4}  {line.Walks,4}  {CompyaGameTracker.FormatAverage(line.Average)}");
            }
            sb.AppendLine("<color=#8C93A3>투수              이닝  투구  탈삼진  실점</color>");
            foreach (var pitcher in pitchers)
            {
                var line = t.PitchOf(pitcher);
                sb.AppendLine($"   {PositionLabel(pitcher),-3} {DisplayName(pitcher),-9}  {CompyaGameTracker.FormatInnings(line.Outs),4}  {line.Pitches,4}  {line.Strikeouts,5}  {line.Runs,4}");
            }
        }
    }
}
