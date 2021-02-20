using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace KECS
{
    public static class EcsTypeManager
    {
        internal static int ComponentTypesCount = 0;
    }
    
    public static class ComponentTypeInfo<T> where T : struct
    {
        public static readonly int TypeIndex;
        public static readonly Type Type;
        
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