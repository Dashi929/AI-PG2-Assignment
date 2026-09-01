using UnityEngine;

namespace EcsFramework
{
     //
    /// 想要存活于 ECS World 中的场景对象的基类。
    ///
    /// 将子类挂到 GameObject 上，在 Inspector 中配置序列化字段，
    /// 当 World 可用时它就会把自己注册为一个实体
    /// （要么在 Awake 中立即注册，要么在 Runner 尚未创建
    /// World 时延迟一帧再注册）。
    /// 
    public abstract class EcsEntity : MonoBehaviour
    {
        protected Entity entity;
        protected bool isBounded;

        private void Awake()
        {
            if (EcsRunner.World != null)
            {
                BindToWorld(EcsRunner.World);
            }
            else
            {
                Invoke(nameof(BindDeferred), 0f);
            }
        }

        protected void BindDeferred()
        {
            if (!isBounded && EcsRunner.World != null) BindToWorld(EcsRunner.World);
        }

        public void BindToWorld(World world)
        {
            if (isBounded) return;
            isBounded = true;
            entity = world.CreateEntity();
            entity.GameObject = gameObject;

            var t = entity.Add<TransformComp>();
            t.basePosition = transform.position;
            t.baseScale    = transform.localScale;

            OnBound();
        }
        
         //在实体绑定后立即调用；在这里添加组件。
        protected virtual void OnBound() { }

        private void OnDestroy()
        {
            // 销毁 GameObject 时同步从 World 移除实体（World.Update 延迟清理），
            // 避免查询拿到已销毁的残留实体（如 Restart 后）
            if (entity != null && entity.IsAlive) entity.Destroy();
        }

        public Entity Entity => entity;
    }
}