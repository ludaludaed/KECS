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


    public struct WorldInfo
    {
        public int ActiveEntities;
        public int ReservedEntities;
        public int Archetypes;
        public int Components;
    }


    public struct WorldConfig
    {
        public int Entities;
        public int Archetypes;
        public int ComponentsTypes;
        public const int DefaultEntities = 256;
        public const int DefaultArchetypes = 256;
        public const int DefaultComponentsTypes = 256;
    }


    public static class Worlds
    {
        private const string DEFAULT_WORLD_NAME = "DEFAULT";
        private static object _lockObject;
        private static IntDispenser _freeWorldsIds;
        private static World[] _worlds;
        private static Dictionary<int, int> _worldsIdx;


        public static World Default
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_lockObject)
                {
                    var hashName = DEFAULT_WORLD_NAME.GetHashCode();
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Create(string name = DEFAULT_WORLD_NAME, WorldConfig config = default)
        {
            lock (_lockObject)
            {
                var hashName = name.GetHashCode();
                if (_worldsIdx.ContainsKey(hashName))
                {
                    throw new Exception($"|KECS| A world with {name} name already exists.");
                }

                var worldId = _freeWorldsIds.GetFreeInt();
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
                Archetypes = config.Archetypes > 0
                    ? config.Archetypes
                    : WorldConfig.DefaultArchetypes,
                Entities = config.Entities > 0
                    ? config.Entities
                    : WorldConfig.DefaultEntities,
                ComponentsTypes = config.ComponentsTypes > 0
                    ? config.ComponentsTypes
                    : WorldConfig.DefaultComponentsTypes
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(string name)
        {
            var hashName = name.GetHashCode();
            lock (_lockObject)
            {
                if (_worldsIdx.TryGetValue(hashName, out int worldId))
                {
                    return _worlds[worldId];
                }

                throw new Exception($"|KECS| No world with {name} name was found.");
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(int worldId)
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World GetOrCreate(string name, WorldConfig config = default)
        {
            var hashName = name.GetHashCode();
            lock (_lockObject)
            {
                if (_worldsIdx.TryGetValue(hashName, out int worldId))
                {
                    return _worlds[worldId];
                }

                return Create(name, config);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Destroy(string name)
        {
            var hashName = name.GetHashCode();
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
            var hash = typeof(T).GetHashCode();
            if (!_data.ContainsKey(hash))
            {
                _data.Add(hash, data);
                return data;
            }

            throw new Exception($"|KECS| You have already added this type{typeof(T).Name} of data");
        }


        internal T Get<T>() where T : class
        {
            var hash = typeof(T).GetHashCode();

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
    // TASK POOLS
    //=============================================================================

    internal interface ITaskPool
    {
        int Count { get; }
        void Execute();
        void Clear();
    }

    internal class TaskPool<T> : ITaskPool where T : struct
    {
        private TaskItem[] _tasks;
        private int _tasksCount;
        private int _removeTasksCount;
        public int Count => _tasksCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TaskPool(World world)
        {
            _tasks = new TaskItem[world.Config.Entities];
            _tasksCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Add(Entity entity, in T component)
        {
            ArrayExtension.EnsureLength(ref _tasks, _tasksCount);
            ref var task = ref _tasks[_tasksCount];
            task.Entity = entity;
            task.Item = component;
            _tasksCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute()
        {
            for (int i = 0, lenght = _tasksCount; i < lenght; i++)
            {
                ref var task = ref _tasks[i];
                if (task.Entity.IsAlive())
                {
                    task.Entity.Set(task.Item);
                }
            }

            _removeTasksCount = _tasksCount;
            _tasksCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (int i = 0, lenght = _removeTasksCount; i < lenght; i++)
            {
                ref var task = ref _tasks[i];
                if (task.Entity.IsAlive())
                {
                    task.Entity.Remove<T>();
                }
            }

            _removeTasksCount = 0;
        }

        private struct TaskItem
        {
            public T Item;
            public Entity Entity;
        }
    }


    //=============================================================================
    // WORLD
    //=============================================================================


    public sealed class World
    {
        private HandleMap<IComponentPool> _componentPools;
        private HandleMap<ITaskPool> _taskPools;
        private int _componentsTypesCount;

        private IntDispenser _freeEntityIds;
        private EntityData[] _entities;
        private int _entitiesCount;

        private List<Filter> _filters;

        private int _worldId;
        private string _name;
        private bool _isAlive;
        public readonly WorldConfig Config;

        public string Name => _name;
        public int WorldId => _worldId;
        public bool IsAlive => _isAlive;

        internal ArchetypeManager ArchetypeManager;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal World(int worldId, WorldConfig config, string name)
        {
            _name = name;
            _isAlive = true;
            _worldId = worldId;
            Config = config;

            _componentPools = new HandleMap<IComponentPool>(config.ComponentsTypes, config.ComponentsTypes);
            _componentsTypesCount = 0;

            _taskPools = new HandleMap<ITaskPool>(config.ComponentsTypes, config.ComponentsTypes);

            _entities = new EntityData[config.Entities];
            _freeEntityIds = new IntDispenser();
            _entitiesCount = 0;

            _filters = new List<Filter>();
            ArchetypeManager = new ArchetypeManager(this);
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Filter Filter()
        {
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_worldId} was destroyed. You cannot create filter.");
            var filter = new Filter(this);
            _filters.Add(filter);
            return filter;
        }


        public WorldInfo GetInfo()
        {
            return new WorldInfo()
            {
                ActiveEntities = _entitiesCount,
                ReservedEntities = _freeEntityIds.Count,
                Archetypes = ArchetypeManager.Count,
                Components = _componentsTypesCount
            };
        }

        internal bool EntityIsAlive(in Entity entity)
        {
            if (entity.World != this || !_isAlive)
            {
                return false;
            }

            return _entities[entity.Id].Age == entity.Age;
        }


        internal ref EntityData GetEntityData(Entity entity)
        {
            if (entity.World != this) throw new Exception("|KECS| Invalid world.");
            if (!_isAlive) throw new Exception("|KECS| World already destroyed.");
            if (entity.Age != _entities[entity.Id].Age)
                throw new Exception($"|KECS| Entity {entity.ToString()} was destroyed.");
            return ref _entities[entity.Id];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity CreateEntity()
        {
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot create entity.");

            Entity entity;
            entity.World = this;

            var isNew = _freeEntityIds.TryGetNewInt(out var newEntityId);

            if (_entities.Length == newEntityId)
            {
                Array.Resize(ref _entities, EcsMath.Pot(newEntityId << 1));
            }

            ref var entityData = ref _entities[newEntityId];
            entity.Id = newEntityId;
            entityData.Archetype = ArchetypeManager.EmptyArchetype;

            if (isNew)
            {
                entity.Age = 1;
                entityData.Age = 1;
            }
            else
            {
                entity.Age = entityData.Age;
            }

            ArchetypeManager.EmptyArchetype.AddEntity(entity);
            _entitiesCount++;
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners[i].OnEntityCreated(entity);
            }
#endif
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

            _freeEntityIds.ReleaseInt(entity.Id);
            _entitiesCount--;
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners[i].OnEntityDestroyed(entity);
            }
#endif
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentPool<T> GetPool<T>() where T : struct
        {
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot get pool.");
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (!_componentPools.Contains(idx))
            {
                var pool = new ComponentPool<T>(this);
                _componentPools.Set(idx, pool);
                _componentsTypesCount++;
            }

            return (ComponentPool<T>) _componentPools.Get(idx);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IComponentPool GetPool(int idx)
        {
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot get pool.");
            var pool = _componentPools.Get(idx);
            return pool;
        }
        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TaskPool<T> GetTaskPool<T>() where T : struct
        {
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot get pool.");
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (!_taskPools.Contains(idx))
            {
                var pool = new TaskPool<T>(this);
                _taskPools.Set(idx, pool);
            }

            return (TaskPool<T>) _taskPools.Get(idx);
        }
        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteTasks()
        {
            for (int i = 0, lenght = _taskPools.Count; i < lenght; i++)
            {
                _taskPools[i].Execute();
            }
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearTasks()
        {
            for (int i = 0, lenght = _taskPools.Count; i < lenght; i++)
            {
                _taskPools[i].Clear();
            }
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_worldId} already destroy");

            _componentsTypesCount = 0;
            for (int i = 0, lenght = _componentPools.Count; i < lenght; i++)
            {
                _componentPools[i]?.Dispose();
            }

            _componentPools.Clear();
            _componentPools = null;

            for (int i = 0, lenght = _filters.Count; i < lenght; i++)
            {
                _filters[i]?.Dispose();
            }

            _filters.Clear();
            _filters = null;

            _freeEntityIds.Clear();
            _freeEntityIds = null;
            _entities = null;
            _entitiesCount = 0;

            _worldId = -1;
            _name = null;
            _isAlive = false;

            ArchetypeManager.Dispose();
            ArchetypeManager = null;
        }

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
            return $"World - {_worldId} <{_name}>";
        }
    }


    //=============================================================================
    // ENTITY
    //=============================================================================

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

#if DEBUG
        public override string ToString()
        {
            if (!this.IsAlive()) return "Destroyed Entity";
            return $"Entity {Id} {Age}";
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in Entity lhs, in Entity rhs)
        {
            return lhs.Equals(rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in Entity lhs, in Entity rhs)
        {
            return !lhs.Equals(rhs);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAlive(in this Entity entity)
        {
            return entity.World.EntityIsAlive(in entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmpty(in this Entity entity)
        {
            return entity.Id == 0 && entity.Age == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Set<T>(in this Entity entity, in T value) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);

            var pool = world.GetPool<T>();
            pool.Set(entity.Id, value);

            if (!entity.Has<T>())
            {
                GotoNextArchetype(ref entityData, in entity, idx);
            }

            return ref pool.Get(entity.Id);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Event<T>(in this Entity entity, in T value = default) where T : struct
        {
            if(!entity.IsAlive()) return;
            entity.World.GetTaskPool<T>().Add(entity, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remove<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var pool = world.GetPool<T>();

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Get<T>(in this Entity entity) where T : struct
        {
            var pool = entity.World.GetPool<T>();

            if (entity.Has<T>())
            {
                return ref pool.Get(entity.Id);
            }

            return ref pool.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Has<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            return entityData.Archetype.Mask.GetBit(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GotoNextArchetype(ref EntityData entityData, in Entity entity, int index)
        {
            var world = entity.World;
            entityData.Archetype.RemoveEntity(entity);
            var newArchetype = world.ArchetypeManager.FindOrCreateNextArchetype(entityData.Archetype, index);
            entityData.Archetype = newArchetype;
            entityData.Archetype.AddEntity(entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GotoPriorArchetype(ref EntityData entityData, in Entity entity, int index)
        {
            var world = entity.World;
            entityData.Archetype.RemoveEntity(entity);
            var newArchetype = world.ArchetypeManager.FindOrCreatePriorArchetype(entityData.Archetype, index);
            entityData.Archetype = newArchetype;
            entityData.Archetype.AddEntity(entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Destroy(in this Entity entity)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);

            foreach (var comp in entityData.Archetype.Mask)
            {
                world.GetPool(comp).Remove(entity.Id);
            }

            entityData.Archetype.RemoveEntity(entity);
            world.RecycleEntity(entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remove(in this Entity entity, int typeIdx)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);

            if (entityData.Archetype.Mask.GetBit(typeIdx))
            {
                GotoPriorArchetype(ref entityData, in entity, typeIdx);
                world.GetPool(typeIdx).Remove(entity.Id);
            }

            if (entityData.Archetype.Mask.Count == 0)
            {
                entity.Destroy();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set(in this Entity entity, object value, int typeIdx)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);

            var pool = world.GetPool(typeIdx);
            pool.SetObject(entity.Id, value);

            if (!entityData.Archetype.Mask.GetBit(typeIdx))
            {
                GotoNextArchetype(ref entityData, in entity, typeIdx);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetComponentsIndexes(in this Entity entity, ref int[] typeIndexes)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetComponents(in this Entity entity, ref object[] objects)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var mask = entityData.Archetype.Mask;
            var lenght = mask.Count;
            if (objects == null || objects.Length < lenght)
            {
                objects = new object[lenght];
            }

            int counter = 0;
            foreach (var idx in mask)
            {
                objects[counter++] = world.GetPool(idx).GetObject(entity.Id);
            }

            return lenght;
        }
    }


    //=============================================================================
    // ARCHETYPES
    //=============================================================================


    public sealed class ArchetypeManager : IDisposable
    {
        private GrowList<Archetype> _archetypes;
        internal Archetype EmptyArchetype => _archetypes[0];
        private World _world;

        public int Count => _archetypes.Count;

        internal ArchetypeManager(World world)
        {
            _world = world;
            _archetypes = new GrowList<Archetype>(world.Config.Archetypes);
            _archetypes.Add(new Archetype(world, new BitMask(world.Config.ComponentsTypes)));
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
            var curArchetype = EmptyArchetype;
            var newMask = new BitMask(_world.Config.ComponentsTypes);

            foreach (var index in mask)
            {
                newMask.SetBit(index);

                var nextArchetype = curArchetype.Next.Get(index);

                if (nextArchetype == null)
                {
                    nextArchetype = new Archetype(_world, newMask);

                    nextArchetype.Prior.Set(index, curArchetype);
                    curArchetype.Next.Set(index, nextArchetype);

                    _archetypes.Add(nextArchetype);
                }

                curArchetype = nextArchetype;
            }

            return curArchetype;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreatePriorArchetype(Archetype archetype, int removeIndex)
        {
            var priorArchetype = archetype.Prior.Get(removeIndex);
            if (priorArchetype != null)
                return priorArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.ClearBit(removeIndex);

            return FindOrCreateArchetype(mask);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreateNextArchetype(Archetype archetype, int addIndex)
        {
            var nextArchetype = archetype.Next.Get(addIndex);
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
            _archetypes.Clear();
            _archetypes = null;
        }
    }


    public sealed class Archetype : IEnumerable<Entity>, IDisposable
    {
        internal HandleMap<Entity> Entities;
        internal HandleMap<Archetype> Next;
        internal HandleMap<Archetype> Prior;

        public Type[] TypesCache;

        private int _lockCount;
        private DelayedChange[] _delayedChanges;
        private int _delayedOpsCount;

        public int Count => Entities.Count;
        internal BitMask Mask { get; }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype(World world, BitMask mask)
        {
            Mask = mask;
            _lockCount = 0;

            _delayedChanges = new DelayedChange[64];
            _delayedOpsCount = 0;

            Next = new HandleMap<Archetype>(world.Config.ComponentsTypes,
                world.Config.ComponentsTypes);
            Prior = new HandleMap<Archetype>(world.Config.ComponentsTypes,
                world.Config.ComponentsTypes);

            Entities = new HandleMap<Entity>(world.Config.Entities,
                world.Config.Entities);

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
            if (_lockCount != 0 || _delayedOpsCount <= 0) return;
            for (int i = 0; i < _delayedOpsCount; i++)
            {
                ref var operation = ref _delayedChanges[i];
                if (operation.IsAdd)
                {
                    AddEntity(operation.Entity);
                }
                else
                {
                    RemoveEntity(operation.Entity);
                }
            }

            _delayedOpsCount = 0;
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

            Entities.Set(entity.Id, entity);
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
        }
    }


    //=============================================================================
    // POOLS
    //=============================================================================

    public static class EcsTypeManager
    {
        internal static int ComponentTypesCount = 0;
        public static Type[] ComponentsTypes = new Type[WorldConfig.DefaultComponentsTypes];

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
        object GetObject(int entityId);
        void SetObject(int entityId, object value);
    }


    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private HandleMap<T> _components;
        private int Length => _components.Count;
        private World _owner;
        internal ref T Empty => ref _components.Empty;


        public ComponentPool(World world)
        {
            _owner = world;
            _components = new HandleMap<T>(world.Config.Entities,
                world.Config.Entities);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityId)
        {
            return ref _components.Get(entityId);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetObject(int entityId)
        {
            return _components.Get(entityId);
        }

        public void SetObject(int entityId, object value)
        {
            if (value is T component)
            {
                _components.Set(entityId, component);
            }
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

    public delegate void ForEachHandler<T, Y, U, I, O>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3, ref O comp4)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct
        where O : struct;

    public delegate void ForEachHandler<T, Y, U, I, O, P>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3, ref O comp4, ref P comp5)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct
        where O : struct
        where P : struct;


    public sealed class Filter
    {
        internal BitMask Include;
        internal BitMask Exclude;
        internal int Version { get; set; }

        private GrowList<Archetype> _archetypes;
        private ArchetypeManager _archetypeManager;
        private World _world;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Filter(World world)
        {
            Version = 0;
            _world = world;
            Include = new BitMask(world.Config.ComponentsTypes);
            Exclude = new BitMask(world.Config.ComponentsTypes);
            _archetypes = new GrowList<Archetype>(world.Config.Archetypes);

            _archetypeManager = world.ArchetypeManager;
        }

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
            var poolT = _world.GetPool<T>();
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
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
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
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
            var poolU = _world.GetPool<U>();
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
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
            var poolU = _world.GetPool<U>();
            var poolI = _world.GetPool<I>();
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

        public void ForEach<T, Y, U, I, O>(ForEachHandler<T, Y, U, I, O> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
        {
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
            var poolU = _world.GetPool<U>();
            var poolI = _world.GetPool<I>();
            var poolO = _world.GetPool<O>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id), ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id), ref poolO.Get(entity.Id));
                }
            });
        }

        public void ForEach<T, Y, U, I, O, P>(ForEachHandler<T, Y, U, I, O, P> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
        {
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
            var poolU = _world.GetPool<U>();
            var poolI = _world.GetPool<I>();
            var poolO = _world.GetPool<O>();
            var poolP = _world.GetPool<P>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id), ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id), ref poolO.Get(entity.Id), ref poolP.Get(entity.Id));
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            Version = 0;
            _archetypeManager = null;
            _archetypes.Clear();
            _archetypes = null;
            _world = null;
        }
    }


    //=============================================================================
    // SYSTEMS
    //=============================================================================


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

    public abstract class SystemBase : IDisposable
    {
        protected World _world;
        protected Systems _systems;
        public abstract void Initialize();


        internal void StartUp(World world, Systems systems)
        {
            _world = world;
            _systems = systems;
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

        public Systems AddShared<T>(T data) where T : class
        {
            if (_initialized)
            {
                throw new Exception($"|KECS| Systems was initialized. You cannot add shared data.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot add shared data.");
            }

            _sharedData.Add(data);
            return this;
        }


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
                    else
                    {
                        if (systemValue is ILateUpdate lateSystem)
                        {
                            collection = _lateSystems;
                            impl = lateSystem;
                        }
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


    //=============================================================================
    // HANDLE MAP
    //=============================================================================

    public sealed class HandleMap<T> : IEnumerable<T>
    {
        private const int None = -1;
        private T[] _instances;
        private int[] _dense;
        private int[] _sparse;
        private int _denseCount;

        private T _empty;
        public int Count => _denseCount;
        public ref T Empty => ref _empty;

        public HandleMap(int sparseCapacity, int denseCapacity)
        {
            _dense = new int[denseCapacity];
            _sparse = new int[sparseCapacity];
            _instances = new T[denseCapacity];
            _sparse.Fill(None);
            _denseCount = 0;
            _empty = default(T);
        }

        public bool Contains(int sparseIdx) =>
            _denseCount > 0 && sparseIdx < _sparse.Length && _sparse[sparseIdx] != None;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < _denseCount)
                {
                    return ref _instances[index];
                }

                throw new Exception($"|KECS| Out of range HandleMap {index}.");
            }
        }

        public ref T Get(int sparseIdx)
        {
            if (Contains(sparseIdx))
            {
                return ref _instances[_sparse[sparseIdx]];
            }

            return ref Empty;
        }

        public void Set(int sparseIdx, T value)
        {
            if (!Contains(sparseIdx))
            {
                ArrayExtension.EnsureLength(ref _sparse, sparseIdx, None);
                ArrayExtension.EnsureLength(ref _dense, _denseCount);
                ArrayExtension.EnsureLength(ref _instances, _denseCount);

                _sparse[sparseIdx] = _denseCount;

                _dense[_denseCount] = sparseIdx;
                _instances[_denseCount] = value;

                _denseCount++;
            }
            else
            {
                _instances[_sparse[sparseIdx]] = value;
            }
        }

        public void Remove(int sparseIdx)
        {
            if (!Contains(sparseIdx))
            {
                throw new Exception($"|KECS| Unable to remove sparse idx {sparseIdx}: not present.");
            }

            var packedIdx = _sparse[sparseIdx];
            _sparse[sparseIdx] = None;
            _denseCount--;

            if (packedIdx >= _denseCount) return;

            var lastSparseIdx = _dense[_denseCount];
            var lastValueIdx = _instances[_denseCount];

            _dense[packedIdx] = lastSparseIdx;
            _instances[packedIdx] = lastValueIdx;
            _sparse[lastSparseIdx] = packedIdx;
        }

        public void Clear()
        {
            _denseCount = 0;
            Array.Clear(_instances, 0, _instances.Length);
            Array.Clear(_dense, 0, _dense.Length);
            _sparse.Fill(None);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private struct Enumerator : IEnumerator<T>
        {
            private int _count;
            private int _index;
            private HandleMap<T> _handleMap;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(HandleMap<T> handleMap)
            {
                this._handleMap = handleMap;
                _count = handleMap.Count;
                _index = 0;
                Current = default;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _count = 0;
                _index = 0;
                _handleMap = null;
                Current = default;
            }

            object IEnumerator.Current => Current;
            public T Current { get; private set; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_index < _count)
                {
                    Current = _handleMap[_index++];
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
        public bool TryGetNewInt(out int freeInt)
        {
            if (_freeInts.TryPop(out freeInt)) return false;
            freeInt = Interlocked.Increment(ref _lastInt);
            return true;
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
        public static int Pot(int input)
        {
            if (input < 2)
            {
                return 2;
            }

            var n = input - 1;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
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
}