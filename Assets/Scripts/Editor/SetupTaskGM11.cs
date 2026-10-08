using KBOManager.Controllers;
using KBOManager.Services;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-11] 씬 점검(씬 변경 없음, SetupMasterBinding 체인) - 프런트 오피스 허브(SetupTaskGM06이 다시 조립)에 시즌 결산실 · 계약 협상실 화면이 있는지 확인하고
    /// 3지선다 협상 상수를 로그로 남긴다.
    /// </summary>
    public static class SetupTaskGM11
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-GM-11 (Season Review + Negotiation Room)")]
        public static void ApplyAll()
        {
            var hub = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            var root = hub != null ? hub.transform.Find(GMOotpFrontOfficeUIController.RootName) : null;
            string area = GMOotpFrontOfficeUIController.ContentAreaName;
            bool summary = root != null && root.Find($"{area}/{GMOotpFrontOfficeUIController.PaneSeasonSummary}") != null;
            bool negotiation = root != null && root.Find($"{area}/{GMOotpFrontOfficeUIController.PaneNegotiation}") != null;
            Debug.Log($"[SetupTaskGM11] TASK-GM-11 점검 - 시즌 결산실 {(summary ? "있음" : "없음")} · 계약 협상실 {(negotiation ? "있음" : "없음")} · " +
                      $"협상 카드 기본 +{GMNegotiationRoom.CardBaseBonus * 100:0}%p × 성향 {GMNegotiationRoom.MinArchetypeMultiplier}~{GMNegotiationRoom.MaxArchetypeMultiplier} · " +
                      $"후보 풀 {GMNegotiationRoom.PoolMin}~{GMNegotiationRoom.PoolMax}장 중 {GMNegotiationRoom.OfferCount}장 제시");
        }
    }
}
