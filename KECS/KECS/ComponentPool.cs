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
        private SparseSet<T> components;
        private int Length => components.Count;
        private T empty;
        private World owner;

        public ref T Empty() => ref empty;

        public ComponentPool(World world)
        {
            owner = world;
            components = new SparseSet<T>(world.Config.ComponentsCapacity, world.Config.ComponentsCapacity);
        }

        public ref T Get(int entityId)
        {
            return ref components.GetValue(entityId);
        }

        public void Add(int entityId)
        {
            components.Add(entityId);
        }

        public void Add(int entityId, T value)
        {
            components.Add(entityId, value);
        }

        public void Remove(int entityId)
        {
            components.Remove(entityId);
        }

        public void Set(int entityId, T value)
        {
            components.Set(entityId, value);
        }

        public void EnsureLength(int capacity)
        {
            components.EnsureSparseCapacity(capacity);
        }

        public void Dispose()
        {
            components = null;
        }
    }
}