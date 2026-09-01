using EcsFramework;
using UnityEngine;
using UnityEngine.AI;

namespace EcsFramework
{
     //
    /// 动物类型枚举。决定 AnimalBrain 构建哪一种行为树。
    /// 
    public enum AnimalType
    {
        Bird,   // 鸟：飞往栖息点之间短距离移动
        Sheep,  // 羊：在草地吃草 + 群体游荡
        Dog,    // 狗：跟随主人 / 领队，或自主游荡
    }
}

 //
/// 动物的行为树控制器。挂到任意动物 GameObject 上，选择类型并构建行为树。
///
/// 三种动物的日常行为：
///   鸟（Bird）  ：在附近栖息点之间低空"飞"（FlyMove），随时落下来休息
///   羊（Sheep） ：在草地上漫游，走到一处停下吃草（Graze），再换地方
///   狗（Dog）   ：跟随一个目标（主人），目标太远就追上去，没目标就自主游荡
///
/// 与人类 NPC（CitizenBrain）使用同一套行为树框架，但行为完全不同。
/// 模型通过 animalPrefab 配置（项目里可用 animal-dog、animal-parrot 等近似）。
/// 
public class AnimalEntity : EcsEntity
{
    public AnimalType kind = AnimalType.Dog;


    public Transform followTarget;

    public float wanderRadius = 15f;

    public float settleDuration = 4f;

    public float followDistance = 2.5f;

    public bool birdSkyMode = true;
    public float birdAltitude = 20f;

    private AIComponent controller;
    public AIComponent GetAIController() { return controller; }

    protected override void OnBound()
    {

        controller = entity.Add<AIComponent>();

        // 配置速度：鸟快一点，羊慢一点，狗适中
        controller.walkSpeed = kind == AnimalType.Bird ? 4f : kind == AnimalType.Sheep ? 1.5f : 3f;

        controller.Init(gameObject,BuildTree());

        // 把目标放进黑板，供 Follow 节点读取（Init 之后 Context 才存在）
        if (followTarget != null)
            controller.Context.Set("followTarget", followTarget);
    }

     //
    /// 用当前字段（kind / birdSkyMode 等）重新构建行为树。
    /// 因为 AddComponent 时 Awake 会立刻绑定并构建一次默认树，
    /// 若之后再改 kind 等配置，需要调用本方法让行为树同步更新。
    /// 
    public void RebuildTree()
    {
        if (controller == null)
        {
            // 尚未绑定：由 OnBound 构建即可
            return;
        }
        controller.walkSpeed = kind == AnimalType.Bird ? 4f : kind == AnimalType.Sheep ? 1.5f : 3f;
        controller.Init(gameObject, BuildTree());
        if (followTarget != null)
            controller.Context.Set("followTarget", followTarget);
    }

     //
    /// 鸟的盘旋中心（地面点）：从当前位置向下采样 NavMesh，
    /// 找不到就退回当前位置的地面投影。
    /// 
    private Vector3 GroundCenter()
    {
        if (NavMesh.SamplePosition(transform.position, out var hit, 500f, NavMesh.AllAreas & ~(1 << 1)))
            return hit.position;
        var p = transform.position;
        return new Vector3(p.x, 0f, p.z);
    }

 
     //根据动物类型构建行为树。
    private BTNode BuildTree()
    {
        var wander = new Wander { Radius = wanderRadius, Name = "wander" };

        switch (kind)
        {
            case AnimalType.Bird:
                // 鸟：在天空盘旋（SkyMode）或在地面栖息点间短飞
                var fly = new FlyMove
                {
                    Radius = wanderRadius,
                    FlightSpeed = 4f,
                    SkyMode = birdSkyMode,
                    SkyCenter = GroundCenter(),
                    Altitude = birdAltitude,
                };
                if (birdSkyMode)
                {
                    // 天空模式：持续盘旋，不需要 Idle 栖息
                    return new Repeater(fly, 0) { Name = "bird-sky" };
                }
                return new Repeater(new Sequence(
                    fly,
                    new Idle { Duration = settleDuration, Name = "perch" }
                ), 0)
                { Name = "bird-behaviour" };

            case AnimalType.Sheep:
                // 羊：走到一处 -> 低头吃草，再换地方
                return new Repeater(new Sequence(
                    new Wander { Radius = wanderRadius, Loop = false, Name = "roam" },
                    new Graze { Duration = settleDuration, Name = "graze" }
                ), 0)
                { Name = "sheep-behaviour" };

            case AnimalType.Dog:
            default:
                // 狗：有目标就跟随，没目标就自主游荡
                var follow = new Follow { FollowRadius = followDistance, TargetKey = "followTarget", Name = "follow" };
                return new Repeater(new Selector(
                    follow,
                    wander
                ), 0)
                { Name = "dog-behaviour" };
        }
    }
}
