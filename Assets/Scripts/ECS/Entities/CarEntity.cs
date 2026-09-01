using UnityEngine;

namespace EcsFramework
{
     //
    /// 车辆实体：挂到车辆 GameObject 上，绑定进 ECS World。
    /// 包含 NavMeshAgent（导航）和 CarComponent（车辆数据/交互）。
    /// 车辆可作为 NPC 的互动目标（右键点车，NPC 上车坐好）。
    /// 
    [RequireComponent(typeof(UnityEngine.AI.NavMeshAgent))]
    public class CarEntity : EcsEntity
    {
        protected override void OnBound()
        {
            var car = entity.Add<CarComponent>();
            car.OnAwake(gameObject);
        }
    }
}
