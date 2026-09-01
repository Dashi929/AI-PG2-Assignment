using EcsFramework;
using UnityEngine;

 //
/// 人类 NPC 实体：注册进 ECS World 并携带 AIComponent。
/// 行为树由 AIGenerator 按 NPC 种类（NpcKind）构建并注入。
/// 
public class HumanEntity : EcsEntity
{
    private AIComponent controller;

    public AIComponent GetAIController() { return controller; }

    protected override void OnBound()
    {
        controller = entity.Add<AIComponent>();
    }
}
