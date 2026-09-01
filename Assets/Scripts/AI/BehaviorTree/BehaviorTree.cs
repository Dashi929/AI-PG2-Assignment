using System.Collections.Generic;

namespace EcsFramework
{
     //行为树节点的执行结果。
    public enum NodeState
    {
        Running,   // 仍在执行中，将再次 tick
        Success,   // 执行成功
        Failure,   // 执行失败（或被中止）
    }

     //
    /// 每次 tick 沿树向下传递的上下文。保存节点可能需要的一切：
    /// 所属 AI 控制器、移动 / 导航 agent、共享的
    /// blackboard 数据以及当前时刻（用于每日日程）。
    /// 
    public class AIContext
    {
        public AIComponent Controller { get; set; }
        public float DeltaTime { get; set; }
        public float TimeOfDay { get; set; }          // 小时，例如 8.5 = 08:30
        public bool HasAgent { get { return Controller != null && Controller.Agent != null; } }

        // 简单的 blackboard，供节点之间共享数据
        public Dictionary<string, object> Blackboard { get; private set; }
            = new Dictionary<string, object>();

        public void Set(string key, object value) => Blackboard[key] = value;
        public T Get<T>(string key)
        {
            if (Blackboard.TryGetValue(key, out var v) && v is T t) return t;
            return default;
        }
        public bool Has(string key) => Blackboard.ContainsKey(key);

         //
        /// 让节点在本次 tick 请求播放某个动画。控制器会在每帧结束时
        /// 把最后请求的动画意图交给 Animator，从而实现"不同行为 -> 不同动画"。
        /// 
        public void SetAnim(string state)
        {
            if (Controller != null) Controller.RequestAnim(state);
        }
    }
     //所有行为树节点的基类。
    public abstract class BTNode
    {
        public string Name { get; set; }

        public abstract NodeState Tick(AIContext ctx);

         //
        /// 重置节点内部状态（用于玩家命令打断当前行为后，让行为树从干净状态重新开始）。
        /// 组合节点应递归调用子节点的 Reset。
        /// 
        public virtual void Reset() { }

        public override string ToString() => string.IsNullOrEmpty(Name) ? GetType().Name : Name;
    }


     //
    /// 组合节点（Composite nodes）， 按顺序运行子节点，直到其中一个失败；只有全部成功才算成功。
    /// 
    public class Sequence : BTNode
    {
        private readonly List<BTNode> children = new List<BTNode>();
        private int current;

        public Sequence Add(BTNode child) { children.Add(child); return this; }
        public Sequence(params BTNode[] nodes) { foreach (var n in nodes) children.Add(n); }

        public override NodeState Tick(AIContext ctx)
        {
            while (current < children.Count)
            {
                var state = children[current].Tick(ctx);
                if (state == NodeState.Running) return NodeState.Running;
                if (state == NodeState.Failure) { current = 0; return NodeState.Failure; }
                current++; // 成功，继续下一个
            }
            current = 0;
            return NodeState.Success;
        }

        public override void Reset()
        {
            current = 0;
            for (int i = 0; i < children.Count; i++) children[i].Reset();
        }
    }

     //
    /// 按顺序尝试子节点；在第一个成功的子节点处返回 Success。
    /// 
    public class Selector : BTNode
    {
        private readonly List<BTNode> children = new List<BTNode>();
        private int current;

        public Selector Add(BTNode child) { children.Add(child); return this; }
        public Selector(params BTNode[] nodes) { foreach (var n in nodes) children.Add(n); }

        public override NodeState Tick(AIContext ctx)
        {
            while (current < children.Count)
            {
                var state = children[current].Tick(ctx);
                if (state == NodeState.Running) return NodeState.Running;
                if (state == NodeState.Success) { current = 0; return NodeState.Success; }
                current++; // 失败，尝试下一个
            }
            current = 0;
            return NodeState.Failure;
        }

        public override void Reset()
        {
            current = 0;
            for (int i = 0; i < children.Count; i++) children[i].Reset();
        }
    }

    // 装饰节点
     //
    /// 永远（或最多 count 次）运行子节点，直到它失败。
    /// 
    public class Repeater : BTNode
    {
        public BTNode Child { get; set; }
        public int MaxTimes { get; set; }   // <= 0 表示无限
        private int times;

        public Repeater(BTNode child, int maxTimes = 0) { Child = child; MaxTimes = maxTimes; }

        public override NodeState Tick(AIContext ctx)
        {
            if (MaxTimes > 0 && times >= MaxTimes) { times = 0; return NodeState.Success; }
            var s = Child.Tick(ctx);
            if (s == NodeState.Failure) { times = 0; return NodeState.Success; } // 重复结束
            if (s == NodeState.Success) times++;
            return NodeState.Running;
        }

        public override void Reset()
        {
            times = 0;
            if (Child != null) Child.Reset();
        }
    }

     //包装一个子节点；若子节点运行时间超过 maxSeconds 则使其失败。
    public class TimeLimit : BTNode
    {
        public BTNode Child { get; set; }
        public float MaxSeconds { get; set; }
        private float elapsed;

        public TimeLimit(BTNode child, float maxSeconds) { Child = child; MaxSeconds = maxSeconds; }

        public override NodeState Tick(AIContext ctx)
        {
            elapsed += ctx.DeltaTime;
            if (elapsed >= MaxSeconds) { elapsed = 0f; return NodeState.Failure; }
            var s = Child.Tick(ctx);
            if (s != NodeState.Running) elapsed = 0f;
            return s;
        }

        public override void Reset()
        {
            elapsed = 0f;
            if (Child != null) Child.Reset();
        }
    }
}
