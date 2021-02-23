using System.Runtime.CompilerServices;

namespace KECS
{
    public struct BitMask
    {
        private const int ChunkCapacity = sizeof(ulong) * 8;
        private readonly ulong[] _chunks;
        private readonly int _capacity;

        public int Count { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask(int capacity = 0)
        {
            this._capacity = capacity;
            var newSize = capacity / ChunkCapacity;
            if (capacity % ChunkCapacity != 0)
            {
                newSize++;
            }
            Count = 0;
            _chunks = new ulong[newSize];
        }
        
        public BitMask(in BitMask copy)
        {
            this._capacity = copy._capacity;
            var newSize = _capacity / ChunkCapacity;
            if (_capacity % ChunkCapacity != 0)
            {
                newSize++;
            }

            Count = 0;

            _chunks = new ulong[newSize];

            foreach (var item in copy)
            {
                SetBit(item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldV = _chunks[chunk];
            var newV = oldV | (1UL << (index % ChunkCapacity));
            if (oldV == newV) return;
            _chunks[chunk] = newV;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
        {
            var chunk = index / ChunkCapacity;
            var oldV = _chunks[chunk];
            var newV = oldV & ~(1UL << (index % ChunkCapacity));
            if (oldV == newV) return;
            _chunks[chunk] = newV;
            Count--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool GetBit(int idx)
        {
            return (_chunks[idx / ChunkCapacity] & (1UL << (idx % ChunkCapacity))) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Contains(BitMask bitMask)
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                if ((_chunks[i] & bitMask._chunks[i]) != bitMask._chunks[i])
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BitMask bitMask)
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                if ((_chunks[i] & bitMask._chunks[i]) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                _chunks[i] = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Merge(BitMask include)
        {
            for (var i = 0; i < _chunks.Length; i++)
            {
                _chunks[i] |= include._chunks[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        public ref struct Enumerator
        {
            private readonly BitMask _bitMask;
            private readonly int _count;
            private int _index;
            private int _returned;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(BitMask bitMask)
            {
                this._bitMask = bitMask;
                _count = bitMask.Count;
                _index = -1;
                _returned = 0;
            }

            public int Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    while (true)
                    {
                        _index++;
                        if (!_bitMask.GetBit(_index)) continue;
                        _returned++;
                        return _index;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                return _returned < _count;
            }
        }
    }
}