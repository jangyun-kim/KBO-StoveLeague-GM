using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-182] 라인업 [타순 변경] - 유저가 지정한 타순(Player.InstanceId 순서)을 기본 타순(포지션 주전 C→DH, 대체 타자 뒤)에 덮어쓴다.
    /// 지정 목록에 있는 선수는 그 순서대로 앞에, 없는 선수(교체로 새로 들어온 선수 등)는 기본 순서를 유지한 채 뒤에 붙는다.
    /// AI 구단 로스터는 지정 목록의 InstanceId와 겹치지 않으므로 경기 엔진에 그대로 넘겨도 영향이 없다(유저 구단에만 적용).
    /// </summary>
    public static class LineupOrder
    {
        public static List<Player> Apply(IList<Player> defaultOrder, IReadOnlyList<string> overrideIds)
        {
            var source = (defaultOrder ?? new List<Player>()).Where(p => p != null).ToList();
            if (overrideIds == null || overrideIds.Count == 0) return source;

            var index = new Dictionary<string, int>();
            for (int i = 0; i < overrideIds.Count; i++)
            {
                if (!string.IsNullOrEmpty(overrideIds[i]) && !index.ContainsKey(overrideIds[i])) index[overrideIds[i]] = i;
            }
            return source
                .Select((p, i) => (p, key: index.TryGetValue(p.InstanceId ?? "", out var o) ? o : 1000 + i))
                .OrderBy(x => x.key)
                .Select(x => x.p)
                .ToList();
        }

        /// <summary>현재 타순에서 두 선수의 순서를 맞바꾼 InstanceId 목록(저장용). 둘 중 하나라도 타순에 없으면 null.</summary>
        public static List<string> Swap(IList<Player> currentOrder, Player a, Player b)
        {
            var order = (currentOrder ?? new List<Player>()).ToList();
            int ia = order.IndexOf(a), ib = order.IndexOf(b);
            if (ia < 0 || ib < 0 || ia == ib) return null;
            order[ia] = b;
            order[ib] = a;
            return order.Select(p => p.InstanceId).ToList();
        }
    }
}
