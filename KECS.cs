using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
        public int Queries;
        public const int DefaultEntities = 1024;
        public const int DefaultArchetypes = 512;
        public const int DefaultComponents = 512;
        public const int DefaultQueries = 32;
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class Worlds
    {
        private static readonly HashMap<World> _worlds = new HashMap<World>(32);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Create(string name, WorldConfig config = default)
        {
            var hashName = name.GetHashCode();
#if DEBUG
            if (string.IsNullOrEmpty(name)) throw new Exception("|KECS| World name cant be null or empty.");
            if (_worlds.Contains(hashName))
                throw new Exception($"|KECS| A world with {name} name already exists.");
#endif
            var newWorld = new World(CheckConfig(config), name);
            _worlds.Set(hashName, newWorld);
            return newWorld;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WorldConfig CheckConfig(WorldConfig config)
        {
            return new WorldConfig
            {
                Archetypes = config.Archetypes > 0 ? config.Archetypes : WorldConfig.DefaultArchetypes,
                Entities = config.Entities > 0 ? config.Entities : WorldConfig.DefaultEntities,
                Components = config.Components > 0 ? config.Components : WorldConfig.DefaultComponents,
                Queries = config.Queries > 0 ? config.Queries : WorldConfig.DefaultQueries,
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(string name)
        {
            var hashName = name.GetHashCode();
            if (_worlds.TryGetValue(hashName, out var world)) return world;
            throw new Exception($"|KECS| No world with {name} name was found.");
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World GetByHash(int hash)
        {
            if (_worlds.TryGetValue(hash, out var world)) return world;
            throw new Exception($"|KECS| No world with {hash} hash was found.");
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Recycle(int hash)
        {
            _worlds.Remove(hash);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyAll()
        {
            foreach (var world in _worlds) world.Destroy();
            _worlds.Clear();
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
                if (!removeTask.Entity.IsAlive() || !removeTask.Entity.Has<T>()) continue;
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
        
        private readonly HashMap<Archetype> _archetypeSignatures;
        private readonly FastList<Archetype> _archetypes;

        private Query[] _queries;
        private int _queriesCount;

        private EntityData[] _entities;
        private int _entitiesLenght;
        
        private int[] _freeEntityIds;
        private int _freeEntityCount;
        
        private int[] _dirtyEntities;
        private int _dirtyCount;
        
        private readonly string _name;
        private readonly int _hashName;

        private int _lockCount;

        private bool _isAlive;
        internal readonly WorldConfig Config;

        public string Name => _name;

        public int HashName => _hashName;
        public bool IsAlive() => _isAlive;

        internal World(WorldConfig config, string name)
        {
            _componentPools = new HandleMap<IComponentPool>(config.Components);
            _taskPools = new HandleMap<ITaskPool>(config.Components);
            _archetypeSignatures = new HashMap<Archetype>(config.Archetypes);
            _archetypes = new FastList<Archetype>(config.Archetypes);
            
            _entities = new EntityData[config.Entities];
            _freeEntityIds = new int[config.Entities];
            _dirtyEntities = new int[config.Entities];
            _queries = new Query[config.Queries];
            
            var emptyArch = new Archetype(new BitMask(config.Components), config.Entities);
            _archetypeSignatures.Set(emptyArch.Hash, emptyArch);
            _archetypes.Add(emptyArch);
            _name = name;
            _hashName = name.GetHashCode();
            _isAlive = true;
            Config = config;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Lock() => _lockCount++;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Unlock()
        {
            _lockCount--;
            if (_lockCount != 0 || _dirtyCount <= 0 || !_isAlive) return;
            
            Entity entity;
            entity.World = this;
            for (int i = 0, lenght = _dirtyCount; i < lenght; i++)
            {
                var entityId = _dirtyEntities[i];
                ref var entityData = ref _entities[entityId];
                entityData.IsDirty = false;
                entity.Id = entityId;
                entity.Age = entityData.Age;
                entity.UpdateArchetype();
            }

            _dirtyCount = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool AddDelayedChange(in int entityId)
        {
            if (_lockCount <= 0) return false;
            ref var entityData = ref _entities[entityId];
            
            if (entityData.IsDirty) return true;
            
            entityData.IsDirty = true;
            ArrayExtension.EnsureLength(ref _dirtyEntities, _dirtyCount);
            _dirtyEntities[_dirtyCount++] = entityId;
            return true;
        }


#if DEBUG
        private readonly FastList<IWorldDebugListener> _debugListeners = new FastList<IWorldDebugListener>();

        public void AddDebugListener(IWorldDebugListener listener)
        {
            if (listener == null) throw new Exception("Listener is null.");
            _debugListeners.Add(listener);
        }

        public void RemoveDebugListener(IWorldDebugListener listener)
        {
            if (listener == null) throw new Exception("Listener is null.");
            _debugListeners.Remove(listener);
        }
#endif


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WorldInfo GetInfo()
        {
            return new WorldInfo()
            {
                EntitiesCount = _entitiesLenght - _freeEntityCount,
                FreeEntitiesCount = _freeEntityCount,
                ArchetypesCount = _archetypes.Count,
                ComponentsCount = _componentPools.Count,
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

            if (_freeEntityCount > 0)
            {
                var newEntityId = _freeEntityIds[--_freeEntityCount];
                ref var entityData = ref _entities[newEntityId];
                entity.Id = newEntityId;
                entityData.Signature.Clear();
                entityData.Archetype = emptyArchetype;
                entityData.IsDirty = false;
                entity.Age = entityData.Age;
                entity.World = this;
            }
            else
            {
                var newEntityId = _entitiesLenght++;
                ArrayExtension.EnsureLength(ref _entities, newEntityId);
                ref var entityData = ref _entities[newEntityId];
                entity.Id = newEntityId;
                entityData.Signature = new BitMask(Config.Components);
                entityData.Archetype = emptyArchetype;
                entityData.IsDirty = false;
                entity.Age = 1;
                entityData.Age = 1;
                entity.World = this;
            }

            emptyArchetype.AddEntity(entity);
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
            entityData.Archetype.RemoveEntity(entity);
            entityData.Archetype = null;
            entityData.IsDirty = false;
            entityData.Age++;
            if (entityData.Age == 0) entityData.Age = 1;

            ArrayExtension.EnsureLength(ref _freeEntityIds, _freeEntityCount);
            _freeEntityIds[_freeEntityCount++] = entity.Id;
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
        public Query CreateQuery()
        {
            if (_queriesCount > 0) return _queries[--_queriesCount];
            return new Query(this);
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecycleQuery(Query query)
        {
            ArrayExtension.EnsureLength(ref _queries, _queriesCount);
            _queries[_queriesCount++] = query;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void FindArchetypes(Query query)
        {
            var include = query.Include;
            var exclude = query.Exclude;

            for (int i = 0, lenght = _archetypes.Count; i < lenght; i++)
            {
                var archetype = _archetypes.Get(i);
                if (archetype.Signature.Contains(include) &&
                    (exclude.Count == 0 || !archetype.Signature.Intersects(exclude)))
                {
                    query.Archetypes.Add(archetype);
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype GetArchetype(BitMask signature)
        {
            var hash = signature.GetHash();
            if (_archetypeSignatures.TryGetValue(hash, out var archetype)) return archetype;
            archetype = new Archetype(signature.Copy(), Config.Entities);
            _archetypes.Add(archetype);
            _archetypeSignatures.Set(hash, archetype);
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
            _lockCount = 0;
            _dirtyCount = 0;
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
            
            _componentPools.Clear();
            _archetypes.Clear();
            _archetypeSignatures.Clear();
            _freeEntityCount = 0;
            _entitiesLenght = 0;
            Worlds.Recycle(_hashName);
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


    public struct EntityData
    {
        public int Age;
        public bool IsDirty;
        public BitMask Signature;
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
            ref var entityData = ref world.GetEntityData(entity);
            for (int i = 0, lenght = _builders.Count; i < lenght; i++)
            {
                var build = _builders.Data[i];
                build.Set(entity);
                entityData.Signature.SetBit(build.GetIdx());
            }

            entity.UpdateArchetype();
            return entity;
        }

        private interface IComponentBuilder
        {
            void Set(in Entity entity);
            int GetIdx();
        }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
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

            if (!entityData.Signature.GetBit(idx))
            {
                entityData.Signature.SetBit(idx);
                entity.UpdateArchetype();
            }

            return ref pool.Get(entity.Id);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remove<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var pool = world.GetPool<T>();
            if (!entityData.Signature.GetBit(idx)) return;
            entityData.Signature.ClearBit(idx);
            pool.Remove(entity.Id);
            entity.UpdateArchetype();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetEvent<T>(in this Entity entity, in T value = default) where T : struct
        {
            if (!entity.IsAlive()) return;
            entity.World.GetTaskPool<T>().Add(entity, value);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Get<T>(in this Entity entity) where T : struct
        {
            var pool = entity.World.GetPool<T>();
            if (entity.Has<T>()) return ref pool.Get(entity.Id);
            return ref pool.Empty;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Has<T>(in this Entity entity) where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            return entity.World.GetEntityData(entity).Signature.GetBit(idx);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void UpdateArchetype(in this Entity entity)
        {
            var world = entity.World;
            if (world.AddDelayedChange(in entity.Id)) return;
            ref var entityData = ref world.GetEntityData(entity);
            
            if (entityData.Signature.Count == 0)
            {
                entity.World.RecycleEntity(in entity);
                return;
            }

            var oldArchetype = entityData.Archetype;
            var newArchetype = world.GetArchetype(entityData.Signature);
            oldArchetype.RemoveEntity(entity);
            newArchetype.AddEntity(entity);
            entityData.Archetype = newArchetype;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Destroy(in this Entity entity)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            foreach (var idx in entityData.Signature)
            {
                world.GetPool(idx).Remove(entity.Id);
            }

            entityData.Signature.Clear();
            entity.UpdateArchetype();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetComponentsIndexes(in this Entity entity, ref int[] typeIndexes)
        {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var signature = entityData.Signature;
            var lenght = signature.Count;
            if (typeIndexes == null || typeIndexes.Length < lenght)
            {
                typeIndexes = new int[lenght];
            }

            var counter = 0;
            foreach (var idx in signature)
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
            var signature = entityData.Signature;
            var lenght = signature.Count;
            if (objects == null || objects.Length < lenght)
            {
                objects = new object[lenght];
            }

            var counter = 0;
            foreach (var idx in signature)
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
    public sealed class Archetype
    {
        internal readonly HandleMap<Entity> Entities;
        public readonly BitMask Signature;
        public readonly int Hash;

        public int Count => Entities.Count;

        internal Archetype(BitMask signature, int entityCapacity)
        {
            Entities = new HandleMap<Entity>(entityCapacity);
            Signature = signature;
            Hash = Signature.GetHash();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddEntity(in Entity entity) => Entities.Set(entity.Id, entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveEntity(in Entity entity) => Entities.Remove(entity.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HandleMap<Entity>.Enumerator GetEnumerator() => Entities.GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Entities.Clear();
            Signature.Clear();
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

    internal interface IComponentPool
    {
        void Remove(int entityId);
        object GetObject(int entityId);
        void Clear();
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
        public void Clear() => _components.Clear();
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
    public sealed class Query
    {
        internal readonly FastList<Archetype> Archetypes;
        internal readonly BitMask Include;
        internal readonly BitMask Exclude;
        internal readonly World World;
        

        internal Query(World world)
        {
            World = world;
            Include = new BitMask(world.Config.Components);
            Exclude = new BitMask(world.Config.Components);
            Archetypes = new FastList<Archetype>(world.Config.Archetypes);
        }

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Query With<T>() where T : struct
        {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;
            if (!Exclude.GetBit(typeIdx)) Include.SetBit(typeIdx);
#if DEBUG
            if (Exclude.GetBit(typeIdx))
                throw new Exception($"|KECS| The component ({typeof(T).Name}) was excluded from the request.");
#endif
            return this;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Query Without<T>() where T : struct
        {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;
            if (!Include.GetBit(typeIdx)) Exclude.SetBit(typeIdx);
#if DEBUG
            if (Include.GetBit(typeIdx))
                throw new Exception($"|KECS| The component ({typeof(T).Name}) was included in the request.");
#endif
            return this;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            var hashResult = Include.Count + Exclude.Count;
            foreach (var idx in Include)
                hashResult = unchecked(hashResult * 31459 + idx);
            foreach (var idx in Exclude)
                hashResult = unchecked(hashResult * 31459 - idx);
            return hashResult;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Recycle()
        {
            Include.Clear();
            Exclude.Clear();
            Archetypes.Clear();
            World.RecycleQuery(this);
        }
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class QueryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach(this Query query, ForEachHandler handler)
        {
            query.World.Lock();
            query.World.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            query.World.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T>(this Query query,
            ForEachHandler<T> handler)
            where T : struct
        {
            var world = query.World;
            query.With<T>();
            
            var poolT = world.GetPool<T>();
            
            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y>(this Query query,
            ForEachHandler<T, Y> handler)
            where T : struct
            where Y : struct
        {
            var world = query.World;
            query.With<T>().With<Y>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U>(this Query query,
            ForEachHandler<T, Y, U> handler)
            where T : struct
            where Y : struct
            where U : struct
        {
            var world = query.World;
            query.With<T>().With<Y>().With<U>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I>(this Query query,
            ForEachHandler<T, Y, U, I> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
        {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O>(this Query query,
            ForEachHandler<T, Y, U, I, O> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
        {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P>(this Query query,
            ForEachHandler<T, Y, U, I, O, P> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
        {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P, A>(this Query query,
            ForEachHandler<T, Y, U, I, O, P, A> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
        {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>().With<A>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P, A, S>(this Query query,
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
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>().With<A>().With<S>();
            
            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();
            var poolS = world.GetPool<S>();

            world.Lock();
            world.FindArchetypes(query);
            var archetypes = query.Archetypes;
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
            world.Unlock();
            query.Recycle();
        }
    }
    

    //=============================================================================
    // SYSTEMS
    //=============================================================================


    public abstract class UpdateSystem : SystemBase
    {
        public abstract void OnUpdate(float deltaTime);
    }

    public abstract class SystemBase
    {
        public World _world;
        public SystemGroup _systemGroup;
        internal bool IsEnable;


        public virtual void Initialize()
        {
        }


        public virtual void OnDestroy()
        {
        }


        public virtual void PostDestroy()
        {
        }
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class SystemGroup
    {
        private readonly FastList<UpdateSystem> _updateSystems;
        private readonly FastList<SystemBase> _allSystems;

        private readonly SharedData _sharedData;

        private readonly World _world;
        private readonly string _name;

        public string Name => _name;

        public SystemGroup(World world, string name = "DEFAULT")
        {
#if DEBUG
            if (string.IsNullOrEmpty(name)) throw new Exception("|KECS| Systems name cant be null or empty.");
#endif
            _allSystems = new FastList<SystemBase>();
            _updateSystems = new FastList<UpdateSystem>();
            _sharedData = new SharedData();
            _name = name;
            _world = world;
        }

#if DEBUG
        private bool _initialized;
        private bool _destroyed;
        private readonly FastList<ISystemsDebugListener> _debugListeners = new FastList<ISystemsDebugListener>();

        public void AddDebugListener(ISystemsDebugListener listener)
        {
            if (listener == null) throw new Exception("|KECS| Listener is null.");
            _debugListeners.Add(listener);
        }

        public void RemoveDebugListener(ISystemsDebugListener listener)
        {
            if (listener == null) throw new Exception("|KECS| Listener is null.");
            _debugListeners.Remove(listener);
        }
#endif

        public SystemGroup AddShared<T>(T data) where T : class
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

        public void SetActive(int idx, bool value) => _allSystems.Get(idx).IsEnable = value;
        public bool GetActive(int idx) => _allSystems.Get(idx).IsEnable;
        public FastList<SystemBase> GetSystems() => _allSystems;

        public SystemGroup Add<T>(T systemValue) where T : SystemBase, new()
        {
#if DEBUG
            if (_initialized) throw new Exception("|KECS| Systems haven't initialized yet.");
#endif
            _allSystems.Add(systemValue);
            systemValue._systemGroup = this;
            systemValue._world = _world;
            systemValue.IsEnable = true;

            if (!(systemValue is UpdateSystem system)) return this;
            _updateSystems.Add(system);
            return this;
        }


        public void Initialize()
        {
#if DEBUG
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot initialize them.");
            _initialized = true;
#endif
            for (int i = 0, lenght = _allSystems.Count; i < lenght; i++)
            {
                _allSystems.Get(i).Initialize();
            }
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
                if (update.IsEnable) update.OnUpdate(deltaTime);
            }
        }


        public void Destroy()
        {
#if DEBUG
            if (_destroyed) throw new Exception("|KECS| The systems were destroyed. You cannot destroy them.");
            _destroyed = true;
#endif
            for (int i = 0, lenght = _allSystems.Count; i < lenght; i++)
            {
                var destroy = _allSystems.Get(i);
                if (destroy.IsEnable) destroy.OnDestroy();
            }

            for (int i = 0, lenght = _allSystems.Count; i < lenght; i++)
            {
                var destroy = _allSystems.Get(i);
                if (destroy.IsEnable) destroy.PostDestroy();
            }

            _allSystems.Clear();
            _updateSystems.Clear();
            _sharedData.Dispose();
#if DEBUG
            for (int i = 0, lenght = _debugListeners.Count; i < lenght; i++)
            {
                _debugListeners.Get(i).OnSystemsDestroyed(this);
            }
#endif
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
        private const int MinCapacity = 16;
        private const int None = -1;
        private int[] _dense;
        private int[] _sparse;
        public T[] Data;
        private int _denseCount;

        private T _empty;
        public int Count => _denseCount;
        public ref T Empty => ref _empty;

        public HandleMap(int capacity)
        {
            if (capacity < MinCapacity) capacity = MinCapacity;
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
#if DEBUG
            if (!Contains(sparseIdx))
                throw new Exception($"|KECS| Unable to remove sparse idx {sparseIdx}: not present.");
#endif

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
        public Enumerator GetEnumerator() => new Enumerator(this);


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

            public int Current => _index;


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (_returned < _count)
                {
                    _index++;
                    if (!_bitMask.GetBit(_index)) continue;
                    _returned++;
                    return true;
                }

                return false;
            }
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
        private Entry[] _entries;
        private int[] _buckets;
        private T[] _data;

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
            _entries = new Entry[_capacity];
            _buckets = new int[_capacity];
            _data = new T[_capacity];

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
        private const int MinCapacity = 16;
        private T[] _data;
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
        void OnSystemsDestroyed(SystemGroup systemGroup);
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