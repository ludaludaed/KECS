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

        private List<Archetype> _archetypes = new List<Archetype>();
        private ArchetypeManager _archetypeManager;
        private World _world;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Filter(World world, ArchetypeManager archetypeManager)
        {
            _archetypeManager = archetypeManager;
            Version = 0;
            _world = world;
            Include = new BitMask(256);
            Exclude = new BitMask(256);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddArchetype(Archetype archetype)
        {
            _archetypes.Add(archetype);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<Entity> GetEnumerator()
        {
            _archetypeManager.FindArchetypes(this, Version);
            return new EntityEnumerator(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Version = 0;
            _archetypes.Clear();
            _archetypes = null;
            _world = null;
            _archetypeManager = null;
        }

        private struct EntityEnumerator : IEnumerator<Entity>
        {
            private readonly List<Archetype> _archetypes;
            private readonly int _archetypeCount;

            private int _index;
            private int _archetypeId;

            private SparseSet<Entity> _archetypeEntities;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal EntityEnumerator(Filter filter)
            {
                _archetypes = filter._archetypes;
                Current = null;

                _archetypeId = 0;
                _archetypeCount = _archetypes.Count;
                _archetypeEntities = _archetypeCount == 0 ? null : _archetypes[0].Entities;

                for (int i = 0; i < _archetypes.Count; i++)
                {
                    _archetypes[i].Lock();
                }

                _index = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_archetypeCount == 1)
                {
                    if (_index < _archetypes[_archetypeId].Count)
                    {
                        Current = _archetypeEntities[_index++];
                        return true;
                    }

                    return false;
                }

                if (_archetypeId < _archetypeCount)
                {
                    if (_index < _archetypes[_archetypeId].Count)
                    {
                        Current = _archetypeEntities[_index++];
                        return true;
                    }

                    while (++_archetypeId < _archetypeCount)
                    {
                        _archetypeEntities = _archetypes[_archetypeId].Entities;
                        if (_archetypeEntities.Count > 0)
                        {
                            _index = 0;
                            Current = _archetypeEntities[_index++];
                            return true;
                        }
                    }
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                Current = null;
                _archetypeId = 0;
                _archetypeEntities = _archetypeCount == 0 ? null : _archetypes[0].Entities;
                _index = 0;
            }

            public Entity Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                for (int i = 0; i < _archetypes.Count; i++)
                {
                    _archetypes[i].Unlock();
                }
            }
        }
    }
}