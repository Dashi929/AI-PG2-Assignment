using EcsFramework;
using UnityEngine;

 //
/// 将一个光照对象标记为太阳或月亮，并携带 DayNightComponent
/// 绑定到 ECS World 中。
/// 
public class DayNightEntity : EcsEntity
{
    public bool isSun;

    protected override void OnBound()
    {
        var c = entity.Add<DayNightComponent>();
        c.isSun = isSun;
        c.light = entity.GameObject.GetComponent<Light>();
    }
}