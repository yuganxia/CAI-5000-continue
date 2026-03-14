using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace CombatAI.Compatibility
{
    internal static class ReflectionHelpers
    {
        public static FieldInfo Field(Type t, string name, BindingFlags flags) => ReflectionCache.GetField(t, name, flags);
        public static PropertyInfo Property(Type t, string name, BindingFlags flags) => ReflectionCache.GetProperty(t, name, flags);
        public static MethodInfo Method(Type t, string name, BindingFlags flags, Type[] paramTypes = null) => ReflectionCache.GetMethod(t, name, flags, paramTypes);
    }

    public static class ThingWithCompsCompat
    {
        public static IList<ThingComp> GetComps(ThingWithComps thing)
        {
            if (thing == null) return null;
            Type t = thing.GetType();
            // try common members in order
            FieldInfo f = ReflectionHelpers.Field(t, "comps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(thing) as IList<ThingComp>;
                if (v != null) return v;
                var arr = f.GetValue(thing) as ThingComp[];
                if (arr != null) return arr.ToList();
            }
            PropertyInfo p = ReflectionHelpers.Property(t, "AllComps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(thing) as IList<ThingComp>;
                if (v != null) return v;
            }
            FieldInfo f2 = ReflectionHelpers.Field(t, "compsByType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f2 != null)
            {
                var dict = f2.GetValue(thing) as System.Collections.IDictionary;
                if (dict != null)
                {
                    List<ThingComp> outList = new List<ThingComp>();
                    foreach (var value in dict.Values)
                    {
                        if (value is ThingComp[] arr2) outList.AddRange(arr2);
                        else if (value is IEnumerable<ThingComp> ie) outList.AddRange(ie);
                    }
                    return outList;
                }
            }
            return null;
        }

        public static T GetComp<T>(ThingWithComps thing) where T : ThingComp
        {
            var comps = GetComps(thing);
            if (comps == null) return null;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is T t) return t;
            }
            return null;
        }
    }

    public static class ThinkNodeCompat
    {
        public static ThinkTreeDef GetTreeDef(ThinkNode_Subtree subtree)
        {
            if (subtree == null) return null;
            var t = subtree.GetType();
            var p = t.GetProperty("treeDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (p != null)
            {
                var v = p.GetValue(subtree) as ThinkTreeDef;
                if (v != null) return v;
            }
            var f = t.GetField("treeDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (f != null)
            {
                var v = f.GetValue(subtree) as ThinkTreeDef;
                if (v != null) return v;
            }
            var p2 = t.GetProperty("TreeDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p2 != null)
            {
                var v = p2.GetValue(subtree) as ThinkTreeDef;
                if (v != null) return v;
            }
            return null;
        }
    }

    public static class PawnMeleeVerbsCompat
    {
        public static Verb GetCurMeleeVerb(object meleeVerbs)
        {
            if (meleeVerbs == null) return null;
            var t = meleeVerbs.GetType();
            var p = t.GetProperty("curMeleeVerb", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (p != null)
            {
                var v = p.GetValue(meleeVerbs) as Verb;
                if (v != null) return v;
            }
            var p2 = t.GetProperty("CurMeleeVerb", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p2 != null)
            {
                var v = p2.GetValue(meleeVerbs) as Verb;
                if (v != null) return v;
            }
            var f = t.GetField("curMeleeVerb", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (f != null)
            {
                var v = f.GetValue(meleeVerbs) as Verb;
                if (v != null) return v;
            }
            return null;
        }
    }

    public static class ListerThingsCompat
    {
        public static bool TryGetThingsByDef(ListerThings lister, ThingDef def, out List<Thing> outList)
        {
            outList = null;
            if (lister == null || def == null) return false;
            var t = lister.GetType();
            var f = t.GetField("listsByDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var dict = f.GetValue(lister) as System.Collections.IDictionary;
                if (dict != null && dict.Contains(def))
                {
                    var v = dict[def] as List<Thing>;
                    if (v != null) { outList = v; return true; }
                }
            }
            var m = t.GetMethod("ThingsOfDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                try
                {
                    var res = m.Invoke(lister, new object[] { def }) as System.Collections.IEnumerable;
                    if (res != null) { outList = res.Cast<Thing>().ToList(); return true; }
                }
                catch { }
            }
            return false;
        }
    }

    public static class CameraDriverCompat
    {
        public static float GetRootPosY(object cameraDriver)
        {
            if (cameraDriver == null) return 30f;
            var t = cameraDriver.GetType();
            var p = t.GetProperty("rootPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(cameraDriver);
                if (v is UnityEngine.Vector3 vec) return vec.y;
            }
            var f = t.GetField("rootPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(cameraDriver);
                if (v is UnityEngine.Vector3 vec2) return vec2.y;
            }
            return 30f;
        }
    }

    public static class GlowGridCompat
    {
        public static ColorInt[] GetGlowGrid(object glowGrid)
        {
            if (glowGrid == null) return null;
            var t = glowGrid.GetType();
            var f = t.GetField("glowGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(glowGrid) as ColorInt[];
                if (v != null) return v;
            }
            var p = t.GetProperty("GlowGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(glowGrid) as ColorInt[];
                if (v != null) return v;
            }
            return null;
        }
    }

    public static class WindowStackCompat
    {
        public static IEnumerable<Window> Windows(WindowStack ws)
        {
            if (ws == null) return Enumerable.Empty<Window>();
            var t = ws.GetType();
            var f = ReflectionHelpers.Field(t, "windows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                if (f.GetValue(ws) is IEnumerable<Window> e) return e;
                if (f.GetValue(ws) is System.Collections.IEnumerable ie) return ie.Cast<Window>();
            }
            var p = ReflectionHelpers.Property(t, "Windows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                if (p.GetValue(ws) is IEnumerable<Window> e2) return e2;
            }
            return Enumerable.Empty<Window>();
        }
    }

    public static class WidgetsCompat
    {
        public static Color MenuSectionBGBorderColor
        {
            get
            {
                var t = typeof(Widgets);
                var f = t.GetField("MenuSectionBGBorderColor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.GetValue(null) is Color c) return c;
                var p = t.GetProperty("MenuSectionBGBorderColor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.GetValue(null) is Color c2) return c2;
                return Color.black;
            }
        }
    }

    public static class RegionGridCompat
    {
        public static Region RegionAt(RegionGrid rg, int index)
        {
            if (rg == null) return null;
            var t = rg.GetType();
            var f = ReflectionHelpers.Field(t, "regionGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var arr = f.GetValue(rg) as Array;
                if (arr != null && index >= 0 && index < arr.Length) return arr.GetValue(index) as Region;
            }
            var p = ReflectionHelpers.Property(t, "Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                try
                {
                    return p.GetValue(rg, new object[] { index }) as Region;
                }
                catch { }
            }
            // fallback: try method GetRegionAt
            var m = ReflectionHelpers.Method(t, "GetRegionAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                try
                {
                    return m.Invoke(rg, new object[] { index }) as Region;
                }
                catch { }
            }
            return null;
        }
    }

    public static class PathFinderCompat
    {
        public static PawnPath FindPath(Map map, IntVec3 start, IntVec3 dest, Pawn pawn)
        {
            if (map == null) return null;
            try
            {
                var mapType = map.GetType();
                var pfField = ReflectionHelpers.Field(mapType, "pathFinder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object pf = pfField != null ? pfField.GetValue(map) : null;
                if (pf == null)
                {
                    var pfProp = ReflectionHelpers.Property(mapType, "pathFinder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pfProp != null) pf = pfProp.GetValue(map);
                }
                if (pf != null)
                {
                    var pfType = pf.GetType();
                    // try several overload shapes
                    MethodInfo m = ReflectionHelpers.Method(pfType, "FindPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[] { typeof(IntVec3), typeof(IntVec3), typeof(Pawn) });
                    if (m != null)
                    {
                        return m.Invoke(pf, new object[] { start, dest, pawn }) as PawnPath;
                    }
                    m = ReflectionHelpers.Method(pfType, "FindPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[] { typeof(IntVec3), typeof(IntVec3), typeof(TraverseParms) });
                    if (m != null)
                    {
                        // build traverse parms from pawn
                        var tp = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn);
                        return m.Invoke(pf, new object[] { start, dest, tp }) as PawnPath;
                    }
                    // fallback: try a method with (IntVec3, IntVec3, Pawn, PathEndMode, object) signature
                    m = ReflectionHelpers.Method(pfType, "FindPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null)
                    {
                        // attempt to invoke with common params
                        try
                        {
                            return m.Invoke(pf, new object[] { start, dest, pawn }) as PawnPath;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    public static class DoorCompat
    {
        public static float GetOpenPct(Building_Door door)
        {
            if (door == null) return 0f;
            var t = door.GetType();
            var p = t.GetProperty("OpenPct", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(door);
                if (v is float f) return f;
                if (v is double d) return (float)d;
            }
            var ffield = t.GetField("openPct", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ffield != null)
            {
                var v = ffield.GetValue(door);
                if (v is float ff) return ff;
                if (v is double dd) return (float)dd;
            }
            return 0f;
        }
    }

    public static class NeedCompat
    {
        public static long GetLastRestTick(Need need)
        {
            if (need == null) return 0;
            var t = need.GetType();
            var f = t.GetField("lastRestTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(need);
                if (v is long l) return l;
                if (v is int i) return i;
            }
            var p = t.GetProperty("LastRestTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(need);
                if (v is long l2) return l2;
                if (v is int i2) return i2;
            }
            return 0;
        }
    }

    public static class PawnPathCompatExtras
    {
        public static bool IsPawnMoving(Pawn pawn)
        {
            if (pawn == null) return false;
            var pather = pawn.pather;
            if (pather == null) return false;
            var t = pather.GetType();
            var p = t.GetProperty("MovingNow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(pather);
                if (v is bool b) return b;
            }
            var f = t.GetField("moving", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(pather);
                if (v is bool b2) return b2;
            }
            return false;
        }
    }

    public static class DefDatabaseCompat
    {
        public static bool TryGetByName<T>(string defName, out T def) where T : Def
        {
            def = null;
            if (string.IsNullOrEmpty(defName)) return false;
            try
            {
                var list = DefDatabase<T>.AllDefsListForReading;
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i];
                    var dn = d.defName ?? d.defName;
                    if (dn == defName)
                    {
                        def = d;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    public static class ProjectilePropertiesCompat
    {
        public static float GetDamageAmount(object projProps)
        {
            if (projProps == null) return 0f;
            var t = projProps.GetType();
            var f = t.GetField("damageAmountBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(projProps);
                if (v is float f2) return f2;
                if (v is double d) return (float)d;
            }
            var p = t.GetProperty("damageAmount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(projProps);
                if (v is float f3) return f3;
                if (v is double d2) return (float)d2;
            }
            return 0f;
        }

        public static float GetArmorPenetration(object projProps)
        {
            if (projProps == null) return 0f;
            var t = projProps.GetType();
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var m in methods)
            {
                if (m.Name != "GetArmorPenetration") continue;
                var ps = m.GetParameters();
                try
                {
                    if (ps.Length == 0)
                    {
                        var r = m.Invoke(projProps, null);
                        if (r is float f) return f;
                        if (r is double d) return (float)d;
                        if (r is int i) return i;
                    }
                    else if (ps.Length == 1)
                    {
                        var p0 = ps[0].ParameterType;
                        object arg = null;
                        if (p0 == typeof(int) || p0 == typeof(float) || p0 == typeof(double)) arg = Convert.ChangeType(1, p0);
                        else arg = null;
                        var r = m.Invoke(projProps, new object[] { arg });
                        if (r is float f2) return f2;
                        if (r is double d2) return (float)d2;
                        if (r is int i2) return i2;
                    }
                }
                catch { }
            }
            return 0f;
        }
    }

    public static class ModContentPackCompat
    {
        public static string GetPackageIdPlayerFacing(ModContentPack pack)
        {
            if (pack == null) return string.Empty;
            var t = pack.GetType();
            var f = t.GetField("packageIdPlayerFacingInt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(pack);
                if (v is string s) return s;
            }
            var p = t.GetProperty("PackageId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(pack) as string;
                if (v != null) return v;
            }
            // fallback to ToString
            return pack.ToString();
        }
    }

    public static class CellIndicesCompat
    {
        public static int GetMapSizeX(CellIndices indices)
        {
            try
            {
                var t = typeof(CellIndices);
                var f = t.GetField("mapSizeX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return (int)f.GetValue(indices);
                var p = t.GetProperty("MapSizeX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return (int)p.GetValue(indices);
            }
            catch { }
            try { return indices.NumGridCells > 0 ? (int)Mathf.Sqrt(indices.NumGridCells) : 0; } catch { return 0; }
        }

        public static int GetMapSizeZ(CellIndices indices)
        {
            try
            {
                var t = typeof(CellIndices);
                var f = t.GetField("mapSizeZ", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return (int)f.GetValue(indices);
                var p = t.GetProperty("MapSizeZ", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return (int)p.GetValue(indices);
            }
            catch { }
            try { return indices.NumGridCells > 0 ? (int)Mathf.Sqrt(indices.NumGridCells) : 0; } catch { return 0; }
        }
    }

    public static class TickManagerCompat
    {
        public static long GetTicksGame()
        {
            try
            {
                var t = typeof(Find).Assembly.GetType("Verse.TickManager");
                if (t == null) t = typeof(TickManager);
                var prop = t.GetProperty("TicksGame", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null) return Convert.ToInt64(prop.GetValue(Find.TickManager));
                var f = t.GetField("ticksGameInt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return Convert.ToInt64(f.GetValue(Find.TickManager));
            }
            catch { }
            return 0L;
        }
    }

    public static class NeedCompatExtras
    {
        public static float GetCurLevel(Need need)
        {
            if (need == null) return 0f;
            var t = need.GetType();
            var f = t.GetField("curLevelInt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(need);
                if (v is float f2) return f2;
                if (v is double d) return (float)d;
            }
            var p = t.GetProperty("CurLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(need);
                if (v is float f3) return f3;
                if (v is double d2) return (float)d2;
            }
            return 0f;
        }
    }

    public static class DebugCellDrawerCompat
    {
        public static void ClearDebugCells(object debugDrawerObj)
        {
            if (debugDrawerObj == null) return;
            var t = debugDrawerObj.GetType();
            var f = t.GetField("debugCells", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                if (f.GetValue(debugDrawerObj) is System.Collections.IList list) list.Clear();
                return;
            }
            var p = t.GetProperty("DebugCells", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                if (p.GetValue(debugDrawerObj) is System.Collections.IList list2) list2.Clear();
            }
        }
    }

    public static class CellRectCompat
    {
        public static IntVec3 GetTopRight(CellRect rect)
        {
            try
            {
                var t = typeof(CellRect);
                var p = t.GetProperty("TopRight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return (IntVec3)p.GetValue(rect);
            }
            catch { }
            return new IntVec3(rect.maxX, 0, rect.maxZ);
        }
    }

    public static class InputCompat
    {
        public static bool AnyKey()
        {
            try
            {
                var t = TryGetInputType();
                if (t == null) return false;
                var p = t.GetProperty("anyKey", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return (bool)p.GetValue(null);
                var f = t.GetField("anyKey", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return (bool)f.GetValue(null);
            }
            catch { }
            return false;
        }

        public static bool GetKey(KeyCode key)
        {
            try
            {
                var t = TryGetInputType();
                if (t == null) return false;
                var m = t.GetMethod("GetKey", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(KeyCode) }, null);
                if (m != null) return (bool)m.Invoke(null, new object[] { key });
                m = t.GetMethod("GetKey", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(string) }, null);
                if (m != null) return (bool)m.Invoke(null, new object[] { key.ToString() });
            }
            catch { }
            return false;
        }

        private static Type TryGetInputType()
        {
            string[] candidates = new[] {
                "UnityEngine.Input, UnityEngine.InputLegacyModule",
                "UnityEngine.Input, UnityEngine",
                "UnityEngine.InputLegacyModule.Input, UnityEngine.InputLegacyModule",
                "UnityEngine.Input, UnityEngine.CoreModule"
            };
            foreach (var name in candidates)
            {
                try
                {
                    var t = Type.GetType(name);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }

    public static class TextCompat
    {
        public static void SetAnchor(TextAnchor anchor)
        {
            try
            {
                var t = typeof(Text);
                var f = t.GetField("anchorInt", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) { f.SetValue(null, anchor); return; }
                var p = t.GetProperty("anchor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) { p.SetValue(null, anchor); return; }
            }
            catch { }
        }

        public static void SetWordWrap(bool wordWrap)
        {
            try
            {
                var t = typeof(Text);
                var f = t.GetField("wordWrapInt", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) { f.SetValue(null, wordWrap); return; }
                var p = t.GetProperty("wordWrap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) { p.SetValue(null, wordWrap); return; }
            }
            catch { }
        }
    }

    public static class AreaCompat
    {
        public static bool InnerGridContains(Area_Home area, IntVec3 cell)
        {
            if (area == null) return false;
            var t = area.GetType();
            try
            {
                var f = t.GetField("innerGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var arr = f.GetValue(area) as System.Collections.IList;
                    if (arr != null)
                    {
                        var map = area.Map;
                        if (map == null) return false;
                        int idx = map.cellIndices.CellToIndex(cell);
                        if (idx >= 0 && idx < arr.Count)
                        {
                            var v = arr[idx];
                            if (v is bool b) return b;
                            if (v is byte by) return by != 0;
                        }
                    }
                }
            }
            catch { }
            try
            {
                var m = t.GetMethod("Contains", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) return (bool)m.Invoke(area, new object[] { cell });
            }
            catch { }
            return false;
        }
    }

    public static class VerbCompat
    {
        private static MethodInfo FindMethodSafe(Type t, string name, Type[] paramTypes)
        {
            try
            {
                // prefer exact signature lookup which may throw AmbiguousMatchException
                return t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, paramTypes, null);
            }
            catch (AmbiguousMatchException) { }
            try
            {
                foreach (var mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (mi.Name != name) continue;
                    var ps = mi.GetParameters();
                    if (ps.Length != paramTypes.Length) continue;
                    bool ok = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var pType = ps[i].ParameterType;
                        var desired = paramTypes[i];
                        if (pType == desired) continue;
                        // allow by-ref match
                        if (pType.IsByRef && pType.GetElementType() == desired) continue;
                        if (pType.Name == desired.Name) continue;
                        ok = false; break;
                    }
                    if (ok) return mi;
                }
            }
            catch { }
            return null;
        }
        public static bool CanHitCellFromCellIgnoringRange(Verb verb, IntVec3 from, IntVec3 to)
        {
            if (verb == null) return false;
            var t = verb.GetType();
            // try common method names with safe lookup
            MethodInfo m = FindMethodSafe(t, "CanHitCellFromCellIgnoringRange", new Type[] { typeof(IntVec3), typeof(IntVec3) });
            if (m != null)
            {
                try { return (bool)m.Invoke(verb, new object[] { from, to }); } catch { }
            }
            m = FindMethodSafe(t, "CanHitCellFromCell", new Type[] { typeof(IntVec3), typeof(IntVec3), typeof(bool) });
            if (m != null)
            {
                try { return (bool)m.Invoke(verb, new object[] { from, to, true }); } catch { }
            }
            // fallback: try CanHitTargetFrom
            m = FindMethodSafe(t, "CanHitTargetFrom", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo) });
            if (m != null)
            {
                try
                {
                    var target = new LocalTargetInfo(to);
                    return (bool)m.Invoke(verb, new object[] { from, target });
                }
                catch { }
            }
            return false;
        }

        public static bool CanHitFromCellIgnoringRange(Verb verb, IntVec3 from, LocalTargetInfo target, out float dist)
        {
            dist = 0f;
            if (verb == null) return false;
            var t = verb.GetType();
            MethodInfo m = FindMethodSafe(t, "CanHitFromCellIgnoringRange", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(float).MakeByRefType() });
            if (m != null)
            {
                try
                {
                    object[] args = new object[] { from, target, 0f };
                    var res = m.Invoke(verb, args);
                    if (res is bool b)
                    {
                        try { if (m.GetParameters().Length >= 3 && m.GetParameters()[2].ParameterType.IsByRef) dist = (float)args[2]; } catch { }
                        return b;
                    }
                }
                catch { }
            }
            // fallback: use CanHitTargetFrom (safe)
            m = FindMethodSafe(t, "CanHitTargetFrom", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo) });
            if (m != null)
            {
                try
                {
                    var r = (bool)m.Invoke(verb, new object[] { from, target });
                    if (r)
                    {
                        // distance approximate
                        dist = from.DistanceTo(target.Cell);
                    }
                    return r;
                }
                catch { }
            }
            return false;
        }
    }

    public static partial class CompatHelpers
    {
        public static Pawn GetPawn(object owner)
        {
            if (owner == null) return null;
            var t = owner.GetType();
            var p = t.GetProperty("pawn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (p != null)
            {
                var v = p.GetValue(owner);
                if (v is Pawn pw) return pw;
            }
            var p2 = t.GetProperty("Pawn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p2 != null)
            {
                var v = p2.GetValue(owner);
                if (v is Pawn pw2) return pw2;
            }
            var f = t.GetField("pawn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (f != null)
            {
                var v = f.GetValue(owner);
                if (v is Pawn pf) return pf;
            }
            return null;
        }

        public static Map GetMap(object owner)
        {
            if (owner == null) return null;
            var t = owner.GetType();
            var p = t.GetProperty("map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (p != null)
            {
                var v = p.GetValue(owner);
                if (v is Map m) return m;
            }
            var p2 = t.GetProperty("Map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p2 != null)
            {
                var v = p2.GetValue(owner);
                if (v is Map m2) return m2;
            }
            var f = t.GetField("map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (f != null)
            {
                var v = f.GetValue(owner);
                if (v is Map mf) return mf;
            }
            return null;
        }

        public static bool IsTurretManned(Building_TurretGun turret)
        {
            if (turret == null) return false;
            var t = turret.GetType();
            var p = t.GetProperty("MannableComp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object comp = null;
            if (p != null) comp = p.GetValue(turret);
            else
            {
                var f = t.GetField("mannableComp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) comp = f.GetValue(turret);
            }
            if (comp == null) return false;
            var ct = comp.GetType();
            var mp = ct.GetProperty("MannedNow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mp != null)
            {
                var v = mp.GetValue(comp);
                if (v is bool b) return b;
            }
            var mf = ct.GetField("mannedNow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mf != null)
            {
                var v = mf.GetValue(comp);
                if (v is bool b2) return b2;
            }
            return false;
        }
    }

    // helpers for dormancy
    public static partial class CompatHelpers
    {
        public static bool DormantIsAwake(object dormant)
        {
            if (dormant == null) return false;
            var t = dormant.GetType();
            var p = t.GetProperty("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(dormant);
                if (v is bool b) return b;
            }
            var f = t.GetField("awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(dormant);
                if (v is bool b2) return b2;
            }
            return false;
        }

        public static bool DormantWaitingToWakeUp(object dormant)
        {
            if (dormant == null) return false;
            var t = dormant.GetType();
            var p = t.GetProperty("WaitingToWakeUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(dormant);
                if (v is bool b) return b;
            }
            var f = t.GetField("waitingToWakeUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(dormant);
                if (v is bool b2) return b2;
            }
            return false;
        }
    }
}
