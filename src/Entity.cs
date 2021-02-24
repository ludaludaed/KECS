using System;
using System.Runtime.CompilerServices;

namespace Ludaludaed.KECS
{
    public class Entity
    {
        private World _world;
        private ArchetypeManager _archetypeManager;
        private Archetype _currentArchetype;
        public bool IsAlive { get; private set; }

        internal int Id { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Entity(World world, ArchetypeManager archetypeManager, int internalId)
        {
            this._world = world;
            Id = internalId;
            this._archetypeManager = archetypeManager;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Initialize()
        {
            _currentArchetype = _archetypeManager.Empty;
            _currentArchetype.AddEntity(this);
            IsAlive = true;
        }

        public override string ToString()
        {
            return $"Entity_{Id}";
        }
        
        /// <summary>
        /// Adding a component to an entity.
        /// </summary>
        /// <param name="value">Component instance.</param>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns>Component.</returns>
        /// <exception cref="Exception"></exception>
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

            return ref pool.Get(Id);
        }
        /// <summary>
        /// Sets an instance of a component to an entity. If it is not on the entity adds it.
        /// </summary>
        /// <param name="value">Component instance.</param>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns>Component.</returns>
        /// <exception cref="Exception"></exception>
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
        
        /// <summary>
        /// Removes a component from an entity.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <exception cref="Exception"></exception>

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
        /// <summary>
        /// Returns a component from an entity.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns>Component.</returns>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>() where T : struct
        {
            if (!IsAlive) throw new Exception($"|KECS| You are trying to get component an already destroyed entity {ToString()}.");
            var pool = _world.GetPool<T>();

            if (HasComponent<T>())
            {
                return ref pool.Get(Id);
            }
            
            throw new Exception($"|KECS| This entity ({ToString()}) has no such component.");
        }
        /// <summary>
        /// Checks if the entity has a component.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
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
        /// <summary>
        /// Destroys the entity.
        /// </summary>
        /// <exception cref="Exception"></exception>
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