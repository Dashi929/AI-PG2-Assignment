using System.Collections.Generic;
using UnityEngine;

namespace EcsFramework
{
     //
    /// 一项日常活动：一个时间窗口（开始..结束小时）以及在该时间段内
    /// 运行的行为树。可放入 DaySchedule，由后者挑选与当前时钟小时
    /// 匹配的活动。
    /// 
    public class DailyActivity
    {
        public string Name;
        public float StartHour;
        public float EndHour;
        public BTNode Behaviour;

        public bool IsActive(float hour)
            => hour >= StartHour && hour < EndHour;
    }

     //
    /// 一个选择节点，根据当前时刻在一系列日常活动之间切换。
    /// 每帧都会找出其时间窗口包含 <see cref="AIContext.TimeOfDay"/> 的活动，
    /// 并对它的行为树执行 tick。如果没有任何活动匹配
    /// （日程中存在空隙），则返回 Success（休息）。
    ///
    /// 正是这个节点让一个人早晨（去上班）、下午（购物 / 聊天）和
    /// 晚上（开车回家、休息）的行为各不相同。
    /// 
    public class DailySchedule : BTNode
    {
        private readonly List<DailyActivity> activities =
            new List<DailyActivity>();

        public DailySchedule Add(DailyActivity activity)
        {
            activities.Add(activity);
            return this;
        }

        public DailySchedule Add(string name, float start, float end, BTNode behaviour)
        {
            activities.Add(new DailyActivity
            {
                Name = name,
                StartHour = start,
                EndHour = end,
                Behaviour = behaviour,
            });
            return this;
        }

        public override NodeState Tick(AIContext ctx)
        {
            float hour = ctx.TimeOfDay;
            int index = FindActive(hour);

            if (index < 0)
            {
                // 没有安排活动——原地待着
                return NodeState.Success;
            }

            // 切换活动本身不会重置任何状态；组合节点
            // 自己维护状态，这对于基于时间的日程来说没有问题
            var behaviour = activities[index].Behaviour;
            if (behaviour == null) return NodeState.Success;
            return behaviour.Tick(ctx);
        }

        public override void Reset()
        {
            for (int i = 0; i < activities.Count; i++)
                if (activities[i].Behaviour != null) activities[i].Behaviour.Reset();
        }

        private int FindActive(float hour)
        {
            for (int i = 0; i < activities.Count; i++)
                if (activities[i].IsActive(hour)) return i;
            return -1;
        }
    }
}
