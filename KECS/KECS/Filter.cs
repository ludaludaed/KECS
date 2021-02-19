using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public sealed class Filter : IEnumerable<Entity>
    {
        public BitMask Include;
        public BitMask Exclude;
        public int Version { get; set; }

        private readonly List<Archetype> archetypes = new List<Archetype>();
        private readonly ArchetypeManager archetypeManager;
        private World world;

        public Filter(World world, ArchetypeManager archetypeManager)
        {
            this.archetypeManager = archetypeManager;
            Version = 0;
            this.world = world;

            Include = new BitMask(256);
            Exclude = new BitMask(256);
        }

        public Filter With<T>() where T : struct
        {
            int typeIdx = ComponentTypeInfo<T>.TypeIndex;

            if (Exclude.GetBit(typeIdx))
            {
                return this;
            }

            Include.SetBit(typeIdx);
            return this;
        }

        public Filter WithOut<T>() where T : struct
        {
            int typeIdx = ComponentTypeInfo<T>.TypeIndex;

            if (Include.GetBit(typeIdx))
            {
                return this;
            }

            Exclude.SetBit(typeIdx);
            return this;
        }

        public void AddArchetype(Archetype archetype)
        {
            archetypes.Add(archetype);
        }

        public IEnumerator<Entity> GetEnumerator()
        {
            archetypeManager.FindArchetypes(this, Version);
            return new EntityEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private struct EntityEnumerator : IEnumerator<Entity>
        {
            private readonly List<Archetype> archetypes;
            private readonly int archetypeCount;

            private int index;
            private int archetypeId;

            private Entity current;

            private SparseSet<Entity> archetypeEntities;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal EntityEnumerator(Filter filter)
            {
                archetypes = filter.archetypes;
                current = null;

                archetypeId = 0;
                archetypeCount = archetypes.Count;
                archetypeEntities = archetypeCount == 0 ? null : archetypes[0].Entities;
                index = archetypeEntities?.Count - 1 ?? 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (archetypeCount == 1)
                {
                    if (index >= 0)
                    {
                        current = archetypeEntities[index--];
                        return true;
                    }

                    return false;
                }

                if (archetypeId < archetypeCount)
                {
                    if (index >= 0)
                    {
                        current = archetypeEntities[index--];
                        return true;
                    }

                    while (++archetypeId < archetypeCount)
                    {
                        archetypeEntities = archetypes[archetypeId].Entities;
                        if (archetypeEntities.Count > 0)
                        {
                            index = archetypeEntities.Count - 1;
                            current = archetypeEntities[index--];
                            return true;
                        }
                    }
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                current = null;
                archetypeId = 0;
                archetypeEntities = archetypeCount == 0 ? null : archetypes[0].Entities;
                index = archetypeEntities?.Count - 1 ?? 0;
            }

            public Entity Current => current;

            object IEnumerator.Current => current;

            public void Dispose()
            {
            }
        }
    }
}