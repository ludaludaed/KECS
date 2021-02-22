using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    internal class ArchetypeManager
    {
        private List<Archetype> _archetypes;
        internal Archetype Empty { get; private set; }
        private World _world;
        private object _locker = new object();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ArchetypeManager(World world)
        {
            _world = world;
            Empty = new Archetype(this._world, 0, new BitMask(256));
            _archetypes = new List<Archetype>(world.Config.ArchetypeCapacity) {Empty};
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void FindArchetypes(Filter filter, int startId)
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
                filter.AddArchetype(archetype);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Archetype InnerFindOrCreateArchetype(BitMask mask)
        {
            lock (_locker)
            {
                Archetype curArchetype = Empty;
                var newMask = new BitMask(256);

                foreach (var index in mask)
                {
                    newMask.SetBit(index);

                    Archetype nextArchetype = curArchetype.Next.GetValue(index);

                    if (nextArchetype == null)
                    {
                        nextArchetype = new Archetype(_world, _archetypes.Count, newMask);

                        nextArchetype.Prior.Add(index, curArchetype);
                        curArchetype.Next.Add(index, nextArchetype);

                        _archetypes.Add(nextArchetype);
                    }

                    curArchetype = nextArchetype;
                }

                return curArchetype;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreatePriorArchetype(Archetype archetype, int removeIndex)
        {
            Archetype priorArchetype = archetype.Prior.GetValue(removeIndex);
            if (priorArchetype != null)
                return priorArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.ClearBit(removeIndex);

            return InnerFindOrCreateArchetype(mask);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype FindOrCreateNextArchetype(Archetype archetype, int addIndex)
        {
            Archetype nextArchetype = archetype.Next.GetValue(addIndex);
            if (nextArchetype != null)
                return nextArchetype;

            var mask = new BitMask(archetype.Mask);
            mask.SetBit(addIndex);

            return InnerFindOrCreateArchetype(mask);
        }

        internal void Dispose()
        {
            for (int i = 0; i < _archetypes.Count; i++)
            {
                _archetypes[i].Dispose();
            }
            _archetypes.Clear();
            _archetypes = null;
            _world = null;
            _locker = null;
            Empty = null;
        }
    }
}