using System;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 시즌 개막일(기본: 게임 내 3월 25일)부터 시작하는 리그 날짜를 관리하는 싱글톤. 실제 시간 흐름과는
    /// 무관하며(현실의 초/분/시는 전혀 참조하지 않는다), 오직 "하루치 스케줄이 끝나면 하루가 넘어간다"는
    /// 게임 내 논리로만 전진한다.
    ///
    /// LeagueManager가 사용자 경기 1건을 마칠 때마다 AdvanceToNextGameDay()를 호출한다 - KBO는 월요일에
    /// 리그 전체가 경기를 쉬므로, 다음 날짜가 월요일이면 자동으로 한 번 더 건너뛰어 "경기가 있는 날"만
    /// CurrentDate로 취급한다(건너뛴 사실은 OnDayAdvanced의 두 번째 인자로 알려준다).
    ///
    /// 이 클래스는 날짜 계산만 담당하고 로스터/체력에는 전혀 관여하지 않는다 - 실제 체력 회복/컨디션
    /// 갱신은 OnDayAdvanced를 구독하는 LeagueManager가 수행한다(관심사 분리).
    /// </summary>
    public class LeagueCalendar : MonoBehaviour
    {
        public static LeagueCalendar Instance { get; private set; }

        /// <summary>KBO는 월요일에 리그 전체가 경기를 쉰다.</summary>
        public const DayOfWeek RestDayOfWeek = DayOfWeek.Monday;

        private const int SeasonOpeningMonth = 3;
        private const int SeasonOpeningDay = 25;

        private DateTime currentDate;

        public DateTime CurrentDate => currentDate;
        public bool IsRestDay => currentDate.DayOfWeek == RestDayOfWeek;

        /// <summary>
        /// 날짜가 하루 이상 넘어갈 때마다 발생한다. 첫 인자는 넘어간 뒤의 최종 날짜, 두 번째 인자는
        /// 그 과정에서 휴식일(월요일)을 하나 이상 건너뛰었는지 여부다 - true면 "밤새 완전히 쉬었다"는
        /// 뜻이므로 구독자(LeagueManager)가 평소보다 큰 폭의 체력 회복을 적용할 근거가 된다.
        /// </summary>
        public event Action<DateTime, bool> OnDayAdvanced;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>새 시즌의 개막일로 CurrentDate를 초기화한다. 개막일 자체가 월요일이면(달력상 우연히
        /// 그런 해가 있을 수 있으므로) 하루씩 밀어 첫 유효한 경기일로 맞춘다.</summary>
        public void InitializeSeason(int year)
        {
            currentDate = new DateTime(year, SeasonOpeningMonth, SeasonOpeningDay);
            while (currentDate.DayOfWeek == RestDayOfWeek)
            {
                currentDate = currentDate.AddDays(1);
            }
        }

        /// <summary>
        /// 사용자 경기 1건이 끝나 다음 경기일로 넘어간다. 다음 날이 월요일이면 리그 전체가 쉬는 날이므로
        /// 자동으로 하루 더 건너뛴다(연속으로 여러 날이 월요일일 수는 없으므로 최대 한 번만 건너뛴다).
        /// </summary>
        public void AdvanceToNextGameDay()
        {
            bool passedRestDay = false;
            currentDate = currentDate.AddDays(1);

            while (currentDate.DayOfWeek == RestDayOfWeek)
            {
                passedRestDay = true;
                currentDate = currentDate.AddDays(1);
            }

            OnDayAdvanced?.Invoke(currentDate, passedRestDay);
        }
    }
}
