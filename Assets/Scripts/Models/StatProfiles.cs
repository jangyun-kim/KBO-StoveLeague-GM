namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-180] 세부 능력치 분포(편차) 규칙 - "OVR 89 = 5개 스탯 전부 89" 평준화 버그의 재발 방지용 공용 기준표.
    ///
    /// OVR 산출 공식은 그대로다(Player.AverageOf / PlayerTemplate.GetBaseOverall): 타자 OVR = (파워 + 정확 + 선구) / 3,
    /// 투수 OVR = (구위 + 구속 + 변화 + 제구) / 4. 주력·수비(타자)와 체력(투수)은 OVR 계산에 들어가지 않는 보조 스탯이다.
    /// 아래 프로파일은 OVR 구성 스탯의 오프셋 합이 0이 되도록 설계해 "평균 = 목표 OVR"을 정확히 지키면서 포지션·보직 고유의
    /// 편차를 만든다(포수/1루/지명 = 파워형, 2루/유격/중견 = 정확·주력·수비형, 선발 = 체력·제구 균형, 마무리 = 구위·구속형).
    /// GenerateKBODatabase.py의 BATTER_POSITION_PROFILE / PITCHER_ROLE_PROFILE과 같은 수치다(실존 선수는 생성기가 타이틀 부문
    /// 특화와 선수별 고정 편차까지 더해 players.csv에 쓴다 - 이 클래스는 DB에 없는 절차 생성 선수(AI/스타터/배치 시뮬레이터)용).
    /// </summary>
    public static class StatProfiles
    {
        // (파워, 정확, 선구 | 합 0), 주력, 수비 - 레벨 대비 오프셋
        private static (int p, int c, int d, int speed, int def) BatterOffsets(BatterPosition position)
        {
            switch (position)
            {
                case BatterPosition.Catcher: return (3, -1, -2, -14, 6);
                case BatterPosition.FirstBase: return (6, -1, -5, -10, -2);
                case BatterPosition.SecondBase: return (-6, 4, 2, 6, 5);
                case BatterPosition.ThirdBase: return (3, 0, -3, -3, 4);
                case BatterPosition.ShortStop: return (-5, 3, 2, 7, 8);
                case BatterPosition.LeftField: return (3, 1, -4, 0, -2);
                case BatterPosition.CenterField: return (-5, 3, 2, 10, 7);
                case BatterPosition.RightField: return (3, 0, -3, 1, 2);
                default: return (6, 0, -6, -12, -15); // DH
            }
        }

        // (구위, 구속, 변화, 제구 | 합 0), 체력
        private static (int stuff, int velocity, int movement, int control, int stamina) PitcherOffsets(PitcherRole role)
        {
            switch (role)
            {
                case PitcherRole.StartingPitcher: return (1, -1, -1, 1, 10);
                case PitcherRole.Closer: return (6, 6, -5, -7, -25);
                case PitcherRole.LongReliever: return (-1, -2, 2, 1, -5);
                default: return (3, 4, -3, -4, -18); // 승리조/추격조 불펜
            }
        }

        /// <summary>level(목표 OVR)을 포지션 프로파일로 분배한다. seed가 같으면 결과도 같다(±2 개인 편차).</summary>
        public static BatterStats SpreadBatter(int level, BatterPosition position, int seed = 0)
        {
            var o = BatterOffsets(position);
            int j1 = Jitter(seed, 1), j2 = Jitter(seed, 2);
            int[] core = Distribute(level, new[] { o.p + j1, o.c - j1 + j2, o.d - j2 });
            int speed = Clamp(level + o.speed + Jitter(seed, 3)), defense = Clamp(level + o.def + Jitter(seed, 4));
            // 극단값(레벨 100 등)에서 OVR 구성 스탯이 전부 상한에 붙으면 보조 스탯으로 편차를 남긴다(전 스탯 동일 금지).
            if (core[0] == core[1] && core[1] == core[2] && speed == core[0] && defense == core[0]) speed = Clamp(speed - 3);
            return new BatterStats(core[0], core[1], core[2], speed, defense);
        }

        public static PitcherStats SpreadPitcher(int level, PitcherRole role, int seed = 0)
        {
            var o = PitcherOffsets(role);
            int j1 = Jitter(seed, 1), j2 = Jitter(seed, 2);
            int[] core = Distribute(level, new[] { o.stuff + j1, o.velocity - j1, o.movement + j2, o.control - j2 });
            int stamina = Clamp(level + o.stamina + Jitter(seed, 3));
            if (core[0] == core[1] && core[1] == core[2] && core[2] == core[3] && stamina == core[0]) stamina = Clamp(stamina - 3);
            return new PitcherStats(core[0], core[1], core[2], core[3], stamina);
        }

        /// <summary>OVR 구성 스탯: level + 오프셋을 1~100으로 자르고, 잘린 만큼을 다른 스탯에 1씩 되돌려 합(= 평균 OVR)을 정확히 보존한다.</summary>
        public static int[] Distribute(int level, int[] offsets)
        {
            level = Clamp(level);
            var stats = new int[offsets.Length];
            int target = level * offsets.Length, sum = 0;
            for (int i = 0; i < offsets.Length; i++) { stats[i] = Clamp(level + offsets[i]); sum += stats[i]; }
            for (int guard = 0; sum != target && guard < 1000; guard++)
            {
                int i = guard % stats.Length;
                if (sum < target && stats[i] < 100) { stats[i]++; sum++; }
                else if (sum > target && stats[i] > 1) { stats[i]--; sum--; }
            }
            return stats;
        }

        private static int Clamp(int value) => value < 1 ? 1 : value > 100 ? 100 : value;

        /// <summary>시드 기반 결정론적 -2~+2 편차(UnityEngine.Random 미사용 - 재현성/테스트 용이).</summary>
        private static int Jitter(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)(h % 5) - 2;
            }
        }
    }
}
