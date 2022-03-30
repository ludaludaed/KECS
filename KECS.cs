using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludaludaed.KECS {
    //==================================================================================================================
    // WORLDS
    //==================================================================================================================

    public struct WorldInfo {
        public int EntitiesCount;
        public int FreeEntitiesCount;
        public int ArchetypesCount;
        public int ComponentsCount;
    }

    public struct WorldConfig {
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
    public static class Worlds {
        private static readonly IntHashMap<World> _worlds;
        private static readonly object _lockObject;

        static Worlds() {
            _worlds = new IntHashMap<World>(32);
            _lockObject = new object();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Create(string name, WorldConfig config = default) {
            lock (_lockObject) {
                var hashName = name.GetHashCode();
#if DEBUG
                if (string.IsNullOrEmpty(name))
                    throw new Exception("|KECS| World name cant be null or empty.");
                if (_worlds.Contains(hashName))
                    throw new Exception($"|KECS| A world with {name} name already exists.");
#endif
                var newWorld = new World(CheckConfig(config), name);
                _worlds.Set(hashName, newWorld);
                return newWorld;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WorldConfig CheckConfig(WorldConfig config) {
            return new WorldConfig {
                Archetypes = config.Archetypes > 0 ? config.Archetypes : WorldConfig.DefaultArchetypes,
                Entities = config.Entities > 0 ? config.Entities : WorldConfig.DefaultEntities,
                Components = config.Components > 0 ? config.Components : WorldConfig.DefaultComponents,
                Queries = config.Queries > 0 ? config.Queries : WorldConfig.DefaultQueries,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static World Get(string name) {
            var hashName = name.GetHashCode();
            lock (_lockObject) {
#if DEBUG
                if (!_worlds.Contains(hashName))
                    throw new Exception($"|KECS| No world with {name} name was found.");
#endif
                return _worlds.Get(hashName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Remove(string name) {
            lock (_lockObject) {
                var hashName = name.GetHashCode();
                _worlds.Remove(hashName);
            }
        }
    }

    //==================================================================================================================
    // TASK POOLS
    //==================================================================================================================

    internal interface ITaskPool {
        void Execute();
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal class TaskPool<T> : ITaskPool where T : struct {
        private DelayedTask[] _addTasks;
        private DelayedTask[] _removeTasks;
        private int _addTasksCount;
        private int _removeTasksCount;

        internal TaskPool(int capacity) {
            _addTasks = new DelayedTask[capacity];
            _removeTasks = new DelayedTask[capacity];
            _addTasksCount = 0;
            _removeTasksCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Add(Entity entity, in T component) {
            ArrayExtension.EnsureLength(ref _addTasks, _addTasksCount);
            ArrayExtension.EnsureLength(ref _removeTasks, _addTasksCount);

            ref var task = ref _addTasks[_addTasksCount++];
            task.Entity = entity;
            task.Component = component;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute() {
            for (int i = 0, length = _removeTasksCount; i < length; i++) {
                _removeTasksCount = 0;
                ref var removeTask = ref _removeTasks[i];
                if (removeTask.Entity.IsAlive() && removeTask.Entity.Has<T>()) {
                    removeTask.Entity.Remove<T>();
                }
            }

            for (int i = 0, length = _addTasksCount; i < length; i++) {
                _addTasksCount = 0;
                ref var task = ref _addTasks[i];
                if (!task.Entity.IsAlive()) continue;
                task.Entity.Set(task.Component);
                ref var removeTask = ref _removeTasks[_removeTasksCount++];
                removeTask.Entity = task.Entity;
                removeTask.Component = task.Component;
            }
        }

        private struct DelayedTask {
            public T Component;
            public Entity Entity;
        }
    }

    //==================================================================================================================
    // WORLD
    //==================================================================================================================

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class World {
        private readonly HandleMap<IComponentPool> _componentPools;
        private readonly HandleMap<ITaskPool> _taskPools;

        private readonly IntHashMap<Archetype> _archetypeSignatures;
        internal readonly FastList<Archetype> Archetypes;

        private Query[] _queries;
        private int _queriesCount;

        private EntityData[] _entities;
        private int _entitiesLength;

        private int[] _freeEntityIds;
        private int _freeEntityCount;

        private readonly SparseSet _dirtyEntities;
        private int _lockCount;

        internal readonly WorldConfig Config;
        public readonly string Name;
        private bool _isAlive;

        internal World(WorldConfig config, string name) {
            _componentPools = new HandleMap<IComponentPool>(config.Components);
            _taskPools = new HandleMap<ITaskPool>(config.Components);
            _archetypeSignatures = new IntHashMap<Archetype>(config.Archetypes);
            Archetypes = new FastList<Archetype>(config.Archetypes);

            _entities = new EntityData[config.Entities];
            _freeEntityIds = new int[config.Entities];
            _dirtyEntities = new SparseSet(config.Entities);
            _queries = new Query[config.Queries];

            var emptyArch = new Archetype(new BitSet(config.Components), config.Entities);
            _archetypeSignatures.Set(emptyArch.Hash, emptyArch);
            Archetypes.Add(emptyArch);
            _isAlive = true;
            Config = config;
            Name = name;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive() {
            return _isAlive;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Lock() {
            _lockCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Unlock() {
            _lockCount--;
            if (_lockCount != 0 || _dirtyEntities.Count <= 0 || !_isAlive) return;

            Entity entity;
            entity.World = this;
            for (int i = 0, length = _dirtyEntities.Count; i < length; i++) {
                var entityId = _dirtyEntities[i];
                ref var entityData = ref _entities[entityId];
                entity.Id = entityId;
                entity.Age = entityData.Age;
                entity.UpdateArchetype();
            }

            _dirtyEntities.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool AddDelayedChange(in int entityId) {
            if (_lockCount <= 0) {
                return false;
            }

            if (!_dirtyEntities.Contains(entityId)) {
                _dirtyEntities.Set(entityId);
            }

            return true;
        }

#if DEBUG
        public readonly FastList<IWorldDebugListener> DebugListeners = new FastList<IWorldDebugListener>(16);

        public void AddDebugListener(IWorldDebugListener listener) {
            if (listener == null)
                throw new Exception("Listener is null.");
            DebugListeners.Add(listener);
        }

        public void RemoveDebugListener(IWorldDebugListener listener) {
            if (listener == null)
                throw new Exception("Listener is null.");
            DebugListeners.Remove(listener);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WorldInfo GetInfo() {
            return new WorldInfo() {
                EntitiesCount = _entitiesLength - _freeEntityCount,
                FreeEntitiesCount = _freeEntityCount,
                ArchetypesCount = Archetypes.Count,
                ComponentsCount = _componentPools.Count,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool EntityIsAlive(in Entity entity) {
            return entity.World == this && _isAlive && _entities[entity.Id].Age == entity.Age;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref EntityData GetEntityData(Entity entity) {
#if DEBUG
            if (entity.World != this)
                throw new Exception("|KECS| Invalid world.");
            if (!_isAlive)
                throw new Exception("|KECS| World already destroyed.");
            if (entity.Age != _entities[entity.Id].Age)
                throw new Exception($"|KECS| Entity {entity.Id} was destroyed.");
#endif
            return ref _entities[entity.Id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref EntityData GetEntityData(int id) {
#if DEBUG
            if (!_isAlive)
                throw new Exception("|KECS| World already destroyed.");
            if (id < 0 || id >= _entitiesLength)
                throw new Exception($"|KECS| Invalid entity ({id}).");
#endif
            return ref _entities[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity CreateEntity() {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {Name} was destroyed. You cannot create entity.");
#endif
            ref var emptyArchetype = ref Archetypes[0];
            Entity entity;
            entity.World = this;
            if (_freeEntityCount > 0) {
                var newEntityId = _freeEntityIds[--_freeEntityCount];
                ref var entityData = ref _entities[newEntityId];
                entity.Id = newEntityId;
                entityData.Signature.ClearAll();
                entityData.Archetype = emptyArchetype;
                entity.Age = entityData.Age;
            } else {
                var newEntityId = _entitiesLength++;
                ArrayExtension.EnsureLength(ref _entities, newEntityId);
                ref var entityData = ref _entities[newEntityId];
                entity.Id = newEntityId;
                entityData.Signature = new BitSet(Config.Components);
                entityData.Archetype = emptyArchetype;
                entity.Age = 1;
                entityData.Age = 1;
            }

            emptyArchetype.AddEntity(entity.Id);
#if DEBUG
            for (int i = 0, length = DebugListeners.Count; i < length; i++) {
                DebugListeners[i].OnEntityCreated(entity);
            }
#endif
            return entity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecycleEntity(in Entity entity) {
            ref var entityData = ref _entities[entity.Id];
            entityData.Archetype.RemoveEntity(entity.Id);
            entityData.Archetype = null;
            entityData.Age++;
            if (entityData.Age == 0) {
                entityData.Age = 1;
            }

            ArrayExtension.EnsureLength(ref _freeEntityIds, _freeEntityCount);
            _freeEntityIds[_freeEntityCount++] = entity.Id;
#if DEBUG
            for (int i = 0, length = DebugListeners.Count; i < length; i++) {
                DebugListeners[i].OnEntityDestroyed(entity);
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentPool<T> GetPool<T>() where T : struct {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {Name} was destroyed. You cannot get pool.");
#endif
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (!_componentPools.Contains(idx)) {
                var pool = new ComponentPool<T>(Config.Entities);
                _componentPools.Set(idx, pool);
            }

            return (ComponentPool<T>) _componentPools.Get(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IComponentPool GetPool(int idx) {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {Name} was destroyed. You cannot get pool.");
#endif
            return _componentPools.Get(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TaskPool<T> GetTaskPool<T>() where T : struct {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {Name} was destroyed. You cannot get pool.");
#endif
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (!_taskPools.Contains(idx)) {
                var pool = new TaskPool<T>(Config.Entities);
                _taskPools.Set(idx, pool);
            }

            return (TaskPool<T>) _taskPools.Get(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteTasks() {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {Name} destroyed");
#endif
            for (int i = 0, length = _taskPools.Count; i < length; i++) {
                _taskPools[i].Execute();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Query CreateQuery() {
            if (_queriesCount > 0) {
                return _queries[--_queriesCount];
            }

            return new Query(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecycleQuery(Query query) {
            ArrayExtension.EnsureLength(ref _queries, _queriesCount);
            _queries[_queriesCount++] = query;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype GetArchetype(BitSet signature) {
            var hash = signature.GetHashCode();

            if (!_archetypeSignatures.TryGetValue(hash, out var archetype)) {
                archetype = new Archetype(new BitSet(signature), Config.Entities);
                Archetypes.Add(archetype);
                _archetypeSignatures.Set(hash, archetype);
#if DEBUG
                for (int i = 0, length = DebugListeners.Count; i < length; i++) {
                    DebugListeners[i].OnArchetypeCreated(archetype);
                }
#endif
            }

            return archetype;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy() {
#if DEBUG
            if (!_isAlive)
                throw new Exception($"|KECS| World - {Name} already destroy");
#endif
            _lockCount = 0;
            _dirtyEntities.Clear();
            Entity entity;
            entity.World = this;
            for (int i = 0, length = _entities.Length; i < length; i++) {
                ref var entityData = ref _entities[i];
                if (entityData.Archetype == null) continue;
                entity.Id = i;
                entity.Age = entityData.Age;
                entity.Destroy();
            }

            _componentPools.Clear();
            Archetypes.Clear();
            _archetypeSignatures.Clear();
            _freeEntityCount = 0;
            _entitiesLength = 0;
            Worlds.Remove(Name);
            _isAlive = false;
#if DEBUG
            for (int i = 0, length = DebugListeners.Count; i < length; i++) {
                DebugListeners[i].OnWorldDestroyed(this);
            }
#endif
        }
    }

    //==================================================================================================================
    // ENTITY
    //==================================================================================================================

    public struct EntityData {
        public int Age;
        public BitSet Signature;
        public Archetype Archetype;
    }

    public struct Entity {
        public int Id;
        public int Age;
        public World World;
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public class EntityBuilder {
        private HandleMap<IComponentBuilder> _builders;

        public EntityBuilder() {
            _builders = new HandleMap<IComponentBuilder>(WorldConfig.DefaultComponents);
        }

        public void Clear() {
            _builders.Clear();
        }

        public EntityBuilder Append<T>(in T component = default) where T : struct {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            _builders.Set(idx, new ComponentBuilder<T>(component));
            return this;
        }

        public Entity Build(World world) {
            var entity = world.CreateEntity();
            for (int i = 0, length = _builders.Count; i < length; i++) {
                _builders[i].Set(entity);
            }

            entity.UpdateArchetype();
            return entity;
        }

        private interface IComponentBuilder {
            void Set(in Entity entity);
        }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
        private class ComponentBuilder<T> : IComponentBuilder where T : struct {
            private readonly T _component;

            internal ComponentBuilder(in T component) {
                _component = component;
            }

            public void Set(in Entity entity) {
                entity.SetFast(in _component);
            }
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class EntityExtensions {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAlive(in this Entity entity) {
            return entity.World != null && entity.World.EntityIsAlive(in entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEqual(in this Entity entityL, in Entity entityR) {
            return entityL.Id == entityR.Id && entityL.Age == entityR.Age && entityL.World == entityR.World;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Count(in this Entity entity) {
            return entity.World.GetEntityData(entity).Signature.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmpty(in this Entity entity) {
            return entity.Id == 0 && entity.Age == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFast<T>(in this Entity entity, in T value) where T : struct {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            world.GetPool<T>().Set(entity.Id, value);
            entityData.Signature.SetBit(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Set<T>(in this Entity entity, in T value) where T : struct {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);

            var pool = world.GetPool<T>();
            pool.Set(entity.Id, value);

            if (!entityData.Signature.GetBit(idx)) {
                entityData.Signature.SetBit(idx);
                entity.UpdateArchetype();
            }

            return ref pool.Get(entity.Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remove<T>(in this Entity entity) where T : struct {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var pool = world.GetPool<T>();
            if (entityData.Signature.GetBit(idx)) {
                entityData.Signature.ClearBit(idx);
                pool.Remove(entity.Id);
                entity.UpdateArchetype();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetEvent<T>(in this Entity entity, in T value) where T : struct {
            entity.World.GetTaskPool<T>().Add(entity, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Get<T>(in this Entity entity) where T : struct {
            var pool = entity.World.GetPool<T>();
            if (entity.Has<T>()) return ref pool.Get(entity.Id);
            return ref pool.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Has<T>(in this Entity entity) where T : struct {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            return entity.World.GetEntityData(entity).Signature.GetBit(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateArchetype(in this Entity entity) {
            var world = entity.World;
            if (world.AddDelayedChange(in entity.Id)) {
                return;
            }

            ref var entityData = ref world.GetEntityData(entity);

            if (entityData.Signature.Count == 0) {
                entity.World.RecycleEntity(in entity);
                return;
            }

            var oldArchetype = entityData.Archetype;
            var newArchetype = world.GetArchetype(entityData.Signature);
            oldArchetype.RemoveEntity(entity.Id);
            newArchetype.AddEntity(entity.Id);
            entityData.Archetype = newArchetype;
#if DEBUG
            var debugListeners = world.DebugListeners;
            for (int i = 0, length = debugListeners.Count; i < length; i++) {
                debugListeners[i].OnEntityChanged(entity);
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Destroy(in this Entity entity) {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            foreach (var idx in entityData.Signature) {
                world.GetPool(idx).Remove(entity.Id);
            }

            entityData.Signature.ClearAll();
            entity.UpdateArchetype();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetComponents(in this Entity entity, ref (int, object)[] typeIndexes) {
            var world = entity.World;
            ref var entityData = ref world.GetEntityData(entity);
            var signature = entityData.Signature;
            var length = signature.Count;

            if (typeIndexes == null || typeIndexes.Length < length) {
                typeIndexes = new (int, object)[length];
            }

            var counter = 0;
            foreach (var idx in signature) {
                typeIndexes[counter++] = (idx, world.GetPool(idx).GetObject(entity.Id));
            }

            return length;
        }
    }

    //==================================================================================================================
    // ARCHETYPES
    //==================================================================================================================

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Archetype {
        private readonly SparseSet _entities;
        public readonly BitSet Signature;
        public readonly int Hash;

        public int Count => _entities.Count;

        internal Archetype(BitSet signature, int entityCapacity) {
            _entities = new SparseSet(entityCapacity);
            Signature = signature;
            Hash = Signature.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddEntity(in int entityId) {
            _entities.Set(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveEntity(in int entityId) {
            _entities.Remove(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetEntityByIndex(in int index) {
            return _entities[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SparseSet.Enumerator GetEnumerator() {
            return _entities.GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            _entities.Clear();
            Signature.ClearAll();
        }
    }

    //==================================================================================================================
    // POOLS
    //==================================================================================================================

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class EcsTypeManager {
        internal static int ComponentTypesCount;
        internal static Type[] ComponentsTypes = new Type[WorldConfig.DefaultComponents];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetTypeByIndex(int idx) {
            return idx >= ComponentTypesCount ? default : ComponentsTypes[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetAllTypes(ref Type[] infos) {
            if (infos == null || infos.Length < ComponentTypesCount) {
                infos = new Type[ComponentTypesCount];
            }

            Array.Copy(ComponentsTypes, infos, ComponentTypesCount);
            return ComponentTypesCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type[] GetAllTypes() {
            var infos = new Type[ComponentTypesCount];
            Array.Copy(ComponentsTypes, infos, ComponentTypesCount);
            return infos;
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class ComponentTypeInfo<T> where T : struct {
        public static readonly int TypeIndex;
        public static readonly Type Type;
        private static readonly object _lockObject = new object();

        static ComponentTypeInfo() {
            lock (_lockObject) {
                TypeIndex = EcsTypeManager.ComponentTypesCount++;
                Type = typeof(T);
                ArrayExtension.EnsureLength(ref EcsTypeManager.ComponentsTypes, TypeIndex);
                EcsTypeManager.ComponentsTypes[TypeIndex] = Type;
            }
        }
    }

    internal interface IComponentPool {
        void Remove(int entityId);
        object GetObject(int entityId);
        void Clear();
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal sealed class ComponentPool<T> : IComponentPool where T : struct {
        private readonly HandleMap<T> _components;
        internal ref T Empty => ref _components.Empty;

        public ComponentPool(int capacity) {
            _components = new HandleMap<T>(capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityId) {
            return ref _components.Get(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetObject(int entityId) {
            return _components.Get(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int entityId) {
            _components.Remove(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityId, in T value) {
            _components.Set(entityId, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            _components.Clear();
        }
    }

    //==================================================================================================================
    // QUERY
    //==================================================================================================================

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

    public delegate void ForEachHandler<T, Y, U, I, O, P, A, S, D>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3, ref O comp4, ref P comp5, ref A comp6, ref S comp7, ref D comp8)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct
        where O : struct
        where P : struct
        where A : struct
        where S : struct
        where D : struct;

    public delegate void ForEachHandler<T, Y, U, I, O, P, A, S, D, F>(Entity entity, ref T comp0, ref Y comp1,
        ref U comp2, ref I comp3, ref O comp4, ref P comp5, ref A comp6, ref S comp7, ref D comp8, ref F comp9)
        where T : struct
        where Y : struct
        where U : struct
        where I : struct
        where O : struct
        where P : struct
        where A : struct
        where S : struct
        where D : struct
        where F : struct;

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Query {
        internal readonly BitSet Include;
        internal readonly BitSet Exclude;
        internal readonly World World;

        internal Query(World world) {
            World = world;
            Include = new BitSet(world.Config.Components);
            Exclude = new BitSet(world.Config.Components);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Query With<T>() where T : struct {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;
            if (!Exclude.GetBit(typeIdx)) {
                Include.SetBit(typeIdx);
            }
#if DEBUG
            if (Exclude.GetBit(typeIdx))
                throw new Exception($"|KECS| The component ({typeof(T).Name}) was excluded from the request.");
#endif
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Query Without<T>() where T : struct {
            var typeIdx = ComponentTypeInfo<T>.TypeIndex;
            if (!Include.GetBit(typeIdx)) {
                Exclude.SetBit(typeIdx);
            }
#if DEBUG
            if (Include.GetBit(typeIdx))
                throw new Exception($"|KECS| The component ({typeof(T).Name}) was included in the request.");
#endif
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() {
            var hashResult = Include.Count + Exclude.Count;
            foreach (var idx in Include) {
                hashResult = unchecked(hashResult * 31459 + idx);
            }

            foreach (var idx in Exclude) {
                hashResult = unchecked(hashResult * 31459 - idx);
            }

            return hashResult;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Recycle() {
            Include.ClearAll();
            Exclude.ClearAll();
            World.RecycleQuery(this);
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class QueryExtensions {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsMatch(this Query query, Archetype archetype) {
            return archetype.Count > 0 && archetype.Signature.Contains(query.Include) &&
                   (query.Exclude.Count == 0 || !archetype.Signature.Intersects(query.Exclude));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach(this Query query, ForEachHandler handler) {
            var world = query.World;

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity);
                }
            }

            world.Unlock();
            query.Recycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T>(this Query query,
            ForEachHandler<T> handler)
            where T : struct {
            var world = query.World;
            query.With<T>();

            var poolT = world.GetPool<T>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId));
                }
            }

            world.Unlock();
            query.Recycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y>(this Query query,
            ForEachHandler<T, Y> handler)
            where T : struct
            where Y : struct {
            var world = query.World;
            query.With<T>().With<Y>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId));
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
            where U : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId));
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
            where I : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId));
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
            where O : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId),
                        ref poolO.Get(entityId));
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
            where P : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId),
                        ref poolO.Get(entityId),
                        ref poolP.Get(entityId));
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
            where A : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>().With<A>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId),
                        ref poolO.Get(entityId),
                        ref poolP.Get(entityId),
                        ref poolA.Get(entityId));
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
            where S : struct {
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

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId),
                        ref poolO.Get(entityId),
                        ref poolP.Get(entityId),
                        ref poolA.Get(entityId),
                        ref poolS.Get(entityId));
                }
            }

            world.Unlock();
            query.Recycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P, A, S, D>(this Query query,
            ForEachHandler<T, Y, U, I, O, P, A, S, D> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
            where S : struct
            where D : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>().With<A>().With<S>().With<D>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();
            var poolS = world.GetPool<S>();
            var poolD = world.GetPool<D>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId),
                        ref poolO.Get(entityId),
                        ref poolP.Get(entityId),
                        ref poolA.Get(entityId),
                        ref poolS.Get(entityId),
                        ref poolD.Get(entityId));
                }
            }

            world.Unlock();
            query.Recycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T, Y, U, I, O, P, A, S, D, F>(this Query query,
            ForEachHandler<T, Y, U, I, O, P, A, S, D, F> handler)
            where T : struct
            where Y : struct
            where U : struct
            where I : struct
            where O : struct
            where P : struct
            where A : struct
            where S : struct
            where D : struct
            where F : struct {
            var world = query.World;
            query.With<T>().With<Y>().With<U>().With<I>().With<O>().With<P>().With<A>().With<S>().With<D>().With<F>();

            var poolT = world.GetPool<T>();
            var poolY = world.GetPool<Y>();
            var poolU = world.GetPool<U>();
            var poolI = world.GetPool<I>();
            var poolO = world.GetPool<O>();
            var poolP = world.GetPool<P>();
            var poolA = world.GetPool<A>();
            var poolS = world.GetPool<S>();
            var poolD = world.GetPool<D>();
            var poolF = world.GetPool<F>();

            Entity entity;
            entity.World = world;

            world.Lock();
            foreach (var archetype in world.Archetypes) {
                if (!query.IsMatch(archetype)) continue;
                foreach (var entityId in archetype) {
                    entity.Id = entityId;
                    entity.Age = world.GetEntityData(entityId).Age;
                    handler(entity,
                        ref poolT.Get(entityId),
                        ref poolY.Get(entityId),
                        ref poolU.Get(entityId),
                        ref poolI.Get(entityId),
                        ref poolO.Get(entityId),
                        ref poolP.Get(entityId),
                        ref poolA.Get(entityId),
                        ref poolS.Get(entityId),
                        ref poolD.Get(entityId),
                        ref poolF.Get(entityId));
                }
            }

            world.Unlock();
            query.Recycle();
        }
    }

    //==================================================================================================================
    // SYSTEMS
    //==================================================================================================================

    public abstract class UpdateSystem : SystemBase {
        public abstract void OnUpdate(float deltaTime);
    }

    public abstract class SystemBase {
        public World _world;
        public Systems _systems;
        public bool IsEnable;

        public virtual void Initialize() { }

        public virtual void OnDestroy() { }

        public virtual void PostDestroy() { }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class Systems : UpdateSystem {
        private readonly FastList<UpdateSystem> _updateSystems;
        private readonly FastList<SystemBase> _allSystems;
        private IntHashMap<object> _sharedData;
        public readonly string Name;

        public Systems(World world, string name = "DEFAULT") {
#if DEBUG
            if (string.IsNullOrEmpty(name))
                throw new Exception("|KECS| Systems name cant be null or empty.");
#endif
            _sharedData = new IntHashMap<object>();
            _allSystems = new FastList<SystemBase>();
            _updateSystems = new FastList<UpdateSystem>();
            _world = world;
            Name = name;
        }

#if DEBUG
        private bool _initialized;
        private bool _destroyed;
        private readonly FastList<ISystemsDebugListener> _debugListeners = new FastList<ISystemsDebugListener>();

        public void AddDebugListener(ISystemsDebugListener listener) {
            if (listener == null)
                throw new Exception("|KECS| Listener is null.");
            _debugListeners.Add(listener);
        }

        public void RemoveDebugListener(ISystemsDebugListener listener) {
            if (listener == null)
                throw new Exception("|KECS| Listener is null.");
            _debugListeners.Remove(listener);
        }
#endif

        public Systems AddShared<T>(T data) where T : class {
            var hash = typeof(T).GetHashCode();
#if DEBUG
            if (_initialized)
                throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed)
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            if (_sharedData.Contains(hash))
                throw new Exception($"|KECS| You have already added this type{typeof(T).Name} of data");
#endif
            _sharedData.Set(hash, data);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetShared<T>() where T : class {
            var hash = typeof(T).GetHashCode();
#if DEBUG
            if (!_initialized)
                throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed)
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            if (!_sharedData.Contains(hash))
                throw new Exception($"|KECS| No data of this type {typeof(T).Name} was found");
#endif
            return _sharedData.Get(hash) as T;
        }

        public FastList<SystemBase> GetSystems() {
            return _allSystems;
        }

        public Systems Add<T>(T systemValue) where T : SystemBase {
#if DEBUG
            if (_initialized)
                throw new Exception("|KECS| Systems haven't initialized yet.");
#endif
            _allSystems.Add(systemValue);

            systemValue._systems = this;
            systemValue.IsEnable = true;

            if (systemValue is UpdateSystem system) _updateSystems.Add(system);
            if (systemValue is Systems systemGroup) {
                systemGroup._sharedData = _sharedData;
            } else {
                systemValue._world = _world;
            }

            return this;
        }

        public override void Initialize() {
#if DEBUG
            if (_destroyed)
                throw new Exception("|KECS| The systems were destroyed. You cannot initialize them.");
            _initialized = true;
#endif
            for (int i = 0, length = _allSystems.Count; i < length; i++) {
                _allSystems[i].Initialize();
            }
        }

        public override void OnUpdate(float deltaTime) {
#if DEBUG
            if (!_initialized)
                throw new Exception("|KECS| Systems haven't initialized yet.");
            if (_destroyed)
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
#endif
            for (int i = 0, length = _updateSystems.Count; i < length; i++) {
                var update = _updateSystems[i];
                if (update.IsEnable) {
                    update.OnUpdate(deltaTime);
                }
            }
        }

        public override void OnDestroy() {
#if DEBUG
            if (_destroyed)
                throw new Exception("|KECS| The systems were destroyed. You cannot destroy them.");
            _destroyed = true;
#endif
            for (int i = 0, length = _allSystems.Count; i < length; i++) {
                var destroy = _allSystems[i];
                if (destroy.IsEnable) destroy.OnDestroy();
            }

            for (int i = 0, length = _allSystems.Count; i < length; i++) {
                var destroy = _allSystems[i];
                if (destroy.IsEnable) {
                    destroy.PostDestroy();
                }
            }

            _allSystems.Clear();
            _updateSystems.Clear();
            _sharedData.Clear();
#if DEBUG
            for (int i = 0, length = _debugListeners.Count; i < length; i++) {
                _debugListeners[i].OnSystemsDestroyed(this);
            }
#endif
        }
    }

    //==================================================================================================================
    // HELPER
    //==================================================================================================================

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class SparseSet {
        private const int None = -1;
        private int[] _sparse;
        private int[] _dense;
        private int _denseCount;

        public int Count => _denseCount;

        public SparseSet(int capacity) {
            capacity = Math.Pot(capacity);
            _dense = new int[capacity];
            _sparse = new int[capacity];
            _sparse.Fill(None);
            _denseCount = 0;
        }

        public int this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index >= _denseCount || index < 0)
                    throw new Exception($"|KECS| Index {index} out of bounds of dense array");
#endif
                return _dense[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int sparseIdx) {
            return sparseIdx < _sparse.Length && _sparse[sparseIdx] != None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int sparseIdx) {
            if (Contains(sparseIdx)) return;
            ArrayExtension.EnsureLength(ref _sparse, sparseIdx, None);
            ArrayExtension.EnsureLength(ref _dense, _denseCount);
            _sparse[sparseIdx] = _denseCount;
            _dense[_denseCount] = sparseIdx;
            _denseCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int sparseIdx) {
            if (!Contains(sparseIdx)) return;
            var packedIdx = _sparse[sparseIdx];
            _sparse[sparseIdx] = None;
            _denseCount--;
            if (packedIdx < _denseCount) {
                var lastSparseIdx = _dense[_denseCount];
                _dense[packedIdx] = lastSparseIdx;
                _sparse[lastSparseIdx] = packedIdx;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            _denseCount = 0;
            Array.Clear(_dense, 0, _dense.Length);
            _sparse.Fill(None);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }

        public struct Enumerator {
            private int _index;
            private readonly SparseSet _set;

            public Enumerator(SparseSet set) {
                _set = set;
                _index = 0;
            }

            public int Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _set._dense[_index++];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                return _index < _set.Count;
            }
        }
    }


#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class HandleMap<T> {
        private const int None = -1;
        private int[] _dense;
        private int[] _sparse;
        private T[] _data;
        private int _denseCount;

        private T _empty;
        public int Count => _denseCount;
        public ref T Empty => ref _empty;

        public HandleMap(int capacity) {
            capacity = Math.Pot(capacity);
            _dense = new int[capacity];
            _sparse = new int[capacity];
            _data = new T[capacity];
            _sparse.Fill(None);
            _denseCount = 0;
            _empty = default(T);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int sparseIdx) {
            return _denseCount > 0 && sparseIdx < _sparse.Length && _sparse[sparseIdx] != None;
        }

        public ref T this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index >= _denseCount || index < 0)
                    throw new Exception($"|KECS| Index {index} out of bounds of data array");
#endif
                return ref _data[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int sparseIdx) {
            if (Contains(sparseIdx)) {
                return ref _data[_sparse[sparseIdx]];
            }

            return ref Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int sparseIdx, T value) {
            if (Contains(sparseIdx)) {
                _data[_sparse[sparseIdx]] = value;
                return;
            }

            ArrayExtension.EnsureLength(ref _sparse, sparseIdx, None);
            ArrayExtension.EnsureLength(ref _dense, _denseCount);
            ArrayExtension.EnsureLength(ref _data, _denseCount);

            _sparse[sparseIdx] = _denseCount;
            _dense[_denseCount] = sparseIdx;
            _data[_denseCount] = value;
            _denseCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int sparseIdx) {
#if DEBUG
            if (!Contains(sparseIdx))
                throw new Exception($"|KECS| Unable to remove sparse idx {sparseIdx}: not present.");
#endif
            var packedIdx = _sparse[sparseIdx];
            _sparse[sparseIdx] = None;
            _denseCount--;

            if (packedIdx < _denseCount) {
                var lastSparseIdx = _dense[_denseCount];
                var lastValue = _data[_denseCount];

                _dense[packedIdx] = lastSparseIdx;
                _data[packedIdx] = lastValue;
                _sparse[lastSparseIdx] = packedIdx;
            }

            _data[_denseCount] = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            _denseCount = 0;
            Array.Clear(_data, 0, _data.Length);
            Array.Clear(_dense, 0, _dense.Length);
            _sparse.Fill(None);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }

        public struct Enumerator {
            private int _index;
            private HandleMap<T> _handleMap;

            public Enumerator(HandleMap<T> handleMap) {
                _handleMap = handleMap;
                _index = 0;
            }

            public ref T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _handleMap._data[_index++];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                return _index < _handleMap.Count;
            }
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class BitSet {
        private const int ChunkCapacity = sizeof(ulong) * 8;
        private ulong[] _chunks;
        private int _count;

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public BitSet(int capacity) {
            var newSize = capacity / ChunkCapacity;
            if (capacity % ChunkCapacity != 0) newSize++;
            _chunks = new ulong[newSize];
            _count = 0;
        }

        public BitSet(BitSet other) {
            var newSize = other._chunks.Length;
            _chunks = new ulong[newSize];
            for (var i = 0; i < newSize; i++) {
                _chunks[i] = other._chunks[i];
            }

            _count = other._count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index) {
            var chunk = index / BitSet.ChunkCapacity;
            ArrayExtension.EnsureLength(ref _chunks, chunk);
            var oldValue = _chunks[chunk];
            var newValue = oldValue | (1UL << (index % BitSet.ChunkCapacity));

            if (oldValue != newValue) {
                _chunks[chunk] = newValue;
                _count++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index) {
            var chunk = index / BitSet.ChunkCapacity;
            ArrayExtension.EnsureLength(ref _chunks, chunk);
            var oldValue = _chunks[chunk];
            var newValue = oldValue & ~(1UL << (index % BitSet.ChunkCapacity));

            if (oldValue != newValue) {
                _chunks[chunk] = newValue;
                _count--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetBit(int index) {
            var chunk = index / BitSet.ChunkCapacity;
            return chunk < _chunks.Length &&
                   (_chunks[chunk] & (1UL << (index % BitSet.ChunkCapacity))) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(BitSet other) {
            ArrayExtension.EnsureLength(ref other._chunks, _chunks.Length);
            for (int i = 0, length = _chunks.Length; i < length; i++) {
                if ((_chunks[i] & other._chunks[i]) != other._chunks[i]) {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BitSet other) {
            ArrayExtension.EnsureLength(ref other._chunks, _chunks.Length);
            for (int i = 0, length = _chunks.Length; i < length; i++) {
                if ((_chunks[i] & other._chunks[i]) != 0) {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAll() {
            for (int i = 0, length = _chunks.Length; i < length; i++) {
                _chunks[i] = ulong.MaxValue;
            }

            _count = _chunks.Length * ChunkCapacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll() {
            for (int i = 0, length = _chunks.Length; i < length; i++) {
                _chunks[i] = 0UL;
            }

            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Merge(BitSet include) {
            ArrayExtension.EnsureLength(ref include._chunks, _chunks.Length);
            for (int i = 0, length = _chunks.Length; i < length; i++) {
                _chunks[i] |= include._chunks[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() {
            ulong hashResult = 31459;
            for (var i = _chunks.Length - 1; i >= 0; i--) {
                var word = _chunks[i];
                if (word != 0UL) {
                    hashResult = unchecked(hashResult ^ ((ulong) i + 1) * word);
                }
            }

            return (int) ((hashResult >> 32) ^ hashResult);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(BitSet left, BitSet right) {
            if (left is null && right is null) {
                return true;
            }

            if (left is null || right is null) {
                return false;
            }

            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(BitSet left, BitSet right) {
            if (left is null && right is null) {
                return false;
            }

            if (left is null || right is null) {
                return true;
            }

            return !left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Equals(BitSet other) {
            if (other is null) {
                return false;
            }

            if (_count != other._count) {
                return false;
            }

            if (_chunks.Length >= other._chunks.Length) {
                for (int i = 0, length = other._chunks.Length; i < length; i++) {
                    var word = _chunks[i];
                    var otherWord = other._chunks[i];
                    if (word != otherWord) {
                        return false;
                    }
                }

                for (int i = other._chunks.Length, length = _chunks.Length; i < length; i++) {
                    var word = _chunks[i];
                    if (word != 0UL) {
                        return false;
                    }
                }

                return true;
            } else {
                for (int i = 0, length = _chunks.Length; i < length; i++) {
                    var word = _chunks[i];
                    var otherWord = other._chunks[i];
                    if (word != otherWord) {
                        return false;
                    }
                }

                for (int i = _chunks.Length, length = other._chunks.Length; i < length; i++) {
                    var word = other._chunks[i];
                    if (word != 0UL) {
                        return false;
                    }
                }

                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) {
            return obj is BitSet bitSet && Equals(bitSet);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }

        public ref struct Enumerator {
            private int _count;
            private BitSet _bitSet;
            private int _index;
            private int _returned;

            public Enumerator(BitSet bitSet) {
                _bitSet = bitSet;
                _count = bitSet._count;
                _index = -1;
                _returned = 0;
            }

            public int Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _index;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                while (_returned < _count) {
                    _index++;
                    if (_bitSet.GetBit(_index)) {
                        _returned++;
                        return true;
                    }
                }

                _bitSet = null;
                _count = 0;
                _index = -1;
                _returned = 0;
                return false;
            }
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class IntHashMap<T> {
        private Entry[] _entries;
        private int[] _buckets;
        private T[] _data;

        private int _freeListIdx;
        private int _capacity;
        private int _length;
        private int _count;
        private T _empty;

        public int Count => _count;

        private struct Entry {
            public int Next;
            public int Key;
        }

        public IntHashMap(int capacity = 0) {
            _length = 0;
            _count = 0;
            _freeListIdx = -1;

            _capacity = Math.Pot(capacity);
            _empty = default;
            _entries = new Entry[_capacity];
            _buckets = new int[_capacity];
            _data = new T[_capacity];

            _buckets.Fill(-1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int IndexFor(int key) {
            return key & (_capacity - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, T value) {
            var index = IndexFor(key);

            for (var i = _buckets[index]; i != -1; i = _entries[i].Next) {
                if (_entries[i].Key == key) {
                    _data[i] = value;
                    return;
                }
            }

            int entryIdx;

            if (_freeListIdx >= 0) {
                entryIdx = _freeListIdx;
                _freeListIdx = _entries[entryIdx].Next;
            } else {
                EnsureRehash();
                index = IndexFor(key);
                entryIdx = _length++;
            }

            ref var entry = ref _entries[entryIdx];
            entry.Next = _buckets[index];
            entry.Key = key;
            _data[entryIdx] = value;
            _buckets[index] = entryIdx;
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureRehash() {
            if (_length < _capacity) {
                return;
            }

            var newCapacity = Math.Pot(_capacity << 1);

            Array.Resize(ref _data, newCapacity);
            Array.Resize(ref _entries, newCapacity);

            var newBuckets = new int[newCapacity];
            newBuckets.Fill(-1);

            for (int i = 0, length = _length; i < length; i++) {
                ref var rehashEntry = ref _entries[i];
                var rehashIdx = IndexFor(rehashEntry.Key);

                rehashEntry.Next = newBuckets[rehashIdx];
                newBuckets[rehashIdx] = i;
            }

            _buckets = newBuckets;
            _capacity = newCapacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int key) {
            var index = IndexFor(key);

            var priorEntry = -1;
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next) {
                ref var entry = ref _entries[i];
                if (entry.Key == key) {
                    if (priorEntry < 0) {
                        _buckets[index] = entry.Next;
                    } else {
                        _entries[priorEntry].Next = entry.Next;
                    }

                    _data[i] = default;

                    entry.Key = -1;
                    entry.Next = _freeListIdx;

                    _freeListIdx = i;
                    _count--;
                    return;
                }

                priorEntry = i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int key) {
            var index = IndexFor(key);
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next) {
                if (_entries[i].Key == key) {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out T value) {
            var index = IndexFor(key);
            value = default;
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next) {
                if (_entries[i].Key == key) {
                    value = _data[i];
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int key) {
            var index = IndexFor(key);
            for (var i = _buckets[index]; i != -1; i = _entries[i].Next) {
                if (_entries[i].Key == key) {
                    return ref _data[i];
                }
            }

            return ref _empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            Array.Clear(_entries, 0, _length);
            Array.Clear(_data, 0, _length);
            _buckets.Fill(-1);

            _length = 0;
            _count = 0;
            _freeListIdx = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }

        public struct Enumerator {
            private IntHashMap<T> _intHashMap;
            private int _current;
            private int _index;

            public Enumerator(IntHashMap<T> intHashMap) {
                _intHashMap = intHashMap;
                _current = 0;
                _index = -1;
            }

            public ref T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _intHashMap._data[_current];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                while (++_index < _intHashMap._length) {
                    ref var entry = ref _intHashMap._entries[_index];
                    if (entry.Key >= 0) {
                        _current = _index;
                        return true;
                    }
                }

                _intHashMap = null;
                _current = 0;
                _index = -1;
                return false;
            }
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class FastList<T> {
        private T[] _data;
        private int _count;
        private readonly EqualityComparer<T> _comparer;

        public int Count => _count;

        public FastList(int capacity = 0) {
            capacity = Math.Pot(capacity);
            _data = new T[capacity];
            _count = 0;
            _comparer = EqualityComparer<T>.Default;
        }

        public FastList(EqualityComparer<T> comparer, int capacity = 0) : this(capacity) {
            _comparer = comparer;
        }

        public ref T this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index >= _count || index < 0)
                    throw new Exception($"|KECS| Index {index} out of bounds of array");
#endif
                return ref _data[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T value) {
            ArrayExtension.EnsureLength(ref _data, _count);
            _data[_count++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(T value) {
            RemoveAt(_data.IndexOf(value, _comparer));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveSwap(T value) {
            RemoveAtSwap(_data.IndexOf(value, _comparer));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAt(int index) {
#if DEBUG
            if (index >= _count || index < 0)
                throw new Exception($"|KECS| Index {index} out of bounds of array");
#endif
            if (index < --_count) {
                Array.Copy(_data, index + 1, _data, index, _count - index);
            }

            _data[_count] = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAtSwap(int index) {
#if DEBUG
            if (index >= _count || index < 0)
                throw new Exception($"|KECS| Index {index} out of bounds of array");
#endif
            _data[index] = _data[_count - 1];
            _data[_count - 1] = default;
            _count--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            Array.Clear(_data, 0, _data.Length);
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }

        public struct Enumerator {
            private int _index;
            private FastList<T> _list;

            public Enumerator(FastList<T> list) {
                _list = list;
                _index = 0;
            }

            public ref T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _list._data[_index++];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                return _index < _list.Count;
            }
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class ArrayExtension {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InnerEnsureLength<T>(ref T[] array, int index) {
            var newLength = System.Math.Max(1, array.Length);

            while (index >= newLength) {
                newLength <<= 1;
            }

            Array.Resize(ref array, newLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf<T>(this T[] array, T value, EqualityComparer<T> comparer) {
            for (int i = 0, length = array.Length; i < length; ++i) {
                if (comparer.Equals(array[i], value)) {
                    return i;
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fill<T>(this T[] array, in T value, int start = 0) {
            for (int i = start, length = array.Length; i < length; ++i) {
                array[i] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureLength<T>(ref T[] array, int index) {
            if (index >= array.Length) {
                InnerEnsureLength(ref array, index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureLength<T>(ref T[] array, int index, in T defaultValue) {
            if (index < array.Length) return;
            var oldLength = array.Length;
            InnerEnsureLength(ref array, index);
            array.Fill(defaultValue, oldLength);
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    internal static class Math {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Pot(int v) {
            if (v < 2) {
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

#if DEBUG
    public interface IWorldDebugListener {
        void OnEntityCreated(in Entity entity);
        void OnEntityChanged(in Entity entity);
        void OnEntityDestroyed(in Entity entity);
        void OnArchetypeCreated(Archetype archetype);
        void OnWorldDestroyed(World world);
    }

    public interface ISystemsDebugListener {
        void OnSystemsDestroyed(Systems systems);
    }
#endif
}

#if ENABLE_IL2CPP
namespace Unity.IL2CPP.CompilerServices {
    enum Option {
        NullChecks = 1,
        ArrayBoundsChecks = 2
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited =
        false, AllowMultiple = true)]
    class Il2CppSetOptionAttribute : Attribute {
        public Option Option { get; private set; }
        public object Value { get; private set; }

        public Il2CppSetOptionAttribute(Option option, object value) {
            Option = option;
            Value = value;
        }
    }
}
#endif