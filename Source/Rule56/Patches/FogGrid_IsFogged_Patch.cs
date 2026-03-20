using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch]
    public static class FogGrid_IsFogged_Patch
    {
        internal static FieldInfo mapField;

        // Compiled field accessor — replaces reflection on the hot path.
        // Expression.Lambda.Compile() produces IL equivalent to a direct field read.
        private static Func<FogGrid, Map> _getMap;

        [ThreadStatic]
        private static int suppressDeepFogCounter;

        [ThreadStatic]
        private static int ignoreVanillaUnexploredOnMapEdgeCounter;

        // Tick-level cache for IsGravshipLandingSelectionActive.
        // Find.GravshipController scans WorldComponents and is expensive to call every IsFogged.
        private static volatile bool _gravshipCached;
        private static volatile int  _gravshipCacheTick = -9999;

        public static bool SuppressDeepFogForVanillaChecks => suppressDeepFogCounter > 0;

        public static bool IgnoreVanillaUnexploredOnMapEdge => ignoreVanillaUnexploredOnMapEdgeCounter > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Map GetMap(FogGrid fogGrid)
        {
            return _getMap != null ? _getMap(fogGrid) : mapField?.GetValue(fogGrid) as Map;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsGravshipLandingCached()
        {
            try
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick != _gravshipCacheTick)
                {
                    _gravshipCached    = MapComponent_FogGrid.IsGravshipLandingSelectionActive;
                    _gravshipCacheTick = tick;
                }
                return _gravshipCached;
            }
            catch { return false; }
        }

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
            if (mapField != null)
            {
                try
                {
                    var param = Expression.Parameter(typeof(FogGrid), "fg");
                    _getMap   = Expression.Lambda<Func<FogGrid, Map>>(Expression.Field(param, mapField), param).Compile();
                }
                catch { /* fall back to reflection */ }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.IsFogged), new Type[] { typeof(int) })]
        public static void Postfix_Int(FogGrid __instance, int index, ref bool __result)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return;
                Map map = GetMap(__instance);
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
                if (IsGravshipLandingCached()) return;
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
                Map map = GetMap(__instance);
                if (map == null) return;
                if (IgnoreVanillaUnexploredOnMapEdge && c.OnEdge(map))
                {
                    __result = false;
                }
                if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome) return;
                if (!Finder.Settings.FogOfWar_UseVanillaUnexplored) return;
                if (IsGravshipLandingCached()) return;
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

    /// <summary>
    /// Ensures CAI-written fog bits are stripped from the vanilla FogGrid before it saves
    /// and restored immediately after, so the save file only contains genuine vanilla fog.
    /// </summary>
    [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.ExposeData))]
    public static class FogGrid_ExposeData_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(FogGrid __instance)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;
            try
            {
                Map map = FogGrid_IsFogged_Patch.mapField?.GetValue(__instance) as Map;
                map?.GetComp_Fast<MapComponent_FogGrid>()?.ClearCAIFogBitsForSave();
            }
            catch { }
        }

        [HarmonyPostfix]
        public static void Postfix(FogGrid __instance)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;
            try
            {
                Map map = FogGrid_IsFogged_Patch.mapField?.GetValue(__instance) as Map;
                map?.GetComp_Fast<MapComponent_FogGrid>()?.RestoreCAIFogBitsAfterSave();
            }
            catch { }
        }
    }
}
