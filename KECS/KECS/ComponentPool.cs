using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityId)
        {
            return ref _components.GetValue(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int entityId, T value)
        {
            _components.Add(entityId, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int entityId)
        {
            _components.Remove(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityId, T value)
        {
            _components.Set(entityId, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureLength(int capacity)
        {
            _components.EnsureSparseCapacity(capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            _components.Clear();
            _components = null;
        }
    }
}