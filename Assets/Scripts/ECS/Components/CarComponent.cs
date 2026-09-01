using UnityEngine;
using UnityEngine.AI;
using EcsFramework;

namespace EcsFramework
{
     //
    /// 车辆数据组件：可作为 NPC 的互动目标（上车坐好）。
    /// 
    [RequireComponent(typeof(NavMeshAgent))]
    public class CarComponent : IComponent
    {
        public GameObject go;
        public float driveSpeed = 30f;
        public float turnSharpness = 6f;
        public Transform driverSeat;   // 可选：驾驶员“坐”的位置；无则自动创建在车顶

        public NavMeshAgent Agent { get; private set; }
        public AIComponent Driver { get; private set; }
        public bool IsDriven => Driver != null;

        // 主动规避：前方障碍检测距离与减速比例
        public float lookAhead = 6f;
        public float obstacleDetectRadius = 2.5f;

        public void OnAwake(GameObject car)
        {
            go = car;
            Agent = go.GetComponent<NavMeshAgent>();
            Agent.speed = driveSpeed;
            // 与其他 agent（行人/动物/车）互相规避
            Agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

            // 自动创建驾驶员座位（车顶中部偏前）
            if (driverSeat == null)
            {
                var seat = new GameObject("DriverSeat");
                seat.transform.SetParent(go.transform, false);
                seat.transform.localPosition = new Vector3(0f, 1.2f, -0.2f);
                driverSeat = seat.transform;
            }
        }

         //让一个人坐上这辆车；如果已被占用则返回 false。
        public bool Enter(AIComponent person)
        {
            if (IsDriven) return false;
            Driver = person;
            person.Vehicle = go;
            if (driverSeat != null)
            {
                // 把 NPC 放到驾驶座并作为车的子物体
                person.go.transform.SetParent(driverSeat, false);
                person.go.transform.localPosition = Vector3.zero;
                person.go.transform.localRotation = Quaternion.identity;
            }
            // 上车后隐藏 NPC 模型（人在车内）
            SetRendererEnabled(person.go, false);
            return true;
        }

         //让驾驶员下车，并将其交还给自己的 transform。
        public void Exit()
        {
            if (Driver == null) return;
            Driver.Vehicle = null;
            if (driverSeat != null)
            {
                Driver.go.transform.SetParent(null, false);
                // 下车后放到车旁地面
                Driver.go.transform.position = go.transform.position + go.transform.forward * 1.5f;
            }
            // 下车后恢复显示 NPC 模型
            SetRendererEnabled(Driver.go, true);
            Driver = null;
        }

         //递归开关 GameObject 所有 Renderer（用于上车隐藏/下车显示）。
        private static void SetRendererEnabled(GameObject root, bool enabled)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = enabled;
        }

        public void Update()
        {
            if (Driver == null) return;

            // 检测前方障碍（行人/动物/车辆），有障碍则转向绕行
            bool blocked = DetectObstacle(out Vector3 avoidanceDir);

            // 沿 agent 的路径行驶
            var vel = Agent.desiredVelocity;
            if (blocked && avoidanceDir.sqrMagnitude > 0.001f)
            {
                // 被阻挡时朝规避方向转向（NavMeshAgent 路径会自动绕行，不侧移以免脱离 NavMesh）
                vel = Vector3.Lerp(vel, avoidanceDir, 0.6f);
            }

            if (vel.sqrMagnitude > 0.01f)
            {
                var targetRot = Quaternion.LookRotation(vel);
                go.transform.rotation = Quaternion.Slerp(go.transform.rotation, targetRot, Time.deltaTime * turnSharpness);
            }
        }

         //
        /// 检测车辆前方是否有障碍物（行人/动物/其他车辆）。
        /// 有障碍时返回 true 并给出规避方向（垂直侧向）。
        /// 
        private bool DetectObstacle(out Vector3 avoidanceDir)
        {
            avoidanceDir = Vector3.zero;
            if (Agent == null || !Agent.isOnNavMesh) return false;

            // 用移动方向而非 transform.forward（车可能尚未转向）
            Vector3 moveDir = Agent.velocity;
            if (moveDir.sqrMagnitude < 0.01f)
                moveDir = go.transform.forward;
            else
                moveDir.Normalize();

            Vector3 origin = go.transform.position + Vector3.up * 0.5f;
            // 前方球体检测：命中任意 collider（行人/动物/车）
            if (Physics.SphereCast(origin, obstacleDetectRadius, moveDir, out var hit, lookAhead,
                ~0, QueryTriggerInteraction.Collide))
            {
                // 忽略自身
                if (hit.collider.transform.root == go.transform.root) return false;
                // 朝向侧面规避
                avoidanceDir = Vector3.Cross(Vector3.up, moveDir);
                if (Vector3.Dot(avoidanceDir, hit.normal) > 0f)
                    avoidanceDir = -avoidanceDir;
                avoidanceDir += moveDir * 0.3f;   // 保留前进分量，平滑绕行
                return true;
            }
            return false;
        }

         //告诉汽车行驶到哪里；如果没有路径则返回 false。
        public bool SetDestination(Vector3 dest)
        {
            if (Agent == null || !Agent.enabled || !Agent.isOnNavMesh) return false;
            return Agent.SetDestination(dest);
        }

        public bool HasArrived(float radius)
        {
            if (Agent == null || !Agent.enabled || !Agent.isOnNavMesh) return false;
            return !Agent.pathPending && Agent.remainingDistance <= radius;
        }
    }
}
