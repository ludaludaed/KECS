using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class Entity
    {
        private World _world;
        private ArchetypeManager _archetypeManager;
        private Archetype _currentArchetype;
        private Archetype _previousArchetype;
        public bool IsAlive { get; private set; }

        public int Id { get; private set; }

        public Entity(World world, ArchetypeManager archetypeManager, int internalId)
        {
            this._world = world;
            Id = internalId;
            this._archetypeManager = archetypeManager;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Initialize()
        {
            _currentArchetype = this._archetypeManager.Empty;
            _currentArchetype.AddEntity(this);
            _previousArchetype = null;
            IsAlive = true;
        }

        public override string ToString()
        {
            return $"Entity_{Id}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddComponent<T>(T value = default) where T : struct
        {
            var pool = _world.GetPool<T>();
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (!HasComponent<T>())
            {
                pool.Add(Id, value);
                GotoNextArchetype(idx);
                return ref pool.Get(Id);
            }

            return ref pool.Empty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T SetComponent<T>(T value) where T : struct
        {
            var pool = _world.GetPool<T>();
            var idx = ComponentTypeInfo<T>.TypeIndex;
            pool.Set(Id, value);

            if (!HasComponent<T>())
            {
                GotoNextArchetype(idx);
            }

            return ref pool.Get(Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>() where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (HasComponent<T>())
            {
                GotoPriorArchetype(idx);
                _world.GetPool<T>().Remove(Id);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>() where T : struct
        {
            var pool = _world.GetPool<T>();

            if (HasComponent<T>())
            {
                return ref pool.Get(Id);
            }

            return ref pool.Empty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>() where T : struct
        {
            return _currentArchetype.Mask.GetBit(ComponentTypeInfo<T>.TypeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GotoNextArchetype(int index)
        {
            _previousArchetype = _currentArchetype;

            var newArchetype = _archetypeManager.FindOrCreateNextArchetype(_currentArchetype, index);
            _currentArchetype = newArchetype;

            _previousArchetype.RemoveEntity(this);
            _currentArchetype.AddEntity(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GotoPriorArchetype(int index)
        {
            _previousArchetype = _currentArchetype;

            var newArchetype = _archetypeManager.FindOrCreatePriorArchetype(_currentArchetype, index);
            _currentArchetype = newArchetype;

            _previousArchetype.RemoveEntity(this);
            _currentArchetype.AddEntity(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy()
        {
            if (!IsAlive)
            {
                return;
            }

            InternalDestroy();
            IsAlive = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalDestroy()
        {
            RemoveComponents();
            _currentArchetype.RemoveEntity(this);
            _previousArchetype = null;
            _world.RecycleEntity(Id);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveComponents()
        {
            foreach (var idx in _currentArchetype.Mask)
            {
                _world.GetPool(idx).Remove(Id);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            RemoveComponents();
            Id = -1;
            _previousArchetype = null;
            _archetypeManager = null;
            IsAlive = false;
            _currentArchetype = null;
            _previousArchetype = null;
            _world = null;
        }
    }
}