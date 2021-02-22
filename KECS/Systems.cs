using System;
using System.Collections.Generic;
using System.Reflection;

namespace KECS
{
    public interface IInitializer
    {
        World World { get; set; }
        void Initialize();
    }

    public interface ISystem : IInitializer
    {
        void Update(float deltaTime);
    }

    public interface IFixedSystem : ISystem
    {
    }

    public interface ILateSystem : ISystem
    {
    }

    public sealed class Systems
    {
        private readonly Dictionary<int, SystemData> _systems;

        private readonly List<SystemData> _updates;
        private readonly List<SystemData> _fixedUpdates;
        private readonly List<SystemData> _lateUpdates;

        private readonly List<IInitializer> _initializers;

        private readonly World _world;
        private bool _initialized;

        public Systems(World world)
        {
            _world = world;
            _initialized = false;
            _systems = new Dictionary<int, SystemData>();
            _initializers = new List<IInitializer>();
            _updates = new List<SystemData>();
            _fixedUpdates = new List<SystemData>();
            _lateUpdates = new List<SystemData>();
        }

        public Systems AddSystem<T>() where T : IInitializer, new()
        {
            var obj = new T();
            return AddSystem(obj);
        }

        private Systems AddSystem<T>(T systemValue) where T : IInitializer
        {
            if (_initialized)
            {
                throw new Exception("|KECS| System cannot be added after initialization.");
            }

            int hash = typeof(T).GetHashCode();

            if (!_systems.ContainsKey(hash))
            {
                _initializers.Add(systemValue);
                systemValue.World = _world;
                if (systemValue is ISystem system)
                {
                    var systemData = new SystemData {IsEnable = true, UpdateImpl = system};
                    var collection = _updates;

                    if (systemValue is IFixedSystem fixedSystem)
                    {
                        systemData.UpdateImpl = fixedSystem;
                        collection = _fixedUpdates;
                    }

                    if (systemValue is ILateSystem lateSystem)
                    {
                        systemData.UpdateImpl = lateSystem;
                        collection = _lateUpdates;
                    }

                    collection.Add(systemData);
                    _systems.Add(hash, systemData);
                }
            }

            return this;
        }

        public Systems DisableSystem<T>() where T : ISystem
        {
            int hash = typeof(T).GetHashCode();

            if (_systems.TryGetValue(hash, out var systemValue))
            {
                systemValue.IsEnable = false;
            }

            return this;
        }

        public Systems EnableSystem<T>() where T : ISystem
        {
            int hash = typeof(T).GetHashCode();

            if (_systems.TryGetValue(hash, out var systemValue))
            {
                systemValue.IsEnable = true;
            }

            return this;
        }

        public Systems OneFrame<T>() where T : struct
        {
            return AddSystem(new RemoveOneFrame<T>());
        }

        public void Update(float deltaTime)
        {
            foreach (var update in _updates)
            {
                if (update.IsEnable)
                {
                    update.UpdateImpl.Update(deltaTime);
                }
            }
        }

        public void FixedUpdate(float deltaTime)
        {
            foreach (var fixedUpdate in _fixedUpdates)
            {
                if (fixedUpdate.IsEnable)
                {
                    fixedUpdate.UpdateImpl.Update(deltaTime);
                }
            }
        }

        public void LateUpdate(float deltaTime)
        {
            foreach (var lateUpdate in _lateUpdates)
            {
                if (lateUpdate.IsEnable)
                {
                    lateUpdate.UpdateImpl.Update(deltaTime);
                }
            }
        }

        public void Initialize()
        {
            _initialized = true;
            foreach (var initializer in _initializers)
            {
                initializer.Initialize();
            }
        }

        private class SystemData
        {
            public bool IsEnable;
            public ISystem UpdateImpl;
        }
    }

    internal class RemoveOneFrame<T> : ISystem where T : struct
    {
        public World World { get; set; }

        private Filter _filter;

        public void Initialize()
        {
            _filter = World.Filter.With<T>();
        }

        public void Update(float deltaTime)
        {
            foreach (var ent in _filter)
            {
                ent.RemoveComponent<T>();
            }
        }
    }
}