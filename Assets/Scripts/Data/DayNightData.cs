using UnityEngine;

 //
/// 保存昼夜循环调校曲线的 ScriptableObject。
/// 通过 Assets > Create > Lighting > Day/Night Config 创建实例，
/// 并让昼夜系统引用它。
/// 
[CreateAssetMenu(fileName = "DayNightConfig", menuName = "Lighting/Day-Night Config")]
public class DayNightConfig : ScriptableObject
{
    public float timeMultiplier = 1f;

    public float hourOfDay = 24f;

    public AnimationCurve sunIntensityCurve    = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve moonIntensityCurve   = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve sunTemperatureCurve  = AnimationCurve.Linear(0f, 3000f, 1f, 6500f);
    public AnimationCurve moonTemperatureCurve = AnimationCurve.Linear(0f, 8000f, 1f, 5000f);
    public AnimationCurve skyboxBlendCurve     = AnimationCurve.Linear(0f, 0f, 1f, 1f);
}