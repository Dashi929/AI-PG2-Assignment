using UnityEngine;

namespace EcsFramework
{
     //实体上的位置快照组件（实体创建时记录基准位置与缩放）。
    public class TransformComp : IComponent
    {
        public Vector3 basePosition;
        public Vector3 baseScale = Vector3.one;
    }
}
