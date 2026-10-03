using System.Linq;
using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static KBOManager.EditorTools.SetupTask181;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-182] 씬 적용(idempotent).
    ///   1) 라인업(RosterPanel/Layout182): 컴프야V26 1:1 - [타자][투수][보관 선수] 탭, 서브 툴바, 그라운드 다이아몬드 수비 위치 9칸 + [후보] 6,
    ///      1~5선발 + 불펜 4열, 하단 세트덱 스코어 바(OVR | 세트덱 스코어 [?] | POINT + 구간 게이지 + 서브 메뉴 3) / 선수 액션 트레이(3버튼).
    ///      TASK-181 Layout181은 삭제한다(옮겨 둔 게이지/버튼은 패널로 되돌린 뒤 다시 옮긴다).
    ///   2) 상세 정보 패널(InventoryPanel/DetailPanel)을 캔버스 최상위로 옮겨 라인업에서도 [상세 정보]가 뜨게 한다.
    ///   3) 세트덱 선택형 버프 패널 색(선택 = 골드, 반대쪽 = 어두운 비활성, 도달/미도달 라벨 구분).
    ///   4) 경기 재생: 씬에 BroadcastUIManager가 없어 "재생 없이 결과만 즉시 처리" → 결과 화면 기록이 0/'-'로 남던 문제 - InGamePanel에
    ///      BroadcastUIManager를 만들고 PlayBallController / CompyaMatchView에 연결한다.
    ///   5) 선수 영입 배너 재조립(시그니처 → 골든글러브 스카우트 문구/확률).
    /// </summary>
    public static class SetupTask182
    {
        private static readonly string[] MovedNames = { "SetDeckGaugeFill", "SetDeckStatusText", "SetDeckActiveGlow", "SetDeckOptionButton", "AutoLineupButton" };
        private static readonly Color Field = new Color(0.78f, 0.81f, 0.86f);
        private static readonly Color Dark = new Color(0.1f, 0.11f, 0.15f, 0.98f);
        private static readonly Color Ink = new Color(0.1f, 0.12f, 0.18f);
        private static readonly Color Light = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color TabBlue = new Color(0.09f, 0.19f, 0.47f);
        private static readonly Color ActionBlue = new Color(0.15f, 0.33f, 0.88f);

        [MenuItem("KBO Manager/Setup/Apply TASK-182 (Compya Lineup + Scout GG + Broadcast)")]
        public static void ApplyAll()
        {
            BuildLineup();
            MoveDetailPanelToCanvas();
            StyleSetDeckOptions();
            EnsureBroadcastManager();
            SetupThemeUI178.BuildScoutPanel();
            Debug.Log("[SetupTask182] TASK-182 적용 완료(컴프야 라인업 · 골든글러브 스카우트 · 경기 재생 연결) - 씬을 저장(Ctrl+S)하십시오.");
        }

        // ================================================================== 1) 라인업

        public static void BuildLineup()
        {
            var controller = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask182] RosterUIController가 없어 라인업을 건너뜁니다.");
                return;
            }
            var panel = (RectTransform)controller.transform;
            foreach (var layoutName in new[] { "Layout181", "Layout182" })
            {
                if (!(panel.Find(layoutName) is Transform previous)) continue;
                foreach (var keep in MovedNames)
                {
                    var moved = previous.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == keep);
                    if (moved != null) Undo.SetTransformParent(moved, panel, "Restore " + keep);
                }
                Undo.DestroyObjectImmediate(previous.gameObject);
            }
            foreach (var name in new[] { "BatterContainer", "BenchContainer", "PitcherContainer", "StarterHeaderText", "StarterHeaderTextBG",
                         "BenchHeaderText", "BenchHeaderTextBG", "PitcherHeaderText", "PitcherHeaderTextBG", "SetDeckBar178", "EmptyStateText" })
            {
                if (panel.Find(name) is Transform t) { Undo.RecordObject(t.gameObject, "Hide Roster"); t.gameObject.SetActive(false); }
            }

            var root = NewRoot(panel, "Layout182");
            var header = panel.Find("Header178");
            root.SetSiblingIndex(header != null ? header.GetSiblingIndex() + 1 : 0);
            Box(root, "Background", 0, 115, W, H, new Color(0.84f, 0.86f, 0.9f));

            // ---- 3탭 + 툴바
            var batterTab = Btn(root, "BatterTab", "타자", 0, 118, 360, 200, TabBlue, Color.white, 40);
            var pitcherTab = Btn(root, "PitcherTab", "투수", 360, 118, 720, 200, Light, Ink, 40);
            var storageTab = Btn(root, "StorageTab", "보관 선수", 720, 118, W, 200, Light, Ink, 40);
            Box(root, "TabUnderline", 0, 200, W, 206, new Color(0.25f, 0.6f, 1f));
            var basic = Btn(root, "BasicViewButton", "기본  ≡", 16, 214, 196, 266, Light, Ink, 26);
            var full = Btn(root, "FullViewButton", "전체 보기", 204, 214, 364, 266, Light, Ink, 26);
            var defense = Btn(root, "DefenseChangeButton", "수비 위치 변경", 744, 214, 912, 266, Light, Ink, 24);
            var order = Btn(root, "BattingOrderButton", "타순 변경", 918, 214, 1064, 266, Light, Ink, 24);
            var hint = Label(root, "ToolbarHint", "", 16, 270, 1064, 306, 22, TextAnchor.MiddleLeft, new Color(0.2f, 0.24f, 0.33f));
            if (panel.Find("AutoLineupButton") is RectTransform auto)
            {
                MoveTo(auto, root, 588, 214, 738, 266);
                Restyle(auto, "자동 교체", Light);
                foreach (var text in auto.GetComponentsInChildren<Text>(true)) text.color = Ink;
            }

            // ---- 타자 페이지: 그라운드 다이아몬드 + 후보
            var batterPage = Page(root, "BatterPage");
            var diamondRoot = Page(batterPage, "DiamondRoot");
            Box(diamondRoot, "Field", 0, 310, W, 1182, Field);
            var infield = Box(diamondRoot, "Infield", 330, 600, 750, 1020, new Color(0.93f, 0.94f, 0.96f));
            infield.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var mound = Box(diamondRoot, "Mound", 515, 785, 565, 835, new Color(0.8f, 0.82f, 0.86f));
            mound.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            // BatterPosition 순서: C, 1B, 2B, 3B, SS, LF, CF, RF, DH - (중심 x, 위 y), 칸 186 x 258
            var centers = new (float x, float y)[] { (470, 915), (930, 690), (660, 645), (150, 690), (420, 645), (250, 382), (540, 312), (830, 382), (690, 915) };
            var diamondSlots = new RectTransform[9];
            for (int i = 0; i < 9; i++)
            {
                var (cx, top) = centers[i];
                diamondSlots[i] = Place(diamondRoot, $"Diamond_{RosterSlotLayout.PositionLabel((BatterPosition)i)}", cx - 93, top, cx + 93, top + 258);
            }
            var fullRoot = Page(batterPage, "FullViewRoot");
            Box(fullRoot, "FullBg", 0, 310, W, 1182, Field);
            var fullGrid = Grid(fullRoot, "FullViewGrid", 316, 1178, 5, 2); // 전체 보기 = 주전 9인 타순 순(후보는 아래 줄)
            fullRoot.gameObject.SetActive(false);
            Box(batterPage, "BenchBand", 180, 1188, 900, 1224, new Color(0.24f, 0.25f, 0.29f));
            Label(batterPage, "BenchLabel", "후보", 180, 1188, 900, 1224, 26, TextAnchor.MiddleCenter, Color.white, true);
            var benchGrid = Grid(batterPage, "BenchGrid", 1228, 1502, 6, 1);

            // ---- 투수 페이지: 1~5선발 + 불펜 4열
            var pitcherPage = Page(root, "PitcherPage");
            Box(pitcherPage, "Field", 0, 310, W, 1504, Field);
            var spGrid = Grid(pitcherPage, "StartingPitcherGrid", 316, 704, 5, 1);
            var groups = new[] { ("승리조", new Color(0.24f, 0.25f, 0.29f)), ("추격조", new Color(0.17f, 0.18f, 0.22f)), ("롱릴리프", new Color(0.24f, 0.25f, 0.29f)), ("마무리", new Color(0.55f, 0.08f, 0.1f)) };
            var columns = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                float x0 = 12 + i * 264, x1 = x0 + 256;
                Box(pitcherPage, $"BullpenHead{i}", x0, 712, x1, 756, groups[i].Item2);
                Label(pitcherPage, $"BullpenHeadText{i}", groups[i].Item1, x0, 712, x1, 756, 28, TextAnchor.MiddleCenter, Color.white, true);
                Box(pitcherPage, $"BullpenLane{i}", x0, 756, x1, 1502, new Color(1f, 1f, 1f, 0.18f));
                columns[i] = Grid(pitcherPage, $"BullpenColumn{i}", 760, 1500, 1, 4);
                columns[i].anchorMin = new Vector2(x0 / W, columns[i].anchorMin.y);
                columns[i].anchorMax = new Vector2(x1 / W, columns[i].anchorMax.y);
            }
            pitcherPage.gameObject.SetActive(false);

            // ---- 보관 선수 페이지(스크롤 그리드)
            var storagePage = Page(root, "StoragePage");
            Box(storagePage, "Field", 0, 310, W, 1504, Field);
            var storageInfo = Label(storagePage, "StorageInfo", "", 24, 312, 1056, 352, 24, TextAnchor.MiddleLeft, Ink, true);
            var storageContent = ScrollGrid(storagePage, "StorageScroll", 12, 356, W - 12, 1500, 5, 300f);
            storagePage.gameObject.SetActive(false);

            // ---- 하단 기본 바(세트덱 스코어)
            var bar = Page(root, "DefaultBar");
            Box(bar, "StripLeft", 0, 1508, 300, 1588, new Color(0.2f, 0.22f, 0.27f));
            Box(bar, "StripRight", 300, 1508, W, 1588, Light);
            var ovrText = Label(bar, "TeamOvrText", "", 20, 1508, 290, 1588, 50, TextAnchor.MiddleCenter, Color.white, true);
            Label(bar, "SetDeckCaption", "세트덱 스코어", 330, 1508, 600, 1588, 32, TextAnchor.MiddleLeft, new Color(0.45f, 0.48f, 0.55f));
            var help = Btn(bar, "HelpButton", "?", 600, 1522, 656, 1574, new Color(0.62f, 0.65f, 0.7f), Color.white, 32);
            var scoreText = Label(bar, "SetDeckScoreText", "", 670, 1508, 1066, 1588, 48, TextAnchor.MiddleRight, Ink, true);
            Box(bar, "GaugeBg", 0, 1588, W, 1786, Dark);
            var markers = new Text[6];
            var icons = new Text[6];
            var iconImages = new Image[6];
            for (int i = 0; i < 6; i++)
            {
                float cx = 20 + 1040 * (i + 0.5f) / 6f;
                Box(bar, $"MarkerBg{i}", cx - 44, 1600, cx + 44, 1648, Light);
                markers[i] = Label(bar, $"Marker{i}", "", cx - 44, 1600, cx + 44, 1648, 30, TextAnchor.MiddleCenter, new Color(0.25f, 0.3f, 0.75f), true);
                var diamond = Box(bar, $"Icon{i}", cx - 30, 1700, cx + 30, 1760, new Color(0.22f, 0.24f, 0.3f));
                diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                iconImages[i] = diamond;
                icons[i] = Label(bar, $"IconText{i}", "", cx - 60, 1700, cx + 60, 1760, 22, TextAnchor.MiddleCenter, Color.white, true);
            }
            Box(bar, "GaugeTrack", 20, 1660, 1060, 1680, new Color(0.3f, 0.32f, 0.38f));
            if (panel.Find("SetDeckGaugeFill") is RectTransform gauge) MoveTo(gauge, bar, 20, 1660, 1060, 1680);
            if (panel.Find("SetDeckStatusText") is RectTransform status) { MoveTo(status, bar, 20, 1764, 1060, 1786); status.gameObject.SetActive(false); }
            if (panel.Find("SetDeckActiveGlow") is RectTransform glow) MoveTo(glow, bar, 0, 1508, W, 1588);
            var lineupInfo = Btn(bar, "LineupInfoButton", "라인업 정보", 0, 1790, 360, 1920, new Color(0.2f, 0.21f, 0.26f), Color.white, 30);
            if (panel.Find("SetDeckOptionButton") is RectTransform option)
            {
                MoveTo(option, bar, 360, 1790, 720, 1920);
                Restyle(option, "세트덱 버프 선택", new Color(0.23f, 0.24f, 0.3f));
            }
            var synergy = Btn(bar, "SynergyButton", "시너지", 720, 1790, W, 1920, new Color(0.2f, 0.21f, 0.26f), Color.white, 30);

            // ---- 선수 액션 트레이(선택 시 기본 바 대체)
            var tray = Page(root, "ActionTray");
            var trayBg = Box(tray, "TrayBg", 0, 1508, W, H, Dark);
            trayBg.raycastTarget = true;
            Box(tray, "CardBg", 0, 1508, 262, H, new Color(0.06f, 0.07f, 0.1f));
            var trayHolder = Place(tray, "TrayCardHolder", 16, 1524, 246, 1900);
            var trayTitle = Label(tray, "TrayTitle", "", 280, 1514, 990, 1566, 28, TextAnchor.MiddleLeft, Color.white);
            var close = Btn(tray, "TrayClose", "X", 996, 1514, 1068, 1580, new Color(0f, 0f, 0f, 0.001f), new Color(0.75f, 0.78f, 0.85f), 48);
            var chipIcons = new Text[3]; var chipTitles = new Text[3]; var chipSubs = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                float cx = 280 + 790 * (i + 0.5f) / 3f;
                var shield = Box(tray, $"ChipIconBg{i}", cx - 46, 1578, cx + 46, 1670, new Color(0.18f, 0.2f, 0.27f));
                shield.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.4f, 0.85f);
                chipIcons[i] = Label(tray, $"ChipIcon{i}", "", cx - 46, 1578, cx + 46, 1670, 44, TextAnchor.MiddleCenter, Color.white, true);
                chipTitles[i] = Label(tray, $"ChipTitle{i}", "", cx - 125, 1672, cx + 125, 1708, 26, TextAnchor.MiddleCenter, Color.white);
                Box(tray, $"ChipSubBg{i}", cx - 70, 1710, cx + 70, 1746, new Color(0.04f, 0.05f, 0.07f));
                chipSubs[i] = Label(tray, $"ChipSub{i}", "", cx - 70, 1710, cx + 70, 1746, 24, TextAnchor.MiddleCenter, new Color(0.98f, 0.85f, 0.3f), true);
            }
            var detail = Btn(tray, "DetailButton", "상세 정보", 276, 1770, 534, 1900, ActionBlue, Color.white, 34);
            var manage = Btn(tray, "ManageButton", "선수 관리", 542, 1770, 800, 1900, ActionBlue, Color.white, 34);
            var swap = Btn(tray, "SwapButton", "교체", 808, 1770, 1066, 1900, ActionBlue, Color.white, 34);
            tray.gameObject.SetActive(false);

            // ---- 정보 팝업(라인업 정보 / 시너지 / ?)
            var popup = Page(root, "InfoPopup");
            var dim = Box(popup, "Dim", 0, 0, W, H, new Color(0f, 0f, 0f, 0.6f));
            dim.raycastTarget = true;
            Box(popup, "Window", 80, 380, 1000, 1480, new Color(0.08f, 0.1f, 0.2f, 0.98f)).raycastTarget = true;
            var infoTitle = Label(popup, "Title", "", 110, 400, 970, 470, 40, TextAnchor.MiddleLeft, new Color(1f, 0.82f, 0.27f), true);
            var infoBody = Label(popup, "Body", "", 110, 480, 970, 1360, 28, TextAnchor.UpperLeft, Color.white);
            infoBody.resizeTextMinSize = 16;
            var infoClose = Btn(popup, "Close", "닫기", 390, 1380, 690, 1460, new Color(0.15f, 0.33f, 0.86f), Color.white, 34);
            popup.gameObject.SetActive(false);

            foreach (var top in new[] { "CloseButton", "SwapPopup", "SetDeckOptionPanel" }) panel.Find(top)?.SetAsLastSibling();

            // ---- 바인딩
            var so = new SerializedObject(controller);
            so.FindProperty("useSlotFrames").boolValue = false;
            so.FindProperty("useCompyaLayout").boolValue = true;
            so.FindProperty("batterTabButton").objectReferenceValue = batterTab;
            so.FindProperty("pitcherTabButton").objectReferenceValue = pitcherTab;
            so.FindProperty("storageTabButton").objectReferenceValue = storageTab;
            so.FindProperty("batterPage").objectReferenceValue = batterPage.gameObject;
            so.FindProperty("pitcherPage").objectReferenceValue = pitcherPage.gameObject;
            so.FindProperty("storagePage").objectReferenceValue = storagePage.gameObject;
            so.FindProperty("storageContainer").objectReferenceValue = storageContent;
            so.FindProperty("storageInfoText").objectReferenceValue = storageInfo;
            SetArray(so.FindProperty("diamondSlots"), diamondSlots);
            so.FindProperty("diamondRoot").objectReferenceValue = diamondRoot.gameObject;
            so.FindProperty("fullViewRoot").objectReferenceValue = fullRoot.gameObject;
            so.FindProperty("fullViewContainer").objectReferenceValue = fullGrid;
            so.FindProperty("benchContainer").objectReferenceValue = benchGrid;
            so.FindProperty("batterContainer").objectReferenceValue = fullGrid;
            so.FindProperty("pitcherContainer").objectReferenceValue = spGrid;
            so.FindProperty("startingPitcherContainer").objectReferenceValue = spGrid;
            so.FindProperty("bullpenContainer").objectReferenceValue = columns[1];
            SetArray(so.FindProperty("bullpenColumns"), columns);
            so.FindProperty("basicViewButton").objectReferenceValue = basic;
            so.FindProperty("fullViewButton").objectReferenceValue = full;
            so.FindProperty("defenseChangeButton").objectReferenceValue = defense;
            so.FindProperty("battingOrderButton").objectReferenceValue = order;
            so.FindProperty("toolbarHintText").objectReferenceValue = hint;
            so.FindProperty("defaultBarRoot").objectReferenceValue = bar.gameObject;
            SetArray(so.FindProperty("gaugeMarkerTexts"), markers);
            SetArray(so.FindProperty("gaugeIconTexts"), icons);
            SetArray(so.FindProperty("gaugeIconImages"), iconImages);
            so.FindProperty("helpButton").objectReferenceValue = help;
            so.FindProperty("lineupInfoButton").objectReferenceValue = lineupInfo;
            so.FindProperty("synergyButton").objectReferenceValue = synergy;
            so.FindProperty("teamOvrText").objectReferenceValue = ovrText;
            so.FindProperty("setDeckScoreText").objectReferenceValue = scoreText;
            so.FindProperty("setDeckTeamLogo").objectReferenceValue = null;
            so.FindProperty("setDeckTeamText").objectReferenceValue = null;
            so.FindProperty("lineupHeaderText").objectReferenceValue = null;
            so.FindProperty("benchHeaderText").objectReferenceValue = null;
            so.FindProperty("startingPitcherHeaderText").objectReferenceValue = null;
            so.FindProperty("bullpenHeaderText").objectReferenceValue = null;
            so.FindProperty("emptyStateText").objectReferenceValue = null;
            so.FindProperty("trayRoot").objectReferenceValue = tray.gameObject;
            so.FindProperty("trayCardHolder").objectReferenceValue = trayHolder;
            so.FindProperty("trayTitleText").objectReferenceValue = trayTitle;
            SetArray(so.FindProperty("trayChipTitles"), chipTitles);
            SetArray(so.FindProperty("trayChipSubs"), chipSubs);
            SetArray(so.FindProperty("trayChipIcons"), chipIcons);
            so.FindProperty("trayCloseButton").objectReferenceValue = close;
            so.FindProperty("trayDetailButton").objectReferenceValue = detail;
            so.FindProperty("trayManageButton").objectReferenceValue = manage;
            so.FindProperty("traySwapButton").objectReferenceValue = swap;
            so.FindProperty("infoPopupRoot").objectReferenceValue = popup.gameObject;
            so.FindProperty("infoTitleText").objectReferenceValue = infoTitle;
            so.FindProperty("infoBodyText").objectReferenceValue = infoBody;
            so.FindProperty("infoCloseButton").objectReferenceValue = infoClose;
            so.FindProperty("slotFont").objectReferenceValue = KBOFonts.Medium;
            so.FindProperty("slotBoldFont").objectReferenceValue = KBOFonts.Bold;
            so.FindProperty("tabActiveColor").colorValue = TabBlue;
            so.FindProperty("tabInactiveColor").colorValue = Light;
            var skillGuid = AssetDatabase.FindAssets("t:SkillDB").FirstOrDefault();
            if (skillGuid != null) so.FindProperty("skillDB").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KBOManager.Data.SkillDB>(AssetDatabase.GUIDToAssetPath(skillGuid));
            so.ApplyModifiedProperties();
            MarkDirty(controller);
        }

        /// <summary>세로 스크롤 그리드(보관 선수) - Viewport(Mask) + Content(GridLayoutGroup + ContentSizeFitter).</summary>
        private static RectTransform ScrollGrid(RectTransform parent, string name, float x0, float y0, float x1, float y1, int columns, float cellHeight)
        {
            var viewport = Place(parent, name, x0, y0, x1, y1);
            var image = viewport.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            float width = x1 - x0;
            grid.cellSize = new Vector2((width - 16 - 8 * (columns - 1)) / columns, cellHeight);
            grid.spacing = new Vector2(8f, 10f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            return content;
        }

        // ================================================================== 2) 상세 정보 패널 → 캔버스 최상위

        public static void MoveDetailPanelToCanvas()
        {
            var detail = Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            if (detail == null) return;
            var root = new SerializedObject(detail).FindProperty("panelRoot").objectReferenceValue as GameObject;
            var canvas = root != null ? root.GetComponentInParent<Canvas>(true) : null;
            if (root == null || canvas == null) return;
            var rect = (RectTransform)root.transform;
            if (rect.parent != canvas.transform) Undo.SetTransformParent(rect, canvas.transform, "Detail Panel To Canvas");
            Undo.RecordObject(rect, "Detail Panel Stretch");
            StretchFull(rect);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
            MarkDirty(detail);
        }

        // ================================================================== 3) 세트덱 선택형 버프 색

        public static void StyleSetDeckOptions()
        {
            var option = Object.FindAnyObjectByType<SetDeckOptionUIController>(FindObjectsInactive.Include);
            if (option == null) return;
            var so = new SerializedObject(option);
            so.FindProperty("selectedColor").colorValue = new Color(1f, 0.82f, 0.2f);
            so.FindProperty("unselectedColor").colorValue = new Color(0.2f, 0.22f, 0.29f);
            so.FindProperty("selectedTextColor").colorValue = new Color(0.08f, 0.09f, 0.14f);
            so.FindProperty("unselectedTextColor").colorValue = new Color(0.55f, 0.59f, 0.68f);
            so.FindProperty("reachedLabelColor").colorValue = new Color(0.45f, 0.95f, 0.65f);
            so.FindProperty("unreachedLabelColor").colorValue = new Color(0.62f, 0.66f, 0.76f);
            so.ApplyModifiedProperties();
            MarkDirty(option);
        }

        // ================================================================== 4) 경기 재생(BroadcastUIManager)

        public static void EnsureBroadcastManager()
        {
            var inGame = Object.FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
            if (inGame == null)
            {
                Debug.LogWarning("[SetupTask182] InGameUIController가 없어 BroadcastUIManager를 만들지 못했습니다.");
                return;
            }
            var broadcast = Object.FindAnyObjectByType<BroadcastUIManager>(FindObjectsInactive.Include);
            if (broadcast == null)
            {
                // 자식 버튼이 없는 전용 오브젝트에 둔다 - BroadcastUIManager가 스킵 버튼을 자식에서 자동 탐색하다 엉뚱한 버튼을 잡지 않도록.
                var go = new GameObject("BroadcastManager182", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create BroadcastUIManager");
                go.transform.SetParent(inGame.transform, false);
                broadcast = Undo.AddComponent<BroadcastUIManager>(go);
            }
            var bso = new SerializedObject(broadcast);
            bso.FindProperty("inGameUIController").objectReferenceValue = inGame;
            bso.ApplyModifiedProperties();
            MarkDirty(broadcast);

            var playBall = Object.FindAnyObjectByType<PlayBallController>(FindObjectsInactive.Include);
            if (playBall != null)
            {
                var pso = new SerializedObject(playBall);
                pso.FindProperty("broadcastUIManager").objectReferenceValue = broadcast;
                pso.ApplyModifiedProperties();
                MarkDirty(playBall);
            }
            var view = Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            if (view != null)
            {
                var vso = new SerializedObject(view);
                vso.FindProperty("broadcastUIManager").objectReferenceValue = broadcast;
                vso.ApplyModifiedProperties();
                MarkDirty(view);
            }
        }
    }
}
