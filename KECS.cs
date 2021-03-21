using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace Ludaludaed.KECS
{
    //=============================================================================
    // WORLDS
    //=============================================================================

#if DEBUG
    public interface IWorldDebugListener
    {
        void OnEntityCreated(in Entity entity);
        void OnEntityDestroyed(in Entity entity);
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
        /// <param name="worldId">World id.</param>
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
    // SHARED DATA
    //=============================================================================


    internal class SharedData
    {
        private Dictionary<int, object> _data;

        internal SharedData()
        {
            _data = new Dictionary<int, object>();
        }

        internal T Add<T>(T data) where T : class
        {
            int hash = typeof(T).GetHashCode();
            if (!_data.ContainsKey(hash))
            {
                _data.Add(hash, data);
                return data;
            }

            throw new Exception($"|KECS| You have already added this type{typeof(T).Name} of data");
        }


        internal T Get<T>() where T : class
        {
            int hash = typeof(T).GetHashCode();

            if (_data.TryGetValue(hash, out var data))
            {
                return data as T;
            }

            throw new Exception($"|KECS| No data of this type {typeof(T).Name} was found");
        }


        internal void Dispose()
        {
            _data.Clear();
            _data = null;
        }
    }


    //=============================================================================
    // WORLD
    //=============================================================================


    public sealed class World
    {
        private List<Filter> _filters;

        private int _worldId;
        private string _name;
        private bool _isAlive;
        public readonly WorldConfig Config;

        public int WorldId => _worldId;
        public string Name => _name;
        public bool IsAlive => _isAlive;

        internal EntityManager EntityManager;
        internal ArchetypeManager ArchetypeManager;
        internal ComponentManager ComponentManager;


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
                var filter = new Filter(this);
                _filters.Add(filter);
                return filter;
            }
        }


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
                ActiveEntities = EntityManager.ActiveEntities,
                ReservedEntities = EntityManager.ReservedEntities,
                Archetypes = ArchetypeManager.Count,
                Components = ComponentManager.Count
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal World(int worldId, WorldConfig config, string name)
        {
            _name = name;
            _isAlive = true;
            _worldId = worldId;
            Config = config;
            _filters = new List<Filter>();
            ArchetypeManager = new ArchetypeManager(this);
            ComponentManager = new ComponentManager(this);
            EntityManager = new EntityManager(this);
        }

        /// <summary>
        /// Entity creation.
        /// </summary>
        /// <returns>Entity</returns>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity CreateEntity()
        {
            return EntityManager.CreateEntity();
        }

        internal void ArchetypeCreated(Archetype archetype)
        {
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners[i].OnArchetypeCreated(archetype);
            }
#endif
        }


        internal void EntityCreated(Entity entity)
        {
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners[i].OnEntityCreated(entity);
            }
#endif
        }


        internal void EntityDestroyed(Entity entity)
        {
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners[i].OnEntityDestroyed(entity);
            }
#endif
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} already destroy");
            foreach (var filter in _filters)
            {
                filter?.Dispose();
            }

            _filters.Clear();

            _worldId = -1;
            _name = null;
            _filters = null;
            _isAlive = false;

            EntityManager.Dispose();
            ArchetypeManager.Dispose();
            ComponentManager.Dispose();
        }

        /// <summary>
        /// Destroy the world.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy()
        {
            Worlds.Destroy(_name);

#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners[i].OnWorldDestroyed(this);
            }
#endif
        }


        public override string ToString()
        {
            return $"World - {_worldId} <{Name}>";
        }
    }


    //=============================================================================
    // ENTITY
    //=============================================================================


    public sealed class EntityManager : IDisposable
    {
        private IntDispenser _freeIds;
        private EntityData[] _entities;
        private int _entitiesCount;

        public int ActiveEntities => _entitiesCount;
        public int ReservedEntities => _freeIds.Count;

        private World _world;
        private ArchetypeManager _archetypeManager;
        private ComponentManager _componentManager;

        internal EntityManager(World world)
        {
            _world = world;
            _archetypeManager = world.ArchetypeManager;
            _componentManager = world.ComponentManager;
            _entities = new EntityData[world.Config.CACHE_ENTITIES_CAPACITY];
            _freeIds = new IntDispenser();
        }


        public bool IsAlive(in Entity entity)
        {
            if (entity.World != _world || !_world.IsAlive)
            {
                return false;
            }

            return _entities[entity.Id].Age == entity.Age;
        }


        internal ref EntityData GetEntityData(Entity entity)
        {
            if (entity.World != _world) throw new Exception("|KECS| Invalid world.");
            if (!_world.IsAlive) throw new Exception("|KECS| World already destroyed.");
            if (entity.Age != _entities[entity.Id].Age)
                throw new Exception($"|KECS| Entity {entity.ToString()} was destroyed.");
            return ref _entities[entity.Id];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Entity CreateEntity()
        {
            if (!_world.IsAlive)
                throw new Exception($"|KECS| World - {_world.Name} was destroyed. You cannot create entity.");

            int newEntityId = _freeIds.GetFreeInt(out var isNew);

            if (_entities.Length == newEntityId)
            {
                EnsureEntitiesCapacity(newEntityId << 1);
            }

            ref var entityData = ref _entities[newEntityId];
            Entity entity;
            entity.World = _world;
            entity.Id = newEntityId;
            entityData.Archetype = _archetypeManager.EmptyArchetype;

            if (isNew)
            {
                entity.Age = 1;
                entityData.Age = 1;
            }
            else
            {
                entity.Age = entityData.Age;
            }

            _archetypeManager.EmptyArchetype.AddEntity(entity);
            _entitiesCount++;
            _world.EntityCreated(entity);
            return entity;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecycleEntity(in Entity entity)
        {
            ref var entityData = ref _entities[entity.Id];
            entityData.Age++;
            if (entityData.Age == 0)
            {
                entityData.Age = 1;
            }

            _freeIds.ReleaseInt(entity.Id);
            _entitiesCount--;
            _world.EntityDestroyed(entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureEntitiesCapacity(int capacity)
        {
            if (_entities.Length < capacity)
            {
                var newCapacity = EcsMath.Pot(capacity);
                Array.Resize(ref _entities, newCapacity);
                _componentManager.EnsurePoolsCapacity(newCapacity);
            }
        }

        public void Dispose()
        {
            _freeIds.Clear();
            _archetypeManager = null;
            _freeIds = null;
            _entities = null;
            _world = null;
            _entitiesCount = 0;
        }
    }

    public struct EntityData
    {
        public int Age;
        public Archetype Archetype;
    }

    public struct Entity : IEquatable<Entity>
    {
        public int Id;
        public int Age;
        public World World;

        public static readonly Entity Empty = new Entity();

        public override string ToString()
        {
            return $"Entity_ID-{Id}:AGE-{Age}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in Entity lhs, in Entity rhs)
        {
            return lhs.Id == rhs.Id && lhs.Age == rhs.Age;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in Entity lhs, in Entity rhs)
        {
            return lhs.Id != rhs.Id || lhs.Age != rhs.Age;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other)
        {
            return Id == other.Id && Age == other.Age && Equals(World, other.World);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            return obj is Entity other && Equals(other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEmpty()
        {
            return Id == 0 && Age == 0;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id;
                hashCode = (hashCode * 397) ^ Age;
                hashCode = (hashCode * 397) ^ (World != null ? World.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    public static class EntityExtensions
    {
        public static bool IsAlive(in this Entity entity)
        {
            return entity.World.EntityManager.IsAlive(in entity);
        }

        public static ref T Set<T>(in this Entity entity, in T value) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);

            var pool = world.ComponentManager.GetPool<T>();
            pool.Set(entity.Id, value);

            if (!entity.Has<T>())
            {
                GotoNextArchetype(ref entityData, in entity, idx);
            }

            return ref pool.Get(entity.Id);
        }

        public static void Set(in this Entity entity, object value, int typeIdx)
        {
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);

            var pool = world.ComponentManager.GetPool(typeIdx);
            pool.SetObject(entity.Id, value);

            if (!entityData.Archetype.Mask.GetBit(typeIdx))
            {
                GotoNextArchetype(ref entityData, in entity, typeIdx);
            }
        }

        public static void Remove<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);
            var pool = world.ComponentManager.GetPool<T>();

            if (entity.Has<T>())
            {
                GotoPriorArchetype(ref entityData, in entity, idx);
                pool.Remove(entity.Id);
            }

            if (entityData.Archetype.Mask.Count == 0)
            {
                entity.Destroy();
            }
        }

        public static void Remove(in this Entity entity, int typeIdx)
        {
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);

            if (entityData.Archetype.Mask.GetBit(typeIdx))
            {
                GotoPriorArchetype(ref entityData, in entity, typeIdx);
                world.ComponentManager.GetPool(typeIdx).Remove(entity.Id);
            }

            if (entityData.Archetype.Mask.Count == 0)
            {
                entity.Destroy();
            }
        }

        public static ref T Get<T>(in this Entity entity) where T : struct
        {
            var world = entity.World;

            var pool = world.ComponentManager.GetPool<T>();

            if (entity.Has<T>())
            {
                return ref pool.Get(entity.Id);
            }

            return ref pool.Empty;
        }

        public static bool Has<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);
            return entityData.Archetype.Mask.GetBit(idx);
        }

        public static int GetComponentsIndexes(in this Entity entity, ref int[] typeIndexes)
        {
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);
            var mask = entityData.Archetype.Mask;
            var lenght = mask.Count;
            if (typeIndexes == null || typeIndexes.Length < lenght)
            {
                typeIndexes = new int[lenght];
            }

            int counter = 0;
            foreach (var idx in mask)
            {
                typeIndexes[counter++] = idx;
            }

            return lenght;
        }

        public static int GetComponentsValues(in this Entity entity, ref object[] objects)
        {
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);
            var mask = entityData.Archetype.Mask;
            var lenght = mask.Count;
            if (objects == null || objects.Length < lenght)
            {
                objects = new object[lenght];
            }

            int counter = 0;
            foreach (var idx in mask)
            {
                objects[counter++] = world.ComponentManager.GetPool(idx).GetObject(entity.Id);
            }

            return lenght;
        }

        private static void GotoNextArchetype(ref EntityData entityData, in Entity entity, int index)
        {
            var world = entity.World;
            entityData.Archetype.RemoveEntity(entity);
            var newArchetype = world.ArchetypeManager.FindOrCreateNextArchetype(entityData.Archetype, index);
            entityData.Archetype = newArchetype;
            entityData.Archetype.AddEntity(entity);
        }

        private static void GotoPriorArchetype(ref EntityData entityData, in Entity entity, int index)
        {
            var world = entity.World;
            entityData.Archetype.RemoveEntity(entity);
            var newArchetype = world.ArchetypeManager.FindOrCreatePriorArchetype(entityData.Archetype, index);
            entityData.Archetype = newArchetype;
            entityData.Archetype.AddEntity(entity);
        }

        public static void Destroy(in this Entity entity)
        {
            var world = entity.World;
            ref var entityData = ref world.EntityManager.GetEntityData(entity);

            foreach (var comp in entityData.Archetype.Mask)
            {
                world.ComponentManager.GetPool(comp).Remove(entity.Id);
            }

            entityData.Archetype.RemoveEntity(entity);
            world.EntityManager.RecycleEntity(entity);
        }
    }


    //=============================================================================
    // ARCHETYPES
    //=============================================================================


    public sealed class ArchetypeManager : IDisposable
    {
        private World _world;
        private GrowList<Archetype> _archetypes;
        internal Archetype EmptyArchetype => _archetypes[0];
        private object _lockObject = new object();

        public int Count => _archetypes.Count;

        internal ArchetypeManager(World world)
        {
            _world = world;
            _archetypes = new GrowList<Archetype>(world.Config.CACHE_ARCHETYPES_CAPACITY);
            _archetypes.Add(new Archetype(world, 0, new BitMask(world.Config.CACHE_COMPONENTS_CAPACITY)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void FindArchetypes(Filter filter, int startId)
        {
            for (int i = startId, lenght = _archetypes.Count; i < lenght; i++)
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
        private Archetype FindOrCreateArchetype(BitMask mask)
        {
            lock (_lockObject)
            {
                var curArchetype = EmptyArchetype;
                var newMask = new BitMask(_world.Config.CACHE_COMPONENTS_CAPACITY);

                foreach (var index in mask)
                {
                    newMask.SetBit(index);

                    var nextArchetype = curArchetype.Next.GetValue(index);

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
            var priorArchetype = archetype.Prior.GetValue(removeIndex);
            if (priorArchetype != null)
                return priorArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.ClearBit(removeIndex);

            return FindOrCreateArchetype(mask);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreateNextArchetype(Archetype archetype, int addIndex)
        {
            var nextArchetype = archetype.Next.GetValue(addIndex);
            if (nextArchetype != null)
                return nextArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.SetBit(addIndex);

            return FindOrCreateArchetype(mask);
        }


        public void Dispose()
        {
            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes[i].Dispose();
            }

            _world = null;
            _lockObject = null;
            _archetypes.Clear();
            _archetypes = null;
        }
    }


    public sealed class Archetype : IEnumerable<Entity>, IDisposable
    {
        internal SparseSet<Entity> Entities;
        internal SparseSet<Archetype> Next;
        internal SparseSet<Archetype> Prior;

        public Type[] TypesCache;

        private int _lockCount;
        private DelayedChange[] _delayedChanges;
        private int _delayedOpsCount;

        public int Count => Entities.Count;
        public int Id { get; private set; }
        internal BitMask Mask { get; }

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
            _delayedOpsCount = 0;

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
        private bool AddDelayedChange(in Entity entity, bool isAdd)
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
        internal void AddEntity(in Entity entity)
        {
            if (AddDelayedChange(entity, true))
            {
                return;
            }

            Entities.Add(entity.Id, entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveEntity(in Entity entity)
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
        public void Dispose()
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
    // POOLS
    //=============================================================================


    public class ComponentManager : IDisposable
    {
        private SparseSet<IComponentPool> _pools;
        private World _world;
        private int _componentsTypesCount;

        public int Count => _componentsTypesCount;

        public ComponentManager(World world)
        {
            _world = world;
            _pools = new SparseSet<IComponentPool>(world.Config.CACHE_COMPONENTS_CAPACITY,
                world.Config.CACHE_COMPONENTS_CAPACITY);
            _componentsTypesCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EnsurePoolsCapacity(int capacity)
        {
            var newCapacity = EcsMath.Pot(capacity);
            for (int i = 0, lenght = _pools.Count; i < lenght; i++)
            {
                _pools[i].EnsureLength(newCapacity);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentPool<T> GetPool<T>() where T : struct
        {
            if (!_world.IsAlive)
                throw new Exception($"|KECS| World - {_world.Name} was destroyed. You cannot get pool.");
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (!_pools.Contains(idx))
            {
                var pool = new ComponentPool<T>(_world);
                _pools.Add(idx, pool);
                _componentsTypesCount++;
            }

            return (ComponentPool<T>) _pools.GetValue(idx);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IComponentPool GetPool(int idx)
        {
            if (!_world.IsAlive)
                throw new Exception($"|KECS| World - {_world.Name} was destroyed. You cannot get pool.");
            var pool = _pools.GetValue(idx);
            return pool;
        }


        public void Dispose()
        {
            _world = null;
            _componentsTypesCount = 0;
            foreach (var pool in _pools)
            {
                pool?.Dispose();
            }

            _pools.Clear();
            _pools = null;
        }
    }


    public static class EcsTypeManager
    {
        internal static int ComponentTypesCount = 0;
        public static Type[] ComponentsTypes = new Type[WorldConfig.DEFAULT_CACHE_COMPONENTS_CAPACITY];

        public static int GetIdx(Type type)
        {
            return Array.IndexOf(ComponentsTypes, type);
        }
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
        void SetObject(int entityId, object value);
    }


    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private SparseSet<T> _components;
        private int Length => _components.Count;
        private World _owner;
        internal ref T Empty => ref _components.Empty;


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

        public void SetObject(int entityId, object value)
        {
            if (value is T component)
            {
                _components.Set(entityId, component);
            }
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
    // FILTER
    //=============================================================================
    

    public delegate void ForEachArchetypeHandler(Archetype archetype);
    
    public delegate void ForEachHandler(Entity entity);

    public delegate void ForEachHandler<T>(Entity entity, ref T comp0)
        where T : struct;

    public delegate void ForEachHandler<T, Y>(Entity entity, ref T comp0, ref Y comp1)
        where T : struct
        where Y : struct;

    public delegate void ForEachHandler<T, Y, U>(Entity entity, ref T comp0, ref Y comp1, ref U comp2)
        where T : struct
        where Y : struct
        where U : struct;

    public delegate void ForEachHandler<T, Y, U, I>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct;


    public sealed class Filter
    {
        internal BitMask Include;
        internal BitMask Exclude;
        internal int Version { get; set; }

        private GrowList<Archetype> _archetypes;
        private World _world;
        private ArchetypeManager _archetypeManager;
        private EntityManager _entityManager;
        private ComponentManager _componentManager;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Filter(World world)
        {
            Version = 0;
            _world = world;
            Include = new BitMask(world.Config.CACHE_COMPONENTS_CAPACITY);
            Exclude = new BitMask(world.Config.CACHE_COMPONENTS_CAPACITY);
            _archetypes = new GrowList<Archetype>(world.Config.CACHE_ARCHETYPES_CAPACITY);

            _archetypeManager = world.ArchetypeManager;
            _entityManager = world.EntityManager;
            _componentManager = world.ComponentManager;
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
        private void ForEach(ForEachArchetypeHandler handler)
        {
            _archetypeManager.FindArchetypes(this, Version);
            
            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes[i].Lock();
            }

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                if (_archetypes[i].Count > 0)
                {
                    handler(_archetypes[i]);
                }
            }

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes[i].Unlock();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach(ForEachHandler handler)
        {
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    handler(archetype.Entities[i]);
                }
            });
        }


        public void ForEach<T>(ForEachHandler<T> handler)
            where T : struct
        {
            var poolT = _componentManager.GetPool<T>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id));
                }
            });
        }

        public void ForEach<T, Y>(ForEachHandler<T, Y> handler)
            where T : struct
            where Y : struct
        {
            var poolT = _componentManager.GetPool<T>();
            var poolY = _componentManager.GetPool<Y>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id));
                }
            });
        }

        public void ForEach<T, Y, U>(ForEachHandler<T, Y, U> handler)
            where T : struct
            where Y : struct
            where U : struct
        {
            var poolT = _componentManager.GetPool<T>();
            var poolY = _componentManager.GetPool<Y>();
            var poolU = _componentManager.GetPool<U>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id), ref poolU.Get(entity.Id));
                }
            });
        }


        public void ForEach<T, Y, U, I>(ForEachHandler<T, Y, U, I> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
        {
            var poolT = _componentManager.GetPool<T>();
            var poolY = _componentManager.GetPool<Y>();
            var poolU = _componentManager.GetPool<U>();
            var poolI = _componentManager.GetPool<I>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id), ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id));
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            Version = 0;
            _archetypes.Clear();
            _archetypes = null;
            _world = null;
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
        protected World world;
        protected Systems systems;
        public abstract void Initialize();


        internal void StartUp(World world, Systems systems)
        {
            this.world = world;
            this.systems = systems;
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

        private SharedData _sharedData;

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
            _sharedData = new SharedData();
            _systems = new Dictionary<int, SystemData>();
            _allSystems = new List<SystemData>();
            _updateSystems = new List<SystemData>();
            _fixedSystems = new List<SystemData>();
            _lateSystems = new List<SystemData>();
            _onlyBaseSystems = new List<SystemData>();
        }

#if DEBUG
        private readonly List<ISystemsDebugListener> _debugListeners = new List<ISystemsDebugListener>();


        public void AddDebugListener(ISystemsDebugListener listener)
        {
            if (listener == null)
            {
                throw new Exception("|KECS| Listener is null.");
            }

            _debugListeners.Add(listener);
        }


        public void RemoveDebugListener(ISystemsDebugListener listener)
        {
            if (listener == null)
            {
                throw new Exception("|KECS| Listener is null.");
            }

            _debugListeners.Remove(listener);
        }
#endif
        /// <summary>
        /// Add shared data for systems.
        /// </summary>
        /// <param name="data">Data.</param>
        /// <typeparam name="T">Type of shared data.</typeparam>
        /// <returns></returns>
        public T AddShared<T>(T data) where T : class
        {
            if (_initialized)
            {
                throw new Exception($"|KECS| Systems was initialized. You cannot add shared data.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot add shared data.");
            }

            return _sharedData.Add(data);
        }


        /// <summary>
        /// Get shared data of systems.
        /// </summary>
        /// <typeparam name="T">Type of shared data.</typeparam>
        /// <returns></returns>
        public T GetShared<T>() where T : class
        {
            if (!_initialized)
            {
                throw new Exception($"|KECS| Systems haven't initialized yet. You cannot get shared data.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot get shared data.");
            }

            return _sharedData.Get<T>();
        }

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
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
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
            _sharedData.Dispose();
            _sharedData = null;
        }
    }

    internal class RemoveOneFrame<T> : SystemBase, ILateUpdate where T : struct
    {
        private Filter _filter;


        public override void Initialize()
        {
            _filter = world.Filter.With<T>();
        }


        public void OnUpdate(float deltaTime)
        {
            _filter.ForEach(entity =>
            {
                entity.Remove<T>();
            });
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
            Sparse.Fill(None);
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
            if (Contains(sparseIdx))
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
            if (!Contains(sparseIdx))
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
                _sparseSet = sparseSet;
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
        public ref T Empty => ref _empty;


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
            if (Contains(sparseIdx))
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
            if (!Contains(sparseIdx))
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
            if (!Contains(sparseIdx))
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


    internal class GrowList<T>
    {
        private T[] _data;
        internal int Count => _lenght;
        private int _lenght;

        internal ref T this[int index]
        {
            get
            {
                ArrayExtension.EnsureLength(ref _data, index);
                return ref _data[index];
            }
        }

        internal GrowList(int capacity)
        {
            _data = new T[capacity];
            _lenght = 0;
        }

        internal void Add(in T value)
        {
            int index = _lenght++;
            ArrayExtension.EnsureLength(ref _data, index);
            _data[index] = value;
        }

        internal void Clear()
        {
            Array.Clear(_data, 0, _data.Length);
        }
    }


    internal class IntDispenser : IDisposable
    {
        private ConcurrentStack<int> _freeInts;
        private int _lastInt;
        public int LastInt => _lastInt;
        private readonly int _startInt;
        public int Count => _freeInts.Count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntDispenser()
        {
            _freeInts = new ConcurrentStack<int>();
            _startInt = -1;
            _lastInt = -1;
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
        public int GetFreeInt(out bool isNew)
        {
            isNew = false;
            if (!_freeInts.TryPop(out int freeInt))
            {
                freeInt = Interlocked.Increment(ref _lastInt);
                isNew = true;
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
        public static void RemoveAtFast<T>(this List<T> list, int index)
        {
            var count = list.Count;
            list[index] = list[count - 1];
            list.RemoveAt(count - 1);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveFast<T>(this List<T> list, T item)
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

            newLength <<= 1;

            while (index >= newLength)
            {
                newLength <<= 1;
            }

            Array.Resize(ref array, newLength);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fill<T>(this T[] array, in T value, int start = 0)
        {
            for (int i = start, lenght = array.Length; i < lenght; ++i)
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
    }


    //=============================================================================
    // BIT MASK
    //=============================================================================


    public struct BitMask
    {
        private const int CHUNK_CAPACITY = sizeof(ulong) * 8;
        private readonly ulong[] _chunks;
        private readonly int _capacity;


        public int Count { get; private set; }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask(int capacity = 0)
        {
            this._capacity = capacity;
            var newSize = capacity / CHUNK_CAPACITY;
            if (capacity % CHUNK_CAPACITY != 0)
            {
                newSize++;
            }

            Count = 0;
            _chunks = new ulong[newSize];
        }


        public BitMask(in BitMask copy)
        {
            this._capacity = copy._capacity;
            var newSize = _capacity / CHUNK_CAPACITY;
            if (_capacity % CHUNK_CAPACITY != 0)
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
            var chunk = index / CHUNK_CAPACITY;
            var oldValue = _chunks[chunk];
            var newValue = oldValue | (1UL << (index % CHUNK_CAPACITY));
            if (oldValue == newValue) return;
            _chunks[chunk] = newValue;
            Count++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
        {
            var chunk = index / CHUNK_CAPACITY;
            var oldValue = _chunks[chunk];
            var newValue = oldValue & ~(1UL << (index % CHUNK_CAPACITY));
            if (oldValue == newValue) return;
            _chunks[chunk] = newValue;
            Count--;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetBit(int index)
        {
            return (_chunks[index / CHUNK_CAPACITY] & (1UL << (index % CHUNK_CAPACITY))) != 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(BitMask bitMask)
        {
            for (int i = 0, lenght = _chunks.Length; i < lenght; i++)
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
            for (int i = 0, lenght = _chunks.Length; i < lenght; i++)
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
            for (int i = 0, lenght = _chunks.Length; i < lenght; i++)
            {
                _chunks[i] = 0;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Merge(BitMask include)
        {
            for (int i = 0, lenght = _chunks.Length; i < lenght; i++)
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
                        if (!_bitMask.GetBit(_index))
                        {
                            continue;
                        }

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