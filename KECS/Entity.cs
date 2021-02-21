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
        public bool IsAlive { get; private set; }

        public int Id { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            IsAlive = true;
        }

        public override string ToString()
        {
            string result = string.Empty;

            foreach (var item in _currentArchetype.Mask)
            {
                result += item;
            }

            return $"Entity_{Id} {result}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddComponent<T>(T value = default) where T : struct
        {
            if (!IsAlive) throw new Exception($"|KECS| You are trying to add component an already destroyed entity {ToString()}.");
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
            if (!IsAlive) throw new Exception($"|KECS| You are trying to set component an already destroyed entity {ToString()}.");
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
            if (!IsAlive) throw new Exception($"|KECS| You are trying to remove component an already destroyed entity {ToString()}.");

                var idx = ComponentTypeInfo<T>.TypeIndex;

            if (HasComponent<T>())
            {
                GotoPriorArchetype(idx);
                _world.GetPool<T>().Remove(Id);
            }

            if (_currentArchetype == _archetypeManager.Empty)
            {
                Destroy();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>() where T : struct
        {
            if (!IsAlive) throw new Exception($"|KECS| You are trying to get component an already destroyed entity {ToString()}.");
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
            if (!IsAlive) throw new Exception($"|KECS| You are trying to check component an already destroyed entity {ToString()}.");
            return _currentArchetype.Mask.GetBit(ComponentTypeInfo<T>.TypeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GotoNextArchetype(int index)
        {
            _currentArchetype.RemoveEntity(this);
            var newArchetype = _archetypeManager.FindOrCreateNextArchetype(_currentArchetype, index);
            _currentArchetype = newArchetype;
            _currentArchetype.AddEntity(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GotoPriorArchetype(int index)
        {
            _currentArchetype.RemoveEntity(this);
            var newArchetype = _archetypeManager.FindOrCreatePriorArchetype(_currentArchetype, index);
            _currentArchetype = newArchetype;
            _currentArchetype.AddEntity(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy()
        {
            if (!IsAlive) throw new Exception($"|KECS| You are trying to destroy an already destroyed entity {ToString()}.");
            RemoveComponents();
            _currentArchetype.RemoveEntity(this);
            _world.RecycleEntity(Id);
            IsAlive = false;
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
        internal void Dispose()
        {
            RemoveComponents();
            Id = -1;
            _archetypeManager = null;
            IsAlive = false;
            _currentArchetype = null;
            _world = null;
        }
    }
}