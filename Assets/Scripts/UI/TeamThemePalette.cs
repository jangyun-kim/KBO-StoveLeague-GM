using KBOManager.Models;
using UnityEngine;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-GM-02] 10구단 대표색 팔레트(지시서 지정 값). Primary = 대시보드 헤더 · 강조 행 · 활성 탭, Secondary = 보조 포인트.
    /// 삼성 #074CA1 · LG #C30452 · KIA #EA0029 · 한화 #F37321 · 두산 #131230 · KT 블랙/레드 · SSG 레드 · NC #315288 · 롯데 네이비/레드 · 키움 #570514.
    /// </summary>
    public static class TeamThemePalette
    {
        public static Color Primary(Team team)
        {
            switch (team)
            {
                case Team.Samsung: return Hex(0x07, 0x4C, 0xA1);
                case Team.LG: return Hex(0xC3, 0x04, 0x52);
                case Team.KIA: return Hex(0xEA, 0x00, 0x29);
                case Team.Hanwha: return Hex(0xF3, 0x73, 0x21);
                case Team.Doosan: return Hex(0x13, 0x12, 0x30);
                case Team.KT: return Hex(0x1A, 0x1A, 0x1A);
                case Team.SSG: return Hex(0xCE, 0x0E, 0x2D);
                case Team.NC: return Hex(0x31, 0x52, 0x88);
                case Team.Lotte: return Hex(0x04, 0x1E, 0x42);
                case Team.Kiwoom: return Hex(0x57, 0x05, 0x14);
                default: return Hex(0x33, 0x3A, 0x48);
            }
        }

        public static Color Secondary(Team team)
        {
            switch (team)
            {
                case Team.KT: return Hex(0xEB, 0x1C, 0x24);
                case Team.Lotte: return Hex(0xD0, 0x0F, 0x31);
                case Team.Doosan: return Hex(0xED, 0x1C, 0x24);
                case Team.Samsung: return Hex(0xC0, 0xC0, 0xC0);
                case Team.NC: return Hex(0xC7, 0xA0, 0x79);
                case Team.Kiwoom: return Hex(0xD8, 0x9B, 0xA6);
                default: return Hex(0xFF, 0xD5, 0x4A);
            }
        }

        /// <summary>배경(대표색을 어둡게 - 흰 글씨 대비 확보).</summary>
        public static Color Background(Team team)
        {
            var c = Primary(team);
            return new Color(c.r * 0.35f + 0.03f, c.g * 0.35f + 0.03f, c.b * 0.35f + 0.05f, 1f);
        }

        private static Color Hex(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 0xFF);
    }
}
