using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludaludaed.KECS
{
    public static class Worlds
    {
        private const string DEFAULT_WORLD_NAME = "DEFAULT";
        private static object _lockObject;
        private static IntDispenser _freeWorldsIds;
        private static World[] _worlds;
        private static Dictionary<int, int> _worldsIdx;

        /// <summary>
        /// Return default world.
        /// </summary>

        public static World Default
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_lockObject)
                {
                    int hashName = DEFAULT_WORLD_NAME.GetHashCode();
                    if (_worldsIdx.TryGetValue(hashName, out var worldId))
                    {
                        return _worlds[worldId];
                    }

                    return Create();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Worlds()
        {
            _lockObject = new object();
            _worlds = new World[2];
            _freeWorldsIds = new IntDispenser();
            _worldsIdx = new Dictionary<int, int>(32);
        }

        /// <summary>
        /// Create new world.
        /// </summary>
        /// <param name="name">World name.</param>
        /// <param name="config">Configuration for world</param>
        /// <returns>World.</returns>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Create(string name = DEFAULT_WORLD_NAME, WorldConfig config = default)
        {
            lock (_lockObject)
            {
                int hashName = name.GetHashCode();
                if (_worldsIdx.ContainsKey(hashName))
                {
                    throw new Exception("|KECS| A world with that name already exists.");
                }

                int worldId = _freeWorldsIds.GetFreeInt();
                ArrayExtension.EnsureLength(ref _worlds, worldId);
                var newWorld = new World(worldId, CheckConfig(config), name);
                _worlds[worldId] = newWorld;
                _worldsIdx.Add(hashName, worldId);
                return newWorld;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WorldConfig CheckConfig(WorldConfig config)
        {
            return new WorldConfig
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
        }

        /// <summary>
        /// Returns the world by name.
        /// </summary>
        /// <param name="name">World name</param>
        /// <returns>World.</returns>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(string name)
        {
            int hashName = name.GetHashCode();
            lock (_lockObject)
            {
                if (_worldsIdx.TryGetValue(hashName, out int worldId))
                {
                    return _worlds[worldId];
                }

                throw new Exception("|KECS| No world with that name was found.");
            }
        }

        /// <summary>
        /// Destroy the world by name.
        /// </summary>
        /// <param name="name">World name</param>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Destroy(string name)
        {
            int hashName = name.GetHashCode();
            lock (_lockObject)
            {
                if (_worldsIdx.TryGetValue(hashName, out int worldId))
                {
                    _worldsIdx.Remove(hashName);
                    _worlds[worldId].Dispose();
                    _worlds[worldId] = null;
                    _freeWorldsIds.ReleaseInt(worldId);
                    return;
                }

                throw new Exception("|KECS| A world with that name has not been found. Unable to delete.");
            }
        }

        /// <summary>
        /// Destroys all worlds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyAll()
        {
            lock (_lockObject)
            {
                foreach (var item in _worlds)
                {
                    item?.Dispose();
                }

                Array.Clear(_worlds, 0, _worlds.Length);

                _worldsIdx.Clear();
                _freeWorldsIds.Clear();
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

        /// <summary>
        /// Count of entities.
        /// </summary>
        public int Count { get; private set; }

        private int _worldId;
        private string _name;
        internal readonly WorldConfig Config;

        internal int WorldId => _worldId;
        public string Name => _name;

        private bool _isAlive;
        public bool IsAlive => _isAlive;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal World(int worldId, WorldConfig config, string name)
        {
            _name = name;
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

        /// <summary>
        /// Entity creation.
        /// </summary>
        /// <returns>Entity</returns>
        /// <exception cref="Exception"></exception>
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
        internal void RecycleEntity(int id)
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot recycle entity.");
            _freeIds.ReleaseInt(id);
            Count--;
        }

        /// <summary>
        /// Empty filter.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public Filter Filter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_isAlive)
                    throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot create filter.");
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
        internal ComponentPool<T> GetPool<T>() where T : struct
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
        internal IComponentPool GetPool(int idx)
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot get pool.");
            var pool = _pools.GetValue(idx);
            return pool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
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
        }

        /// <summary>
        /// Destroy the world.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy()
        {
            Worlds.Destroy(_name);
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