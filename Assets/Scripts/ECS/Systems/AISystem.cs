using EcsFramework;
using UnityEngine;

namespace EcsFramework
{
    public class AISystem : ISystem
    {
        public void OnCreate(World world) { }

        public void OnUpdate(World world, float dt)
        {
            var list = world.Query<AIComponent>();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.GameObject == null) continue;
                var ai = e.Get<AIComponent>();
                if (ai == null || ai.go == null) continue;
                ai.OnUpdate();
            }
        }
    }
}
