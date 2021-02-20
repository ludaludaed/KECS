﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public static class Worlds
    {
        private static object _lockObject;
        private static IntDispenser _freeWorldsIds;
        private static World[] _worlds;

        public static World Default
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_lockObject)
                {
                    if (_worlds[0] != null) return _worlds[0];
                    return CreateWorld();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Worlds()
        {
            _lockObject = new object();
            _worlds = new World[2];
            _freeWorldsIds = new IntDispenser();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World CreateWorld(WorldConfig config = default)
        {
            lock (_lockObject)
            {
                int worldId = _freeWorldsIds.GetFreeInt();
                ArrayExtension.EnsureLength(ref _worlds, worldId);
                var newWorld = new World(worldId, CheckConfig(config));
                _worlds[worldId] = newWorld;
                return newWorld;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WorldConfig CheckConfig(WorldConfig config)
        {
            WorldConfig resultConfig = new WorldConfig
            {
                ArchetypeCapacity = config.ArchetypeCapacity > 0
                    ? config.ArchetypeCapacity
                    : WorldConfig.DefaultArchetypeCapacity,
                EntitiesCapacity = config.EntitiesCapacity > 0
                    ? config.EntitiesCapacity
                    : WorldConfig.DefaultEntitiesCapacity,
                ComponentsCapacity = config.ComponentsCapacity > 0
                    ? config.ComponentsCapacity
                    : WorldConfig.DefaultComponentsCapacity,
                TypeCapacity = config.TypeCapacity > 0
                    ? config.TypeCapacity
                    : WorldConfig.DefaultTypeCapacity
            };

            return resultConfig;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(int worldId)
        {
            lock (_lockObject)
            {
                return _worlds[worldId];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Destroy(int worldId)
        {
            lock (_lockObject)
            {
                _worlds[worldId] = null;
                _freeWorldsIds.ReleaseInt(worldId);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Dispose()
        {
            lock (_lockObject)
            {
                foreach (var item in _worlds)
                {
                    item?.Dispose();
                }
                _freeWorldsIds.Dispose();
                _worlds = null;
                _freeWorldsIds = null;
                _lockObject = null;
            }
        }
    }

    public sealed class World
    {
        private SparseSet<IComponentPool> _pools;
        private List<Filter> _filters;
        private ArchetypeManager _archetypeManager;

        private IntDispenser _freeIds;
        private Entity[] _entities;
        public int Count { get; private set; }

        private int _worldId;
        public int WorldId => _worldId;
        public readonly WorldConfig Config;
        private bool _isAlive;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public World(int worldId, WorldConfig config)
        {
            _isAlive = true;
            _worldId = worldId;
            Config = config;
            _pools = new SparseSet<IComponentPool>(Config.TypeCapacity, Config.TypeCapacity);
            _filters = new List<Filter>();
            _freeIds = new IntDispenser();
            _entities = new Entity[Config.EntitiesCapacity];
            _archetypeManager = new ArchetypeManager(this);
            Count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity CreateEntity()
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot create entity.");
            int newEntityId = _freeIds.GetFreeInt();

            if (_entities.Length == Count)
            {
                EnsureEntitiesCapacity(newEntityId << 1);
            }
            
            _entities[newEntityId] ??= new Entity(this, _archetypeManager, newEntityId);
            _entities[newEntityId].Initialize();
            Count++;
            return _entities[newEntityId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecycleEntity(int id)
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot recycle entity.");
            _freeIds.ReleaseInt(id);
            Count--;
        }
        
        public Filter Filter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot create filter.");
                var filter = new Filter(this, _archetypeManager);
                _filters.Add(filter);
                return filter;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentPool<T> GetPool<T>() where T : struct
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot get pool.");
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (!_pools.Contains(idx))
            {
                var pool = new ComponentPool<T>(this);
                _pools.Add(idx, pool);
            }

            return (ComponentPool<T>) _pools.GetValue(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentPool GetPool(int idx)
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot get pool.");
            var pool = _pools.GetValue(idx);
            return pool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} already destroy");
            foreach (var item in _filters)
            {
                item?.Dispose();
            }

            foreach (var item in _entities)
            {
                item?.Dispose();
            }

            foreach (var pool in _pools)
            {
                pool?.Dispose();
            }
            
            _archetypeManager.Dispose();
            _filters.Clear();
            _pools.Clear();
            _freeIds.Dispose();
            _pools = null;
            _filters = null;
            _freeIds = null;
            _entities = null;
            _archetypeManager = null;
            _isAlive = false;
            Worlds.Destroy(_worldId);
        }

        public override string ToString()
        {
            return $"World - {_worldId} entities count: {Count}";
        }
    }

    public struct WorldConfig
    {
        public int ComponentsCapacity;
        public int EntitiesCapacity;
        public int ArchetypeCapacity;
        public int TypeCapacity;
        public const int DefaultComponentsCapacity = 256;
        public const int DefaultEntitiesCapacity = 256;
        public const int DefaultArchetypeCapacity = 256;
        public const int DefaultTypeCapacity = 256;
    }
}