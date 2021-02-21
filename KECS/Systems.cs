using System;
using System.Collections.Generic;
using System.Reflection;

namespace KECS
{
    public interface ISystem
    {
        World World { get; set; }
    }

    public interface IPreInitSystem : ISystem
    {
        void PreInit();
    }

    public interface IInitSystem : ISystem
    {
        void OnAwake();
    }

    public interface IPostDestroySystem : ISystem
    {
        void PostDestroy();
    }

    public interface IDestroySystem : ISystem
    {
        void Destroy();
    }

    public interface IUpdateSystem : ISystem
    {
        void OnUpdate(float dt);
    }
    
    public interface IFixedSystem : IUpdateSystem
    {
    }
    public interface ILateSystem : IUpdateSystem
    {
    }

    public sealed class Systems
    {
        private readonly List<ISystem> _allSystems = new List<ISystem>();
        private readonly List<UpdatableItem> _updateSystems = new List<UpdatableItem>();
        private readonly List<UpdatableItem> _fixedSystems = new List<UpdatableItem>();
        private readonly List<UpdatableItem> _lateSystems = new List<UpdatableItem>();
        private readonly World _world;

        private struct UpdatableItem
        {
            public IUpdateSystem Update;
            public bool IsEnable;
        }

        public Systems(World world)
        {
            _world = world;
        }

        public Systems Add(ISystem system)
        {
            _allSystems.Add(system);
            
            if (system is IUpdateSystem updateSystem)
            {
                _updateSystems.Add(new UpdatableItem(){Update = updateSystem, IsEnable = true});
            }
            if (system is IFixedSystem fixedSystem)
            {
                _fixedSystems.Add(new UpdatableItem(){Update = fixedSystem, IsEnable = true});
            }
            if (system is ILateSystem lateSystem)
            {
                _lateSystems.Add(new UpdatableItem(){Update = lateSystem, IsEnable = true});
            }

            system.World = _world;
            return this;
        }

        public Systems OneFrame<T>() where T : struct
        {
            return Add(new RemoveOneFrame<T>());
        }

        public void Awake()
        {
            for (int i = 0; i < _allSystems.Count; i++)
            {
                var system = _allSystems[i];
                if (system is IPreInitSystem preInitSystem)
                {
                    preInitSystem.PreInit();
                }
            }

            for (int i = 0; i < _allSystems.Count; i++)
            {
                var system = _allSystems[i];
                if (system is IInitSystem initSystem)
                {
                    initSystem.OnAwake();
                }
            }
        }

        public void Update(float dt)
        {
            for (int i = 0; i < _updateSystems.Count; i++)
            {
                var update = _updateSystems[i];
                if (update.IsEnable)
                {
                    update.Update.OnUpdate(dt);
                }
                
            }
        }
        
        public void FixedUpdate(float dt)
        {
            for (int i = 0; i < _fixedSystems.Count; i++)
            {
                var update = _fixedSystems[i];
                if (update.IsEnable)
                {
                    update.Update.OnUpdate(dt);
                }
            }
        }
        
        public void LateUpdate(float dt)
        {
            for (int i = 0; i < _fixedSystems.Count; i++)
            {
                var update = _lateSystems[i];
                if (update.IsEnable)
                {
                    update.Update.OnUpdate(dt);
                }
            }
        }

        public void Destroy()
        {
            for (var i = _allSystems.Count - 1; i >= 0; i--)
            {
                var system = _allSystems[i];
                if (system is IDestroySystem destroySystem)
                {
                    destroySystem.Destroy();
                }
            }

            for (var i = _allSystems.Count - 1; i >= 0; i--)
            {
                var system = _allSystems[i];
                if (system is IPostDestroySystem postDestroySystem)
                {
                    postDestroySystem.PostDestroy();
                }
            }
        }
    }

    internal sealed class RemoveOneFrame<T> : IUpdateSystem, IInitSystem where T : struct
    {
        private Filter _filter;
        public World World { get; set; }

        public void OnAwake()
        {
            _filter = World.Filter.With<T>();
        }

        public void OnUpdate(float dt)
        {
            foreach (var item in _filter)
            {
                item.RemoveComponent<T>();
            }
        }
    }
}