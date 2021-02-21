using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class Archetype : IEnumerable<Entity>
    {
        public int Count => Entities.Count;
        public int Id { get; private set; }

        private DelayedChange[] _delayedChanges;
        private int _delayedOpsCount;

        public SparseSet<Entity> Entities;
        public BitMask Mask { get; }

        public SparseSet<Archetype> Next;
        public SparseSet<Archetype> Prior;

        private int _lockCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Archetype(World world, int id, BitMask mask)
        {
            Mask = mask;
            Id = id;
            _lockCount = 0;

            _delayedChanges = new DelayedChange[64];
            Next = new SparseSet<Archetype>(world.Config.ComponentsCapacity, world.Config.ComponentsCapacity);
            Prior = new SparseSet<Archetype>(world.Config.ComponentsCapacity, world.Config.ComponentsCapacity);

            Entities = new SparseSet<Entity>(world.Config.EntitiesCapacity, world.Config.EntitiesCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lock() => _lockCount++;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unlock()
        {
            _lockCount--;

            if (_lockCount == 0 && _delayedOpsCount > 0)
            {
                for (int i = 0; i < _delayedOpsCount; i++)
                {
                    ref var op = ref _delayedChanges[i];
                    if (op.isAdd)
                    {
                        AddEntity(op.entity);
                    }
                    else
                    {
                        RemoveEntity(op.entity);
                    }
                }
                _delayedOpsCount = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool AddDelayedChange(Entity entity, bool isAdd)
        {
            if (_lockCount <= 0)
            {
                return false;
            }

            ArrayExtension.EnsureLength(ref _delayedChanges, _delayedOpsCount);
            ref var op = ref _delayedChanges[_delayedOpsCount++];
            op.entity = entity;
            op.isAdd = isAdd;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEntity(Entity entity)
        {
            if (AddDelayedChange(entity, true))
            {
                return;
            }

            Entities.Add(entity.Id, entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(Entity entity)
        {
            if (AddDelayedChange(entity, false))
            {
                return;
            }

            Entities.Remove(entity.Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<Entity> GetEnumerator()
        {
            return Entities.GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        
        private struct DelayedChange
        {
            public bool isAdd;
            public Entity entity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Entities.Clear();
            Next.Clear();
            Prior.Clear();
            
            Entities = null;
            Next = null;
            Prior = null;
            _delayedChanges = null;
            _lockCount = 0;
            _delayedOpsCount = 0;
            Id = -1;
        }
    }
}