using EcsFramework;
using UnityEngine;

 //
/// 建筑实体：挂在建筑 prefab 根上，运行时自动注册进 ECS World。
///
/// 阻挡与通行约定：
///  - 墙体由墙片子物体（BoxCollider + NavMeshObstacle carving）阻挡，agent 寻路自动绕行；
///  - 门所在墙面留缺口（不 carve），因此 NPC 可寻路进入建筑内部。
/// 
public class BuildingEntity : EcsEntity
{
    // 门口在建筑本地坐标中的位置（默认南面门，紧贴外墙）
    public Vector3 doorLocalPosition = new Vector3(0f, 0f, -0.5f);

    // 门外方向（建筑本地坐标，默认朝南 -z）
    public Vector3 doorLocalForward = new Vector3(0f, 0f, -1f);

    public float width = 1f;
    public float depth = 1f;
    public float height = 1f;

    private BuildingComponent comp;

     //建筑组件（ECS 组件，通过 Entity 获取：entity.Get&lt;BuildingComponent&gt;()）。
    public BuildingComponent Building
    {
        get
        {
            if (comp == null && entity != null) comp = entity.Get<BuildingComponent>();
            return comp;
        }
    }

    protected override void OnBound()
    {
        comp = entity.Add<BuildingComponent>();
        comp.go = gameObject;
        comp.width = width;
        comp.depth = depth;
        comp.height = height;
        comp.doorPosition = transform.TransformPoint(doorLocalPosition);
        comp.doorForward = transform.TransformDirection(doorLocalForward);
    }
}
