using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace EcsFramework
{
    public interface IComponent
    {
    }

     //一个实体是一个 ID 加上一组组件。这里不包含任何行为逻辑。
    public sealed class Entity
    {
        public int Id { get; internal set; }
        public World World { get; internal set; }
        public bool IsAlive { get; internal set; } = true;
        public GameObject GameObject { get; set; }

        private readonly Dictionary<Type, IComponent> components = new Dictionary<Type, IComponent>();

        public T Add<T>() where T : IComponent, new()
        {
            var c = new T();
            components[typeof(T)] = c;
            return c;
        }

        public T Get<T>() where T : IComponent => (T)components[typeof(T)];

        public bool Has<T>() where T : IComponent => components.ContainsKey(typeof(T));

        public void Remove<T>() where T : IComponent => components.Remove(typeof(T));

        public void Destroy()
        {
            if (IsAlive) World.DestroyEntity(this);
        }
    }

     //System 包含逻辑；每帧执行一次 Update 循环。
    public interface ISystem
    {
        void OnCreate(World world);
        void OnUpdate(World world, float dt);
    }

    public sealed class World
    {
        private readonly List<Entity> entities      = new List<Entity>();
        private readonly List<ISystem> systems      = new List<ISystem>();
        private readonly Dictionary<Type, List<Entity>> index = new Dictionary<Type, List<Entity>>();
        private readonly List<Entity> pendingDestroy = new List<Entity>();
        private int nextId = 1;

        public Entity CreateEntity()
        {
            var e = new Entity { Id = nextId++, World = this };
            entities.Add(e);
            return e;
        }

        public void DestroyEntity(Entity e)
        {
            if (!e.IsAlive) return;
            e.IsAlive = false;
            pendingDestroy.Add(e);
        }

        public void AddSystem(ISystem system)
        {
            system.OnCreate(this);
            systems.Add(system);
        }
        public ISystem GetSystem<T>() where T : ISystem
        {
            foreach(var system in systems)
            {
                if(system.GetType() == typeof(T))
                {
                    return system;
                }
            }
            return null;
        }
        public void Update(float dt)
        {
            for (int i = 0; i < systems.Count; i++)
                systems[i].OnUpdate(this, dt);

            if (pendingDestroy.Count > 0)
            {
                foreach (var e in pendingDestroy)
                {
                    entities.Remove(e);
                    foreach (var list in index.Values)
                        list.Remove(e);
                }
                pendingDestroy.Clear();
            }
        }

        public IEnumerable<Entity> All() => entities;

         //
        /// 返回携带组件 T 的实体。返回的列表是一个
        /// 临时缓冲区（每次调用都会清空）——请勿持有对它的引用。
        /// 
        public List<Entity> Query<T>() where T : IComponent
        {
            List<Entity> list;
            if (!index.TryGetValue(typeof(T), out list))
            {
                list = new List<Entity>();
                index[typeof(T)] = list;
            }
            else
            {
                list.Clear();
            }
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].IsAlive && entities[i].Has<T>()) list.Add(entities[i]);
            return list;
        }

        public Entity Find(Func<Entity, bool> predicate)
        {
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].IsAlive && predicate(entities[i])) return entities[i];
            return null;
        }
    }
}