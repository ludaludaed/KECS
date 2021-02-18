using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class Entity
    {
        private readonly World _owner;
        private readonly ArchetypeManager _archetypeManager;
        private Archetype _archetype;
        public int Id { get; }

        public Entity(World world, ArchetypeManager archetypeManager, int internalId)
        {
            _owner = world;
            _archetypeManager = archetypeManager;
            this.Id = internalId;
            
            _archetype = _archetypeManager.Empty;
            _archetype.AddEntity(this);
        }

        public override string ToString()
        {
            return $"Entity_{Id}";
        }

        public ref T AddComponent<T>(T value = default) where T : struct
        {
            var pool = _owner.GetPool<T>();
            if (!HasComponent<T>())
            {
                var idx = ComponentTypeInfo<T>.TypeIndex;
                var newArchetype = _archetypeManager.FindOrCreateNextArchetype(_archetype, idx);
                pool.Add(Id, value);
                _archetype.RemoveEntity(this);
                _archetype = newArchetype;
                _archetype.AddEntity(this);
                return ref pool.Get(Id);
            }

            return ref pool.Empty();
        }

        public void RemoveComponent<T>() where T : struct
        {
            if (HasComponent<T>())
            {
                var idx = ComponentTypeInfo<T>.TypeIndex;
                var newArchetype = _archetypeManager.FindOrCreatePriorArchetype(_archetype, idx);
                _owner.GetPool<T>().Remove(Id);
                _archetype.RemoveEntity(this);
                _archetype = newArchetype;
                _archetype.AddEntity(this);
            }
        }

        public ref T GetComponent<T>() where T : struct
        {
            var pool = _owner.GetPool<T>();

            if (HasComponent<T>())
            {
                return ref pool.Get(Id);
            }

            return ref pool.Empty();
        }

        public void Destroy()
        {
            foreach (var idx in _archetype.Mask)
            {
                _owner.GetPool(idx).Remove(Id);
            }
            _archetype.RemoveEntity(this);
            _archetype = _archetypeManager.Empty;
            _owner.InternalEntityDestroy(Id);
        }

        public bool HasComponent<T>() where T : struct
        {
            return _archetype.Mask.GetBit(ComponentTypeInfo<T>.TypeIndex);
        }
    }
}