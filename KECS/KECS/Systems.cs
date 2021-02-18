using System;
using System.Collections.Generic;
using System.Reflection;

namespace KECS
{
    public interface ISystem
    {
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
        void Run();
    }

    public sealed class Systems : IInitSystem, IDestroySystem, IRunSystem
    {
        public readonly World World;
        readonly List<ISystem> _allSystems = new List<ISystem>();
        readonly List<SystemsRunItem> _runSystems = new List<SystemsRunItem>();


        public Systems(World world)
        {
            World = world;
        }
        
        public Systems Add(ISystem system, string namedRunSystem = null)
        {
            _allSystems.Add(system);
            if (system is IRunSystem)
            {
                _runSystems.Add(new SystemsRunItem {Active = true, System = (IRunSystem) system});
            }

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
        
        public void Run()
        {
            for (int i = 0; i < _runSystems.Count; i++)
            {
                var runItem = _runSystems[i];
                if (runItem.Active)
                {
                    runItem.System.Run();
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

    public sealed class SystemsRunItem
    {
        public bool Active;
        public IRunSystem System;
    }
}