using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class Entity
    {
        private readonly World world;
        private readonly ArchetypeManager archetypeManager;
        private Archetype currentArchetype;
        private Archetype previousArchetype;
        private bool isDisposed;
        public int Id { get; }

        public Entity(World world, ArchetypeManager archetypeManager, int internalId)
        {
            this.world = world;
            Id = internalId;
            this.archetypeManager = archetypeManager;
            currentArchetype = this.archetypeManager.Empty;
            currentArchetype.AddEntity(this);
        }

        public override string ToString()
        {
            return $"Entity_{Id}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddComponent<T>(T value = default) where T : struct
        {
            var pool = world.GetPool<T>();
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (!HasComponent<T>())
            {
                pool.Add(Id, value);
                AddTransfer(idx);
                return ref pool.Get(Id);
            }

            return ref pool.Empty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T SetComponent<T>(T value) where T : struct
        {
            var pool = world.GetPool<T>();
            var idx = ComponentTypeInfo<T>.TypeIndex;
            pool.Set(Id, value);

            if (!HasComponent<T>())
            {
                AddTransfer(idx);
            }

            return ref pool.Get(Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>() where T : struct
        {
            var idx = ComponentTypeInfo<T>.TypeIndex;
            if (HasComponent<T>())
            {
                RemoveTransfer(idx);
                world.GetPool<T>().Remove(Id);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>() where T : struct
        {
            var pool = world.GetPool<T>();

            if (HasComponent<T>())
            {
                return ref pool.Get(Id);
            }

            return ref pool.Empty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>() where T : struct
        {
            return currentArchetype.Mask.GetBit(ComponentTypeInfo<T>.TypeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddTransfer(int index)
        {
            previousArchetype = currentArchetype;

            var newArchetype = archetypeManager.FindOrCreateNextArchetype(currentArchetype, index);
            currentArchetype = newArchetype;

            previousArchetype.RemoveEntity(this);
            currentArchetype.AddEntity(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveTransfer(int index)
        {
            previousArchetype = currentArchetype;

            var newArchetype = archetypeManager.FindOrCreatePriorArchetype(currentArchetype, index);
            currentArchetype = newArchetype;

            previousArchetype.RemoveEntity(this);
            currentArchetype.AddEntity(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            foreach (var idx in currentArchetype.Mask)
            {
                world.GetPool(idx).Remove(Id);
            }

            currentArchetype.RemoveEntity(this);
            world.InternalEntityDestroy(Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Recycle()
        {
            isDisposed = false;
            previousArchetype = null;
            currentArchetype = archetypeManager.Empty;
            currentArchetype.AddEntity(this);
        }
    }
}