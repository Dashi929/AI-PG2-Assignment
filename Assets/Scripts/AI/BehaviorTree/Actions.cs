using EcsFramework;
using UnityEngine;
using UnityEngine.AI;

namespace EcsFramework
{
    public class WalkTo : BTNode
    {
        public Vector3 Destination { get; set; }
        public float ArriveRadius { get; set; } = 1.5f;
        public string DestKey { get; set; }

        private bool arrived;

        public WalkTo() { }
        public WalkTo(Vector3 dest, float arriveRadius = 1.5f)
        { Destination = dest; ArriveRadius = arriveRadius; }

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;

            // 新目的地会使“已到达”锁存失效
            var target = Destination;
            if (!string.IsNullOrEmpty(DestKey) && ctx.Has(DestKey))
                target = ctx.Get<Vector3>(DestKey);

            if (arrived && (target - Destination).sqrMagnitude < 0.001f)
                return NodeState.Success;
            Destination = target;

            var agent = ctx.Controller.Agent;
            if (agent.pathPending)
            {
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }

            if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                if (!agent.isOnNavMesh || !agent.SetDestination(Destination))
                {
                    ctx.SetAnim("Idle");
                    return NodeState.Failure;
                }
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }

            if (agent.remainingDistance <= ArriveRadius && !agent.pathPending)
            {
                agent.ResetPath();
                ctx.Controller.FaceDirection(Vector3.zero);
                arrived = true;
                ctx.SetAnim("Idle");
                return NodeState.Success;
            }
            ctx.SetAnim("Walk");
            return NodeState.Running;
        }

        public override void Reset()
        {
            arrived = false;
        }
    }

     //
    /// 汽车驾驶：寻找附近空闲的汽车，上车，沿 NavMesh 快速行驶到
    /// 目的地，然后下车并把控制权交还给角色。行驶中返回 Running，
    /// 停好车后返回 Success。
    /// 
    public class DriveTo : BTNode
    {
        public Vector3 Destination { get; set; }
        public float ArriveRadius { get; set; } = 4f;
        public float CarSearchRadius { get; set; } = 25f;
        public float WalkToCarRadius { get; set; } = 6f;

        private CarComponent car;
        private bool walkingToCar;
        private bool hasDestination;
        private float findTimer;
        private const float FindCooldown = 0.5f;

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var controller = ctx.Controller;

            // 1) 如果已经拥有一辆车，就把它开到目的地
            if (car != null)
            {
                if (!hasDestination)
                {
                    if (!car.SetDestination(Destination)) { car.Exit(); car = null; return NodeState.Failure; }
                    hasDestination = true;
                }

                if (car.HasArrived(ArriveRadius))
                {
                    car.Exit();
                    car = null;
                    hasDestination = false;
                    return NodeState.Success;
                }
                return NodeState.Running;
            }

            // 2) 还没上车——在附近找一辆（带冷却）
            if (car == null)
            {
                findTimer -= ctx.DeltaTime;
                if (findTimer > 0f) return NodeState.Running;
                findTimer = FindCooldown;
                car = FindCar(ctx, CarSearchRadius);
                if (car == null) return NodeState.Running; // 附近没有车，稍后重试
            }

            // 3) 先走到车旁，再上车
            var dist = Vector3.Distance(controller.go.transform.position, car.go.transform.position);
            if (dist > WalkToCarRadius)
            {
                if (controller.Agent.isOnNavMesh) controller.Agent.SetDestination(car.go.transform.position);
                walkingToCar = true;
                return NodeState.Running;
            }
            if (walkingToCar)
            {
                controller.Agent.ResetPath();
                walkingToCar = false;
            }
            if (!car.Enter(controller))
            {
                car = null; // 被别人抢先了
                return NodeState.Running;
            }
            return NodeState.Running;
        }

        private static CarComponent FindCar(AIContext ctx, float radius)
        {
            CarComponent best = null;
            var cars = EcsRunner.World.Query<CarComponent>();
            float bestD = radius;
            var pos = ctx.Controller.go.transform.position;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i].Get<CarComponent>();
                if (c.IsDriven) continue;
                float d = Vector3.Distance(pos, c.go.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        public override void Reset()
        {
            if (car != null) { car.Exit(); car = null; }
            walkingToCar = false;
            hasDestination = false;
        }
    }

     //
    /// 四处闲逛：挑选一个附近的可行走随机点并走过去，循环进行。
    /// 非常适合作为日程任务之间的后备或填充行为。
    /// 将 Loop 设为 false 时，走到目标即返回 Success（供一次走一段的组合用）。
    /// 
    public class Wander : BTNode
    {
        public float Radius { get; set; } = 20f;
        public bool Loop { get; set; } = true;

        // 可选约束：设置后闲逛点只会在该圆盘内选取（用于让 NPC 不离开聚落）
        public Vector3? ConstrainCenter { get; set; }
        public float ConstrainRadius { get; set; } = 0f;

        // 目标点与当前位置的最小距离：避免选中过近的点，造成"走两步就停"的抖动
        public float MinTargetDistance { get; set; } = 5f;

        // 选点冷却（秒）：选点失败后短暂等待，避免高频 NavMesh 采样
        public float PickCooldown { get; set; } = 0.3f;

        private Vector3 target;
        private bool hasTarget;
        private float pickTimer;

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var agent = ctx.Controller.Agent;
            // agent 不在 NavMesh 上（被禁用/未放置）时不能 SetDestination
            if (!agent.isOnNavMesh || !agent.enabled) return NodeState.Failure;

            if (!hasTarget)
            {
                // 选点冷却：避免高频 NavMesh 采样
                pickTimer -= ctx.DeltaTime;
                if (pickTimer > 0f) return NodeState.Running;
                pickTimer = PickCooldown;

                if (!PickRandomPoint(ctx, agent.transform.position, Radius, out target))
                    return NodeState.Success; // 附近什么都没有——停止闲逛
                if (agent.isOnNavMesh) agent.SetDestination(target);
                hasTarget = true;
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }

            // 等待路径计算完成
            if (agent.pathPending)
            {
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }

            if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                hasTarget = false; // 无法到达那里，重新选一个点
                return NodeState.Running;
            }
            if (agent.remainingDistance <= 1f)
            {
                agent.ResetPath();
                hasTarget = false; // 已到达
                return Loop ? NodeState.Running : NodeState.Success;
            }
            ctx.SetAnim("Walk");
            return NodeState.Running;
        }

        public override void Reset()
        {
            target = default;
            hasTarget = false;
        }

        private bool PickRandomPoint(AIContext ctx, Vector3 center, float radius, out Vector3 point)
        {
            // 有约束时，采样圆心取约束中心、采样半径取约束半径，
            // 并把步长调小一点，确保点不会超出约束圆
            Vector3 sampleCenter = center;
            float sampleRadius = radius;
            if (ConstrainCenter.HasValue)
            {
                sampleCenter = ConstrainCenter.Value;
                sampleRadius = ConstrainRadius > 0f ? ConstrainRadius : radius;
            }

            for (int i = 0; i < 12; i++)
            {
                var r = sampleCenter + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * sampleRadius;
                r.y = sampleCenter.y + 50f;   // 从上方采样，这样能命中地面表面
                // 排除 Not Walkable（area 1：湖泊被挖除），避免走进湖里
                if (NavMesh.SamplePosition(r, out var hit, 100f, NavMesh.AllAreas & ~(1 << 1)))
                {
                    // 过滤掉距离当前位置太近的点，避免频繁到达导致的走停抖动
                    if (MinTargetDistance > 0f)
                    {
                        float d = Vector3.Distance(hit.position, center);
                        if (d < MinTargetDistance) continue;
                    }
                    point = hit.position;
                    return true;
                }
            }
            point = center;
            return false;
        }
    }

     //
    /// 静止不动一段时间（待机）。持续时间结束后返回 Success。
    /// 
    public class Idle : BTNode
    {
        public float Duration { get; set; } = 2f;
        private float elapsed;

        public override NodeState Tick(AIContext ctx)
        {
            if (ctx.HasAgent && ctx.Controller.Agent.hasPath)
                ctx.Controller.Agent.ResetPath();

            // 待机：播放 Idle 动画
            ctx.SetAnim("Idle");

            elapsed += ctx.DeltaTime;
            if (elapsed >= Duration) { elapsed = 0f; return NodeState.Success; }
            return NodeState.Running;
        }

        public override void Reset()
        {
            elapsed = 0f;
        }
    }


   
    /// 聊天：走到附近的同伴面前并“交谈”一段时间（双方面对面）。
    /// 同伴从上下文 / blackboard 或范围内的最近 NPC 中选取。
    /// 闲聊结束后进入冷却，冷却期间返回 Failure（不触发闲聊）。
 
    public class Chat : BTNode
    {
        public float ApproachRadius { get; set; } = 4f;
        public float SearchRadius { get; set; } = 15f;
        public float TalkDuration { get; set; } = 5f;
        public float Cooldown { get; set; } = 10f;
        public string PartnerKey { get; set; } = "chatPartner";

        private AIComponent partner;
        private float talkElapsed;
        private float cooldownTimer;

        public override NodeState Tick(AIContext ctx)
        {
            // 冷却中：不触发闲聊
            if (cooldownTimer > 0f)
            {
                cooldownTimer -= ctx.DeltaTime;
                return NodeState.Failure;
            }

            if (partner == null || partner.go == null)
            {
                partner = ResolvePartner(ctx);
                if (partner == null) return NodeState.Failure; // 没有人可以聊天
                ctx.Set(PartnerKey, partner);
            }

            // 靠近同伴
            if (ctx.HasAgent && ctx.Controller.Agent.hasPath == false)
            {
                var d = Vector3.Distance(ctx.Controller.go.transform.position, partner.go.transform.position);
                if (d > ApproachRadius)
                {
                    if (ctx.Controller.Agent.isOnNavMesh) ctx.Controller.Agent.SetDestination(partner.go.transform.position);
                    ctx.SetAnim("Walk");
                    return NodeState.Running;
                }
            }

            // 足够近了：双方面对面交谈
            var dir = partner.go.transform.position - ctx.Controller.go.transform.position;
            ctx.Controller.FaceDirection(dir);
            partner.FaceDirection(-dir);
            ctx.SetAnim("Idle");

            if (ctx.HasAgent && ctx.Controller.Agent.hasPath)
                ctx.Controller.Agent.ResetPath();

            talkElapsed += ctx.DeltaTime;
            if (talkElapsed >= TalkDuration)
            {
                talkElapsed = 0f;
                partner = null;
                ctx.Set(PartnerKey, null);
                // 闲聊结束：进入冷却
                cooldownTimer = Cooldown;
                return NodeState.Success;
            }
            return NodeState.Running;
        }

        public override void Reset()
        {
            partner = null;
            talkElapsed = 0f;
        }

        private AIComponent ResolvePartner(AIContext ctx)
        {
            // 范围内最近的另一个 AIController
            var entities = EcsRunner.World.Query<AIComponent>();
            AIComponent best = null;
            float bestD = SearchRadius;
            var myPos = ctx.Controller.go.transform.position;
            for (int i = 0; i < entities.Count; i++)
            {
                var c = entities[i].Get<AIComponent>();
                if (c == ctx.Controller || c.Agent == null) continue;
                float d = Vector3.Distance(myPos, c.go.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }
    }


    /// 吃草 / 觅食：在当前点停下来低头一段时间，模拟动物在原地吃草。
    /// 返回 Running 直到持续时长结束，然后 Success。
      public class Graze : BTNode
    {
        public float Duration { get; set; } = 4f;

        private float elapsed;

        public override NodeState Tick(AIContext ctx)
        {
            if (ctx.HasAgent && ctx.Controller.Agent.hasPath)
                ctx.Controller.Agent.ResetPath();

            // 吃草：播放 Eat 动画
            ctx.SetAnim("Eat");

            elapsed += ctx.DeltaTime;
            if (elapsed >= Duration)
            {
                elapsed = 0f;
                return NodeState.Success;
            }
            return NodeState.Running;
        }

        public override void Reset()
        {
            elapsed = 0f;
        }
    }

     //
    /// 跟随：持续朝一个目标移动（用于狗跟随主人，或动物群体跟随领队）。
    /// 目标从黑板的 "followTarget" 读取，或由外部设置。
    /// 没有目标时返回 Failure（让 Selector 回退到其他行为）；离目标太近停住，
    /// 太远则追上。
    /// 
    public class Follow : BTNode
    {
        public float FollowRadius { get; set; } = 2.5f;
        public string TargetKey { get; set; } = "followTarget";

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var target = ctx.Get<Transform>(TargetKey);
            if (target == null) return NodeState.Failure; // 没有目标，交给其他行为

            var agent = ctx.Controller.Agent;
            float dist = Vector3.Distance(agent.transform.position, target.position);

            if (dist > FollowRadius)
            {
                if (agent.isOnNavMesh) agent.SetDestination(target.position);
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }
            if (agent.hasPath) agent.ResetPath();
            ctx.SetAnim("Idle");
            return NodeState.Running;
        }
    }

     //
    /// 飞行 / 栖息：鸟类在栖息点之间移动，并模拟扑翼起落。
    ///
    /// SkyMode（天空模式）下鸟不依赖 NavMesh，而是绕一个锚点（Center）在
    /// 离地 Altitude 的高度做圆周盘旋 + 上下起伏，用于"天空生成鸟"。
    /// 地面模式（SkyMode = false）用 NavMesh 在栖息点间短距离移动。
    /// 
    public class FlyMove : BTNode
    {
        public float Radius { get; set; } = 12f;
        public float FlightSpeed { get; set; } = 4f;

        // 天空模式
        public bool SkyMode { get; set; }
        public Vector3 SkyCenter { get; set; }
        public float Altitude { get; set; } = 20f;
        public float BankSpeed { get; set; } = 0.4f;   // 盘旋角速度

        private Vector3 target;
        private bool hasTarget;
        private float angle;

        public override NodeState Tick(AIContext ctx)
        {
            if (SkyMode) return TickSky(ctx);
            return TickGround(ctx);
        }

         //天空模式：绕锚点圆周飞行，不接触 NavMesh。
        private NodeState TickSky(AIContext ctx)
        {
            // 停用 NavMeshAgent，避免它把鸟拉回地面
            if (ctx.HasAgent)
            {
                var ag = ctx.Controller.Agent;
                ag.enabled = false;
                if (ag.hasPath) ag.ResetPath();
            }

            // 飞行：播放 Walk 动画模拟扑翼
            ctx.SetAnim("Walk");

            angle += BankSpeed * ctx.DeltaTime;
            float r = Radius;
            float x = SkyCenter.x + Mathf.Cos(angle) * r;
            float z = SkyCenter.z + Mathf.Sin(angle) * r;
            float y = SkyCenter.y + Altitude + Mathf.Sin(angle * 2.5f) * 1.5f;

            var pos = ctx.Controller.go.transform.position;
            var desired = new Vector3(x, y, z);
            ctx.Controller.go.transform.position = Vector3.MoveTowards(pos, desired, FlightSpeed * ctx.DeltaTime * 3f);

            // 朝向运动方向
            var dir = desired - pos;
            if (dir.sqrMagnitude > 0.01f) ctx.Controller.FaceDirection(dir);
            return NodeState.Running;
        }

         //地面模式：在栖息点之间用 NavMesh 短距离移动。
        private NodeState TickGround(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var agent = ctx.Controller.Agent;
            agent.speed = FlightSpeed;

            if (!hasTarget)
            {
                if (!PickRandomPoint(ctx, agent.transform.position, Radius, out target))
                    return NodeState.Success;
                if (agent.isOnNavMesh) agent.SetDestination(target);
                hasTarget = true;
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }

            if (agent.pathPending)
            {
                ctx.SetAnim("Walk");
                return NodeState.Running;
            }

            if (agent.hasPath)
            {
                // 飞行中轻微抬高
                float progress = Mathf.Sin(Time.time * 6f) * 0.15f;
                var pos = agent.transform.position;
                agent.transform.position = new Vector3(pos.x, pos.y + progress, pos.z);
                ctx.SetAnim("Walk");
            }

            if (!agent.hasPath || agent.remainingDistance <= 1f)
            {
                // 到达栖息点
                agent.ResetPath();
                hasTarget = false;
                ctx.SetAnim("Idle");
                return NodeState.Success;
            }
            ctx.SetAnim("Walk");
            return NodeState.Running;
        }

        private static bool PickRandomPoint(AIContext ctx, Vector3 center, float radius, out Vector3 point)
        {
            for (int i = 0; i < 12; i++)
            {
                var r = center + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * radius;
                r.y = center.y + 50f;
                // 排除 Not Walkable（湖泊），避免飞鸟/动物落到湖里
                if (NavMesh.SamplePosition(r, out var hit, 100f, NavMesh.AllAreas & ~(1 << 1)))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = center;
            return false;
        }

        public override void Reset()
        {
            target = default;
            hasTarget = false;
            angle = 0f;
        }
    }
}
