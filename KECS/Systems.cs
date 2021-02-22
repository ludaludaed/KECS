using System;
using System.Collections.Generic;
using System.Reflection;

namespace KECS
{
    public interface IUpdate
    {
        void Update(float deltaTime);
    }

    public interface IFixedUpdate : IUpdate
    {
    }

    public interface ILateUpdate : IUpdate
    {
    }

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

    internal sealed class SystemData
    {
        public bool IsEnable;
        public SystemBase Base;
        public IUpdate UpdateImpl;
    }

    public sealed class Systems : IDisposable
    {
        private readonly Dictionary<int, SystemData> _systems;

        private readonly List<SystemData> _updates;
        private readonly List<SystemData> _fixedUpdates;
        private readonly List<SystemData> _lateUpdates;
        private readonly List<SystemData> _allSystems;

        private readonly World _world;
        private bool _initialized;
        private bool _destroyed;

        public Systems(World world)
        {
            _world = world;
            _initialized = false;
            _destroyed = false;
            _systems = new Dictionary<int, SystemData>();
            _allSystems = new List<SystemData>();
            _updates = new List<SystemData>();
            _fixedUpdates = new List<SystemData>();
            _lateUpdates = new List<SystemData>();
        }

        public Systems AddSystem<T>() where T : SystemBase, new()
        {
            var obj = new T();
            return AddSystem(obj);
        }

        private Systems AddSystem<T>(T systemValue) where T : SystemBase
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
                    systemData.UpdateImpl = system;
                    var collection = _updates;

                    if (systemValue is IFixedUpdate fixedSystem)
                    {
                        systemData.UpdateImpl = fixedSystem;
                        collection = _fixedUpdates;
                    }

                    if (systemValue is ILateUpdate lateSystem)
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

        public Systems DisableSystem<T>() where T : SystemBase
        {
            int hash = typeof(T).GetHashCode();

            if (_systems.TryGetValue(hash, out var systemValue))
            {
                systemValue.IsEnable = false;
            }

            return this;
        }

        public Systems EnableSystem<T>() where T : SystemBase
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
            if (!_initialized)
            {
                throw new Exception("|KECS| Systems haven't initialized yet.");
            }

            if (_destroyed)
            {
                throw new Exception("|KECS| The systems were destroyed. You cannot update them.");
            }

            foreach (var update in _updates)
            {
                if (update.IsEnable)
                {
                    update.UpdateImpl?.Update(deltaTime);
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

            foreach (var fixedUpdate in _fixedUpdates)
            {
                if (fixedUpdate.IsEnable)
                {
                    fixedUpdate.UpdateImpl?.Update(deltaTime);
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

            foreach (var lateUpdate in _lateUpdates)
            {
                if (lateUpdate.IsEnable)
                {
                    lateUpdate.UpdateImpl?.Update(deltaTime);
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
            _updates.Clear();
            _fixedUpdates.Clear();
            _lateUpdates.Clear();
        }
    }

    internal class RemoveOneFrame<T> : SystemBase, ILateUpdate where T : struct
    {
        private Filter _filter;

        public override void Initialize()
        {
            _filter = world.Filter.With<T>();
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