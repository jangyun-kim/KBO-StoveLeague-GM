using System.Collections.Generic;
using System.Text;

namespace KBOManager.Broadcast.Utils
{
    /// <summary>
    /// 최소 기능의 RFC4180풍 CSV 파서. 헤더 1행 + 데이터 N행 구조를 가정하며, 각 행을
    /// "컬럼명 -> 원본 문자열 값" 딕셔너리로 반환한다. 컬럼 순서/개수가 나중에 바뀌거나(새 컬럼 추가)
    /// 특정 행에 값이 비어 있어도 이름 기준으로 찾으므로 크래시 없이 흡수된다 - 존재하지 않는 컬럼을
    /// 요청하면 호출부(DataManager)가 기본값으로 대체한다.
    /// </summary>
    public static class CsvParser
    {
        public static List<Dictionary<string, string>> ParseToRows(string csvText)
        {
            var rows = new List<Dictionary<string, string>>();
            if (string.IsNullOrEmpty(csvText)) return rows;

            var lines = SplitLines(csvText);
            if (lines.Count == 0) return rows;

            var header = ParseLine(lines[0]);

            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var fields = ParseLine(lines[i]);
                var row = new Dictionary<string, string>();
                for (int c = 0; c < header.Count; c++)
                {
                    row[header[c]] = c < fields.Count ? fields[c] : string.Empty; // 값 누락 컬럼은 빈 문자열로 채워 인덱스 밖 접근 방지
                }

                rows.Add(row);
            }

            return rows;
        }

        private static List<string> SplitLines(string text)
        {
            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var raw = normalized.Split('\n');
            var lines = new List<string>(raw.Length);
            foreach (var line in raw)
            {
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }

            return lines;
        }

        /// <summary>따옴표로 감싼 필드 안의 콤마/따옴표 이스케이프("")를 지원하는 한 줄 파서.</summary>
        private static List<string> ParseLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        bool isEscapedQuote = i + 1 < line.Length && line[i + 1] == '"';
                        if (isEscapedQuote)
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }

            fields.Add(current.ToString().Trim());
            return fields;
        }

        // ----- 타입 안전 조회 헬퍼 -----
        // 컬럼이 없거나(구버전 CSV) 파싱 불가능한 값이면 예외를 던지지 않고 defaultValue로 대체한다.
        // 상위 등급(Enum 3~7 등) 추가처럼 스키마가 점진적으로 확장되어도 로드 자체가 죽지 않게 하기 위함이다.

        public static string GetString(this Dictionary<string, string> row, string key, string defaultValue = "")
        {
            return row.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : defaultValue;
        }

        public static int GetInt(this Dictionary<string, string> row, string key, int defaultValue = 0)
        {
            if (row.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)) return parsed;
            return defaultValue;
        }

        public static float GetFloat(this Dictionary<string, string> row, string key, float defaultValue = 0f)
        {
            if (row.TryGetValue(key, out var value) &&
                float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        public static bool GetBool(this Dictionary<string, string> row, string key, bool defaultValue = false)
        {
            if (row.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)) return parsed;
            return defaultValue;
        }
    }
}
