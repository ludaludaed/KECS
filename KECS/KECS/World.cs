using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public sealed class World
    {
        private readonly SparseSet<IComponentPool> pools;
        private readonly IntDispenser freeIds = new IntDispenser(-1);
        private readonly ArchetypeManager archetypeManager;
        private readonly List<Filter> filters = new List<Filter>();
        private Entity[] entities;

        public WorldConfig Config;
        public int Count => countOfEntities;
        private int countOfEntities = 0;

        public World()
        {
            Config = new WorldConfig()
            {
                ComponentsCapacity = 256,
                EntitiesCapacity = 256,
                ArchetypeCapacity = 256,
                TypeCapacity = 256
            };
            entities = new Entity[Config.EntitiesCapacity];
            pools = new SparseSet<IComponentPool>(Config.TypeCapacity, Config.TypeCapacity);
            archetypeManager = new ArchetypeManager(this);
        }

        public Entity New()
        {
            int id = freeIds.GetFreeInt();

            if (entities.Length == countOfEntities)
            {
                EnsureEntitiesCapacity(id << 1);
            }

            if (entities[id] == null)
            {
                entities[id] = new Entity(this, archetypeManager, id);
            }
            else
            {
                entities[id].Recycle();
            }

            countOfEntities++;
            return entities[id];
        }

        public void InternalEntityDestroy(int id)
        {
            freeIds.ReleaseInt(id);
            countOfEntities--;
        }

        public Filter Filter
        {
            get
            {
                var filter = new Filter(this, archetypeManager);
                filters.Add(filter);
                return filter;
            }
        }

        private void EnsureEntitiesCapacity(int capacity)
        {
            if (entities.Length < capacity)
            {
                var newCapacity = EcsMath.Pot(capacity);
                Array.Resize(ref entities, newCapacity);
                for (int i = 0; i < pools.Count; i++)
                {
                    pools[i].EnsureLength(newCapacity);
                }
            }
        }

        public ComponentPool<T> GetPool<T>() where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (!pools.Contains(idx))
            {
                var pool = new ComponentPool<T>(this);
                pools.Add(idx, pool);
            }

            return (ComponentPool<T>) pools.GetValue(idx);
        }

        public IComponentPool GetPool(int idx)
        {
            var pool = pools.GetValue(idx);
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