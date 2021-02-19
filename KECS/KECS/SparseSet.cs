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
            private int count;
            private int index;
            private SparseSet sparseSet;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(SparseSet sparseSet)
            {
                this.sparseSet = sparseSet;
                count = sparseSet.Count;
                index = 0;
                Current = default;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                count = 0;
                index = 0;
                sparseSet = null;
                Current = default;
            }

            object IEnumerator.Current => Current;
            public int Current { get; private set; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (index >= count) return false;
                Current = sparseSet.Dense[index++];
                return true;
            }

            public void Dispose()
            {
            }
        }
    }


    public class SparseSet<T> : SparseSet, IEnumerable<T>
    {
        private T[] instances;
        private T empty;

        public SparseSet(int denseCapacity, int sparseCapacity) : base(denseCapacity, sparseCapacity)
        {
            instances = new T[denseCapacity];
            empty = default;
        }

        public new ref T this[int index]
        {
            get
            {
                if (index < DenseCount)
                {
                    return ref instances[index];
                }

                throw new Exception($"Out of range SparseSet index {index}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetValue(int sparseIdx)
        {
            var packedIdx = Get(sparseIdx);
            return ref packedIdx != None ? ref instances[packedIdx] : ref empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Add(int sparseIdx)
        {
            Add(sparseIdx, empty);
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
            instances[DenseCount] = value;
            DenseCount++;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int sparseIdx, T value)
        {
            if (Get(sparseIdx) == None)
            {
                Add(sparseIdx,value);
                return;
            }
            
            if (Dense.Length == DenseCount)
            {
                EnsurePackedCapacity(DenseCount << 1);
            }
            
            instances[sparseIdx] = value;
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
                var lastValue = instances[DenseCount];
                var lastSparseIdx = Dense[DenseCount];
                Dense[packedIdx] = lastSparseIdx;
                instances[packedIdx] = lastValue;
                Sparse[lastSparseIdx] = packedIdx;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EnsurePackedCapacity(int capacity)
        {
            base.EnsurePackedCapacity(capacity);
            Array.Resize(ref instances, capacity);
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
            private int count;
            private int index;
            private SparseSet<T> sparseSet;
            private T current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(SparseSet<T> sparseSet)
            {
                this.sparseSet = sparseSet;
                count = sparseSet.Count;
                index = 0;
                current = default;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                count = 0;
                index = 0;
                sparseSet = null;
                current = default;
            }

            object IEnumerator.Current => current;
            public T Current => current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (index < count)
                {
                    current = sparseSet[index++];
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