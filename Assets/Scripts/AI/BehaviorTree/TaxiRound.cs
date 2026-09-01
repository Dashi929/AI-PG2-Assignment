using UnityEngine;

namespace EcsFramework
{
     //
    /// 司机行为：在城镇（聚落）之间开闲置车辆往返。
    /// 流程：找附近闲置车 → 上车 → 依次开往各聚落中心 → 到达短暂停留 → 循环。
    /// 被玩家命令打断时（AIComponent 命令优先），本节点状态被重置并释放车辆。
    /// 
    public class TaxiRound : BTNode
    {
        public float CarSearchRadius { get; set; } = 40f;
        public float WalkToCarRadius { get; set; } = 8f;
        public float ArriveRadius { get; set; } = 5f;
        public float StayDuration { get; set; } = 3f;

        // 城镇中心列表（世界坐标），由外部提供
        public Vector3[] Towns { get; set; }

        // 找车冷却（秒）：找不到车时不每帧查询，降低开销
        public float SearchCooldown { get; set; } = 0.5f;

        private CarComponent car;
        private bool walkingToCar;
        private bool hasDestination;
        private int townIndex;
        private float stayElapsed;
        private float searchTimer;

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var controller = ctx.Controller;

            // 0) 城镇列表为空：失败
            if (Towns == null || Towns.Length == 0) return NodeState.Failure;

            // 1) 已拥有车：开往当前城镇
            if (car != null)
            {
                if (!hasDestination)
                {
                    Vector3 dest = Towns[townIndex % Towns.Length];
                    bool ok = car.SetDestination(dest);
                    // SetDestination 失败不释放车：保持车内状态，下一帧重试
                    if (!ok) return NodeState.Running;
                    hasDestination = true;
                }

                if (car.HasArrived(ArriveRadius))
                {
                    // 到达：短暂停留，然后去下一个城镇
                    stayElapsed += ctx.DeltaTime;
                    if (stayElapsed >= StayDuration)
                    {
                        stayElapsed = 0f;
                        hasDestination = false;
                        townIndex = (townIndex + 1) % Towns.Length;
                    }
                }
                else if (car.Agent != null && !car.Agent.hasPath && !car.Agent.pathPending)
                {
                    // 路径丢失：重新设置
                    hasDestination = false;
                }
                return NodeState.Running;
            }

            // 没车：恢复步行行为标志
            controller.BehaviorContinuesWhileDriving = false;

            // 2) 还没车：找附近闲置车（带冷却，避免每帧全量查询）
            searchTimer -= ctx.DeltaTime;
            if (searchTimer > 0f) return NodeState.Running;
            searchTimer = SearchCooldown;

            car = FindIdleCar(ctx, CarSearchRadius);
            if (car == null) return NodeState.Running;

            // 3) 走到车旁再上车
            float dist = Vector3.Distance(controller.go.transform.position, car.go.transform.position);
            if (dist > WalkToCarRadius)
            {
                if (controller.Agent.isOnNavMesh) controller.Agent.SetDestination(car.go.transform.position);
                walkingToCar = true;
                return NodeState.Running;
            }
            if (walkingToCar)
            {
                if (controller.Agent.isOnNavMesh) controller.Agent.ResetPath();
                walkingToCar = false;
            }
            if (!car.Enter(controller))
            {
                car = null; // 被别人抢先
                return NodeState.Running;
            }
            // 司机行为在乘车时继续执行（自动开车）
            controller.BehaviorContinuesWhileDriving = true;
            return NodeState.Running;
        }

         //查找范围内最近的闲置车辆。
        private static CarComponent FindIdleCar(AIContext ctx, float radius)
        {
            CarComponent best = null;
            var cars = EcsRunner.World.Query<CarComponent>();
            float bestD = radius;
            var pos = ctx.Controller.go.transform.position;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i].Get<CarComponent>();
                if (c == null || c.IsDriven || c.go == null) continue;
                float d = Vector3.Distance(pos, c.go.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private void ReleaseCar()
        {
            if (car != null) car.Exit();
            car = null;
            hasDestination = false;
            walkingToCar = false;
        }

        public override void Reset()
        {
            ReleaseCar();
            townIndex = 0;
            stayElapsed = 0f;
        }
    }
}
