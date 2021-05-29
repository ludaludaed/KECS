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
        public int Components;
        public int Filters;
        public const int DefaultEntities = 256;
        public const int DefaultArchetypes = 256;
        public const int DefaultComponents = 256;
        public const int DefaultFilters = 32;
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class Worlds
    {
        private const string DefaultWorldName = "DEFAULT";
        private static readonly object _lockObject;
        private static readonly IntDispenser _freeWorldsIds;
        private static World[] _worlds;
        private static readonly Dictionary<int, int> _worldsIdx;


        public static World Default
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_lockObject)
                {
                    var hashName = DefaultWorldName.GetHashCode();
                    if (_worldsIdx.TryGetValue(hashName, out var worldId))
                    {
                        return _worlds[worldId];
                    }

                    return Create();
                }
            }
        }


        static Worlds()
        {
            _lockObject = new object();
            _worlds = new World[2];
            _freeWorldsIds = new IntDispenser();
            _worldsIdx = new Dictionary<int, int>(32);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Create(string name = DefaultWorldName, WorldConfig config = default)
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
                Archetypes = config.Archetypes > 0 ? config.Archetypes : WorldConfig.DefaultArchetypes,
                Entities = config.Entities > 0 ? config.Entities : WorldConfig.DefaultEntities,
                Components = config.Components > 0 ? config.Components : WorldConfig.DefaultComponents,
                Filters = config.Filters > 0 ? config.Filters : WorldConfig.DefaultFilters
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
        internal static World Get(int worldId)
        {
            lock (_lockObject)
            {
                return worldId < _worlds.Length ? _worlds[worldId] : null;
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
                    _worlds[worldId].InternalDestroy();
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
                    item?.InternalDestroy();
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

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal class SharedData
    {
        private readonly Dictionary<int, object> _data;

        internal SharedData()
        {
            _data = new Dictionary<int, object>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T Add<T>(T data) where T : class
        {
            var hash = typeof(T).GetHashCode();
            if (_data.ContainsKey(hash))
                throw new Exception($"|KECS| You have already added this type{typeof(T).Name} of data");
            _data.Add(hash, data);
            return data;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T Get<T>() where T : class
        {
            var hash = typeof(T).GetHashCode();
            if (_data.TryGetValue(hash, out var data)) return data as T;
            throw new Exception($"|KECS| No data of this type {typeof(T).Name} was found");
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose() => _data.Clear();
    }


    //=============================================================================
    // TASK POOLS
    //=============================================================================

    internal interface ITaskPool
    {
        void Execute();
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal class TaskPool<T> : ITaskPool where T : struct
    {
        private TaskItem[] _addTasks;
        private TaskItem[] _removeTasks;
        private int _addTasksCount;
        private int _removeTasksCount;


        internal TaskPool(World world)
        {
            _addTasks = new TaskItem[world.Config.Entities];
            _removeTasks = new TaskItem[world.Config.Entities];
            _addTasksCount = 0;
            _removeTasksCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Add(Entity entity, in T component)
        {
            ArrayExtension.EnsureLength(ref _addTasks, _addTasksCount);
            ArrayExtension.EnsureLength(ref _removeTasks, _addTasksCount);

            ref var task = ref _addTasks[_addTasksCount++];
            task.Entity = entity;
            task.Item = component;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute()
        {
            for (int i = 0, lenght = _removeTasksCount; i < lenght; i++)
            {
                _removeTasksCount = 0;
                ref var removeTask = ref _removeTasks[i];
                if (!removeTask.Entity.IsAlive()) continue;
                removeTask.Entity.Remove<T>();
            }

            for (int i = 0, lenght = _addTasksCount; i < lenght; i++)
            {
                _addTasksCount = 0;
                ref var task = ref _addTasks[i];
                if (!task.Entity.IsAlive()) continue;
                task.Entity.Set(task.Item);

                ref var removeTask = ref _removeTasks[_removeTasksCount++];
                removeTask.Entity = task.Entity;
                removeTask.Item = task.Item;
            }
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

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class World
    {
        private readonly HandleMap<IComponentPool> _componentPools;
        private readonly HandleMap<ITaskPool> _taskPools;
        private readonly GrowList<Archetype> _archetypes;
        private readonly GrowList<Filter> _filters;

        private readonly IntDispenser _freeEntityIds;
        private EntityData[] _entities;
        private int _entitiesCount;

        private int _worldId;
        private readonly string _name;
        private bool _isAlive;
        internal readonly WorldConfig Config;

        public string Name => _name;
        public bool IsAlive => _isAlive;

        internal World(int worldId, WorldConfig config, string name)
        {
            _name = name;
            _isAlive = true;
            _worldId = worldId;
            Config = config;

            _componentPools = new HandleMap<IComponentPool>(config.Components, config.Components);
            _taskPools = new HandleMap<ITaskPool>(config.Components, config.Components);

            _archetypes = new GrowList<Archetype>(Config.Archetypes);
            _archetypes.Add(new Archetype(this, new BitMask(Config.Components)));

            _entities = new EntityData[config.Entities];
            _freeEntityIds = new IntDispenser();
            _entitiesCount = 0;

            _filters = new GrowList<Filter>(Config.Filters);
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
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot create filter.");
#endif
            var filter = new Filter(this);
            _filters.Add(filter);
            return filter;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WorldInfo GetInfo()
        {
            return new WorldInfo()
            {
                ActiveEntities = _entitiesCount,
                ReservedEntities = _freeEntityIds.Count,
                Archetypes = _archetypes.Count,
                Components = _componentPools.Count
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool EntityIsAlive(in Entity entity)
        {
            if (entity.World != this || !_isAlive) return false;
            return _entities[entity.Id].Age == entity.Age;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref EntityData GetEntityData(Entity entity)
        {
#if DEBUG
            if (entity.World != this) throw new Exception("|KECS| Invalid world.");
            if (!_isAlive) throw new Exception("|KECS| World already destroyed.");
            if (entity.Age != _entities[entity.Id].Age)
                throw new Exception($"|KECS| Entity {entity.ToString()} was destroyed.");
#endif
            return ref _entities[entity.Id];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity CreateEntity()
        {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot create entity.");
#endif
            ref var emptyArchetype = ref _archetypes.Get(0);

            Entity entity;
            entity.World = this;

            if (_freeEntityIds.TryGetNewInt(out var newEntityId))
            {
                ArrayExtension.EnsureLength(ref _entities, newEntityId);
                ref var entityData = ref _entities[newEntityId];
                entity.Id = newEntityId;
                entityData.Archetype = emptyArchetype;
                entity.Age = 1;
                entityData.Age = 1;
            }
            else
            {
                ref var entityData = ref _entities[newEntityId];
                entity.Id = newEntityId;
                entityData.Archetype = emptyArchetype;
                entity.Age = entityData.Age;
            }

            emptyArchetype.AddEntity(entity);
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
            entityData.Archetype = null;
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
        public World Registry<T>() where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (_componentPools.Contains(idx)) return this;
            var pool = new ComponentPool<T>(this);
            _componentPools.Set(idx, pool);
            return this;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentPool<T> GetPool<T>() where T : struct
        {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot get pool.");
#endif
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (_componentPools.Contains(idx)) return (ComponentPool<T>) _componentPools.Get(idx);
            var pool = new ComponentPool<T>(this);
            _componentPools.Set(idx, pool);

            return (ComponentPool<T>) _componentPools.Get(idx);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IComponentPool GetPool(int idx)
        {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot get pool.");
#endif
            var pool = _componentPools.Get(idx);
            return pool;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TaskPool<T> GetTaskPool<T>() where T : struct
        {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {_name} was destroyed. You cannot get pool.");
#endif
            var idx = ComponentTypeInfo<T>.TypeIndex;

            if (_taskPools.Contains(idx)) return (TaskPool<T>) _taskPools.Get(idx);
            var pool = new TaskPool<T>(this);
            _taskPools.Set(idx, pool);

            return (TaskPool<T>) _taskPools.Get(idx);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteTasks()
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_name} destroyed");
            for (int i = 0, lenght = _taskPools.Count; i < lenght; i++)
            {
                _taskPools[i].Execute();
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
        internal void FindArchetypes(Filter filter, int version)
        {
            var include = filter.Include;
            var exclude = filter.Exclude;

            for (int i = version, lenght = _archetypes.Count; i < lenght; i++)
            {
                var archetype = _archetypes.Get(i);
                if (archetype.Mask.Contains(include) && (exclude.Count == 0 || !archetype.Mask.Contains(exclude)))
                {
                    filter.AddArchetype(archetype);
                }
            }

            filter.Version = _archetypes.Count;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Archetype FindOrCreateArchetype(BitMask mask)
        {
            var curArchetype = _archetypes.Get(0);
            var newMask = new BitMask(Config.Components);

            foreach (var index in mask)
            {
                newMask.SetBit(index);

                var nextArchetype = curArchetype.Next.Get(index);

                if (nextArchetype == null)
                {
                    nextArchetype = new Archetype(this, newMask);

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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InternalDestroy()
        {
#if DEBUG
            if (!_isAlive) throw new Exception($"|KECS| World - {_name} already destroy");
#endif

            Entity entity;
            entity.World = this;
            for (int i = 0, lenght = _entities.Length; i < lenght; i++)
            {
                ref var entityData = ref _entities[i];
                if (entityData.Archetype == null) continue;
                entity.Id = i;
                entity.Age = entityData.Age;
                entity.Destroy();
            }

            for (int i = 0, lenght = _componentPools.Count; i < lenght; i++)
            {
                _componentPools[i].Dispose();
            }

            _componentPools.Clear();
            for (int i = 0, lenght = _filters.Count; i < lenght; i++)
            {
                _filters.Get(i).Dispose();
            }

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes.Get(i).Dispose();
            }

            _archetypes.Clear();
            _filters.Clear();
            _freeEntityIds.Clear();
            _entitiesCount = 0;
            _worldId = -1;
            _isAlive = false;
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


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
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

            if (!entityData.Archetype.Mask.GetBit(idx))
            {
                GotoNextArchetype(ref entityData, in entity, idx);
            }

            return ref pool.Get(entity.Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Event<T>(in this Entity entity, in T value = default) where T : struct
        {
            if (!entity.IsAlive()) return;
            entity.World.GetTaskPool<T>().Add(entity, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remove<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var pool = world.GetPool<T>();

            if (entityData.Archetype.Mask.GetBit(idx))
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
        public static bool Has(in this Entity entity, int idx)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            return entityData.Archetype.Mask.GetBit(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GotoNextArchetype(ref EntityData entityData, in Entity entity, int index)
        {
            var world = entity.World;
            entityData.Archetype.RemoveEntity(entity);
            var newArchetype = world.FindOrCreateNextArchetype(entityData.Archetype, index);
            entityData.Archetype = newArchetype;
            entityData.Archetype.AddEntity(entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GotoPriorArchetype(ref EntityData entityData, in Entity entity, int index)
        {
            var world = entity.World;
            entityData.Archetype.RemoveEntity(entity);
            var newArchetype = world.FindOrCreatePriorArchetype(entityData.Archetype, index);
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

            if (entity.Has(typeIdx))
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

            var counter = 0;
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

            var counter = 0;
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

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Archetype : IEnumerable<Entity>, IDisposable
    {
        internal readonly HandleMap<Entity> Entities;
        internal readonly HandleMap<Archetype> Next;
        internal readonly HandleMap<Archetype> Prior;

        private int _lockCount;
        private DelayedChange[] _delayedChanges;
        private int _delayedOpsCount;

        public int Count => Entities.Count;
        public BitMask Mask { get; }

        internal Archetype(World world, BitMask mask)
        {
            Mask = mask;
            _lockCount = 0;

            _delayedChanges = new DelayedChange[64];
            _delayedOpsCount = 0;

            Next = new HandleMap<Archetype>(world.Config.Components,
                world.Config.Components);
            Prior = new HandleMap<Archetype>(world.Config.Components,
                world.Config.Components);

            Entities = new HandleMap<Entity>(world.Config.Entities,
                world.Config.Entities);
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
            ref var delayedChange = ref _delayedChanges[_delayedOpsCount++];
            delayedChange.Entity = entity;
            delayedChange.IsAdd = isAdd;
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Entities.Clear();
            Next.Clear();
            Prior.Clear();
            
            _lockCount = 0;
            _delayedOpsCount = 0;
        }


        private struct DelayedChange
        {
            public bool IsAdd;
            public Entity Entity;
        }
    }


    //=============================================================================
    // POOLS
    //=============================================================================
#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class EcsTypeManager
    {
        public static int ComponentTypesCount;
        public static TypeInfo[] ComponentsInfos = new TypeInfo[WorldConfig.DefaultComponents];

        public readonly struct TypeInfo
        {
            public readonly int Index;
            public readonly Type Type;

            public TypeInfo(int idx, Type type)
            {
                Index = idx;
                Type = type;
            }
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class ComponentTypeInfo<T> where T : struct
    {
        public static readonly int TypeIndex;
        public static readonly Type Type;

        private static readonly object _lockObject = new object();

        static ComponentTypeInfo()
        {
            lock (_lockObject)
            {
                TypeIndex = EcsTypeManager.ComponentTypesCount++;
                Type = typeof(T);
                ArrayExtension.EnsureLength(ref EcsTypeManager.ComponentsInfos, TypeIndex);
                EcsTypeManager.ComponentsInfos[TypeIndex] = new EcsTypeManager.TypeInfo(TypeIndex, Type);
            }
        }
    }


    internal interface IComponentPool : IDisposable
    {
        void Remove(int entityId);
        object GetObject(int entityId);
        void SetObject(int entityId, object value);
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private readonly HandleMap<T> _components;
        internal ref T Empty => ref _components.Empty;

        public ComponentPool(World world)
        {
            _components = new HandleMap<T>(world.Config.Entities,
                world.Config.Entities);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityId) => ref _components.Get(entityId);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetObject(int entityId) => _components.Get(entityId);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetObject(int entityId, object value)
        {
            if (value is T component)
            {
                _components.Set(entityId, component);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int entityId) => _components.Remove(entityId);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityId, in T value) => _components.Set(entityId, value);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _components.Clear();
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

    public delegate void ForEachHandler<T, Y, U, I, O, P, A>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3, ref O comp4, ref P comp5, ref A comp6)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct
        where O : struct
        where P : struct
        where A : struct;

    public delegate void ForEachHandler<T, Y, U, I, O, P, A, S>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3, ref O comp4, ref P comp5, ref A comp6, ref S comp7)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct
        where O : struct
        where P : struct
        where A : struct
        where S : struct;

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Filter
    {
        internal BitMask Include;
        internal BitMask Exclude;
        internal int Version { get; set; }

        private readonly GrowList<Archetype> _archetypes;
        private readonly World _world;


        internal Filter(World world)
        {
            Include = new BitMask(world.Config.Components);
            Exclude = new BitMask(world.Config.Components);
            _archetypes = new GrowList<Archetype>(world.Config.Archetypes);

            _world = world;
            Version = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Filter With<T>() where T : struct
        {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;

            if (Exclude.GetBit(typeIdx))
            {
                return this;
            }

            Include.SetBit(typeIdx);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Filter Without<T>() where T : struct
        {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;

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
            _world.FindArchetypes(this, Version);

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes.Get(i).Lock();
            }

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                if (_archetypes.Get(i).Count > 0)
                {
                    handler(_archetypes.Get(i));
                }
            }

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes.Get(i).Unlock();
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T>(ForEachHandler<T> handler)
            where T : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T, Y>(ForEachHandler<T, Y> handler)
            where T : struct
            where Y : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T, Y, U>(ForEachHandler<T, Y, U> handler)
            where T : struct
            where Y : struct
            where U : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T, Y, U, I>(ForEachHandler<T, Y, U, I> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T, Y, U, I, O>(ForEachHandler<T, Y, U, I, O> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T, Y, U, I, O, P>(ForEachHandler<T, Y, U, I, O, P> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<P>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
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
        public void ForEach<T, Y, U, I, O, P, A>(ForEachHandler<T, Y, U, I, O, P, A> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<P>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<A>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
            var poolU = _world.GetPool<U>();
            var poolI = _world.GetPool<I>();
            var poolO = _world.GetPool<O>();
            var poolP = _world.GetPool<P>();
            var poolA = _world.GetPool<A>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id), ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id), ref poolO.Get(entity.Id), ref poolP.Get(entity.Id),
                        ref poolA.Get(entity.Id));
                }
            });
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<T, Y, U, I, O, P, A, S>(ForEachHandler<T, Y, U, I, O, P, A, S> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
            where S : struct
        {
#if DEBUG
            if (!Include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<P>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<A>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!Include.GetBit(ComponentTypeInfo<S>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = _world.GetPool<T>();
            var poolY = _world.GetPool<Y>();
            var poolU = _world.GetPool<U>();
            var poolI = _world.GetPool<I>();
            var poolO = _world.GetPool<O>();
            var poolP = _world.GetPool<P>();
            var poolA = _world.GetPool<A>();
            var poolS = _world.GetPool<S>();
            ForEach(archetype =>
            {
                for (int i = 0, lenght = archetype.Count; i < lenght; i++)
                {
                    var entity = archetype.Entities[i];
                    handler(entity, ref poolT.Get(entity.Id), ref poolY.Get(entity.Id), ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id), ref poolO.Get(entity.Id), ref poolP.Get(entity.Id),
                        ref poolA.Get(entity.Id), ref poolS.Get(entity.Id));
                }
            });
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Dispose()
        {
            Version = 0;
            _archetypes.Clear();
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


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Systems : IDisposable
    {
        private readonly Dictionary<int, SystemData> _systems;

        private readonly GrowList<SystemData> _updateSystems;
        private readonly GrowList<SystemData> _fixedSystems;
        private readonly GrowList<SystemData> _lateSystems;
        private readonly GrowList<SystemData> _allSystems;
        private readonly GrowList<SystemData> _onlyBaseSystems;

        private readonly SharedData _sharedData;

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
            _allSystems = new GrowList<SystemData>();
            _updateSystems = new GrowList<SystemData>();
            _fixedSystems = new GrowList<SystemData>();
            _lateSystems = new GrowList<SystemData>();
            _onlyBaseSystems = new GrowList<SystemData>();
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
#if DEBUG
            if (_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            _sharedData.Add(data);
            return this;
        }


        public T GetShared<T>() where T : class
        {
#if DEBUG
            if (!_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            return _sharedData.Get<T>();
        }


        public GrowList<SystemData> GetUpdateSystems()
        {
            return _updateSystems;
        }


        public GrowList<SystemData> GetFixedUpdateSystems()
        {
            return _fixedSystems;
        }


        public GrowList<SystemData> GetLateUpdateSystems()
        {
            return _lateSystems;
        }


        public GrowList<SystemData> GetOnlyBaseSystems()
        {
            return _onlyBaseSystems;
        }

        public Systems Add<T>() where T : SystemBase, new()
        {
#if DEBUG
            if (_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
#endif

            var systemValue = new T();

            var hash = typeof(T).GetHashCode();

            if (_systems.ContainsKey(hash)) return this;

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

            return this;
        }


        public Systems Disable<T>() where T : SystemBase
        {
#if DEBUG
            if (!_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            var hash = typeof(T).GetHashCode();
            if (_systems.TryGetValue(hash, out var systemValue)) systemValue.IsEnable = false;
            return this;
        }


        public Systems Enable<T>() where T : SystemBase
        {
#if DEBUG
            if (!_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            var hash = typeof(T).GetHashCode();
            if (_systems.TryGetValue(hash, out var systemValue)) systemValue.IsEnable = true;
            return this;
        }


        public void Update(float deltaTime)
        {
#if DEBUG
            if (!_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            for (int i = 0, lenght = _updateSystems.Count; i < lenght; i++)
            {
                var update = _updateSystems.Get(i);
                if (update.IsEnable) update.UpdateImpl?.OnUpdate(deltaTime);
            }
        }


        public void FixedUpdate(float deltaTime)
        {
#if DEBUG
            if (!_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif

            for (int i = 0, lenght = _fixedSystems.Count; i < lenght; i++)
            {
                var update = _fixedSystems.Get(i);
                if (update.IsEnable) update.UpdateImpl?.OnUpdate(deltaTime);
            }
        }


        public void LateUpdate(float deltaTime)
        {
#if DEBUG
            if (!_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            for (int i = 0, lenght = _lateSystems.Count; i < lenght; i++)
            {
                var update = _lateSystems.Get(i);
                if (update.IsEnable) update.UpdateImpl?.OnUpdate(deltaTime);
            }
        }


        public void Initialize()
        {
#if DEBUG
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot initialize them.");
#endif
            _initialized = true;

            for (int i = 0, lenght = _allSystems.Count; i < lenght; i++)
            {
                _allSystems.Get(i).Base.Initialize();
            }
        }


        public void Destroy()
        {
#if DEBUG
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot destroy them.");
#endif
            _destroyed = true;

            for (int i = 0, lenght = _allSystems.Count; i < lenght; i++)
            {
                var destroy = _allSystems.Get(i);
                if (destroy.IsEnable) destroy.Base.OnDestroy();
            }

            for (int i = 0, lenght = _allSystems.Count; i < lenght; i++)
            {
                var destroy = _allSystems.Get(i);
                if (destroy.IsEnable) destroy.Base.PostDestroy();
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
        }
    }


    //=============================================================================
    // HANDLE MAP
    //=============================================================================

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int sparseIdx) =>
            _denseCount > 0 && sparseIdx < _sparse.Length && _sparse[sparseIdx] != None;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < _denseCount) return ref _instances[index];
                throw new Exception($"|KECS| Out of range HandleMap {index}.");
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int sparseIdx)
        {
            if (Contains(sparseIdx)) return ref _instances[_sparse[sparseIdx]];
            return ref Empty;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int sparseIdx)
        {
            if (!Contains(sparseIdx))
            {
                throw new Exception($"|KECS| Unable to remove sparse idx {sparseIdx}: not present.");
            }

            var packedIdx = _sparse[sparseIdx];
            _sparse[sparseIdx] = None;
            _denseCount--;

            if (packedIdx < _denseCount)
            {
                var lastSparseIdx = _dense[_denseCount];
                var lastValue = _instances[_denseCount];

                _dense[packedIdx] = lastSparseIdx;
                _instances[packedIdx] = lastValue;
                _sparse[lastSparseIdx] = packedIdx;
            }

            _instances[_denseCount] = default;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _denseCount = 0;
            Array.Clear(_instances, 0, _instances.Length);
            Array.Clear(_dense, 0, _dense.Length);
            _sparse.Fill(None);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<T> GetEnumerator() => new Enumerator(this);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        private struct Enumerator : IEnumerator<T>
        {
            private int _count;
            private int _index;
            private HandleMap<T> _handleMap;


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


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public class GrowList<T>
    {
        private const int DefaultCapacity = 16;
        private T[] _data;
        private T _empty;
        private int _lenght;
        public int Count => _lenght;

        public GrowList(int capacity = DefaultCapacity)
        {
            if (capacity < DefaultCapacity) capacity = DefaultCapacity;
            _data = new T[capacity];
            _empty = default;
            _lenght = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in T value)
        {
            var index = _lenght++;
            ArrayExtension.EnsureLength(ref _data, index);
            _data[index] = value;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int idx)
        {
            if (idx < _lenght) return ref _data[idx];
            return ref _empty;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => Array.Clear(_data, 0, _data.Length);
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal class IntDispenser
    {
        private readonly ConcurrentStack<int> _freeInts;
        private int _lastInt;
        private readonly int _startInt;
        internal int Count => _freeInts.Count;


        public IntDispenser()
        {
            _freeInts = new ConcurrentStack<int>();
            _startInt = -1;
            _lastInt = -1;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetFreeInt()
        {
            if (_freeInts.TryPop(out var freeInt)) return freeInt;
            freeInt = Interlocked.Increment(ref _lastInt);
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
        public void Clear()
        {
            _freeInts.Clear();
            _lastInt = _startInt;
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class ArrayExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InnerEnsureLength<T>(ref T[] array, int index)
        {
            var newLength = Math.Max(1, array.Length);

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
                var oldLength = array.Length;

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
        private const int ChunkCapacity = sizeof(ulong) * 8;
        private readonly ulong[] _chunks;

        public int Count { get; private set; }

        public BitMask(int capacity = 0)
        {
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
            var newSize = copy._chunks.Length;
            _chunks = new ulong[newSize];
            for (var i = 0; i < newSize; i++)
            {
                _chunks[i] = copy._chunks[i];
            }

            Count = copy.Count;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldValue = _chunks[chunk];
            var newValue = oldValue | (1UL << (index % ChunkCapacity));
            if (oldValue == newValue) return;
            _chunks[chunk] = newValue;
            Count++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldValue = _chunks[chunk];
            var newValue = oldValue & ~(1UL << (index % ChunkCapacity));
            if (oldValue == newValue) return;
            _chunks[chunk] = newValue;
            Count--;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetBit(int index)
        {
            return (_chunks[index / ChunkCapacity] & (1UL << (index % ChunkCapacity))) != 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(BitMask bitMask)
        {
            for (int i = 0, lenght = _chunks.Length; i < lenght; i++)
            {
                if ((_chunks[i] & bitMask._chunks[i]) != bitMask._chunks[i]) return false;
            }

            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BitMask bitMask)
        {
            for (int i = 0, lenght = _chunks.Length; i < lenght; i++)
            {
                if ((_chunks[i] & bitMask._chunks[i]) != 0) return true;
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
            public bool MoveNext() => _returned < _count;
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

#if ENABLE_IL2CPP
namespace Unity.IL2CPP.CompilerServices
{
    enum Option
    {
        NullChecks = 1,
        ArrayBoundsChecks = 2
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited =
        false, AllowMultiple = true)]
    class Il2CppSetOptionAttribute : Attribute
    {
        public Option Option { get; private set; }
        public object Value { get; private set; }

        public Il2CppSetOptionAttribute(Option option, object value)
        {
            Option = option;
            Value = value;
        }
    }
}
#endif