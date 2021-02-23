using System;
using System.Collections.Generic;

namespace KECS
{
    public interface IRunSystem
    {
        void Run(float deltaTime);
    }

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

    internal sealed class SystemData
    {
        public bool IsEnable;
        public SystemBase Base;
        public IRunSystem runSystemImpl;
    }

    public sealed class Systems : IDisposable
    {
        private readonly Dictionary<int, SystemData> _systems;

        private readonly List<SystemData> _runSystems;
        private readonly List<SystemData> _allSystems;

        private readonly World _world;
        private bool _initialized;
        private bool _destroyed;

        public string Name { get; private set; }

        public Systems(World world, string name = "DEFAULT_SYSTEMS")
        {
            Name = name;
            _world = world;
            _initialized = false;
            _destroyed = false;
            _systems = new Dictionary<int, SystemData>();
            _allSystems = new List<SystemData>();
            _runSystems = new List<SystemData>();
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

                if (systemValue is IRunSystem system)
                {
                    systemData.runSystemImpl = system;
                    _runSystems.Add(systemData);
                    _systems.Add(hash, systemData);
                }
            }

            return this;
        }

        public Systems Disable<T>() where T : SystemBase
        {
            int hash = typeof(T).GetHashCode();

            if (_systems.TryGetValue(hash, out var systemValue))
            {
                systemValue.IsEnable = false;
            }

            return this;
        }

        public Systems Enable<T>() where T : SystemBase
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
            return Add(new RemoveOneFrame<T>());
        }

        public void Run(float deltaTime)
        {
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            foreach (var update in _runSystems)
            {
                if (update.IsEnable)
                {
                    update.runSystemImpl?.Run(deltaTime);
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
        }

        public void Dispose()
        {
            _systems.Clear();
            _allSystems.Clear();
            _runSystems.Clear();
        }
    }

    internal class RemoveOneFrame<T> : SystemBase, IRunSystem where T : struct
    {
        private Filter _filter;

        public override void Initialize()
        {
            _filter = World.Filter.With<T>();
        }

        public void Run(float deltaTime)
        {
            foreach (var ent in _filter)
            {
                ent.RemoveComponent<T>();
            }
        }
    }
}