using UnityEngine;

namespace EcsFramework
{
     //
    /// 建筑数据组件：记录门口位置（NPC 进入建筑的入口）与占地尺寸。
    /// 
    public class BuildingComponent : IComponent
    {
        public GameObject go;

         //门口世界坐标（地面点），NPC 寻路进入建筑时作为目标。
        public Vector3 doorPosition;

         //门外方向（世界坐标），即门正对的朝向。
        public Vector3 doorForward;

        public float width;
        public float depth;
        public float height;
    }
}
