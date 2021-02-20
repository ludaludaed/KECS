using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public sealed class World
    {
        private SparseSet<IComponentPool> _pools;
        private List<Filter> _filters;
        private ArchetypeManager _archetypeManager;

        private IntDispenser _freeIds;
        private Entity[] _entities;
        public List<Entity> dirtyEntities;

        public WorldConfig Config;
        public int Count { get; private set; }

        public World()
        {
            Config = new WorldConfig()
                {ComponentsCapacity = 256, EntitiesCapacity = 256, ArchetypeCapacity = 256, TypeCapacity = 256};
            _pools = new SparseSet<IComponentPool>(Config.TypeCapacity, Config.TypeCapacity);
            _filters = new List<Filter>();
            _freeIds = new IntDispenser();
            dirtyEntities = new List<Entity>();
            _entities = new Entity[Config.EntitiesCapacity];
            _archetypeManager = new ArchetypeManager(this);
            Count = 0;
        }

        public Entity CreateEntity()
        {
            int id = _freeIds.GetFreeInt();

            if (_entities.Length == Count)
            {
                EnsureEntitiesCapacity(id << 1);
            }

            _entities[id] ??= new Entity(this, _archetypeManager, id);
            _entities[id].Initialize();
            Count++;
            return _entities[id];
        }

        public void RecycleEntity(int id)
        {
            _freeIds.ReleaseInt(id);
            Count--;
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

        public void Dispose()
        {
            foreach (var item in _filters)
            {
                item?.Dispose();
            }

            _filters.Clear();

            foreach (var item in _entities)
            {
                item?.Dispose();
            }

            _filters = null;
            _freeIds = null;
            _pools = null;
            _entities = null;
            _archetypeManager = null;
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