using System;
using System.Collections.Generic;
using KBOManager.Core;
using KBOManager.Models;

namespace KBOManager.Data
{
    /// <summary>
    /// [TASK-GM-01] KBO 10개 구단 역사적 계보 통합(1986~2026) + 실명 ↔ 가상명 전환기.
    ///   - ResolveCanonicalTeamCode(): 현재 이름 · 옛 이름 · 영문 토큰 · CSV team_id · Team enum 이름을 10개 계보 코드
    ///     (SAM / LG / HAN / NC / KT / KIA / LOT / DOO / KIW / SSG)로 모은다(예: "MBC 청룡" → LG, "해태 타이거즈" → KIA).
    ///   - GetDisplayPlayerName(): GameSettings.UseVirtualNames가 true면 playerId 해시 시드로 일관된 한국형 가상명을,
    ///     false(개인 플레이 기본값)면 실명을 그대로 돌려준다. 기록 · ID · 능력치는 이름과 무관하게 그대로다.
    /// </summary>
    public static class NameAliasTable
    {
        public const string SAM = "SAM", LG = "LG", HAN = "HAN", NC = "NC", KT = "KT", KIA = "KIA", LOT = "LOT", DOO = "DOO", KIW = "KIW", SSG = "SSG";

        /// <summary>10개 계보 코드(표시 순서 고정).</summary>
        public static readonly string[] CanonicalTeamCodes = { SAM, LG, HAN, NC, KT, KIA, LOT, DOO, KIW, SSG };

        private static readonly Dictionary<string, Team> CodeToTeam = new Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase)
        {
            { SAM, Team.Samsung }, { LG, Team.LG }, { HAN, Team.Hanwha }, { NC, Team.NC }, { KT, Team.KT },
            { KIA, Team.KIA }, { LOT, Team.Lotte }, { DOO, Team.Doosan }, { KIW, Team.Kiwoom }, { SSG, Team.SSG },
        };

        private static readonly Dictionary<string, string> CurrentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { SAM, "삼성 라이온즈" }, { LG, "LG 트윈스" }, { HAN, "한화 이글스" }, { NC, "NC 다이노스" }, { KT, "KT wiz" },
            { KIA, "KIA 타이거즈" }, { LOT, "롯데 자이언츠" }, { DOO, "두산 베어스" }, { KIW, "키움 히어로즈" }, { SSG, "SSG 랜더스" },
        };

        // 계보별 별칭(공백 제거 · 대소문자 무시로 비교). 옛 구단명은 같은 연고 계보로 묶는다.
        private static readonly Dictionary<string, string[]> Lineage = new Dictionary<string, string[]>
        {
            { SAM, new[] { "삼성", "삼성라이온즈", "Samsung", "SAMSUNG", "TEM_002" } },
            { LG, new[] { "LG", "LG트윈스", "MBC", "MBC청룡", "청룡", "TEM_003" } },
            { HAN, new[] { "한화", "한화이글스", "빙그레", "빙그레이글스", "Hanwha", "HANWHA", "TEM_008" } },
            { NC, new[] { "NC", "NC다이노스", "엔씨", "TEM_009" } },
            { KT, new[] { "KT", "KTwiz", "kt위즈", "KT위즈", "TEM_005" } },
            { KIA, new[] { "KIA", "KIA타이거즈", "기아", "기아타이거즈", "해태", "해태타이거즈", "TEM_001" } },
            { LOT, new[] { "롯데", "롯데자이언츠", "Lotte", "LOTTE", "TEM_007" } },
            { DOO, new[] { "두산", "두산베어스", "OB", "OB베어스", "Doosan", "DOOSAN", "TEM_004" } },
            { KIW, new[] { "키움", "키움히어로즈", "넥센", "넥센히어로즈", "히어로즈", "우리", "우리히어로즈", "현대", "현대유니콘스",
                           "태평양", "태평양돌핀스", "청보", "청보핀토스", "삼미", "삼미슈퍼스타즈", "Kiwoom", "KIWOOM", "TEM_010" } },
            { SSG, new[] { "SSG", "SSG랜더스", "SK", "SK와이번스", "쌍방울", "쌍방울레이더스", "TEM_006" } },
        };

        private static readonly Dictionary<string, string> AliasToCode = BuildAliasIndex();

        private static Dictionary<string, string> BuildAliasIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Lineage)
            {
                index[pair.Key] = pair.Key;
                foreach (var alias in pair.Value) index[Normalize(alias)] = pair.Key;
            }
            foreach (var pair in CodeToTeam) index[pair.Value.ToString()] = pair.Key; // Team enum 이름(Samsung, Kiwoom ...)
            foreach (var pair in CurrentNames) index[Normalize(pair.Value)] = pair.Key;
            return index;
        }

        private static string Normalize(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace(" ", "").Trim();

        /// <summary>구단 이름/옛 이름/코드/토큰 → 10개 계보 코드. 알 수 없으면 null.</summary>
        public static string ResolveCanonicalTeamCode(string teamNameOrCode)
        {
            string key = Normalize(teamNameOrCode);
            if (key.Length == 0) return null;
            return AliasToCode.TryGetValue(key, out var code) ? code : null;
        }

        public static Team ToTeam(string canonicalCode) =>
            canonicalCode != null && CodeToTeam.TryGetValue(canonicalCode, out var team) ? team : Team.None;

        public static string ToCode(Team team)
        {
            foreach (var pair in CodeToTeam) if (pair.Value == team) return pair.Key;
            return null;
        }

        /// <summary>계보 코드 → 현재 구단명(예: KIW → "키움 히어로즈").</summary>
        public static string DisplayTeamName(string canonicalCode) =>
            canonicalCode != null && CurrentNames.TryGetValue(canonicalCode, out var name) ? name : canonicalCode;

        // ================================================================== 실명 ↔ 가상명

        /// <summary>현재 설정(GameSettings.UseVirtualNames)에 맞는 선수 표시 이름.</summary>
        public static string GetDisplayPlayerName(string playerId, string realName) =>
            GetDisplayPlayerName(playerId, realName, GameSettings.UseVirtualNames);

        public static string GetDisplayPlayerName(string playerId, string realName, bool useVirtualNames) =>
            useVirtualNames ? GenerateVirtualPlayerName(playerId, realName) : realName;

        /// <summary>치어리더 표시 이름. 같은 사람이 여러 카드(연도 · 등급)로 있어도 같은 가상명이 되도록 실명을 시드로 쓴다.</summary>
        public static string GetDisplayCheerleaderName(string realName, bool useVirtualNames) =>
            useVirtualNames ? GenerateVirtualCheerleaderName(realName) : realName;

        /// <summary>
        /// 템플릿 표시 이름(PlayerName)을 설정에 맞게 다시 쓴다. RealName이 비어 있으면(구 템플릿) 현재 PlayerName을 실명으로 보존한다.
        /// TemplateId · RealPlayerId · 능력치는 건드리지 않는다. GameSettings.UseVirtualNames도 함께 갱신한다.
        /// </summary>
        public static void ApplyDisplayNames(IEnumerable<PlayerTemplate> templates, bool useVirtualNames)
        {
            GameSettings.UseVirtualNames = useVirtualNames;
            if (templates == null) return;
            foreach (var t in templates)
            {
                if (t == null) continue;
                if (string.IsNullOrEmpty(t.RealName)) t.RealName = t.PlayerName;
                t.PlayerName = GetDisplayPlayerName(t.RealPlayerId ?? t.TemplateId, t.RealName, useVirtualNames);
            }
        }

        // 한국 성씨(1글자 + 복성). 실명이 이 성씨로 시작하는 3글자 이상 한글 이름이면 성은 그대로 두고 이름만 바꾼다(구자욱 → 구태웅).
        private static readonly string[] DoubleSurnames = { "남궁", "황보", "제갈", "선우", "독고", "사공", "서문" };
        private const string SingleSurnames = "김이박최정강조윤장임한오서신권황안송류전홍고문양손배백허유남심노하곽성차주우구민나진지엄채원천방공현함변염여추도소석선설마길연위표명기반왕금옥육인맹제모탁국어은편용예봉경";

        private static readonly string[] MaleSyllableA = { "태", "현", "준", "민", "승", "재", "동", "성", "영", "진", "우", "정", "경", "상", "도", "한", "건", "규", "형", "석" };
        private static readonly string[] MaleSyllableB = { "웅", "성", "호", "혁", "훈", "우", "민", "준", "수", "철", "환", "범", "원", "빈", "찬", "율", "겸", "결", "균", "후" };
        private static readonly string[] FemaleSyllableA = { "하", "서", "지", "예", "수", "다", "유", "채", "소", "가", "나", "윤", "세", "아", "보", "주" };
        private static readonly string[] FemaleSyllableB = { "린", "은", "윤", "영", "아", "현", "희", "진", "빈", "연", "슬", "솔", "람", "율", "나", "경" };
        private static readonly string[] ForeignGiven = { "로건", "케일럽", "브랜든", "타일러", "제이크", "카터", "오웬", "헌터", "라이언", "딜런", "에반", "콜린", "트래비스", "네이선", "카일", "마커스" };
        private static readonly string[] CheerSurnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "한", "서", "신", "오", "송", "안", "홍" };

        public static string GenerateVirtualPlayerName(string playerId, string realName)
        {
            string seed = string.IsNullOrEmpty(playerId) ? realName ?? string.Empty : playerId;
            uint h = Fnv1a(seed);
            string surname = SplitKoreanSurname(realName, out string given);
            if (surname == null)
            {
                // 외국인 선수 등 한국식 성씨로 시작하지 않는 이름 - 외국 이름 풀에서 고른다.
                string pick = ForeignGiven[(int)(h % (uint)ForeignGiven.Length)];
                if (pick == realName) pick = ForeignGiven[(int)((h + 1) % (uint)ForeignGiven.Length)];
                return pick;
            }
            return surname + PickGiven(h, given, MaleSyllableA, MaleSyllableB);
        }

        public static string GenerateVirtualCheerleaderName(string realName)
        {
            uint h = Fnv1a("CHR_" + (realName ?? string.Empty));
            string surname = SplitKoreanSurname(realName, out string given) ?? CheerSurnames[(int)(h % (uint)CheerSurnames.Length)];
            return surname + PickGiven(h >> 3, given, FemaleSyllableA, FemaleSyllableB);
        }

        private static string PickGiven(uint h, string realGiven, string[] a, string[] b)
        {
            for (uint i = 0; i < 8; i++)
            {
                uint x = h + i * 7919u;
                string given = a[(int)(x % (uint)a.Length)] + b[(int)((x / (uint)a.Length) % (uint)b.Length)];
                if (given != realGiven) return given;
            }
            return a[0] + b[1];
        }

        /// <summary>한국식 성(복성 포함)을 떼어 낸다. 3글자 이상 한글 이름이 아니거나 성씨 목록에 없으면 null.</summary>
        private static string SplitKoreanSurname(string realName, out string given)
        {
            given = null;
            string name = Normalize(realName);
            if (name.Length < 2 || !IsHangul(name)) return null;
            foreach (var d in DoubleSurnames)
            {
                if (name.Length >= 4 && name.StartsWith(d, StringComparison.Ordinal)) { given = name.Substring(2); return d; }
            }
            if (name.Length > 4) return null;
            if (SingleSurnames.IndexOf(name[0]) < 0) return null;
            given = name.Substring(1);
            return name.Substring(0, 1);
        }

        private static bool IsHangul(string s)
        {
            foreach (char c in s) if (c < '가' || c > '힣') return false;
            return true;
        }

        /// <summary>플랫폼과 무관하게 같은 값을 내는 32비트 FNV-1a(string.GetHashCode는 런타임마다 달라질 수 있다).</summary>
        private static uint Fnv1a(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s ?? string.Empty)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
