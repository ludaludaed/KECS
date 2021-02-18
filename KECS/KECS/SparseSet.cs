using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public class SparseSet : IEnumerable
    {
        protected const int None = -1;
        protected int[] Dense;
        protected int DenseCount;
        protected int[] Sparse;

        public ref int this[int index]
        {
            get
            {
                if (index < DenseCount)
                {
                    return ref Dense[index];
                }

                throw new Exception($"Out of range SparseSet index {index}");
            }
        }

        public SparseSet(int denseCapacity, int sparseCapacity)
        {
            Dense = new int[denseCapacity];
            Sparse = new int[sparseCapacity];

            for (int i = 0; i < sparseCapacity; i++)
            {
                Sparse[i] = None;
            }

            DenseCount = 0;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => DenseCount;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Get(int sparseIdx)
        {
            ArrayExtension.EnsureLength(ref Sparse, sparseIdx, None);

            var packedIdx = Sparse[sparseIdx];
            if (packedIdx != None && packedIdx < DenseCount)
            {
                return packedIdx;
            }

            return None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int sparseIdx)
        {
            if (Get(sparseIdx) != None)
            {
                throw new Exception($"cant set sparse idx {sparseIdx}: already present.");
            }

            if (Dense.Length == DenseCount)
            {
                EnsurePackedCapacity(DenseCount << 1);
            }

            Sparse[sparseIdx] = DenseCount;
            Dense[DenseCount] = sparseIdx;
            DenseCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int sparseIdx)
        {
            if (Get(sparseIdx) == None)
            {
                throw new Exception($"Cant unset sparse idx {sparseIdx}: not present.");
            }

            var packedIdx = Sparse[sparseIdx];
            Sparse[sparseIdx] = None;
            DenseCount--;
            if (packedIdx < DenseCount)
            {
                var lastSparseIdx = Dense[DenseCount];
                Dense[packedIdx] = lastSparseIdx;
                Sparse[lastSparseIdx] = packedIdx;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int sparseIdx)
        {
            ArrayExtension.EnsureLength(ref Sparse, sparseIdx, None);
            return Sparse[sparseIdx] != None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureSparseCapacity(int capacity)
        {
            int start = Sparse.Length - 1;

            Array.Resize(ref Sparse, capacity);

            for (int i = start; i < Sparse.Length; ++i)
            {
                Sparse[i] = None;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void EnsurePackedCapacity(int capacity)
        {
            Array.Resize(ref Dense, capacity);
        }

        public IEnumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private struct Enumerator : IEnumerator
        {
            private int _count;
            private int _index;
            private SparseSet _sparseSet;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(SparseSet sparseSet)
            {
                _sparseSet = sparseSet;
                _count = sparseSet.Count;
                _index = 0;
                Current = default;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _count = 0;
                _index = 0;
                _sparseSet = null;
                Current = default;
            }

            object IEnumerator.Current => Current;
            public int Current { get; private set; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_index >= _count) return false;
                Current = _sparseSet.Dense[_index++];
                return true;
            }

            public void Dispose()
            {
            }
        }
    }


    public class SparseSet<T> : SparseSet, IEnumerable<T>
    {
        private T[] _instances;
        private T _empty;

        public SparseSet(int denseCapacity, int sparseCapacity) : base(denseCapacity, sparseCapacity)
        {
            _instances = new T[denseCapacity];
            _empty = default;
        }

        public new ref T this[int index]
        {
            get
            {
                if (index < DenseCount)
                {
                    return ref _instances[index];
                }

                throw new Exception($"Out of range SparseSet index {index}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetValue(int sparseIdx)
        {
            var packedIdx = Get(sparseIdx);
            return ref packedIdx != None ? ref _instances[packedIdx] : ref _empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Add(int sparseIdx)
        {
            Add(sparseIdx, _empty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int sparseIdx, T value)
        {
            if (Get(sparseIdx) != None)
            {
                throw new Exception($"cant set sparse idx {sparseIdx}: already present.");
            }

            if (Dense.Length == DenseCount)
            {
                EnsurePackedCapacity(DenseCount << 1);
            }

            Sparse[sparseIdx] = DenseCount;
            Dense[DenseCount] = sparseIdx;
            _instances[DenseCount] = value;
            DenseCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Remove(int sparseIdx)
        {
            if (Get(sparseIdx) == None)
            {
                throw new Exception($"Cant unset sparse idx {sparseIdx}: not present.");
            }

            var packedIdx = Sparse[sparseIdx];
            Sparse[sparseIdx] = None;
            DenseCount--;
            if (packedIdx < DenseCount)
            {
                var lastValue = _instances[DenseCount];
                var lastSparseIdx = Dense[DenseCount];
                Dense[packedIdx] = lastSparseIdx;
                _instances[packedIdx] = lastValue;
                Sparse[lastSparseIdx] = packedIdx;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EnsurePackedCapacity(int capacity)
        {
            base.EnsurePackedCapacity(capacity);
            Array.Resize(ref _instances, capacity);
        }

        public new IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private struct Enumerator : IEnumerator<T>
        {
            private int _count;
            private int _index;
            private SparseSet<T> _sparseSet;
            private T _current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(SparseSet<T> sparseSet)
            {
                _sparseSet = sparseSet;
                _count = sparseSet.Count;
                _index = 0;
                _current = default;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _count = 0;
                _index = 0;
                _sparseSet = null;
                _current = default;
            }

            object IEnumerator.Current => _current;
            public T Current => _current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_index < _count)
                {
                    _current = _sparseSet[_index++];
                    return true;
                }

                return false;
            }

            public void Dispose()
            {
            }
        }
    }
}