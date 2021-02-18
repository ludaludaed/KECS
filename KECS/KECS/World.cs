using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public sealed class World
    {
        private readonly SparseSet<IComponentPool> _pools;
        private readonly IntDispenser _freeIds = new IntDispenser(-1);
        private readonly ArchetypeManager _archetypeManager;
        private readonly List<Filter> _filters = new List<Filter>();
        private Entity[] _entities;

        public WorldConfig Config;
        public int Count => _countOfEntities;
        private int _countOfEntities = 0;

        public World()
        {
            Config = new WorldConfig()
            {
                ComponentsCapacity = 256,
                EntitiesCapacity = 256,
                ArchetypeCapacity = 256,
                TypeCapacity = 256
            };
            _entities = new Entity[Config.EntitiesCapacity];
            _pools = new SparseSet<IComponentPool>(Config.TypeCapacity, Config.TypeCapacity);
            _archetypeManager = new ArchetypeManager(this);
        }

        public Entity New()
        {
            int id = _freeIds.GetFreeInt();

            if (_entities.Length == _countOfEntities)
            {
                EnsureEntitiesCapacity(id << 1);
            }

            if (_entities[id] == null)
            {
                _entities[id] = new Entity(this, _archetypeManager, id);
            }

            _countOfEntities++;
            return _entities[id];
        }

        public void InternalEntityDestroy(int id)
        {
            _freeIds.ReleaseInt(id);
            _countOfEntities--;
        }

        public Filter Filter
        {
            get
            {
                var filter = new Filter(this, _archetypeManager);
                _filters.Add(filter);
                return filter;
            }
        }

        private void EnsureEntitiesCapacity(int capacity)
        {
            if (_entities.Length < capacity)
            {
                var newCapacity = EcsMath.Pot(capacity);
                Array.Resize(ref _entities, newCapacity);
                for (int i = 0; i < _pools.Count; i++)
                {
                    _pools[i].EnsureLength(newCapacity);
                }
            }
        }

        public ComponentPool<T> GetPool<T>() where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (!_pools.Contains(idx))
            {
                var pool = new ComponentPool<T>(this);
                _pools.Add(idx, pool);
            }

            return (ComponentPool<T>) _pools.GetValue(idx);
        }

        public IComponentPool GetPool(int idx)
        {
            var pool = _pools.GetValue(idx);
            return pool;
        }
    }

    public struct WorldConfig
    {
        public int ComponentsCapacity;
        public int EntitiesCapacity;
        public int ArchetypeCapacity;
        public int TypeCapacity;
    }
}