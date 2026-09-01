using EcsFramework;
using UnityEngine;

 //
/// 用于太阳或月亮光照的组件。昼夜系统每帧都会把更新后的
/// 方向 / 色温 / 强度值写入其中。
/// 
public class DayNightComponent : IComponent
{
    public bool isSun;
    public Light light;

    public void ApplyDirection(Vector3 eulerAngles)
        => light.transform.localEulerAngles = eulerAngles;

    public void ApplyColorTemperature(float kelvin)
        => light.colorTemperature = kelvin;

    public void ApplyIntensity(float intensity)
        => light.intensity = intensity;


}