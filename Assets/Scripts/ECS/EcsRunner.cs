using UnityEngine;

namespace EcsFramework
{
     //
    /// 引导 ECS World 并每帧驱动一次更新。
    ///
    /// 将此类挂在任意场景对象上。它会创建 World、注册
    /// 内置的 System，然后绑定场景中所有找到的 EcsEntity。
    /// 
    public class EcsRunner : MonoBehaviour
    {
        public static World World { get; private set; }

        private void Awake()
        {
            if (World != null) return;
            World = new World();
            World.AddSystem(new DayNightCycleSystem());
            World.AddSystem(new AISystem());
            World.AddSystem(new CarSystem());
        }

        private void Start()
        {
            BindSceneEntities();
        }

        private void Update()
        {
            if (World != null) World.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (World != null) World = null;
        }

        private void BindSceneEntities()
        {
            var refs = Object.FindObjectsByType<EcsEntity>(FindObjectsSortMode.InstanceID);
            for (int i = 0; i < refs.Length; i++)
                refs[i].BindToWorld(World);
        }
    }
}
