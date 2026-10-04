using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-187] 치어리더 관리 화면의 [응원단 성장] - 강화(+0~+10강, 포인트) · ★각성(동일 인물 +2★ / 동일 구단 +1★, 최대 ★5) · 구단 응원단 도감.
    /// 화면 아래쪽 띠(도감 요약 + [응원단 성장] 버튼)와 전체 덮개 패널을 코드로 만든다(Setup과 런타임 Awake가 같은 Build()).
    /// 규칙은 CheerGrowth(단위 테스트 대상), 실행은 GameManager.TryReinforceCheerleader / TryAwakenCheerleader.
    /// </summary>
    public class CheerGrowthView : MonoBehaviour
    {
        public const string StripName = "CheerGrowthStrip187";
        public const string OverlayName = "CheerGrowthOverlay187";

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;
        [Tooltip("띠를 붙일 부모(치어리더 화면 Layout178). 비우면 이 오브젝트.")]
        [SerializeField] private RectTransform stripParent;

        private static readonly Color Bg = new Color(0.05f, 0.07f, 0.13f, 0.98f);
        private static readonly Color Panel = new Color(0.11f, 0.14f, 0.24f);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Muted = new Color(0.68f, 0.73f, 0.84f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Blue = new Color(0.18f, 0.42f, 0.95f);
        private static readonly Color Purple = new Color(0.55f, 0.3f, 0.85f);
        private static readonly Color Grey = new Color(0.3f, 0.33f, 0.42f);

        private CompyaUiKit kit;
        private RectTransform strip, overlay;
        private Text stripText, nameText, growthText, effectText, reinforceInfo, awakenInfo, statusText, collectionText;
        private Button reinforceButton, awakenButton;
        private int targetIndex;
        private string status = "";

        public void Configure(Font bold, Font regular, RectTransform stripHost)
        {
            boldFont = bold;
            regularFont = regular;
            stripParent = stripHost;
        }

        private void Awake() => Build();

        private void OnEnable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnCheerleaderChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnCheerleaderChanged -= Refresh;
        }

        public void Build()
        {
            kit = new CompyaUiKit(boldFont, regularFont);
            var host = stripParent != null ? stripParent : (RectTransform)transform;
            Remove(host.Find(StripName));
            Remove(transform.Find(OverlayName));

            // ---- 띠: 도감 요약 + [응원단 성장]
            strip = new GameObject(StripName, typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(host, false);
            strip.anchorMin = new Vector2(0.03f, 0.432f);
            strip.anchorMax = new Vector2(0.97f, 0.482f);
            strip.offsetMin = strip.offsetMax = Vector2.zero;
            CompyaUiKit.Paint(strip, Panel);
            stripText = kit.LabelOn(CompyaUiKit.Norm(strip, "Collection", 0.02f, 0f, 0.66f, 1f), "", 26, TextAnchor.MiddleLeft, White, true);
            var open = MakeButton(CompyaUiKit.Norm(strip, "OpenGrowth", 0.68f, 0.1f, 0.99f, 0.9f), "응원단 성장 ★", Purple, 28);
            open.onClick.AddListener(() => Show(true));

            // ---- 덮개 패널
            overlay = CompyaUiKit.Fill(transform, OverlayName);
            CompyaUiKit.Paint(overlay, Bg, true);
            kit.Label(overlay, "Title", "응원단 성장 · 도감", 60, 40, 1188, 130, 50, TextAnchor.MiddleLeft, Gold, true);
            kit.Button(overlay, "Close", "X", 1100, 45, 1190, 125, Grey, White, 46).onClick.AddListener(() => Show(false));

            CompyaUiKit.Box(overlay, "TargetBox", 60, 150, 1188, 700, Panel);
            kit.Button(overlay, "Prev", "◀", 80, 170, 180, 290, Grey, White, 46).onClick.AddListener(() => Step(-1));
            kit.Button(overlay, "Next", "▶", 1068, 170, 1168, 290, Grey, White, 46).onClick.AddListener(() => Step(1));
            nameText = kit.Label(overlay, "Name", "", 200, 165, 1048, 235, 44, TextAnchor.MiddleCenter, White, true);
            growthText = kit.Label(overlay, "Growth", "", 200, 235, 1048, 295, 40, TextAnchor.MiddleCenter, Gold, true);
            effectText = kit.Label(overlay, "Effect", "", 90, 310, 1158, 690, 30, TextAnchor.UpperLeft, White, false);

            CompyaUiKit.Box(overlay, "ReinforceBox", 60, 720, 610, 1060, Panel);
            kit.Label(overlay, "ReinforceTitle", "① 응원단 강화 (+0 ~ +10강)", 80, 730, 590, 790, 32, TextAnchor.MiddleLeft, Gold, true);
            reinforceInfo = kit.Label(overlay, "ReinforceInfo", "", 80, 795, 590, 950, 26, TextAnchor.UpperLeft, White, false);
            reinforceButton = kit.Button(overlay, "Reinforce", "강화", 80, 960, 590, 1045, Blue, White, 34);
            reinforceButton.onClick.AddListener(OnReinforce);

            CompyaUiKit.Box(overlay, "AwakenBox", 638, 720, 1188, 1060, Panel);
            kit.Label(overlay, "AwakenTitle", "② ★ 각성 (★1 ~ ★5)", 658, 730, 1168, 790, 32, TextAnchor.MiddleLeft, Gold, true);
            awakenInfo = kit.Label(overlay, "AwakenInfo", "", 658, 795, 1168, 950, 26, TextAnchor.UpperLeft, White, false);
            awakenButton = kit.Button(overlay, "Awaken", "각성", 658, 960, 1168, 1045, Purple, White, 34);
            awakenButton.onClick.AddListener(OnAwaken);

            statusText = kit.Label(overlay, "Status", "", 60, 1075, 1188, 1150, 30, TextAnchor.MiddleCenter, Gold, true);

            CompyaUiKit.Box(overlay, "CollectionBox", 60, 1165, 1188, 1930, Panel);
            kit.Label(overlay, "CollectionTitle", "③ 구단 응원단 도감 (수집 1명당 홈 관중 수익 +1%)", 80, 1175, 1168, 1235, 32, TextAnchor.MiddleLeft, Gold, true);
            collectionText = kit.Label(overlay, "CollectionList", "", 80, 1240, 1168, 1920, 28, TextAnchor.UpperLeft, White, false);

            overlay.gameObject.SetActive(false);
            Refresh();
        }

        private static void Remove(Transform t)
        {
            if (t == null) return;
            t.SetParent(null, false);
            if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
        }

        private Button MakeButton(RectTransform rect, string text, Color color, float size)
        {
            var image = CompyaUiKit.Paint(rect, color, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            kit.LabelOn(CompyaUiKit.Fill(rect, "Text"), text, size, TextAnchor.MiddleCenter, White, true);
            return button;
        }

        public void Show(bool on)
        {
            if (overlay == null) return;
            overlay.gameObject.SetActive(on);
            if (on) { overlay.SetAsLastSibling(); status = ""; Refresh(); }
        }

        /// <summary>성장 대상 순서 - 6인 편성(슬롯 순) 먼저, 그다음 등급 · 성장 높은 순.</summary>
        public static List<Cheerleader> Targets(IEnumerable<Cheerleader> owned, IReadOnlyList<Cheerleader> squad)
        {
            var list = (owned ?? Enumerable.Empty<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c)).ToList();
            var squadIds = (squad ?? new List<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c)).Select(c => c.InstanceId).ToList();
            return list.OrderBy(c => squadIds.IndexOf(c.InstanceId) is int i && i >= 0 ? i : 99)
                .ThenByDescending(c => c.Grade).ThenByDescending(CheerGrowth.Stars).ThenByDescending(CheerGrowth.Reinforce).ThenBy(c => c.Name).ToList();
        }

        private Cheerleader Current(out List<Cheerleader> targets)
        {
            var gm = GameManager.Instance;
            targets = gm != null ? Targets(gm.OwnedCheerleaders, gm.CheerSquadSlots) : new List<Cheerleader>();
            if (targets.Count == 0) return null;
            targetIndex = ((targetIndex % targets.Count) + targets.Count) % targets.Count;
            return targets[targetIndex];
        }

        private void Step(int delta)
        {
            targetIndex += delta;
            status = "";
            Refresh();
        }

        private void OnReinforce()
        {
            var gm = GameManager.Instance;
            var target = Current(out _);
            if (gm == null || target == null) return;
            gm.TryReinforceCheerleader(target, out status);
            Refresh();
        }

        private void OnAwaken()
        {
            var gm = GameManager.Instance;
            var target = Current(out _);
            if (gm == null || target == null) return;
            var material = CheerGrowth.AwakenMaterials(target, gm.OwnedCheerleaders, gm.CheerSquadSlots).FirstOrDefault();
            if (material == null)
            {
                CheerGrowth.CanAwaken(target, null, gm.CheerSquadSlots, out status);
                if (CheerGrowth.Stars(target) < CheerGrowth.MaxStars) status = "각성 재료가 없습니다 - 동일 인물(+2★) 또는 동일 구단(+1★) 치어리더를 영입하십시오.";
                Refresh();
                return;
            }
            gm.TryAwakenCheerleader(target, material, out status);
            Refresh();
        }

        public void Refresh()
        {
            if (strip == null) return;
            var gm = GameManager.Instance;
            var catalog = CheerGrowth.FullCatalog();
            var favorite = gm != null ? gm.FavoriteTeam : Team.None;
            var owned = gm != null ? gm.OwnedCheerleaders : new List<Cheerleader>();
            if (stripText != null)
                stripText.text = favorite != Team.None ? CheerGrowth.Collection(favorite, owned, catalog).Label : $"보유 치어리더 {owned.Count}명 · 선택 구단을 정하면 도감 보너스가 적용됩니다";
            if (overlay == null || !overlay.gameObject.activeSelf) return;

            var target = Current(out var targets);
            if (target == null)
            {
                nameText.text = "보유한 치어리더가 없습니다";
                growthText.text = effectText.text = reinforceInfo.text = awakenInfo.text = "";
                reinforceButton.interactable = awakenButton.interactable = false;
            }
            else
            {
                var slot = gm.FindSlotOf(target);
                nameText.text = $"[{CheerleaderSlotUI.TierLabel(target.Grade)}] {target.Name}  ·  {(string.IsNullOrEmpty(target.AffiliationLabel) ? "구단 무관" : target.AffiliationLabel)}  ({targetIndex + 1}/{targets.Count})";
                growthText.text = $"{CheerGrowth.StarBadge(target)}   +{CheerGrowth.Reinforce(target)}강   효과 티어 {CheerGrowth.EffectiveTier(target)}";
                var lines = new List<string>
                {
                    slot.HasValue ? $"편성: {(int)slot.Value + 1}.{CheerSquad.RoleName(slot.Value)} - {CheerSquad.DescribeRoleEffect(slot.Value, target)}" : "편성: 미배치 (6인 응원단에 배치해야 경기 효과가 발동합니다)",
                    "역할별 성장 효과(배치 시):",
                };
                lines.AddRange(CheerSquad.Roles.Select(r => $"  · {CheerSquad.RoleName(r)}: {CheerSquad.DescribeRoleEffect(r, target)}"));
                lines.Add("※ 경기 효과 · 세트덱 보강 · 관중 수익 · 팬심 방어만 오르며 구단 OVR에는 더해지지 않습니다.");
                effectText.text = string.Join("\n", lines);

                bool maxed = CheerGrowth.Reinforce(target) >= CheerGrowth.MaxReinforce;
                int cost = CheerGrowth.ReinforceCost(target.ReinforceLevel);
                reinforceInfo.text = maxed
                    ? $"+{CheerGrowth.MaxReinforce}강 달성 (최대)"
                    : $"+{CheerGrowth.Reinforce(target)}강 → +{CheerGrowth.Reinforce(target) + 1}강\n비용 {cost:N0} 포인트 (보유 {gm.GameGold:N0})\n강화마다 역할 효과 비례 상승 · 홈 응원 관중 수익 +2%p";
                reinforceButton.interactable = !maxed && gm.GameGold >= cost;
                CompyaUiKit.SetButtonText(reinforceButton, maxed ? "최대 강화" : $"강화 (-{cost:N0})");

                var materials = CheerGrowth.AwakenMaterials(target, owned, gm.CheerSquadSlots);
                var best = materials.FirstOrDefault();
                bool starMax = CheerGrowth.Stars(target) >= CheerGrowth.MaxStars;
                awakenInfo.text = starMax
                    ? "★5 최종 각성 완료"
                    : best != null
                        ? $"재료 {materials.Count}장 보유 · 다음 재료: {best.Name} ({best.Team}) → +{CheerGrowth.AwakenGain(target, best)}★\n동일 인물 +2★ / 동일 구단 +1★\n★3 · ★5 달성 시 효과 티어 도약"
                        : "재료 없음 - 동일 인물(+2★) / 동일 구단(+1★) 카드가 필요합니다\n★3 · ★5 달성 시 효과 티어 도약";
                awakenButton.interactable = !starMax && best != null;
            }
            statusText.text = status ?? "";

            var teams = System.Enum.GetValues(typeof(Team)).Cast<Team>().Where(t => t != Team.None);
            collectionText.text = string.Join("\n", teams.Select(t =>
            {
                var c = CheerGrowth.Collection(t, owned, catalog);
                return (t == favorite ? "▶ " : "   ") + c.Label;
            }));
        }
    }
}
