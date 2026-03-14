using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
namespace CombatAI.Patches
{
    public static class CoverGrid_Patch
    {
        public static WallGrid grid;
        public static MethodBase mCellToIndex = AccessTools.Method(typeof(CellIndices), nameof(CellIndices.CellToIndex), new[]
        {
            typeof(IntVec3)
        });
        public static MethodBase mCellToIndex2 = AccessTools.Method(typeof(CellIndices), nameof(CellIndices.CellToIndex), new[]
        {
            typeof(int), typeof(int)
        });

        public static void Set(IntVec3 cell, Thing t)
        {
            if (grid != null)
            {
                grid.RecalculateCell(cell, t);
            }
        }

        [HarmonyPatch(typeof(CoverGrid), nameof(CoverGrid.Register))]
        public static class CoverGrid_Register_Patch
        {
            public static void Prefix(CoverGrid __instance, Thing t, out bool __state)
            {
                var map = (Map)AccessTools.Field(typeof(CoverGrid), "map").GetValue(__instance);
                bool shouldUpdate = false;
                if (t != null)
                {
                    if (t.def.fillPercent > 0) shouldUpdate = true;
                    if (t is Building) shouldUpdate = true;
                    if (t.def.passability == Traversability.Impassable) shouldUpdate = true;
                    if (t.def.Fillage == FillCategory.Full) shouldUpdate = true;
                }
                __state = shouldUpdate;
                grid = shouldUpdate ? map.GetComp_Fast<WallGrid>() : null;
            }

            public static void Postfix(CoverGrid __instance, Thing t, bool __state)
            {
                var map = (Map)AccessTools.Field(typeof(CoverGrid), "map").GetValue(__instance);
                if (__state)
                {
                    map.GetComp_Fast<WallGrid>()?.RecalculateCell(t.Position, t);
                }
                grid = null;
            }
        }

        [HarmonyPatch(typeof(CoverGrid), nameof(CoverGrid.DeRegister))]
        public static class CoverGrid_DeRegister_Patch
        {
            public static void Prefix(CoverGrid __instance, Thing t, out object __state)
            {
                var map = (Map)AccessTools.Field(typeof(CoverGrid), "map").GetValue(__instance);
                bool shouldUpdate = false;
                IntVec3 pos = IntVec3.Invalid;
                if (t != null)
                {
                    pos = t.Position;
                    if (t.def.fillPercent > 0) shouldUpdate = true;
                    if (t is Building) shouldUpdate = true;
                    if (t.def.passability == Traversability.Impassable) shouldUpdate = true;
                    if (t.def.Fillage == FillCategory.Full) shouldUpdate = true;
                }
                grid = shouldUpdate ? map.GetComp_Fast<WallGrid>() : null;
                __state = (pos, shouldUpdate);
            }

            public static void Postfix(CoverGrid __instance, Thing t, object __state)
            {
                grid = null;
                var map = (Map)AccessTools.Field(typeof(CoverGrid), "map").GetValue(__instance);
                var tup = ((IntVec3 pos, bool update))__state;
                if (tup.update)
                {
                    map.GetComp_Fast<WallGrid>()?.RecalculateCell(tup.pos, null);
                }
                if (t.def.passability == Traversability.Impassable)
                {
                    map.GetComp_Fast<WallCCTVTracker>()?.Notify_CellChanged(tup.pos);
                }
            }
        }

        
    }
}
