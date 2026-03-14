using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch]
    public static class FogGrid_IsFogged_Patch
    {
        private static FieldInfo mapField;

        [ThreadStatic]
        private static int suppressDeepFogCounter;

        [ThreadStatic]
        private static int ignoreVanillaUnexploredOnMapEdgeCounter;

        public static bool SuppressDeepFogForVanillaChecks => suppressDeepFogCounter > 0;

        public static bool IgnoreVanillaUnexploredOnMapEdge => ignoreVanillaUnexploredOnMapEdgeCounter > 0;

        public static void PushSuppressDeepFogForVanillaChecks()
        {
            suppressDeepFogCounter++;
        }

        public static void PopSuppressDeepFogForVanillaChecks()
        {
            if (suppressDeepFogCounter > 0)
            {
                suppressDeepFogCounter--;
            }
        }

        public static void PushIgnoreVanillaUnexploredOnMapEdge()
        {
            ignoreVanillaUnexploredOnMapEdgeCounter++;
        }

        public static void PopIgnoreVanillaUnexploredOnMapEdge()
        {
            if (ignoreVanillaUnexploredOnMapEdgeCounter > 0)
            {
                ignoreVanillaUnexploredOnMapEdgeCounter--;
            }
        }

        static FogGrid_IsFogged_Patch()
        {
            var fogGridType = typeof(FogGrid);
            mapField = fogGridType.GetField("map", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.IsFogged), new Type[] { typeof(int) })]
        public static void Postfix_Int(FogGrid __instance, int index, ref bool __result)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return;
                Map map = mapField != null ? mapField.GetValue(__instance) as Map : null;
                if (map == null) return;
                if (IgnoreVanillaUnexploredOnMapEdge)
                {
                    IntVec3 edgeCell;
                    try { edgeCell = map.cellIndices.IndexToCell(index); }
                    catch { edgeCell = IntVec3.Invalid; }
                    if (edgeCell.IsValid && edgeCell.OnEdge(map))
                    {
                        __result = false;
                    }
                }
                // respect the player's-map disable setting to avoid changing vanilla behaviour there
                if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome) return;
                if (!Finder.Settings.FogOfWar_UseVanillaUnexplored) return;
                if (MapComponent_FogGrid.IsGravshipLandingSelectionActive) return;
                if (SuppressDeepFogForVanillaChecks) return;
                var comp = map.GetComp_Fast<MapComponent_FogGrid>();
                if (comp == null) return;
                bool deep = false;
                try { deep = comp.IsFogged(index); } catch { deep = false; }
                __result = __result || deep;
            }
            catch { /* swallow to avoid breaking vanilla calls */ }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.IsFogged), new Type[] { typeof(IntVec3) })]
        public static void Postfix_Cell(FogGrid __instance, IntVec3 c, ref bool __result)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return;
                Map map = mapField != null ? mapField.GetValue(__instance) as Map : null;
                if (map == null) return;
                if (IgnoreVanillaUnexploredOnMapEdge && c.OnEdge(map))
                {
                    __result = false;
                }
                if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome) return;
                if (!Finder.Settings.FogOfWar_UseVanillaUnexplored) return;
                if (MapComponent_FogGrid.IsGravshipLandingSelectionActive) return;
                if (SuppressDeepFogForVanillaChecks) return;
                var comp = map.GetComp_Fast<MapComponent_FogGrid>();
                if (comp == null) return;
                bool deep = false;
                try { deep = comp.IsFogged(c); } catch { deep = false; }
                __result = __result || deep;
            }
            catch { }
        }
    }
}
