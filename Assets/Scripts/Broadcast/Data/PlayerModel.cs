using System;

namespace KBOManager.Broadcast.Data
{
    /// <summary>
    /// players.csv 한 행에 대응하는 원시 데이터 클래스. 스탯티즈 원시 지표를 베이지안 보정한
    /// Z-score 6종을 그대로 들고 있으며, 여기서 등급/강화 등 유저 육성 상태는 다루지 않는다
    /// (그건 CardModel/유저 세이브 쪽 책임). Position은 enum이 아닌 문자열로 보관해 CSV에
    /// 새 포지션 코드가 추가돼도 파싱 단계에서 크래시가 나지 않는다.
    /// </summary>
    [Serializable]
    public class PlayerModel
    {
        public string PlayerId;
        public string TeamId;
        public string Name;
        public int Year;
        public string Position;
        public int PaOrIp; // 타자는 타석수(PA), 투수는 이닝(IP) - CSV 컬럼명(pa_ip)을 그대로 따름

        // 스탯티즈 원시 데이터의 베이지안 Z-score. StatCalculator.GetOVR()의 입력값.
        public float ZContact;
        public float ZEye;
        public float ZPower;
        public float ZSpeed;
        public float ZDef;
        public float ZStamina;

        public bool Active;

        public bool IsPitcher => Position == "SP" || Position == "CP" || Position == "RP";
    }
}
