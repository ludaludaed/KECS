using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KECS
{
    public struct BitMask
    {
        private const int CHUNK_CAPACITY = sizeof(ulong) * 8;
        private ulong[] _chunks;
        private int _count;
        private int _capacity;

        public int Count => _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask(int capacity = 0)
        {
            _capacity = capacity;
            var newSize = capacity / CHUNK_CAPACITY;
            if (capacity % CHUNK_CAPACITY != 0)
            {
                newSize++;
            }

            _count = 0;

            _chunks = new ulong[newSize];
        }
        
        public BitMask(in BitMask copy)
        {
            int capacity = copy._capacity;
            _capacity = capacity;
            var newSize = capacity / CHUNK_CAPACITY;
            if (capacity % CHUNK_CAPACITY != 0)
            {
                newSize++;
            }

            _count = 0;

            _chunks = new ulong[newSize];

            foreach (var item in copy)
            {
                SetBit(item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index)
        {
            var chunk = index / CHUNK_CAPACITY;
            var oldV = _chunks[chunk];
            var newV = oldV | (1UL << (index % CHUNK_CAPACITY));
            if (oldV == newV) return;
            _chunks[chunk] = newV;
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
        {
            var chunk = index / CHUNK_CAPACITY;
            var oldV = _chunks[chunk];
            var newV = oldV & ~(1UL << (index % CHUNK_CAPACITY));
            if (oldV == newV) return;
            _chunks[chunk] = newV;
            _count--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool GetBit(int idx)
        {
            return (_chunks[idx / CHUNK_CAPACITY] & (1UL << (idx % CHUNK_CAPACITY))) != 0;
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
                _count = bitMask._count;
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