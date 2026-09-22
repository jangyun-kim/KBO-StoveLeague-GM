using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-122] 삼성 라이온즈 실선수 데이터 시딩.
    ///
    /// [사실 정정] 명령서 1항/4항이 지목한 `PlayerDataSeeder.cs`는 이 태스크 이전에는 존재하지 않았다
    /// (신규 작성). `PlayerDatabase.cs`도 명령서가 적은 `Assets/Scripts/Data/`가 아니라
    /// `Assets/Scripts/Managers/`에 있다. 선수 데이터의 실제 SSOT는 `.asset`이 아니라
    /// `Assets/Resources/Data/players.csv`(`PlayerDatabase.ResourcePath = "Data/players"`가 참조)이며,
    /// 기존에는 4행(`구자욱`/`원태인`이 `TEM_001`=KIA로, `오승환`이 `TEM_003`=LG로, `노시환`이
    /// `TEM_002`=삼성으로 - 전부 실제 소속과 다른 구단에 잘못 배정)만 들어 있었다(콘솔 로그
    /// "템플릿 4개를 로드했습니다"의 근거). 이 파일은 그 4행을 지우고 삼성 라이온즈 11명(타자 7 +
    /// 투수 4)으로 전부 교체한다.
    ///
    /// [데이터 구조 조사] `PlayerTemplate.GetBaseOverall()`(원문 46~51행)은 OVR을 직접 저장하지 않고
    /// 타자는 (Power+Contact+Discipline)/3, 투수는 (Stuff+Velocity+Movement+Control)/4의 평균으로
    /// 계산한다. 각 세부 스탯은 `PlayerDatabase.ConvertZScoreToStat()`(원문 232~233행,
    /// `stat = round(z*15+50)`)로 CSV의 Z-score 컬럼에서 변환된다 - 그래서 이 파일은 명령서가 지정한
    /// 목표 OVR을 역산(`z = (OVR-50)/15`)해 관련 세부 스탯 컬럼 전부에 동일한 Z-score를 채운다
    /// (`LeagueManager.CreateProceduralTemplate()`이 AI 카드의 5개 세부 스탯을 전부 동일한 statLevel로
    /// 채우는 것과 같은 관례). 타자는 `z_contact`/`z_eye`/`z_power`(+ OVR과 무관한 `z_speed`/`z_def`/
    /// `z_stamina`도 동일값), 투수는 `z_speed`(→Velocity)/`z_stuff`/`z_control`/`z_movement`(+ OVR과
    /// 무관한 `z_stamina`도 동일값)에 채운다.
    /// </summary>
    public static class PlayerDataSeeder
    {
        private const string CsvPath = "Assets/Resources/Data/players.csv";

        // PlayerDatabase.TeamIdMapping(원문 47~59행) 확인 - "TEM_002" = Team.Samsung.
        private const string SamsungTeamId = "TEM_002";
        private const string SeasonYear = "2024";

        private const string CsvHeader =
            "player_id,team_id,name,year,position,pa_ip,z_contact,z_eye,z_power,z_speed,z_def,z_stamina,active,z_stuff,z_control,z_movement";

        private readonly struct PlayerSeed
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string Position;
            public readonly int TargetOvr;
            public readonly int PaIp;

            public PlayerSeed(string id, string name, string position, int targetOvr, int paIp)
            {
                Id = id;
                Name = name;
                Position = position;
                TargetOvr = targetOvr;
                PaIp = paIp;
            }
        }

        // 명령서 4항이 지정한 타자 7명. PaIp(타석수)는 OVR 계산에 관여하지 않는(원문 확인 - PlayerDatabase.
        // ParsePlayersCsv()가 columns[5]를 전혀 읽지 않음, TASK-KBO-153에서 개명) 참고용 수치라 포지션
        // 비중에 맞춰 임의로 채웠다.
        private static readonly PlayerSeed[] Batters =
        {
            new PlayerSeed("PLY_0001", "구자욱", "RF", 82, 500),
            new PlayerSeed("PLY_0002", "강민호", "C", 78, 400),
            new PlayerSeed("PLY_0003", "김지찬", "CF", 75, 480),
            new PlayerSeed("PLY_0004", "류지혁", "2B", 73, 450),
            new PlayerSeed("PLY_0005", "이재현", "SS", 74, 470),
            new PlayerSeed("PLY_0006", "김영웅", "3B", 76, 460),
            new PlayerSeed("PLY_0007", "김성윤", "LF", 68, 300),
        };

        // 명령서 4항이 지정한 투수 4명.
        private static readonly PlayerSeed[] Pitchers =
        {
            new PlayerSeed("PLY_0008", "원태인", "SP", 84, 160),
            new PlayerSeed("PLY_0009", "오승환", "CP", 75, 55),
            new PlayerSeed("PLY_0010", "임창민", "RP", 72, 45),
            new PlayerSeed("PLY_0011", "김재윤", "RP", 74, 50),
        };

        [MenuItem("KBO Manager/Generate Default Players (Samsung Lions)")]
        public static void GenerateSamsungLionsRoster()
        {
            var builder = new StringBuilder();
            builder.Append(CsvHeader).Append('\n');

            foreach (var batter in Batters)
            {
                AppendBatterRow(builder, batter);
            }

            foreach (var pitcher in Pitchers)
            {
                AppendPitcherRow(builder, pitcher);
            }

            File.WriteAllText(CsvPath, builder.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"[PlayerDataSeeder] '{CsvPath}'를 삼성 라이온즈 선수 {Batters.Length + Pitchers.Length}명" +
                $"(타자 {Batters.Length}/투수 {Pitchers.Length})으로 덮어썼습니다. 기존 더미 4행(KIA/LG로 잘못 " +
                "배정돼 있던 구자욱/원태인/오승환과 삼성으로 잘못 배정돼 있던 노시환)은 전부 제거됐습니다. " +
                "PlayerDatabase.Awake()가 다음 플레이 모드 진입 시 이 CSV를 다시 파싱합니다.");
        }

        /// <summary>타자 1명 분량의 CSV 행. GetBaseOverall()이 평균 내는 3개(z_contact/z_eye/z_power)에
        /// 동일 Z-score를 채워 목표 OVR과 정확히 일치시킨다. OVR과 무관한 z_speed/z_def/z_stamina도
        /// 같은 값으로 채워 스탯 간 부자연스러운 괴리를 피했다(OVR 계산에는 영향 없음).</summary>
        private static void AppendBatterRow(StringBuilder builder, PlayerSeed seed)
        {
            string z = ToZScoreString(seed.TargetOvr);
            builder.Append(string.Join(",", new[]
            {
                seed.Id, SamsungTeamId, seed.Name, SeasonYear, seed.Position, seed.PaIp.ToString(CultureInfo.InvariantCulture),
                z, z, z, z, z, z, "TRUE", "0.000", "0.000", "0.000",
            })).Append('\n');
        }

        /// <summary>투수 1명 분량의 CSV 행. GetBaseOverall()이 평균 내는 4개(z_speed→Velocity/z_stuff/
        /// z_control/z_movement)에 동일 Z-score를 채워 목표 OVR과 정확히 일치시킨다. 타자 전용 컬럼
        /// (z_contact/z_eye/z_power/z_def)은 파싱 시 전혀 읽히지 않으므로 0.000으로 비운다.</summary>
        private static void AppendPitcherRow(StringBuilder builder, PlayerSeed seed)
        {
            string z = ToZScoreString(seed.TargetOvr);
            builder.Append(string.Join(",", new[]
            {
                seed.Id, SamsungTeamId, seed.Name, SeasonYear, seed.Position, seed.PaIp.ToString(CultureInfo.InvariantCulture),
                "0.000", "0.000", "0.000", z, "0.000", z, "TRUE", z, z, z,
            })).Append('\n');
        }

        /// <summary>PlayerDatabase.ConvertZScoreToStat()(stat = round(z*15+50))의 역함수.</summary>
        private static string ToZScoreString(int targetOvr)
        {
            float z = (targetOvr - 50f) / 15f;
            return z.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
