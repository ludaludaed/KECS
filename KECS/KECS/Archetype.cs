using System;
using System.Collections;
using System.Collections.Generic;

namespace KECS
{
    public class Archetype : IEnumerable<Entity>
    {
        public int Count => Entities.Count;
        public int Id { get; }

        public readonly SparseSet<Entity> Entities;
        public BitMask Mask { get; }

        public readonly SparseSet<Archetype> Next;
        public readonly SparseSet<Archetype> Prior;

        public Archetype(World world, int id, BitMask mask)
        {
            Mask = mask;
            Id = id;

            Next = new SparseSet<Archetype>(world.Config.ComponentsCapacity, world.Config.ComponentsCapacity);
            Prior = new SparseSet<Archetype>(world.Config.ComponentsCapacity, world.Config.ComponentsCapacity);

            Entities = new SparseSet<Entity>(world.Config.EntitiesCapacity, world.Config.EntitiesCapacity);
        }

        public void AddEntity(Entity entity)
        {
            Entities.Add(entity.Id, entity);
        }

        public void RemoveEntity(Entity entity)
        {
            Entities.Remove(entity.Id);
        }

        public IEnumerator<Entity> GetEnumerator()
        {
            return Entities.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}