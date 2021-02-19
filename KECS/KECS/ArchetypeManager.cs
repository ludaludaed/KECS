using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class ArchetypeManager
    {
        private readonly List<Archetype> archetypes;
        public Archetype Empty => emptyArchetype;
        private readonly Archetype emptyArchetype;
        private readonly World world;
        private readonly object locker = new object();

        public ArchetypeManager(World world)
        {
            this.world = world;
            emptyArchetype = new Archetype(this.world, 0, new BitMask(256));
            archetypes = new List<Archetype>(world.Config.ArchetypeCapacity) {emptyArchetype};
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FindArchetypes(Filter filter, int startId)
        {
            for (int i = startId; i < archetypes.Count; i++)
            {
                CheckArchetype(archetypes[i], filter);
            }

            filter.Version = archetypes.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckArchetype(Archetype archetype, Filter filter)
        {
            var include = filter.Include;
            var exclude = filter.Exclude;

            if (archetype.Mask.Contains(include) && (exclude.Count == 0 || !archetype.Mask.Contains(exclude)))
            {
                filter.AddArchetype(archetype);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Archetype InnerFindOrCreateArchetype(BitMask mask)
        {
            lock (locker)
            {
                Archetype curArchetype = emptyArchetype;
                var newMask = new BitMask(256);

                foreach (var index in mask)
                {
                    newMask.SetBit(index);

                    Archetype nextArchetype = curArchetype.Next.GetValue(index);

                    if (nextArchetype == null)
                    {
                        nextArchetype = new Archetype(world, archetypes.Count, newMask);

                        nextArchetype.Prior.Add(index, curArchetype);
                        curArchetype.Next.Add(index, nextArchetype);

                        archetypes.Add(nextArchetype);
                    }

                    curArchetype = nextArchetype;
                }

                return curArchetype;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Archetype FindOrCreatePriorArchetype(Archetype archetype, int removeIndex)
        {
            Archetype priorArchetype = archetype.Prior.GetValue(removeIndex);
            if (priorArchetype != null)
                return priorArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.ClearBit(removeIndex);

            return InnerFindOrCreateArchetype(mask);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Archetype FindOrCreateNextArchetype(Archetype archetype, int addIndex)
        {
            Archetype nextArchetype = archetype.Next.GetValue(addIndex);
            if (nextArchetype != null)
                return nextArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.SetBit(addIndex);

            return InnerFindOrCreateArchetype(mask);
        }
    }
}