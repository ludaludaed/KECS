using System;
using System.Collections.Generic;

namespace KECS
{
    public interface IComponentPool : IDisposable
    {
        public void Remove(int entityId);
        public void EnsureLength(int capacity);
    }


    public sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private SparseSet<T> _components;
        private int Length => _components.Count;
        private T _empty;
        private World _owner;

        public ref T Empty() => ref _empty;

        public ComponentPool(World world)
        {
            _owner = world;
            _components = new SparseSet<T>(world.Config.ComponentsCapacity, world.Config.ComponentsCapacity);
        }

        public ref T Get(int entityId)
        {
            return ref _components.GetValue(entityId);
        }

        public void Add(int entityId)
        {
            _components.Add(entityId);
        }

        public void Add(int entityId, T value)
        {
            _components.Add(entityId, value);
        }

        public void Remove(int entityId)
        {
            _components.Remove(entityId);
        }

        public void Set(int entityId, T value)
        {
            _components.Set(entityId, value);
        }

        public void EnsureLength(int capacity)
        {
            _components.EnsureSparseCapacity(capacity);
        }

        public void Dispose()
        {
            _components = null;
        }
    }
}