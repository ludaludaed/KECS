using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public struct BitMask
    {
        private const int ChunkCapacity = sizeof(ulong) * 8;
        private readonly ulong[] chunks;
        private int count;
        private readonly int capacity;

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask(int capacity = 0)
        {
            this.capacity = capacity;
            var newSize = capacity / ChunkCapacity;
            if (capacity % ChunkCapacity != 0)
            {
                newSize++;
            }

            count = 0;

            chunks = new ulong[newSize];
        }
        
        public BitMask(in BitMask copy)
        {
            this.capacity = copy.capacity;
            var newSize = capacity / ChunkCapacity;
            if (capacity % ChunkCapacity != 0)
            {
                newSize++;
            }

            count = 0;

            chunks = new ulong[newSize];

            foreach (var item in copy)
            {
                SetBit(item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldV = chunks[chunk];
            var newV = oldV | (1UL << (index % ChunkCapacity));
            if (oldV == newV) return;
            chunks[chunk] = newV;
            count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldV = chunks[chunk];
            var newV = oldV & ~(1UL << (index % ChunkCapacity));
            if (oldV == newV) return;
            chunks[chunk] = newV;
            count--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool GetBit(int idx)
        {
            return (chunks[idx / ChunkCapacity] & (1UL << (idx % ChunkCapacity))) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Contains(BitMask bitMask)
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                if ((chunks[i] & bitMask.chunks[i]) != bitMask.chunks[i])
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BitMask bitMask)
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                if ((chunks[i] & bitMask.chunks[i]) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                chunks[i] = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Merge(BitMask include)
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                chunks[i] |= include.chunks[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        public ref struct Enumerator
        {
            private readonly BitMask bitMask;
            private readonly int count;
            private int index;
            private int returned;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(BitMask bitMask)
            {
                this.bitMask = bitMask;
                count = bitMask.count;
                index = -1;
                returned = 0;
            }

            public int Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    while (true)
                    {
                        index++;
                        if (!bitMask.GetBit(index)) continue;
                        returned++;
                        return index;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                return returned < count;
            }
        }
    }
}