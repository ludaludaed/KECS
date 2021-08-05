# 🧁KECS

KECS is a fast and easy C# Entity Component System framework for writing your own games.
> **Important!** KECS is still in development, so its use in projects is only at your own risk.

## Table of Contents

* [About ECS pattern](#about-ecs-pattern)
    * [World](#-world)
    * [Component](#-component)
    * [Entity](#-entity)
        * [Entity builder](#-entitybuilder)
    * [System](#-system)
        * [Events](#-events)
        * [Filter](#-filter)
        * [Data injection](#-data-injection)
    * [Systems](#-systems)
* [License](#-license)
* [Contacts](#-contacts)

## About ECS pattern

### 🌏 World

World is a container for all entities and components. The world is created with `Worlds.Create()`. The world can be set
up with its initial settings using the `WorldConfig` structure.

```csharp
var world = Worlds.Create(worldName,
        new WorldConfig()
        {
            ...
        });
```

The world can be retrieved using its name or id `Worlds.Get()`

```csharp
var world = Worlds.Get(worldName);
world = Worlds.Get(world.Id);
```

The world can be destroyed using the `Destroy ()` method of the `World` class.

```csharp
world.Destroy();
```

### 📦 Component

A component is a container only for users data. In KECS, component is only a value type.

```csharp
public struct MoveComponent 
{
    public Vector2 Direction;
    public float Speed;
}
```

### 🤖 Entity

An entity is a container for components. The entity has methods for adding, removing, and getting components.

```csharp
Entity entity = _world.CreateEntity();

ref var settedSpeedComponent  = ref entity.Set(new SpeedComponent);
ref var gottenSpeedComponent = ref entity.Get<SpeedComponent>();

entity.Remove<SpeedComponent>();

bool has = entity.Has<SpeedComponent>();

entity.Destroy ();
```

> **Important!** An entity without components will be automatically deleted.


#### 🚧 EntityBuilder

EntityBuilder allows you to create entities according to a template you define.

```csharp
var builder = new EntityBuilder();

builder.Append(new FooComponent())
    .Append(new BarComponent())
    .Append(new BazComponent());
    
var entity = builder.Build(World);
```
The `Append()` method allows you to add a component to the entity template and the `Remove<>()` method can remove component from template.
The `Build(world)` method allows you to create an entity from this template in the world.
> **NOTE:**
This way of creating an entity allows you to reduce the number of side archetypes at the initial sequential assignment of entity components.
### 🕹️ System

The system processes entities matching the filter. The system must implement the abstract class `SystemBase`.

```csharp
    public class SystemTest1 : SystemBase, IUpdate
    {
        private Filter _filter;

        public override void OnLaunch()
        {
            // Will be called when adding a system.
        }

        public override void Initialize()
        {
            _filter = _world.Filter().With<Component>();
        }

        public void OnUpdate(float deltaTime)
        {
            _filter.ForEach((Entity entity, ref Component comp) =>
            {
                comp.Counter++;
            });
        }

        public override void OnDestroy()
        {
            // Will be called when the system is destroyed.
        }

        public override void PostDestroy()
        {
            //Will be called after the system is destroyed.
        }
    }
```

#### 💡 Events

Emitting an event for an entity is set by the `entity.SetEvent<>()` method.

```csharp
public struct EventComponent
{
    ...
}
...
entity.SetEvent<EventComponent>();
```
Receiving an event.

```csharp
public class SystemTest1 : SystemBase, IUpdate
{
    private Filter _filter;
    
    public override void Initialize()
    {
        _filter = _world.Filter().With<EventComponent>();
    }

    public void OnUpdate(float deltaTime)
    {
        _filter.ForEach((Entity entity, ref EventComponent event) =>
        {
            ...
        });
    }
}
```
> **Important!** The event hangs on the entity on exactly one frame, so the event appears only on the next frame and deleted after it.

#### 🎰 Filter

You can create a filter using a chain of commands which contain two methods `With<>()` / `.Without<>()`.

```csharp
public class SystemTest1 : SystemBase, IUpdate
{
    private Filter _filter;
    
    public override void Initialize()
    {
        _filter = _world.Filter().With<FooComponent>().With<BarComponent>().Without<BazComponent>();
    }

    public void OnUpdate(float deltaTime)
    {
        _filter.ForEach((Entity entity, ref FooComponent fooComp, ref BarComponent barComp) =>
        {
            ...
        });
    }
}
```

#### 💉 Data injection

Adding shared data to systems is done by calling the `AddShared ()` method of the `Systems` class.

```csharp
public class SharedData
{
    //Some fields
}
...
var world = Worlds.Create();
var systems = new Systems(world);
systems.Add<SystemTest>().
        Add<SystemTest1>().
        AddShared(new SharedData());
systems.Initialize();
```

Shared data for the system is obtained by calling the `GetShared<>()` method.

```csharp
public class SystemTest1 : SystemBase, IUpdate
{
    private Filter _filter;
    private SharedData _configuration;

    public override void Initialize()
    {
        _filter = _world.Filter().With<Component>();
        _configuration = _systems.GetShared<SharedData>();
    }

    public void OnUpdate(float deltaTime)
    {
        _filter.ForEach((Entity entity, ref Component comp) =>
        {
            comp.Counter++;
        });
    }
}
```

### 🎮 Systems

Systems are added using the `Add<>()` method of the `Systems` class.
After adding all systems, you must call the `Intitalize()` method.

```csharp
public class StartUp : MonoBehaviour
{
    public World _world;
    public Systems _systems;

    public void Awake()
    {
        _world = Worlds.Create();
        _systems = new Systems(_world);
        _systems.Add<SystemTest>().
                 Add<SystemTest1>().
        _systems.Initialize();
    }

    public void Update()
    {
        _world.ExecuteTasks();
        _systems.Update(Time.deltaTime);
    }

    public void OnDestroy()
    {
        _systems.Destroy();
        _world.Destroy();
    }
}
```
> **Important!** After the systems are initialized, you cannot add new systems.

## 📘 License
📄 [MIT License](LICENSE)

## 💬 Contacts
Telegram: [ludaludaed](https://t.me/ludaludaed)



