using System;
using System.Collections.Generic;
using KBOManager.Data;
using UnityEngine;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-188] 선수 초상화 경로 해석 - 카드 고유 초상화(Portraits/{팀}/{연도}/{TemplateId})가 없으면
    /// 같은 선수(RealPlayerId)의 다른 등급·연도 초상화로 폴백한다. 구자욱(PLY_004038)처럼 초상화가 일부 카드에만 있어도
    /// LIVE · GOLDEN_GLOVE · SIGNATURE 등 모든 등급 카드에 얼굴이 나온다.
    /// Resources는 런타임에 폴더 목록을 줄 수 없으므로 Setup(SetupTask188.BuildPortraitIndex)이 만든 색인
    /// Resources/Portraits/portrait_index.txt(한 줄 = 확장자 없는 리소스 경로)를 읽는다.
    /// 우선순위: ① 정확한 카드 ② 같은 등급(별칭 LN=NOR, EPIC=EPI) 가까운 연도 ③ 아무 등급 가까운 연도.
    /// </summary>
    public static class PortraitResolver
    {
        public const string IndexResource = "Portraits/portrait_index";

        private struct Entry { public string Path; public int Year; public string Grade; }

        private static Dictionary<string, List<Entry>> byPlayer;
        private static HashSet<string> allPaths;
        private static readonly Dictionary<string, string> ResolvedCache = new Dictionary<string, string>();

        /// <summary>정확한 카드 경로("Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_GG") - 형식이 아니면 null.</summary>
        public static string ExactPath(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;
            var parts = templateId.Split('_');
            if (parts.Length < 2) return null;
            return $"Portraits/{parts[0]}/{parts[1]}/{templateId}";
        }

        /// <summary>색인 줄 목록으로 초기화한다(테스트 주입 겸용).</summary>
        public static void SetIndex(IEnumerable<string> lines)
        {
            byPlayer = new Dictionary<string, List<Entry>>();
            allPaths = new HashSet<string>();
            ResolvedCache.Clear();
            if (lines == null) return;
            foreach (var raw in lines)
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.EndsWith("_BG", StringComparison.Ordinal)) continue;
                int slash = line.LastIndexOf('/');
                if (!TryParse(slash >= 0 ? line.Substring(slash + 1) : line, out string id, out int year, out string grade)) continue;
                if (!byPlayer.TryGetValue(id, out var list)) byPlayer[id] = list = new List<Entry>();
                list.Add(new Entry { Path = line, Year = year, Grade = grade });
                allPaths.Add(line);
            }
        }

        public static void ResetCache()
        {
            byPlayer = null;
            allPaths = null;
            ResolvedCache.Clear();
        }

        /// <summary>"SAMSUNG_2024_PLY_004038_GG" → (PLY_004038, 2024, GG).</summary>
        public static bool TryParse(string templateId, out string playerId, out int year, out string grade)
        {
            playerId = null; year = 0; grade = "";
            if (string.IsNullOrEmpty(templateId)) return false;
            var parts = templateId.Split('_');
            if (parts.Length < 4 || !int.TryParse(parts[1], out year)) return false;
            playerId = $"{parts[2]}_{parts[3]}";
            grade = parts.Length >= 5 ? NormalizeGrade(parts[4]) : "";
            return true;
        }

        private static string NormalizeGrade(string code) => code switch
        {
            "NOR" => "LN",
            "EPI" => "EPIC",
            _ => code
        };

        private static void EnsureIndex()
        {
            if (byPlayer != null) return;
            var asset = Resources.Load<TextAsset>(IndexResource);
            SetIndex(asset != null ? asset.text.Split('\n') : null);
        }

        /// <summary>초상화 리소스 경로(없으면 null). 색인이 비어 있으면 기존 규칙(정확 경로)을 그대로 돌려준다.</summary>
        public static string Resolve(PlayerTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.TemplateId)) return null;
            if (ResolvedCache.TryGetValue(template.TemplateId, out var cached)) return cached;
            EnsureIndex();

            string exact = ExactPath(template.TemplateId);
            string result = null;
            if (exact != null && allPaths.Contains(exact)) result = exact;
            else
            {
                TryParse(template.TemplateId, out string parsedId, out int year, out string grade);
                string playerId = !string.IsNullOrEmpty(template.RealPlayerId) ? template.RealPlayerId : parsedId;
                if (year == 0) year = template.SeasonYear;
                if (playerId != null && byPlayer.TryGetValue(playerId, out var list))
                {
                    int best = int.MaxValue;
                    foreach (var e in list)
                    {
                        int score = (e.Grade == grade ? 0 : 100) + Math.Abs(e.Year - year);
                        if (score < best) { best = score; result = e.Path; }
                    }
                }
                if (result == null && allPaths.Count == 0) result = exact; // 색인 없음 - 기존 규칙대로 시도
            }
            ResolvedCache[template.TemplateId] = result;
            return result;
        }

        /// <summary>같은 선수 초상화가 하나라도 있는지(색인 기준).</summary>
        public static bool HasAnyPortrait(string realPlayerId)
        {
            EnsureIndex();
            return !string.IsNullOrEmpty(realPlayerId) && byPlayer.TryGetValue(realPlayerId, out var list) && list.Count > 0;
        }
    }
}
