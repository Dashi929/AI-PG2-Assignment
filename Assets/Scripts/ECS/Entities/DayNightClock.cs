using UnityEngine;

namespace DayNight
{
     //
    /// 共享的静态时钟，让 AI 系统无需与 ECS 昼夜系统耦合即可读取
    /// 当前时间。DayNightCycleSystem 每帧都会把当前的小时数写入这里；
    /// AIController 和日程节点读取它。
    /// 
    public static class DayNightClock
    {
         //当前一天中的时间，以小时计（0..24）。
        public static float Hour { get; set; } = 8f;
    }
}
