using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace KECS
{
    public static class EcsMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Pot(int v)
        {
            if (v < 2)
            {
                return 2;
            }

            var n = v - 1;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
        }
    }
    
    public static class ListExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveAtFast<T>(this IList<T> list, int index)
        {
            var count = list.Count;
            list[index] = list[count - 1];
            list.RemoveAt(count - 1);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveFast<T>(this IList<T> list, T item)
        {
            var count = list.Count;
            var index = list.IndexOf(item);
            list[index] = list[count - 1];
            list.RemoveAt(count - 1);
        }
    }

    internal static class ArrayExtension
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InnerEnsureLength<T>(ref T[] array, int index)
        {
            int newLength = Math.Max(1, array.Length);

            do
            {
                newLength *= 2;
            } while (index >= newLength);

            Array.Resize(ref array, newLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fill<T>(this T[] array, in T value, int start = 0)
        {
            for (int i = start; i < array.Length; ++i)
            {
                array[i] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureLength<T>(ref T[] array, int index)
        {
            if (index >= array.Length)
            {
                InnerEnsureLength(ref array, index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureLength<T>(ref T[] array, int index, in T defaultValue)
        {
            if (index >= array.Length)
            {
                int oldLength = array.Length;

                InnerEnsureLength(ref array, index);
                array.Fill(defaultValue, oldLength);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf<T>(T[] array, T value, EqualityComparer<T> comparer)
        {
            for (int i = 0, length = array.Length; i < length; ++i)
            {
                if (comparer.Equals(array[i], value))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal static class HashHelpers
    {
        //https://github.com/dotnet/runtime/blob/master/src/libraries/System.Private.CoreLib/src/System/Collections/HashHelpers.cs#L32
        // private static readonly int[] primes =
        // {
        //     3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
        //     1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
        //     17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
        //     187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
        //     1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        // };
        private static readonly int[] Primes = {3, 15, 63, 255, 1023, 4095, 16383, 65535, 262143, 1048575, 4194303};

        public const int MaxPrimeArrayLength = 0x7FEFFFFD;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;

            if ((uint) newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
            {
                return MaxPrimeArrayLength;
            }

            return GetPrime(newSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPrime(int min)
        {
            for (int index = 0, length = Primes.Length; index < length; ++index)
            {
                var prime = Primes[index];
                if (prime >= min)
                {
                    return prime;
                }
            }

            throw new Exception("Prime is too big");
        }
    }

    internal sealed class IntDispenser
    {
        private ConcurrentStack<int> _freeInts;
        private int _lastInt;
        public int LastInt => _lastInt;
        private readonly int _startInt;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntDispenser(int startInt = -1)
        {
            _freeInts = new ConcurrentStack<int>();
            _startInt = startInt;
            _lastInt = startInt;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetFreeInt()
        {
            if (!_freeInts.TryPop(out int freeInt))
            {
                freeInt = Interlocked.Increment(ref _lastInt);
            }

            return freeInt;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseInt(int releasedInt) => _freeInts.Push(releasedInt);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            _freeInts.Clear();
            _freeInts = null;
            _lastInt = _startInt;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _freeInts.Clear();
            _lastInt = _startInt;
        }
    }
}