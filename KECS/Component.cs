using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace KECS
{
    internal static class EcsTypeManager
    {
        internal static int ComponentTypesCount = 0;
    }
    
    internal static class ComponentTypeInfo<T> where T : struct
    {
        internal static readonly int TypeIndex;
        internal static readonly Type Type;
        
        private static object _locker = new object();

        static ComponentTypeInfo()
        {
            lock (_locker)
            {
                TypeIndex = EcsTypeManager.ComponentTypesCount++;
                Type = typeof(T);
            }
        }
    }
}