namespace EcsFramework
{
     //驱动所有车辆组件（行驶逻辑）。
    public class CarSystem : ISystem
    {
        public void OnCreate(World world) { }

        public void OnUpdate(World world, float dt)
        {
            var list = world.Query<CarComponent>();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.GameObject == null) continue;
                var car = e.Get<CarComponent>();
                if (car == null || car.go == null) continue;
                car.Update();
            }
        }
    }
}
