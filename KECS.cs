using System;
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
        public int EntitiesCount;
        public int FreeEntitiesCount;
        public int ArchetypesCount;
        public int ComponentsCount;
    }


    public struct WorldConfig
    {
        public int Entities;
        public int Archetypes;
        public int Components;
        public const int DefaultEntities = 1024;
        public const int DefaultArchetypes = 512;
        public const int DefaultComponents = 512;
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class Worlds
    {
        private static readonly object _lockObject;

        private static readonly HandleMap<World> _worlds;
        private static readonly IntDispenser _freeWorldsIds;
        private static readonly HashMap<int> _worldsIdx;


        static Worlds()
        {
            _lockObject = new object();
            _worlds = new HandleMap<World>(32);
            _worldsIdx = new HashMap<int>(32);
            _freeWorldsIds = new IntDispenser();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Create(string name, WorldConfig config = default)
        {
            lock (_lockObject)
            {
                var hashName = name.GetHashCode();
                if (_worldsIdx.Contains(hashName))
                {
                    throw new Exception($"|KECS| A world with {name} name already exists.");
                }

                var worldId = _freeWorldsIds.GetFreeInt();
                var newWorld = new World(worldId, CheckConfig(config), name);
                _worlds.Set(worldId, newWorld);
                _worldsIdx.Set(hashName, worldId);
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
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(string name)
        {
            var hashName = name.GetHashCode();
            lock (_lockObject)
            {
                if (_worldsIdx.TryGetValue(hashName, out var worldId))
                {
                    return _worlds.Get(worldId);
                }
            }

            throw new Exception($"|KECS| No world with {name} name was found.");
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static World Get(int worldId) => _worlds.Get(worldId);


        internal static void Recycle(int worldId)
        {
            lock (_lockObject)
            {
                var hash = _worlds.Get(worldId).Name.GetHashCode();
                _worldsIdx.Remove(hash);
                _worlds.Remove(worldId);
                _freeWorldsIds.ReleaseInt(worldId);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyAll()
        {
            lock (_lockObject)
            {
                for (int i = 0, lenght = _worlds.Count; i < lenght; i++)
                {
                    var world = _worlds.Get(i);
                    world.Destroy();
                }

                _worlds.Clear();
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
        private readonly HashMap<object> _data;

        internal SharedData()
        {
            _data = new HashMap<object>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T Add<T>(T data) where T : class
        {
            var hash = typeof(T).GetHashCode();
            if (_data.Contains(hash))
                throw new Exception($"|KECS| You have already added this type{typeof(T).Name} of data");
            _data.Set(hash, data);
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
        private DelayedTask[] _addTasks;
        private DelayedTask[] _removeTasks;
        private int _addTasksCount;
        private int _removeTasksCount;


        internal TaskPool(int capacity)
        {
            _addTasks = new DelayedTask[capacity];
            _removeTasks = new DelayedTask[capacity];
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
            task.Component = component;
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
                task.Entity.Set(task.Component);

                ref var removeTask = ref _removeTasks[_removeTasksCount++];
                removeTask.Entity = task.Entity;
                removeTask.Component = task.Component;
            }
        }


        private struct DelayedTask
        {
            public T Component;
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
        private readonly FastList<Filter> _filters;

        private readonly FastList<Archetype> _archetypes;
        private readonly HashMap<Archetype> _archetypesMap;

        private readonly IntDispenser _freeEntityIds;
        private EntityData[] _entities;
        private int _entitiesCount;

        private int _worldId;
        private readonly string _name;
        private bool _isAlive;
        internal readonly WorldConfig Config;

        public string Name => _name;
        public bool IsAlive() => _isAlive;
        public int Id => _worldId;

        internal World(int worldId, WorldConfig config, string name)
        {
            _name = name;
            _isAlive = true;
            _worldId = worldId;
            Config = config;

            _componentPools = new HandleMap<IComponentPool>(config.Components);
            _taskPools = new HandleMap<ITaskPool>(config.Components);

            _archetypesMap = new HashMap<Archetype>(Config.Archetypes);
            _archetypes = new FastList<Archetype>(Config.Archetypes);

            var emptyArch = new Archetype(new BitMask(Config.Components), Config.Entities);

            _archetypesMap.Set(emptyArch.Hash, emptyArch);
            _archetypes.Add(emptyArch);

            _filters = new FastList<Filter>();

            _entities = new EntityData[config.Entities];
            _freeEntityIds = new IntDispenser();
            _entitiesCount = 0;
        }


#if DEBUG
        private readonly FastList<IWorldDebugListener> _debugListeners = new FastList<IWorldDebugListener>();

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
                EntitiesCount = _entitiesCount,
                FreeEntitiesCount = _freeEntityIds.Count,
                ArchetypesCount = _archetypes.Count,
                ComponentsCount = _componentPools.Count
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
                _debugListeners.Get(i).OnEntityCreated(entity);
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
            if (entityData.Age == 0) entityData.Age = 1;
            _freeEntityIds.ReleaseInt(entity.Id);
            _entitiesCount--;
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners.Get(i).OnEntityDestroyed(entity);
            }
#endif
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
            var pool = new ComponentPool<T>(Config.Entities);
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
            return _componentPools.Get(idx);
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
            var pool = new TaskPool<T>(Config.Entities);
            _taskPools.Set(idx, pool);

            return (TaskPool<T>) _taskPools.Get(idx);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteTasks()
        {
            if (!_isAlive) throw new Exception($"|KECS| World - {_name} destroyed");
            for (int i = 0, lenght = _taskPools.Count; i < lenght; i++)
            {
                _taskPools.Data[i].Execute();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void FindArchetypes(Filter filter)
        {
            var include = filter.Include;
            var exclude = filter.Exclude;
            var version = filter.Version;

            for (int i = version, lenght = _archetypes.Count; i < lenght; i++)
            {
                var archetype = _archetypes.Get(i);
                if (archetype.Mask.Contains(include) && (exclude.Count == 0 || !archetype.Mask.Intersects(exclude)))
                {
                    filter.Archetypes.Add(archetype);
                }
            }

            filter.Version = _archetypes.Count;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype GetArchetype(BitMask mask)
        {
            var hash = mask.GetHash();
            if (_archetypesMap.TryGetValue(hash, out var archetype)) return archetype;
            archetype = new Archetype(mask, Config.Entities);
            _archetypes.Add(archetype);
            _archetypesMap.Set(hash, archetype);
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners.Get(i).OnArchetypeCreated(archetype);
            }
#endif
            return archetype;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy()
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

            for (int i = 0, lenght = _filters.Count; i < lenght; i++)
            {
                _filters.Get(i).Dispose();
            }

            for (int i = 0, lenght = _componentPools.Count; i < lenght; i++)
            {
                _componentPools.Data[i].Dispose();
            }

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                _archetypes.Get(i).Dispose();
            }

            _filters.Clear();
            _componentPools.Clear();
            _archetypes.Clear();
            _archetypesMap.Clear();
            _freeEntityIds.Clear();
            _entitiesCount = 0;
            Worlds.Recycle(_worldId);
            _worldId = -1;
            _isAlive = false;
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners.Get(i).OnWorldDestroyed(this);
            }
#endif
        }
    }


    //=============================================================================
    // ENTITY
    //=============================================================================


    public class EntityBuilder
    {
        private HandleMap<IComponentBuilder> _builders;

        public EntityBuilder()
        {
            _builders = new HandleMap<IComponentBuilder>(WorldConfig.DefaultComponents);
        }

        public EntityBuilder Append<T>(in T component = default) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            _builders.Set(idx, new ComponentBuilder<T>(component));
            return this;
        }

        public EntityBuilder Remove<T>() where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (!_builders.Contains(idx)) return this;
            _builders.Remove(idx);
            return this;
        }

        public Entity Build(World world)
        {
            var entity = world.CreateEntity();
            var mask = new BitMask(world.Config.Components);
            for (int i = 0, lenght = _builders.Count; i < lenght; i++)
            {
                var build = _builders.Data[i];
                build.Set(entity);
                mask.SetBit(build.GetIdx());
            }

            entity.SwapArchetype(mask);
            return entity;
        }

        private interface IComponentBuilder
        {
            void Set(in Entity entity);
            int GetIdx();
        }

        private class ComponentBuilder<T> : IComponentBuilder where T : struct
        {
            private T _component;
            private int _idx;


            internal ComponentBuilder(in T component)
            {
                _component = component;
                _idx = ComponentTypeInfo<T>.TypeIndex;
            }

            public int GetIdx() => _idx;

            public void Set(in Entity entity)
            {
                var world = entity.World;
                var pool = world.GetPool<T>();
                pool.Set(entity.Id, in _component);
            }
        }
    }


    public struct EntityData
    {
        public int Age;
        public Archetype Archetype;
    }

    public struct Entity
    {
        public int Id;
        public int Age;
        public World World;
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
            return entity.World != null && entity.World.EntityIsAlive(in entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEqual(in this Entity entityL, in Entity entityR)
        {
            return entityL.Id == entityR.Id && entityL.Age == entityR.Age && entityL.World == entityR.World;
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
                entity.SwapArchetype(entityData.Archetype.Mask.Copy().SetBit(idx));
            }

            return ref pool.Get(entity.Id);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetEvent<T>(in this Entity entity, in T value = default) where T : struct
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
                entity.SwapArchetype(entityData.Archetype.Mask.Copy().ClearBit(idx));
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
        internal static void SwapArchetype(in this Entity entity, BitMask newMask)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var oldArchetype = entityData.Archetype;
            var newArchetype = world.GetArchetype(newMask);
            oldArchetype.RemoveEntity(entity);
            newArchetype.AddEntity(entity);
            entityData.Archetype = newArchetype;
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
    public sealed class Archetype : IDisposable
    {
        internal readonly HandleMap<Entity> Entities;
        public readonly int Hash;
        public readonly BitMask Mask;

        private DelayedChange[] _delayedChanges;
        private int _lockCount;
        private int _delayedOpsCount;

        public int Count => Entities.Count;

        internal Archetype(BitMask mask, int entityCapacity)
        {
            Entities = new HandleMap<Entity>(entityCapacity);
            _delayedChanges = new DelayedChange[64];
            Mask = mask;
            Hash = Mask.GetHash();
            _lockCount = 0;
            _delayedOpsCount = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Lock() => _lockCount++;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Unlock()
        {
            _lockCount--;
            if (_lockCount != 0 || _delayedOpsCount <= 0) return;
            for (var i = 0; i < _delayedOpsCount; i++)
            {
                ref var operation = ref _delayedChanges[i];
                if (operation.IsAdd) AddEntity(operation.Entity);
                else RemoveEntity(operation.Entity);
            }

            _delayedOpsCount = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool AddDelayedChange(in Entity entity, bool isAdd)
        {
            if (_lockCount <= 0) return false;
            ArrayExtension.EnsureLength(ref _delayedChanges, _delayedOpsCount);
            ref var delayedChange = ref _delayedChanges[_delayedOpsCount++];
            delayedChange.Entity = entity;
            delayedChange.IsAdd = isAdd;
            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddEntity(in Entity entity)
        {
            if (AddDelayedChange(entity, true)) return;
            Entities.Set(entity.Id, entity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveEntity(in Entity entity)
        {
            if (AddDelayedChange(entity, false)) return;
            Entities.Remove(entity.Id);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HandleMap<Entity>.Enumerator GetEnumerator()
        {
            return Entities.GetEnumerator();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Entities.Clear();
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
        internal static int ComponentTypesCount;
        internal static Type[] ComponentsTypes = new Type[WorldConfig.DefaultComponents];


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetTypeByIndex(int idx)
        {
            if (idx >= ComponentTypesCount) return default;
            return ComponentsTypes[idx];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type[] GetAllTypes()
        {
            var count = ComponentTypesCount;
            var infos = new Type[count];
            for (var i = 0; i < count; i++)
            {
                infos[i] = ComponentsTypes[i];
            }

            return infos;
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
                ArrayExtension.EnsureLength(ref EcsTypeManager.ComponentsTypes, TypeIndex);
                EcsTypeManager.ComponentsTypes[TypeIndex] = Type;
            }
        }
    }

    internal interface IComponentPool : IDisposable
    {
        void Remove(int entityId);
        object GetObject(int entityId);
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private readonly HandleMap<T> _components;
        internal ref T Empty => ref _components.Empty;

        public ComponentPool(int capacity)
        {
            _components = new HandleMap<T>(capacity);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityId) => ref _components.Get(entityId);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetObject(int entityId) => _components.Get(entityId);


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
        internal FastList<Archetype> Archetypes;
        internal World World;
        internal BitMask Include;
        internal BitMask Exclude;
        internal int Version;

        internal Filter(World world)
        {
            Include = new BitMask(world.Config.Components);
            Exclude = new BitMask(world.Config.Components);
            Archetypes = new FastList<Archetype>(world.Config.Archetypes);

            World = world;
            Version = 0;
        }
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class FilterExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Dispose(this Filter filter)
        {
            filter.Archetypes.Clear();
            filter.Version = 0;
            filter.World = null;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Filter With<T>(this Filter filter) where T : struct
        {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;
            if (filter.Exclude.GetBit(typeIdx)) return filter;
            filter.Include.SetBit(typeIdx);
            return filter;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Filter Without<T>(this Filter filter) where T : struct
        {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;
            if (filter.Include.GetBit(typeIdx)) return filter;
            filter.Exclude.SetBit(typeIdx);
            return filter;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Lock(this Filter filter)
        {
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                archetypes.Get(i).Lock();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Unlock(this Filter filter)
        {
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                archetypes.Get(i).Unlock();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach(this Filter filter, ForEachHandler handler)
        {
            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity);
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T>(this Filter filter, ForEachHandler<T> handler)
            where T : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y>(this Filter filter, ForEachHandler<T, Y> handler)
            where T : struct
            where Y : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U>(this Filter filter, ForEachHandler<T, Y, U> handler)
            where T : struct
            where Y : struct
            where U : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id),
                        ref poolU.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I>(this Filter filter, ForEachHandler<T, Y, U, I> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id),
                        ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O>(this Filter filter, ForEachHandler<T, Y, U, I, O> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id),
                        ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id),
                        ref poolO.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P>(this Filter filter, ForEachHandler<T, Y, U, I, O, P> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<P>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id),
                        ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id),
                        ref poolO.Get(entity.Id),
                        ref poolP.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P, A>(this Filter filter, ForEachHandler<T, Y, U, I, O, P, A> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<P>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<A>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id),
                        ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id),
                        ref poolO.Get(entity.Id),
                        ref poolP.Get(entity.Id),
                        ref poolA.Get(entity.Id));
                }
            }

            filter.Unlock();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P, A, S>(this Filter filter,
            ForEachHandler<T, Y, U, I, O, P, A, S> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
            where S : struct
        {
            var world = filter.World;
#if DEBUG
            ref var include = ref filter.Include;
            if (!include.GetBit(ComponentTypeInfo<T>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<Y>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<U>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<I>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<O>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<P>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<A>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
            if (!include.GetBit(ComponentTypeInfo<S>.TypeIndex))
                throw new Exception("|KECS| There is no such component in the filter.");
#endif
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();
            var poolS = world.GetPool<S>();

            filter.World.FindArchetypes(filter);
            filter.Lock();
            var archetypes = filter.Archetypes;
            for (int i = 0, lenght = archetypes.Count; i < lenght; i++)
            {
                var archetype = archetypes.Get(i);
                if (archetype.Entities.Count <= 0) continue;
                for (int j = 0, lenghtJ = archetype.Entities.Count; j < lenghtJ; j++)
                {
                    var entity = archetype.Entities.Data[j];
                    handler(entity,
                        ref poolT.Get(entity.Id),
                        ref poolY.Get(entity.Id),
                        ref poolU.Get(entity.Id),
                        ref poolI.Get(entity.Id),
                        ref poolO.Get(entity.Id),
                        ref poolP.Get(entity.Id),
                        ref poolA.Get(entity.Id),
                        ref poolS.Get(entity.Id));
                }
            }

            filter.Unlock();
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


        internal void Ctor(World world, Systems systems)
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


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Systems : IDisposable
    {
        private readonly HashMap<SystemData> _systems;

        private readonly FastList<SystemData> _updateSystems;
        private readonly FastList<SystemData> _fixedSystems;
        private readonly FastList<SystemData> _lateSystems;
        private readonly FastList<SystemData> _allSystems;
        private readonly FastList<SystemData> _onlyBaseSystems;

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
            _systems = new HashMap<SystemData>();
            _allSystems = new FastList<SystemData>();
            _updateSystems = new FastList<SystemData>();
            _fixedSystems = new FastList<SystemData>();
            _lateSystems = new FastList<SystemData>();
            _onlyBaseSystems = new FastList<SystemData>();
        }

#if DEBUG
        private readonly FastList<ISystemsDebugListener> _debugListeners = new FastList<ISystemsDebugListener>();


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


        public FastList<SystemData> GetUpdateSystems()
        {
            return _updateSystems;
        }


        public FastList<SystemData> GetFixedUpdateSystems()
        {
            return _fixedSystems;
        }


        public FastList<SystemData> GetLateUpdateSystems()
        {
            return _lateSystems;
        }


        public FastList<SystemData> GetOnlyBaseSystems()
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

            if (_systems.Contains(hash)) return this;

            var systemData = new SystemData {IsEnable = true, Base = systemValue};
            _allSystems.Add(systemData);
            systemValue.Ctor(_world, this);

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

            _systems.Set(hash, systemData);

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
                _debugListeners.Get(i).OnSystemsDestroyed(this);
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

        public sealed class SystemData
        {
            public bool IsEnable;
            public SystemBase Base;
            public IUpdate UpdateImpl;
        }
    }


    //=============================================================================
    // HELPER
    //=============================================================================

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class HandleMap<T>
    {
        private const int None = -1;
        public T[] Data;
        private int[] _dense;
        private int[] _sparse;
        private int _denseCount;

        private T _empty;
        public int Count => _denseCount;
        public ref T Empty => ref _empty;

        public HandleMap(int capacity)
        {
            _dense = new int[capacity];
            _sparse = new int[capacity];
            Data = new T[capacity];
            _sparse.Fill(None);
            _denseCount = 0;
            _empty = default(T);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int sparseIdx) =>
            _denseCount > 0 && sparseIdx < _sparse.Length && _sparse[sparseIdx] != None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int sparseIdx)
        {
            if (Contains(sparseIdx)) return ref Data[_sparse[sparseIdx]];
            return ref Empty;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int sparseIdx, T value)
        {
            if (!Contains(sparseIdx))
            {
                ArrayExtension.EnsureLength(ref _sparse, sparseIdx, None);
                ArrayExtension.EnsureLength(ref _dense, _denseCount);
                ArrayExtension.EnsureLength(ref Data, _denseCount);

                _sparse[sparseIdx] = _denseCount;

                _dense[_denseCount] = sparseIdx;
                Data[_denseCount] = value;

                _denseCount++;
            }
            else
            {
                Data[_sparse[sparseIdx]] = value;
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
                var lastValue = Data[_denseCount];

                _dense[packedIdx] = lastSparseIdx;
                Data[packedIdx] = lastValue;
                _sparse[lastSparseIdx] = packedIdx;
            }

            Data[_denseCount] = default;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _denseCount = 0;
            Array.Clear(Data, 0, Data.Length);
            Array.Clear(_dense, 0, _dense.Length);
            _sparse.Fill(None);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new Enumerator(this);


        public struct Enumerator : IDisposable
        {
            private int _index;
            private HandleMap<T> _list;

            public Enumerator(HandleMap<T> handleMap)
            {
                _list = handleMap;
                _index = 0;
            }

            public ref T Current => ref _list.Data[_index++];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => _index < _list.Count;

            public void Dispose()
            {
            }
        }
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
    public sealed class BitMask
    {
        internal const int ChunkCapacity = sizeof(ulong) * 8;
        internal ulong[] Chunks;
        internal int Count;


        internal BitMask(int capacity)
        {
            var newSize = capacity / ChunkCapacity;
            if (capacity % ChunkCapacity != 0) newSize++;
            Count = 0;
            Chunks = new ulong[newSize];
        }

        internal BitMask()
        {
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }


        public ref struct Enumerator
        {
            private readonly int _count;
            private readonly BitMask _bitMask;
            private int _index;
            private int _returned;


            public Enumerator(BitMask bitMask)
            {
                _bitMask = bitMask;
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


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class BitMaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMask SetBit(this BitMask mask, int index)
        {
            var chunk = index / BitMask.ChunkCapacity;
            ArrayExtension.EnsureLength(ref mask.Chunks, chunk);
            var oldValue = mask.Chunks[chunk];
            var newValue = oldValue | (1UL << (index % BitMask.ChunkCapacity));
            if (oldValue == newValue) return mask;
            mask.Chunks[chunk] = newValue;
            mask.Count++;
            return mask;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMask ClearBit(this BitMask mask, int index)
        {
            var chunk = index / BitMask.ChunkCapacity;
            ArrayExtension.EnsureLength(ref mask.Chunks, chunk);
            var oldValue = mask.Chunks[chunk];
            var newValue = oldValue & ~(1UL << (index % BitMask.ChunkCapacity));
            if (oldValue == newValue) return mask;
            mask.Chunks[chunk] = newValue;
            mask.Count--;
            return mask;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetBit(this BitMask mask, int index)
        {
            var chunk = index / BitMask.ChunkCapacity;
            return chunk < mask.Chunks.Length && (mask.Chunks[chunk] & (1UL << (index % BitMask.ChunkCapacity))) != 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this BitMask mask, BitMask bitMask)
        {
            ArrayExtension.EnsureLength(ref bitMask.Chunks, mask.Chunks.Length);
            for (int i = 0, lenght = mask.Chunks.Length; i < lenght; i++)
            {
                if ((mask.Chunks[i] & bitMask.Chunks[i]) != bitMask.Chunks[i]) return false;
            }

            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Intersects(this BitMask mask, BitMask bitMask)
        {
            ArrayExtension.EnsureLength(ref bitMask.Chunks, mask.Chunks.Length);
            for (int i = 0, lenght = mask.Chunks.Length; i < lenght; i++)
            {
                if ((mask.Chunks[i] & bitMask.Chunks[i]) != 0) return true;
            }

            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear(this BitMask mask)
        {
            mask.Count = 0;
            for (int i = 0, lenght = mask.Chunks.Length; i < lenght; i++)
            {
                mask.Chunks[i] = 0;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Merge(this BitMask mask, BitMask include)
        {
            ArrayExtension.EnsureLength(ref include.Chunks, mask.Chunks.Length);
            for (int i = 0, lenght = mask.Chunks.Length; i < lenght; i++)
            {
                mask.Chunks[i] |= include.Chunks[i];
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMask Copy(this BitMask src)
        {
            var newMask = new BitMask();
            var newSize = src.Chunks.Length;
            newMask.Chunks = new ulong[newSize];
            for (var i = 0; i < newSize; i++)
            {
                newMask.Chunks[i] = src.Chunks[i];
            }

            newMask.Count = src.Count;
            return newMask;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHash(this BitMask mask)
        {
            ulong h = 1234;
            for (var i = mask.Chunks.Length - 1; i >= 0; i--)
            {
                h ^= ((ulong) i + 1) * mask.Chunks[i];
            }

            return (int) ((h >> 32) ^ h);
        }
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class HashMap<T>
    {
        private int[] _buckets;
        private T[] _data;
        private Entry[] _entries;

        private int _freeListIdx;
        private int _capacity;
        private int _lenght;
        private int _count;
        private T _empty;


        public int Count => _count;


        public HashMap(int capacity = 0)
        {
            _lenght = 0;
            _count = 0;
            _freeListIdx = -1;

            _capacity = HashHelpers.GetCapacity(capacity);
            _empty = default;
            _buckets = new int[_capacity];
            _data = new T[_capacity];
            _entries = new Entry[_capacity];

            _buckets.Fill(-1);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IndexFor(int key, int lenght) => key & (lenght - 1);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, T value)
        {
            var index = IndexFor(key, _capacity);

            for (var i = _buckets[index]; i != -1; i = _entries[i].Next)
            {
                if (_entries[i].Key != key) continue;
                _data[i] = value;
                return;
            }

            if (_lenght >= _capacity)
            {
                var newCapacity = HashHelpers.ExpandCapacity(_lenght);
                Array.Resize(ref _data, newCapacity);
                Array.Resize(ref _entries, newCapacity);
                var newBuckets = new int[newCapacity];
                newBuckets.Fill(-1);

                for (int i = 0, lenght = _lenght; i < lenght; i++)
                {
                    ref var rehashEntry = ref _entries[i];
                    var rehashIdx = IndexFor(rehashEntry.Key, newCapacity);
                    rehashEntry.Next = newBuckets[rehashIdx];
                    newBuckets[rehashIdx] = i;
                }

                _buckets = newBuckets;
                _capacity = newCapacity;

                index = IndexFor(key, _capacity);
            }

            var entryIdx = _lenght;

            if (_freeListIdx >= 0)
            {
                entryIdx = _freeListIdx;
                _freeListIdx = _entries[entryIdx].Next;
            }
            else _lenght++;

            ref var entry = ref _entries[entryIdx];
            entry.Next = _buckets[index];
            entry.Key = key;
            entry.IsActive = true;
            _data[entryIdx] = value;
            _buckets[index] = entryIdx;
            _count++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int key)
        {
            var index = IndexFor(key, _capacity);

            var priorEntry = -1;
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next)
            {
                ref var entry = ref _entries[i];
                if (entry.Key == key)
                {
                    if (priorEntry < 0) _buckets[index] = entry.Next;
                    else _entries[priorEntry].Next = entry.Next;
                    _data[i] = default;
                    entry.Key = -1;
                    entry.IsActive = false;
                    entry.Next = _freeListIdx;
                    _freeListIdx = i;
                    _count--;
                    return;
                }

                priorEntry = i;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int key)
        {
            var index = IndexFor(key, _capacity);
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next)
            {
                if (_entries[i].Key == key) return true;
            }

            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out T value)
        {
            var index = IndexFor(key, _capacity);
            value = default;
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next)
            {
                if (_entries[i].Key != key) continue;
                value = _data[i];
                return true;
            }

            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int key)
        {
            var index = IndexFor(key, _capacity);
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next)
            {
                if (_entries[i].Key != key) continue;
                return ref _data[i];
            }

            return ref _empty;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Clear(_entries, 0, _lenght);
            Array.Clear(_data, 0, _lenght);
            _buckets.Fill(-1);

            _lenght = 0;
            _count = 0;
            _freeListIdx = -1;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new Enumerator(this);


        public struct Enumerator : IDisposable
        {
            private HashMap<T> _hashMap;
            private int _current;
            private int _index;

            public Enumerator(HashMap<T> hashMap)
            {
                _hashMap = hashMap;
                _current = 0;
                _index = -1;
            }

            public ref T Current => ref _hashMap._data[_current];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (++_index < _hashMap._lenght)
                {
                    ref var entry = ref _hashMap._entries[_index];
                    if (!entry.IsActive) continue;
                    _current = _index;
                    return true;
                }

                return false;
            }

            public void Dispose()
            {
            }
        }

        private struct Entry
        {
            public bool IsActive;
            public int Next;
            public int Key;
        }
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class FastList<T>
    {
        private T[] _data;
        private const int MinCapacity = 16;
        private int _count;
        private EqualityComparer<T> _comparer;


        public FastList(int capacity = 0)
        {
            if (capacity < MinCapacity) capacity = MinCapacity;
            _data = new T[capacity];
            _count = 0;
            _comparer = EqualityComparer<T>.Default;
        }


        public FastList(EqualityComparer<T> comparer, int capacity = 0)
        {
            if (capacity < MinCapacity) capacity = MinCapacity;
            _data = new T[capacity];
            _count = 0;
            _comparer = comparer;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int index)
        {
#if DEBUG
            if (index >= _count || index < 0) throw new Exception($"|KECS| Index {index} out of bounds of array");
#endif
            return ref _data[index];
        }


        public int Count => _count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T value)
        {
            ArrayExtension.EnsureLength(ref _data, _count);
            _data[_count++] = value;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(T value) => RemoveAt(_data.IndexOf(value, _comparer));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveSwap(T value) => RemoveAtSwap(_data.IndexOf(value, _comparer));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAt(int index)
        {
#if DEBUG
            if (index >= _count || index < 0) throw new Exception($"|KECS| Index {index} out of bounds of array");
#endif
            if (index < --_count)
                Array.Copy(_data, index + 1, _data, index, _count - index);
            _data[_count] = default;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAtSwap(int index)
        {
#if DEBUG
            if (index >= _count || index < 0) throw new Exception($"|KECS| Index {index} out of bounds of array");
#endif
            _data[index] = _data[_count - 1];
            _data[_count - 1] = default;
            _count--;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Clear(_data, 0, _data.Length);
            _count = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new Enumerator(this);


        public struct Enumerator : IDisposable
        {
            private int _index;
            private FastList<T> _list;

            public Enumerator(FastList<T> list)
            {
                _list = list;
                _index = 0;
            }

            public ref T Current => ref _list._data[_index++];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => _index < _list.Count;

            public void Dispose()
            {
            }
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
        public static int IndexOf<T>(this T[] array, T value, EqualityComparer<T> comparer)
        {
            for (int i = 0, length = array.Length; i < length; ++i)
            {
                if (comparer.Equals(array[i], value)) return i;
            }

            return -1;
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

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal static class HashHelpers
    {
        private static readonly int[] capacities =
        {
            4,
            16,
            64,
            256,
            1024,
            4096,
            16384,
            65536,
            262144,
            1048576,
            4194304,
        };

        public static int ExpandCapacity(int oldSize)
        {
            var min = oldSize << 1;
            return min > 2146435069U && 2146435069 > oldSize ? 2146435069 : GetCapacity(min);
        }

        public static int GetCapacity(int min)
        {
            for (int index = 0, length = capacities.Length; index < length; ++index)
            {
                var prime = capacities[index];
                if (prime >= min)
                {
                    return prime;
                }
            }

            throw new Exception("Prime is too big");
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