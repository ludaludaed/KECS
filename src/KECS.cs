using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ludaludaed.KECS
{
    //=============================================================================
    // WORLDS
    //=============================================================================

#if DEBUG
    public interface IWorldDebugListener
    {
        void OnEntityCreated(Entity entity);
        void OnEntityDestroyed(Entity entity);
        void OnArchetypeCreated(Archetype archetype);
        void OnWorldDestroyed(World world);
    }


    public interface ISystemsDebugListener
    {
        void OnSystemsDestroyed(Systems systems);
    }
#endif


    public struct WorldInfo
    {
        public int ActiveEntities;
        public int ReservedEntities;
        public int Archetypes;
        public int Components;
    }


    public struct WorldConfig
    {
        public int ENTITY_COMPONENTS_CAPACITY;
        public int CACHE_ENTITIES_CAPACITY;
        public int CACHE_ARCHETYPES_CAPACITY;
        public int CACHE_COMPONENTS_CAPACITY;
        public const int DEFAULT_ENTITY_COMPONENTS_CAPACITY = 256;
        public const int DEFAULT_CACHE_ENTITIES_CAPACITY = 256;
        public const int DEFAULT_CACHE_ARCHETYPES_CAPACITY = 256;
        public const int DEFAULT_CACHE_COMPONENTS_CAPACITY = 256;
    }


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
                    throw new Exception($"|KECS| A world with {name} name already exists.");
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
                CACHE_ARCHETYPES_CAPACITY = config.CACHE_ARCHETYPES_CAPACITY > 0
                    ? config.CACHE_ARCHETYPES_CAPACITY
                    : WorldConfig.DEFAULT_CACHE_ARCHETYPES_CAPACITY,
                CACHE_ENTITIES_CAPACITY = config.CACHE_ENTITIES_CAPACITY > 0
                    ? config.CACHE_ENTITIES_CAPACITY
                    : WorldConfig.DEFAULT_CACHE_ENTITIES_CAPACITY,
                ENTITY_COMPONENTS_CAPACITY = config.ENTITY_COMPONENTS_CAPACITY > 0
                    ? config.ENTITY_COMPONENTS_CAPACITY
                    : WorldConfig.DEFAULT_ENTITY_COMPONENTS_CAPACITY,
                CACHE_COMPONENTS_CAPACITY = config.CACHE_COMPONENTS_CAPACITY > 0
                    ? config.CACHE_COMPONENTS_CAPACITY
                    : WorldConfig.DEFAULT_CACHE_COMPONENTS_CAPACITY
            };
        }


        /// <summary>
        /// Returns the world by name.
        /// </summary>
        /// <param name="name">World name.</param>
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

                throw new Exception($"|KECS| No world with {name} name was found.");
            }
        }


        /// <summary>
        /// Returns the world by id.
        /// </summary>
        /// <param name="name">World id.</param>
        /// <returns>World.</returns>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World GetById(int worldId)
        {
            lock (_lockObject)
            {
                if (_worlds.Length > worldId)
                {
                    var world = _worlds[worldId];
                    if (world == null)
                    {
                        throw new Exception($"|KECS| World with {worldId} id is null.");
                    }

                    return world;
                }

                throw new Exception($"|KECS| No world with {worldId} id was found.");
            }
        }


        /// <summary>
        /// Returns an existing world or creates a new one if it does not exist.
        /// </summary>
        /// <param name="name">World name.</param>
        /// <param name="config">World configuration.</param>
        /// <returns>World.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World GetOrCreate(string name, WorldConfig config = default)
        {
            int hashName = name.GetHashCode();
            lock (_lockObject)
            {
                if (_worldsIdx.TryGetValue(hashName, out int worldId))
                {
                    return _worlds[worldId];
                }

                return Create(name, config);
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

                throw new Exception($"|KECS| A world with {name} name has not been found. Unable to delete.");
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


    //=============================================================================
    // WORLD
    //=============================================================================


    public sealed class World
    {
        private List<Filter> _filters;
        private ArchetypeManager _archetypeManager;

        private IntDispenser _freeIds;
        private Entity[] _entities;

        private SparseSet<IComponentPool> _pools;
        private int _componentsTypesCount = 0;

        /// <summary>
        /// Count of entities.
        /// </summary>
        public int Count { get; private set; }

        private int _worldId;
        private string _name;
        internal readonly WorldConfig Config;

        public int WorldId => _worldId;
        public string Name => _name;

        private bool _isAlive;
        public bool IsAlive => _isAlive;

#if DEBUG
        private readonly List<IWorldDebugListener> _debugListeners = new List<IWorldDebugListener>();

        public void AddDebugListener(IWorldDebugListener listener)
        {
            if (listener == null)
            {
                throw new Exception("Listener is null.");
            }

            _debugListeners.Add(listener);
        }

        public void RemoveDebugListener(IWorldDebugListener listener)
        {
            if (listener == null)
            {
                throw new Exception("Listener is null.");
            }

            _debugListeners.Remove(listener);
        }
#endif

        public WorldInfo GetInfo()
        {
            return new WorldInfo()
            {
                ActiveEntities = Count,
                ReservedEntities = _freeIds.Count,
                Archetypes = _archetypeManager.Count,
                Components = _componentsTypesCount
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal World(int worldId, WorldConfig config, string name)
        {
            _name = name;
            _isAlive = true;
            _worldId = worldId;
            Config = config;
            _pools = new SparseSet<IComponentPool>(Config.CACHE_COMPONENTS_CAPACITY, Config.CACHE_COMPONENTS_CAPACITY);
            _filters = new List<Filter>();
            _freeIds = new IntDispenser();
            _entities = new Entity[Config.CACHE_ENTITIES_CAPACITY];
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

            var entity = _entities[newEntityId];
            if (entity == null)
            {
                entity = new Entity(this, _archetypeManager, newEntityId);
                _entities[newEntityId] = entity;
            }

            _entities[newEntityId].Initialize();
            Count++;
#if DEBUG
            for (int i = 0; i < _debugListeners.Count; i++)
            {
                _debugListeners[i].OnEntityCreated(_entities[newEntityId]);
            }
#endif

            return _entities[newEntityId];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecycleEntity(int id)
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot recycle entity.");
            _freeIds.ReleaseInt(id);
            Count--;
#if DEBUG
            for (int i = 0; i < _debugListeners.Count; i++)
            {
                _debugListeners[i].OnEntityDestroyed(_entities[id]);
            }
#endif
        }


        internal void ArchetypeCreated(Archetype archetype)
        {
#if DEBUG
            for (int i = 0; i < _debugListeners.Count; i++)
            {
                _debugListeners[i].OnArchetypeCreated(archetype);
            }
#endif
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
                _componentsTypesCount++;
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
            foreach (var filter in _filters)
            {
                filter?.Dispose();
            }

            foreach (var entity in _entities)
            {
                entity?.Dispose();
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

#if DEBUG
            for (int i = 0; i < _debugListeners.Count; i++)
            {
                _debugListeners[i].OnWorldDestroyed(this);
            }
#endif
        }


        public override string ToString()
        {
            return $"World - {_worldId} entities count: {Count}";
        }
    }


    //=============================================================================
    // ENTITY
    //=============================================================================


    public class Entity
    {
        private World _world;
        private ArchetypeManager _archetypeManager;
        private Archetype _currentArchetype;
        public bool IsAlive { get; private set; }
        public Archetype Archetype => _currentArchetype;

        public int Id { get; private set; }

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
        public ref T Add<T>(in T value = default) where T : struct
        {
            if (!IsAlive)
                throw new Exception(
                    $"|KECS| You are trying to add component an already destroyed entity {ToString()}.");
            var pool = _world.GetPool<T>();
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (!Has<T>())
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
        public ref T Set<T>(in T value) where T : struct
        {
            if (!IsAlive)
                throw new Exception(
                    $"|KECS| You are trying to set component an already destroyed entity {ToString()}.");
            var pool = _world.GetPool<T>();
            var idx = ComponentTypeInfo<T>.TypeIndex;
            pool.Set(Id, value);

            if (!Has<T>())
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
        public void Remove<T>() where T : struct
        {
            if (!IsAlive)
                throw new Exception(
                    $"|KECS| You are trying to remove component an already destroyed entity {ToString()}.");

            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (Has<T>())
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
        public ref T Get<T>() where T : struct
        {
            if (!IsAlive)
                throw new Exception(
                    $"|KECS| You are trying to get component an already destroyed entity {ToString()}.");
            var pool = _world.GetPool<T>();

            if (Has<T>())
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
        public bool Has<T>() where T : struct
        {
            if (!IsAlive)
                throw new Exception(
                    $"|KECS| You are trying to check component an already destroyed entity {ToString()}.");
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
            if (!IsAlive)
                throw new Exception($"|KECS| You are trying to destroy an already destroyed entity {ToString()}.");
            RemoveComponents();
            _currentArchetype.RemoveEntity(this);
            _world.RecycleEntity(Id);
            IsAlive = false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveComponents()
        {
            if (IsAlive)
            {
                foreach (var idx in _currentArchetype.Mask)
                {
                    _world.GetPool(idx).Remove(Id);
                }
            }
        }


        private int GetComponentsValues(ref object[] components)
        {
            if (IsAlive)
            {
                var itemsCount = Archetype.Mask.Count;

                if (components == null || components.Length < itemsCount)
                {
                    components = new object[itemsCount];
                }

                int counter = 0;

                foreach (var idx in _currentArchetype.Mask)
                {
                    components[counter++] = _world.GetPool(idx).GetObject(Id);
                }

                return itemsCount;
            }

            throw new Exception(
                $"|KECS| You are trying to get components an already destroyed entity {ToString()}.");
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            RemoveComponents();
            Id = -1;
            _archetypeManager = null;
            _currentArchetype = null;
            _world = null;
            IsAlive = false;
        }
    }


    //=============================================================================
    // ARCHETYPES
    //=============================================================================


    internal class ArchetypeManager
    {
        private List<Archetype> _archetypes;
        internal Archetype Empty { get; private set; }
        private World _world;
        private object _lockObject = new object();

        internal int Count => _archetypes.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ArchetypeManager(World world)
        {
            _world = world;
            Empty = new Archetype(this._world, 0, new BitMask(256));
            _archetypes = new List<Archetype>(world.Config.CACHE_ARCHETYPES_CAPACITY) {Empty};
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void FindArchetypes(Filter filter, int startId)
        {
            for (int i = startId; i < _archetypes.Count; i++)
            {
                CheckArchetype(_archetypes[i], filter);
            }

            filter.Version = _archetypes.Count;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckArchetype(Archetype archetype, Filter filter)
        {
            var include = filter.Include;
            var exclude = filter.Exclude;

            if (archetype.Mask.Contains(include) && (exclude.Count == 0 || !archetype.Mask.Contains(exclude)))
            {
                filter.AddArchetype(archetype);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Archetype InnerFindOrCreateArchetype(BitMask mask)
        {
            lock (_lockObject)
            {
                Archetype curArchetype = Empty;
                var newMask = new BitMask(256);

                foreach (var index in mask)
                {
                    newMask.SetBit(index);

                    Archetype nextArchetype = curArchetype.Next.GetValue(index);

                    if (nextArchetype == null)
                    {
                        nextArchetype = new Archetype(_world, _archetypes.Count, newMask);

                        nextArchetype.Prior.Add(index, curArchetype);
                        curArchetype.Next.Add(index, nextArchetype);

                        _archetypes.Add(nextArchetype);
                    }

                    curArchetype = nextArchetype;
                }

                return curArchetype;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreatePriorArchetype(Archetype archetype, int removeIndex)
        {
            Archetype priorArchetype = archetype.Prior.GetValue(removeIndex);
            if (priorArchetype != null)
                return priorArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.ClearBit(removeIndex);

            return InnerFindOrCreateArchetype(mask);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreateNextArchetype(Archetype archetype, int addIndex)
        {
            Archetype nextArchetype = archetype.Next.GetValue(addIndex);
            if (nextArchetype != null)
                return nextArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.SetBit(addIndex);

            return InnerFindOrCreateArchetype(mask);
        }


        internal void Dispose()
        {
            for (int i = 0; i < _archetypes.Count; i++)
            {
                _archetypes[i].Dispose();
            }

            _archetypes.Clear();
            _archetypes = null;
            _world = null;
            _lockObject = null;
            Empty = null;
        }
    }


    public sealed class Archetype : IEnumerable<Entity>
    {
        public int Count => Entities.Count;
        public int Id { get; private set; }

        private DelayedChange[] _delayedChanges;
        private int _delayedOpsCount;

        internal SparseSet<Entity> Entities;
        internal BitMask Mask { get; }

        internal SparseSet<Archetype> Next;
        internal SparseSet<Archetype> Prior;

        public Type[] TypesCache;

        private int _lockCount;


        public override string ToString()
        {
            return $"Archetype_{Id}";
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype(World world, int id, BitMask mask)
        {
            Mask = mask;
            Id = id;
            _lockCount = 0;

            _delayedChanges = new DelayedChange[64];
            Next = new SparseSet<Archetype>(world.Config.CACHE_COMPONENTS_CAPACITY,
                world.Config.CACHE_COMPONENTS_CAPACITY);
            Prior = new SparseSet<Archetype>(world.Config.CACHE_COMPONENTS_CAPACITY,
                world.Config.CACHE_COMPONENTS_CAPACITY);

            Entities = new SparseSet<Entity>(world.Config.CACHE_ENTITIES_CAPACITY,
                world.Config.CACHE_ENTITIES_CAPACITY);

            TypesCache = new Type[mask.Count];

            int counter = 0;
            foreach (var idx in mask)
            {
                TypesCache[counter++] = EcsTypeManager.ComponentsTypes[idx];
            }

            world.ArchetypeCreated(this);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Lock() => _lockCount++;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Unlock()
        {
            _lockCount--;

            if (_lockCount == 0 && _delayedOpsCount > 0)
            {
                for (int i = 0; i < _delayedOpsCount; i++)
                {
                    ref var op = ref _delayedChanges[i];
                    if (op.IsAdd)
                    {
                        AddEntity(op.Entity);
                    }
                    else
                    {
                        RemoveEntity(op.Entity);
                    }
                }

                _delayedOpsCount = 0;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool AddDelayedChange(Entity entity, bool isAdd)
        {
            if (_lockCount <= 0)
            {
                return false;
            }

            ArrayExtension.EnsureLength(ref _delayedChanges, _delayedOpsCount);
            ref var op = ref _delayedChanges[_delayedOpsCount++];
            op.Entity = entity;
            op.IsAdd = isAdd;
            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddEntity(Entity entity)
        {
            if (AddDelayedChange(entity, true))
            {
                return;
            }

            Entities.Add(entity.Id, entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveEntity(Entity entity)
        {
            if (AddDelayedChange(entity, false))
            {
                return;
            }

            Entities.Remove(entity.Id);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<Entity> GetEnumerator()
        {
            return Entities.GetEnumerator();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        private struct DelayedChange
        {
            public bool IsAdd;
            public Entity Entity;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            Entities.Clear();
            Next.Clear();
            Prior.Clear();

            Array.Clear(TypesCache, 0, TypesCache.Length);
            TypesCache = null;

            Entities = null;
            Next = null;
            Prior = null;
            _delayedChanges = null;
            _lockCount = 0;
            _delayedOpsCount = 0;
            Id = -1;
        }
    }


    //=============================================================================
    // FILTER
    //=============================================================================


    public sealed class Filter : IEnumerable<Entity>
    {
        internal BitMask Include;
        internal BitMask Exclude;
        internal int Version { get; set; }

        private List<Archetype> _archetypes = new List<Archetype>();
        private ArchetypeManager _archetypeManager;
        private World _world;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Filter(World world, ArchetypeManager archetypeManager)
        {
            _archetypeManager = archetypeManager;
            Version = 0;
            _world = world;
            Include = new BitMask(256);
            Exclude = new BitMask(256);
        }


        /// <summary>
        /// Include component.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Filter With<T>() where T : struct
        {
            int typeIdx = ComponentTypeInfo<T>.TypeIndex;

            if (Exclude.GetBit(typeIdx))
            {
                return this;
            }

            Include.SetBit(typeIdx);
            return this;
        }


        /// <summary>
        /// Exclude component.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Filter WithOut<T>() where T : struct
        {
            int typeIdx = ComponentTypeInfo<T>.TypeIndex;

            if (Include.GetBit(typeIdx))
            {
                return this;
            }

            Exclude.SetBit(typeIdx);
            return this;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddArchetype(Archetype archetype)
        {
            _archetypes.Add(archetype);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<Entity> GetEnumerator()
        {
            _archetypeManager.FindArchetypes(this, Version);
            return new EntityEnumerator(this);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            Version = 0;
            _archetypes.Clear();
            _archetypes = null;
            _world = null;
            _archetypeManager = null;
        }


        private struct EntityEnumerator : IEnumerator<Entity>
        {
            private readonly List<Archetype> _archetypes;
            private readonly int _archetypeCount;

            private int _index;
            private int _archetypeId;

            private SparseSet<Entity> _archetypeEntities;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal EntityEnumerator(Filter filter)
            {
                _archetypes = filter._archetypes;
                Current = null;

                _archetypeId = 0;
                _archetypeCount = _archetypes.Count;
                _archetypeEntities = _archetypeCount == 0 ? null : _archetypes[0].Entities;

                for (int i = 0; i < _archetypes.Count; i++)
                {
                    _archetypes[i].Lock();
                }

                _index = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_archetypeCount == 1)
                {
                    if (_index < _archetypes[_archetypeId].Count)
                    {
                        Current = _archetypeEntities[_index++];
                        return true;
                    }

                    return false;
                }

                if (_archetypeId < _archetypeCount)
                {
                    if (_index < _archetypes[_archetypeId].Count)
                    {
                        Current = _archetypeEntities[_index++];
                        return true;
                    }

                    while (++_archetypeId < _archetypeCount)
                    {
                        _archetypeEntities = _archetypes[_archetypeId].Entities;
                        if (_archetypeEntities.Count > 0)
                        {
                            _index = 0;
                            Current = _archetypeEntities[_index++];
                            return true;
                        }
                    }
                }

                return false;
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                Current = null;
                _archetypeId = 0;
                _archetypeEntities = _archetypeCount == 0 ? null : _archetypes[0].Entities;
                _index = 0;
            }


            public Entity Current { get; private set; }


            object IEnumerator.Current => Current;


            public void Dispose()
            {
                for (int i = 0; i < _archetypes.Count; i++)
                {
                    _archetypes[i].Unlock();
                }
            }
        }
    }


    //=============================================================================
    // POOLS
    //=============================================================================


    internal static class EcsTypeManager
    {
        internal static int ComponentTypesCount = 0;
        public static Type[] ComponentsTypes = new Type[WorldConfig.DEFAULT_CACHE_COMPONENTS_CAPACITY];
    }


    internal static class ComponentTypeInfo<T> where T : struct
    {
        internal static readonly int TypeIndex;
        internal static readonly Type Type;

        private static object _lockObject = new object();


        static ComponentTypeInfo()
        {
            lock (_lockObject)
            {
                TypeIndex = EcsTypeManager.ComponentTypesCount++;
                Type = typeof(T);
                ArrayExtension.EnsureLength(ref EcsTypeManager.ComponentsTypes, TypeIndex);
                EcsTypeManager.ComponentsTypes[TypeIndex] = Type;
            }
        }
    }


    internal interface IComponentPool : IDisposable
    {
        void Remove(int entityId);
        void EnsureLength(int capacity);

        object GetObject(int entityId);
    }


    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private SparseSet<T> _components;
        private int Length => _components.Count;
        private T _empty;
        private World _owner;

        public ref T Empty() => ref _empty;


        public ComponentPool(World world)
        {
            _owner = world;
            _components = new SparseSet<T>(world.Config.ENTITY_COMPONENTS_CAPACITY,
                world.Config.ENTITY_COMPONENTS_CAPACITY);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityId)
        {
            return ref _components.GetValue(entityId);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetObject(int entityId)
        {
            return _components.GetValue(entityId);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int entityId, in T value)
        {
            _components.Add(entityId, value);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int entityId)
        {
            _components.Remove(entityId);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityId, in T value)
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


    //=============================================================================
    // SYSTEMS
    //=============================================================================


    /// <summary>
    /// Interface for all running systems.
    /// </summary>
    public interface IUpdate
    {
        void OnUpdate(float deltaTime);
    }

    public interface IFixedUpdate : IUpdate
    {
    }

    public interface ILateUpdate : IUpdate
    {
    }

    /// <summary>
    /// Base class for all systems.
    /// </summary>
    public abstract class SystemBase : IDisposable
    {
        protected World World;
        protected Systems MySystemsGroup;
        public abstract void Initialize();


        internal void StartUp(World world, Systems systems)
        {
            World = world;
            MySystemsGroup = systems;
            OnLaunch();
        }


        public virtual void OnLaunch()
        {
        }


        public virtual void OnDestroy()
        {
        }


        public virtual void PostDestroy()
        {
        }


        public void Dispose() => OnDestroy();
    }


    public sealed class SystemData
    {
        public bool IsEnable;
        public SystemBase Base;
        public IUpdate UpdateImpl;
    }


    public sealed class Systems : IDisposable
    {
        private readonly Dictionary<int, SystemData> _systems;

        private readonly List<SystemData> _updateSystems;
        private readonly List<SystemData> _fixedSystems;
        private readonly List<SystemData> _lateSystems;
        private readonly List<SystemData> _allSystems;
        private readonly List<SystemData> _onlyBaseSystems;

        private readonly World _world;
        private bool _initialized;
        private bool _destroyed;


        public string Name { get; private set; }


        public Systems(World world, string name = "DEFAULT")
        {
            Name = name;
            _world = world;
            _initialized = false;
            _destroyed = false;
            _systems = new Dictionary<int, SystemData>();
            _allSystems = new List<SystemData>();
            _updateSystems = new List<SystemData>();
            _fixedSystems = new List<SystemData>();
            _lateSystems = new List<SystemData>();
            _onlyBaseSystems = new List<SystemData>();
        }

#if DEBUG
        private readonly List<ISystemsDebugListener> _debugListeners = new List<ISystemsDebugListener>(4);


        public void AddDebugListener(ISystemsDebugListener listener)
        {
            if (listener == null)
            {
                throw new Exception("listener is null");
            }

            _debugListeners.Add(listener);
        }


        public void RemoveDebugListener(ISystemsDebugListener listener)
        {
            if (listener == null)
            {
                throw new Exception("listener is null");
            }

            _debugListeners.Remove(listener);
        }
#endif

        /// <summary>
        /// Returns all run systems.
        /// </summary>
        /// <returns></returns>
        public List<SystemData> GetUpdateSystems()
        {
            return _updateSystems;
        }
        
        public List<SystemData> GetFixedUpdateSystems()
        {
            return _fixedSystems;
        }
        
        public List<SystemData> GetLateUpdateSystems()
        {
            return _lateSystems;
        }

        public List<SystemData> GetOnlyBaseSystems()
        {
            return _onlyBaseSystems;
        }

        /// <summary>
        /// Adding a new system.
        /// </summary>
        /// <typeparam name="T">System type.</typeparam>
        /// <returns></returns>
        public Systems Add<T>() where T : SystemBase, new()
        {
            var obj = new T();
            return Add(obj);
        }


        private Systems Add<T>(T systemValue) where T : SystemBase
        {
            if (_initialized)
            {
                throw new Exception("|KECS| System cannot be added after initialization.");
            }

            int hash = typeof(T).GetHashCode();

            if (!_systems.ContainsKey(hash))
            {
                var systemData = new SystemData {IsEnable = true, Base = systemValue};
                _allSystems.Add(systemData);
                systemValue.StartUp(_world, this);
                
                if (systemValue is IUpdate system)
                {
                    var collection = _updateSystems;
                    var impl = system;

                    if (systemValue is IFixedUpdate fixedSystem)
                    {
                        collection = _fixedSystems;
                        impl = fixedSystem;
                    }
                    
                    if (systemValue is ILateUpdate lateSystem)
                    {
                        collection = _lateSystems;
                        impl = lateSystem;
                    }

                    systemData.UpdateImpl = impl;
                    collection.Add(systemData);
                }
                else
                {
                    _onlyBaseSystems.Add(systemData);
                }
                _systems.Add(hash, systemData);
            }

            return this;
        }


        /// <summary>
        /// Disable the system.
        /// </summary>
        /// <typeparam name="T">System type.</typeparam>
        /// <returns></returns>
        public Systems Disable<T>() where T : SystemBase
        {
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            int hash = typeof(T).GetHashCode();

            if (_systems.TryGetValue(hash, out var systemValue))
            {
                systemValue.IsEnable = false;
            }

            return this;
        }
        
        
        /// <summary>
        /// Enable the system.
        /// </summary>
        /// <typeparam name="T">System type.</typeparam>
        /// <returns></returns>
        public Systems Enable<T>() where T : SystemBase
        {
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            int hash = typeof(T).GetHashCode();

            if (_systems.TryGetValue(hash, out var systemValue))
            {
                systemValue.IsEnable = true;
            }

            return this;
        }


        /// <summary>
        /// Building a cleaning system for one frame (event) components.
        /// </summary>
        /// <typeparam name="T">Component type</typeparam>
        /// <returns></returns>
        public Systems OneFrame<T>() where T : struct
        {
            return Add(new RemoveOneFrame<T>());
        }


        /// <summary>
        /// Iterates all IUpdate systems.
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <exception cref="Exception"></exception>
        public void Update(float deltaTime)
        {
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            foreach (var update in _updateSystems)
            {
                if (update.IsEnable)
                {
                    update.UpdateImpl?.OnUpdate(deltaTime);
                }
            }
        }
        
        
        /// <summary>
        /// Iterates all IFixedUpdate systems.
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <exception cref="Exception"></exception>
        public void FixedUpdate(float deltaTime)
        {
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            foreach (var update in _fixedSystems)
            {
                if (update.IsEnable)
                {
                    update.UpdateImpl?.OnUpdate(deltaTime);
                }
            }
        }
        
        
        /// <summary>
        /// Iterates all IUpdate systems.
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <exception cref="Exception"></exception>
        public void LateUpdate(float deltaTime)
        {
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            foreach (var update in _lateSystems)
            {
                if (update.IsEnable)
                {
                    update.UpdateImpl?.OnUpdate(deltaTime);
                }
            }
        }


        /// <summary>
        /// Initialize all systems.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Initialize()
        {
            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot initialize them.");
            }

            _initialized = true;
            foreach (var initializer in _allSystems)
            {
                initializer.Base.Initialize();
            }
        }


        /// <summary>
        /// Destroy all systems.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Destroy()
        {
            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot destroy them.");
            }

            _destroyed = true;

            foreach (var destroy in _allSystems)
            {
                if (destroy.IsEnable)
                {
                    destroy.Base.OnDestroy();
                }
            }

            foreach (var postDestroy in _allSystems)
            {
                if (postDestroy.IsEnable)
                {
                    postDestroy.Base.PostDestroy();
                }
            }
#if DEBUG
            for (int i = 0; i < _debugListeners.Count; i++)
            {
                _debugListeners[i].OnSystemsDestroyed(this);
            }
#endif
        }


        public void Dispose()
        {
            _systems.Clear();
            _allSystems.Clear();
            _updateSystems.Clear();
            _fixedSystems.Clear();
            _lateSystems.Clear();
        }
    }

    internal class RemoveOneFrame<T> : SystemBase, ILateUpdate where T : struct
    {
        private Filter _filter;


        public override void Initialize()
        {
            _filter = World.Filter.With<T>();
        }


        public void OnUpdate(float deltaTime)
        {
            foreach (var ent in _filter)
            {
                ent.Remove<T>();
            }
        }
    }


    //=============================================================================
    // SPARSE SETS
    //=============================================================================


    public class SparseSet : IEnumerable
    {
        protected const int None = -1;
        protected int[] Dense;
        protected int DenseCount;
        protected int[] Sparse;


        public ref int this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < DenseCount)
                {
                    return ref Dense[index];
                }

                throw new Exception($"|KECS| Out of range SparseSet {index}.");
            }
        }


        public SparseSet(int denseCapacity, int sparseCapacity)
        {
            Dense = new int[denseCapacity];
            Sparse = new int[sparseCapacity];

            for (int i = 0; i < sparseCapacity; i++)
            {
                Sparse[i] = None;
            }

            DenseCount = 0;
        }


        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => DenseCount;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Get(int sparseIdx)
        {
            ArrayExtension.EnsureLength(ref Sparse, sparseIdx, None);

            var packedIdx = Sparse[sparseIdx];
            if (packedIdx != None && packedIdx < DenseCount)
            {
                return packedIdx;
            }

            return None;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int sparseIdx)
        {
            if (Get(sparseIdx) != None)
            {
                throw new Exception($"|KECS| Unable to add sparse idx {sparseIdx}: already present.");
            }

            if (Dense.Length == DenseCount)
            {
                EnsurePackedCapacity(DenseCount << 1);
            }

            Sparse[sparseIdx] = DenseCount;
            Dense[DenseCount] = sparseIdx;
            DenseCount++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int sparseIdx)
        {
            if (Get(sparseIdx) == None)
            {
                throw new Exception($"|KECS| Unable to remove sparse idx {sparseIdx}: not present.");
            }

            var packedIdx = Sparse[sparseIdx];
            Sparse[sparseIdx] = None;
            DenseCount--;
            if (packedIdx < DenseCount)
            {
                var lastSparseIdx = Dense[DenseCount];
                Dense[packedIdx] = lastSparseIdx;
                Sparse[lastSparseIdx] = packedIdx;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int sparseIdx)
        {
            ArrayExtension.EnsureLength(ref Sparse, sparseIdx, None);
            return Sparse[sparseIdx] != None;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureSparseCapacity(int capacity)
        {
            ArrayExtension.EnsureLength(ref Sparse, capacity, None);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void EnsurePackedCapacity(int capacity)
        {
            Array.Resize(ref Dense, capacity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator GetEnumerator()
        {
            return new Enumerator(this);
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            DenseCount = 0;
            Array.Clear(Dense, 0, Dense.Length);
            Sparse.Fill(None);
        }


        private struct Enumerator : IEnumerator
        {
            private int _count;
            private int _index;
            private SparseSet _sparseSet;


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(SparseSet sparseSet)
            {
                this._sparseSet = sparseSet;
                _count = sparseSet.Count;
                _index = 0;
                Current = default;
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _count = 0;
                _index = 0;
                _sparseSet = null;
                Current = default;
            }


            object IEnumerator.Current => Current;


            public int Current { get; private set; }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_index >= _count) return false;
                Current = _sparseSet.Dense[_index++];
                return true;
            }


            public void Dispose()
            {
            }
        }
    }


    public class SparseSet<T> : SparseSet, IEnumerable<T>
    {
        private T[] _instances;
        private T _empty;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SparseSet(int denseCapacity, int sparseCapacity) : base(denseCapacity, sparseCapacity)
        {
            _instances = new T[denseCapacity];
            _empty = default;
        }


        public new ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < DenseCount)
                {
                    return ref _instances[index];
                }

                throw new Exception($"|KECS| Out of range SparseSet {index}.");
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetValue(int sparseIdx)
        {
            var packedIdx = Get(sparseIdx);
            return ref packedIdx != None ? ref _instances[packedIdx] : ref _empty;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Add(int sparseIdx)
        {
            Add(sparseIdx, _empty);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int sparseIdx, T value)
        {
            if (Get(sparseIdx) != None)
            {
                throw new Exception($"|KECS| Unable to add sparse idx {sparseIdx}: already present.");
            }

            if (Dense.Length == DenseCount)
            {
                EnsurePackedCapacity(DenseCount << 1);
            }

            Sparse[sparseIdx] = DenseCount;
            Dense[DenseCount] = sparseIdx;
            _instances[DenseCount] = value;
            DenseCount++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int sparseIdx, T value)
        {
            if (Get(sparseIdx) == None)
            {
                Add(sparseIdx, value);
                return;
            }

            if (Dense.Length == DenseCount)
            {
                EnsurePackedCapacity(DenseCount << 1);
            }

            _instances[sparseIdx] = value;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Remove(int sparseIdx)
        {
            if (Get(sparseIdx) == None)
            {
                throw new Exception($"|KECS| Unable to remove sparse idx {sparseIdx}: not present.");
            }

            var packedIdx = Sparse[sparseIdx];
            Sparse[sparseIdx] = None;
            DenseCount--;
            if (packedIdx < DenseCount)
            {
                var lastValue = _instances[DenseCount];
                var lastSparseIdx = Dense[DenseCount];
                Dense[packedIdx] = lastSparseIdx;
                _instances[packedIdx] = lastValue;
                Sparse[lastSparseIdx] = packedIdx;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EnsurePackedCapacity(int capacity)
        {
            base.EnsurePackedCapacity(capacity);
            Array.Resize(ref _instances, capacity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Clear()
        {
            DenseCount = 0;
            Array.Clear(_instances, 0, _instances.Length);
            Array.Clear(Dense, 0, Dense.Length);
            Sparse.Fill(None);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        private struct Enumerator : IEnumerator<T>
        {
            private int _count;
            private int _index;
            private SparseSet<T> _sparseSet;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(SparseSet<T> sparseSet)
            {
                this._sparseSet = sparseSet;
                _count = sparseSet.Count;
                _index = 0;
                Current = default;
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _count = 0;
                _index = 0;
                _sparseSet = null;
                Current = default;
            }


            object IEnumerator.Current => Current;


            public T Current { get; private set; }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_index < _count)
                {
                    Current = _sparseSet[_index++];
                    return true;
                }

                return false;
            }


            public void Dispose()
            {
            }
        }
    }


    //=============================================================================
    // HELPER
    //=============================================================================


    internal sealed class IntDispenser
    {
        private ConcurrentStack<int> _freeInts;
        private int _lastInt;
        public int LastInt => _lastInt;
        private readonly int _startInt;
        public int Count => _freeInts.Count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntDispenser(int startInt = -1)
        {
            _freeInts = new ConcurrentStack<int>();
            _startInt = startInt;
            _lastInt = startInt;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetFreeInt()
        {
            if (!_freeInts.TryPop(out int freeInt))
            {
                freeInt = Interlocked.Increment(ref _lastInt);
            }

            return freeInt;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseInt(int releasedInt) => _freeInts.Push(releasedInt);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            _freeInts.Clear();
            _freeInts = null;
            _lastInt = _startInt;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _freeInts.Clear();
            _lastInt = _startInt;
        }
    }


    public static class EcsMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Pot(int v)
        {
            if (v < 2)
            {
                return 2;
            }

            var n = v - 1;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
        }
    }


    public static class ListExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveAtFast<T>(this IList<T> list, int index)
        {
            var count = list.Count;
            list[index] = list[count - 1];
            list.RemoveAt(count - 1);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveFast<T>(this IList<T> list, T item)
        {
            var count = list.Count;
            var index = list.IndexOf(item);
            list[index] = list[count - 1];
            list.RemoveAt(count - 1);
        }
    }


    internal static class ArrayExtension
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InnerEnsureLength<T>(ref T[] array, int index)
        {
            int newLength = Math.Max(1, array.Length);

            do
            {
                newLength *= 2;
            } while (index >= newLength);

            Array.Resize(ref array, newLength);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fill<T>(this T[] array, in T value, int start = 0)
        {
            for (int i = start; i < array.Length; ++i)
            {
                array[i] = value;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureLength<T>(ref T[] array, int index)
        {
            if (index >= array.Length)
            {
                InnerEnsureLength(ref array, index);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureLength<T>(ref T[] array, int index, in T defaultValue)
        {
            if (index >= array.Length)
            {
                int oldLength = array.Length;

                InnerEnsureLength(ref array, index);
                array.Fill(defaultValue, oldLength);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf<T>(T[] array, T value, EqualityComparer<T> comparer)
        {
            for (int i = 0, length = array.Length; i < length; ++i)
            {
                if (comparer.Equals(array[i], value))
                {
                    return i;
                }
            }

            return -1;
        }
    }


    //=============================================================================
    // BIT MASK
    //=============================================================================


    public struct BitMask
    {
        private const int ChunkCapacity = sizeof(ulong) * 8;
        private readonly ulong[] _chunks;
        private readonly int _capacity;


        public int Count { get; private set; }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask(int capacity = 0)
        {
            this._capacity = capacity;
            var newSize = capacity / ChunkCapacity;
            if (capacity % ChunkCapacity != 0)
            {
                newSize++;
            }

            Count = 0;
            _chunks = new ulong[newSize];
        }


        public BitMask(in BitMask copy)
        {
            this._capacity = copy._capacity;
            var newSize = _capacity / ChunkCapacity;
            if (_capacity % ChunkCapacity != 0)
            {
                newSize++;
            }

            Count = 0;

            _chunks = new ulong[newSize];

            foreach (var item in copy)
            {
                SetBit(item);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldV = _chunks[chunk];
            var newV = oldV | (1UL << (index % ChunkCapacity));
            if (oldV == newV) return;
            _chunks[chunk] = newV;
            Count++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldV = _chunks[chunk];
            var newV = oldV & ~(1UL << (index % ChunkCapacity));
            if (oldV == newV) return;
            _chunks[chunk] = newV;
            Count--;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetBit(int idx)
        {
            return (_chunks[idx / ChunkCapacity] & (1UL << (idx % ChunkCapacity))) != 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(BitMask bitMask)
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                if ((_chunks[i] & bitMask._chunks[i]) != bitMask._chunks[i])
                {
                    return false;
                }
            }

            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BitMask bitMask)
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                if ((_chunks[i] & bitMask._chunks[i]) != 0)
                {
                    return true;
                }
            }

            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                _chunks[i] = 0;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Merge(BitMask include)
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                _chunks[i] |= include._chunks[i];
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }


        public ref struct Enumerator
        {
            private readonly BitMask _bitMask;
            private readonly int _count;
            private int _index;
            private int _returned;


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(BitMask bitMask)
            {
                this._bitMask = bitMask;
                _count = bitMask.Count;
                _index = -1;
                _returned = 0;
            }


            public int Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    while (true)
                    {
                        _index++;
                        if (!_bitMask.GetBit(_index)) continue;
                        _returned++;
                        return _index;
                    }
                }
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                return _returned < _count;
            }
        }
    }
}

#if ENABLE_IL2CPP
namespace Unity.IL2CPP.CompilerServices {
    enum Option {
        NullChecks = 1,
        ArrayBoundsChecks = 2
    }

    [AttributeUsage (AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited =
 false, AllowMultiple = true)]
    class Il2CppSetOptionAttribute : Attribute {
        public Option Option { get; private set; }
        public object Value { get; private set; }

        public Il2CppSetOptionAttribute (Option option, object value) { Option = option; Value = value; }
    }
}
#endif