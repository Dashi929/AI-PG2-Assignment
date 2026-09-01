using UnityEngine;
using UnityEngine.AI;

namespace EcsFramework
{
     //
    /// 动物互动节点基类：检测附近的人（HumanEntity）。
    /// 
    public abstract class PersonInteraction : BTNode
    {
        public float DetectRadius { get; set; } = 15f;

        protected static Transform FindNearestPerson(AIContext ctx, float radius)
        {
            Transform best = null;
            float bestD = radius;
            var myPos = ctx.Controller.go.transform.position;
            var entities = EcsRunner.World.Query<AIComponent>();
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (e.GameObject == null) continue;
                // 只认人类（带 HumanEntity 的实体）
                if (e.GameObject.GetComponent<HumanEntity>() == null) continue;
                var ai = e.Get<AIComponent>();
                if (ai == null || ai.go == null || ai == ctx.Controller) continue;
                float d = Vector3.Distance(myPos, ai.go.transform.position);
                if (d < bestD) { bestD = d; best = ai.go.transform; }
            }
            return best;
        }
    }

     //
    /// 逃跑：附近有人时逃向远离方向（森林动物）。
    /// 
    public class FleePerson : PersonInteraction
    {
        public float FleeDistance { get; set; } = 20f;

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var person = FindNearestPerson(ctx, DetectRadius);
            if (person == null) return NodeState.Failure;   // 附近无人，交给其他行为

            var agent = ctx.Controller.Agent;
            // 逃向远离人的方向
            Vector3 away = (agent.transform.position - person.position).normalized;
            if (away.sqrMagnitude < 0.001f) away = Vector3.right;
            var dest = agent.transform.position + away * FleeDistance;
            if (agent.isOnNavMesh && NavMesh.SamplePosition(dest, out var hit, 50f, NavMesh.AllAreas & ~(1 << 1)))
                agent.SetDestination(hit.position);
            ctx.SetAnim("Run");
            return NodeState.Running;
        }
    }

     //
    /// 跟随：附近有人时跟随保持距离（草地/平原动物，可被互动）。
    /// 
    public class FollowPerson : PersonInteraction
    {
        public float FollowDistance { get; set; } = 3f;

        public override NodeState Tick(AIContext ctx)
        {
            if (!ctx.HasAgent) return NodeState.Failure;
            var person = FindNearestPerson(ctx, DetectRadius);
            if (person == null) return NodeState.Failure;   // 附近无人，交给其他行为

            var agent = ctx.Controller.Agent;
            float dist = Vector3.Distance(agent.transform.position, person.position);

            if (dist > FollowDistance)
            {
                if (agent.isOnNavMesh)
                    agent.SetDestination(person.position);
                ctx.SetAnim("Walk");
            }
            else
            {
                if (agent.hasPath) agent.ResetPath();
                // 靠近时面向人（互动表现）
                var dir = person.position - agent.transform.position;
                if (dir.sqrMagnitude > 0.001f)
                {
                    var fwd = Quaternion.LookRotation(dir);
                    agent.transform.rotation = Quaternion.Slerp(agent.transform.rotation, fwd, Time.deltaTime * 6f);
                }
                ctx.SetAnim("Idle");
            }
            return NodeState.Running;
        }
    }
}
