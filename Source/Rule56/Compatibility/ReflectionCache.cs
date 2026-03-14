using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace CombatAI.Compatibility
{
    internal static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<string, FieldInfo> FieldCache = new ConcurrentDictionary<string, FieldInfo>();
        private static readonly ConcurrentDictionary<string, PropertyInfo> PropertyCache = new ConcurrentDictionary<string, PropertyInfo>();
        private static readonly ConcurrentDictionary<string, MethodInfo> MethodCache = new ConcurrentDictionary<string, MethodInfo>();

        private static string Key(Type t, string name, BindingFlags flags) => t.FullName + ":" + name + ":" + ((int)flags).ToString();

        public static FieldInfo GetField(Type t, string name, BindingFlags flags)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            var k = Key(t, name, flags);
            return FieldCache.GetOrAdd(k, _ => t.GetField(name, flags));
        }

        public static PropertyInfo GetProperty(Type t, string name, BindingFlags flags)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            var k = Key(t, name, flags);
            return PropertyCache.GetOrAdd(k, _ => t.GetProperty(name, flags));
        }

        public static MethodInfo GetMethod(Type t, string name, BindingFlags flags, Type[] paramTypes = null)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            var sig = paramTypes == null ? "#" : string.Join(",", Array.ConvertAll(paramTypes, p => p?.FullName ?? "?"));
            var k = t.FullName + ":" + name + ":" + ((int)flags).ToString() + ":" + sig;
            return MethodCache.GetOrAdd(k, _ =>
            {
                try
                {
                    if (paramTypes == null) return t.GetMethod(name, flags);
                    return t.GetMethod(name, flags, null, paramTypes, null);
                }
                catch
                {
                    return null;
                }
            });
        }
    }
}
