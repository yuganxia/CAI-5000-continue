using System;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace CombatAI.Compatibility
{
    public static partial class CompatHelpers
    {
        public static void RegionwiseBFSWorker_NoOut(IntVec3 root, Map map, ThingRequest request, PathEndMode pe, TraverseParms tp, Predicate<Thing> validator, Func<Thing, float> func, int a, int b, int c)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType("Verse.GenClosest") ?? asm.GetType("GenClosest");
                    if (t == null) continue;
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    foreach (var m in methods)
                    {
                        if (m.Name != "RegionwiseBFSWorker") continue;
                        var parameters = m.GetParameters();
                        // need at least the first 10 params we supply + one out param slot
                        if (parameters.Length < 11) continue;
                        object[] args = new object[parameters.Length];
                        args[0] = root;
                        args[1] = map;
                        args[2] = request;
                        args[3] = pe;
                        args[4] = tp;
                        args[5] = validator;
                        args[6] = func;
                        args[7] = a;
                        args[8] = b;
                        args[9] = c;
                        // prepare out slot if present
                        if (parameters.Length > 10)
                        {
                            args[10] = 0;
                        }
                        // fill remaining with defaults
                        for (int i = 11; i < parameters.Length; i++)
                        {
                            args[i] = GetDefault(parameters[i].ParameterType);
                        }
                        m.Invoke(null, args);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"Compat RegionwiseBFSWorker failed: {e}");
            }
        }

        private static object GetDefault(Type t)
        {
            if (!t.IsValueType) return null;
            return Activator.CreateInstance(t);
        }

        public static void SetBoolField(object obj, string name, bool value)
        {
            if (obj == null) return;
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (p != null && p.PropertyType == typeof(bool)) { p.SetValue(obj, value); return; }
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (f != null && f.FieldType == typeof(bool)) { f.SetValue(obj, value); return; }
        }
    }
}
