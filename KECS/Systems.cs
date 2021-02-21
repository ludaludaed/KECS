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
        void Init();
    }

    public interface IPostDestroySystem : ISystem
    {
        void PostDestroy();
    }

    public interface IDestroySystem : ISystem
    {
        void Destroy();
    }

    public interface IRunSystem : ISystem
    {
        void Run(float dt);
    }

    public sealed class Systems
    {
        readonly List<ISystem> _allSystems = new List<ISystem>();
        readonly List<SystemsRunItem> _runSystems = new List<SystemsRunItem>();
        private World _world;


        public Systems(World world)
        {
            _world = world;
        }

        public Systems Add(ISystem system, string namedRunSystem = null)
        {
            _allSystems.Add(system);
            if (system is IRunSystem)
            {
                _runSystems.Add(new SystemsRunItem {Active = true, System = (IRunSystem) system});
            }

            system.World = _world;
            return this;
        }


        public void SetRunSystemState(int idx, bool state)
        {
            _runSystems[idx].Active = state;
        }

        public bool GetRunSystemState(int idx)
        {
            return _runSystems[idx].Active;
        }

        public List<ISystem> GetAllSystems()
        {
            return _allSystems;
        }

        public List<SystemsRunItem> GetRunSystems()
        {
            return _runSystems;
        }

        public Systems OneFrame<T>() where T : struct
        {
            return Add(new RemoveOneFrame<T>());
        }

        public void Init()
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
                    initSystem.Init();
                }
            }
        }

        public void Run(float dt)
        {
            for (int i = 0; i < _runSystems.Count; i++)
            {
                var runItem = _runSystems[i];
                if (runItem.Active)
                {
                    runItem.System.Run(dt);
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

    sealed class RemoveOneFrame<T> : IRunSystem, IInitSystem where T : struct
    {
        private Filter _filter;
        public World World { get; set; }

        public void Init()
        {
            _filter = World.Filter.With<T>();
        }

        public void Run(float dt)
        {
            foreach (var item in _filter)
            {
                item.RemoveComponent<T>();
            }
        }
    }

    public sealed class SystemsRunItem
    {
        public bool Active;
        public IRunSystem System;
    }
}