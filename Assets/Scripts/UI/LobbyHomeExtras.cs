using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-180] 메인 홈 헤더(구단명#·레벨)·대표 선수 명판(레퍼런스 중앙 3D 선수 위치)·시그니처 획득 아이콘 수치 갱신.
    /// 대표 선수 = 현재 로스터 최고 OVR 카드. 레벨 = 시즌 진행 경기 수 기반(10경기당 1, 최소 1).
    /// </summary>
    public class LobbyHomeExtras : MonoBehaviour
    {
        [SerializeField] private Text profileNameText;
        [SerializeField] private Text levelText;
        [SerializeField] private Text heroNameText;
        [SerializeField] private Text heroDetailText;
        [SerializeField] private Text signatureText;
        [SerializeField] private Image teamBox;
        [SerializeField] private RawImage teamLogo;

        public void Bind(Text profileName, Text level, Text heroName, Text heroDetail, Text signature, Image box, RawImage logo)
        {
            teamBox = box;
            teamLogo = logo;
            profileNameText = profileName;
            levelText = level;
            heroNameText = heroName;
            heroDetailText = heroDetail;
            signatureText = signature;
        }

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            var gm = GameManager.Instance;
            var league = LeagueManager.Instance;
            var team = gm != null && gm.FavoriteTeam != Team.None ? gm.FavoriteTeam : (league != null ? league.UserTeam : Team.None);
            if (profileNameText != null) profileNameText.text = $"{CompyaUiKit.ShortName(team)}#단장";
            if (teamBox != null) teamBox.color = CompyaUiKit.TeamColor(team);
            CompyaUiKit.SetLogo(teamLogo, team);
            if (levelText != null)
            {
                int played = league != null ? league.PlayedGameCount : 0;
                levelText.text = $"레벨 <color=#E8337A><size=40>{Mathf.Max(1, 1 + played / 10)}</size></color>";
            }

            var hero = gm?.Roster?.Where(p => p?.Template != null).OrderByDescending(p => p.CalculateOVR(false)).FirstOrDefault();
            if (heroNameText != null) heroNameText.text = hero != null ? Controllers.CompyaMatchView.DisplayName(hero) : "대표 선수 없음";
            if (heroDetailText != null)
                heroDetailText.text = hero != null
                    ? $"{Controllers.CompyaMatchView.PositionLabel(hero)} · OVR {hero.CalculateOVR(false)} · {hero.Template.Grade}"
                    : "라인업을 편성하십시오";
            if (signatureText != null) signatureText.text = gm != null ? $"시그니처 볼 {gm.SignatureBall}" : "시그니처";
        }
    }
}
