using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace CombatAI.Compatibility
{
    internal static class PawnPathCompat
    {
        private static readonly FieldInfo nodesField = typeof(PawnPath).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo curNodeIndexField = typeof(PawnPath).GetField("curNodeIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static IList<IntVec3> GetNodes(this PawnPath path)
        {
            if (path == null) return null;
            if (nodesField != null)
            {
                var val = nodesField.GetValue(path);
                if (val is IList<IntVec3> list) return list;
                if (val is IntVec3[] arr) return arr;
            }
            var prop = typeof(PawnPath).GetProperty("Nodes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                var val = prop.GetValue(path);
                if (val is IList<IntVec3> list2) return list2;
            }
            return null;
        }

        public static int GetCurNodeIndex(this PawnPath path)
        {
            if (path == null) return -1;
            if (curNodeIndexField != null)
            {
                var val = curNodeIndexField.GetValue(path);
                if (val is int i) return i;
            }
            var prop = typeof(PawnPath).GetProperty("CurNodeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                var val = prop.GetValue(path);
                if (val is int i2) return i2;
            }
            return -1;
        }
    }
}
