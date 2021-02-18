using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class ArchetypeManager
    {
        private readonly List<Archetype> _archetypes;
        public Archetype Empty => _emptyArchetype;
        private readonly Archetype _emptyArchetype;
        private readonly World _owner;
        
        public ArchetypeManager(World world)
        {
            _owner = world;
            _emptyArchetype = new Archetype(_owner, new BitMask(256));
            _archetypes = new List<Archetype>() {_emptyArchetype};
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FindArchetypes(Filter filter, int startId)
        {
            for (int i = startId; i < _archetypes.Count; i++)
            {
                CheckArchetype(_archetypes[i], filter);
            }

            filter.Version = _archetypes.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckArchetype(Archetype archetype, Filter filter)
        {
            var include = filter.Include;
            var exclude = filter.Exclude;

            if (archetype.Mask.Contains(include) && (exclude.Count == 0 || !archetype.Mask.Contains(exclude)))
            {
                filter.Archetypes.Add(archetype);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Archetype InnerFindOrCreateArchetype(BitMask mask)
        {
            Archetype curArchetype = _emptyArchetype;
            var newMask = new BitMask(256);

            foreach (var index in mask)
            {
                newMask.SetBit(index);

                Archetype nextArchetype = curArchetype.Next.GetValue(index);

                if (nextArchetype == null)
                {
                    nextArchetype = new Archetype(_owner, newMask);

                    nextArchetype.Prior.Add(index, curArchetype);
                    curArchetype.Next.Add(index, nextArchetype);

                    _archetypes.Add(nextArchetype);
                }

                curArchetype = nextArchetype;
            }

            return curArchetype;
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