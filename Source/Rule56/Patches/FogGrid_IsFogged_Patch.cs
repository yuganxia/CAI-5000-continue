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

        // When > 0, ALL IsFogged calls return false — overrides both vanilla fog and CAI fog.
        // Use this for enemy AI operations (siege blueprint placement) that must succeed even in
        // areas the player has never explored.
        [ThreadStatic]
        private static int suppressAllFogCounter;

        // When > 0, suppress only CAI-written (CAIFogged) fog while keeping genuine vanilla
        // unexplored fog intact.  Used during incident execution so that drop pods / raids can
        // land in cells the player previously explored but that are currently under CAI war fog,
        // while still being blocked by cells the player has truly never visited.
        [ThreadStatic]
        private static int suppressCAIFogOnlyCounter;

        // Tick-level cache for IsGravshipLandingSelectionActive.
        // Find.GravshipController scans WorldComponents and is expensive to call every IsFogged.
        private static volatile bool _gravshipCached;
        private static volatile int  _gravshipCacheTick = -9999;

        public static bool SuppressDeepFogForVanillaChecks => suppressDeepFogCounter > 0;

        public static bool IgnoreVanillaUnexploredOnMapEdge => ignoreVanillaUnexploredOnMapEdgeCounter > 0;

        public static bool SuppressAllFog => suppressAllFogCounter > 0;

        public static bool SuppressCAIFogOnly => suppressCAIFogOnlyCounter > 0;

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

        public static void PushSuppressAllFog()
        {
            suppressAllFogCounter++;
        }

        public static void PopSuppressAllFog()
        {
            if (suppressAllFogCounter > 0)
            {
                suppressAllFogCounter--;
            }
        }

        public static void PushSuppressCAIFogOnly()
        {
            suppressCAIFogOnlyCounter++;
        }

        public static void PopSuppressCAIFogOnly()
        {
            if (suppressCAIFogOnlyCounter > 0)
            {
                suppressCAIFogOnlyCounter--;
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
                // Full suppress: used by siege blueprint placement so enemies can build in
                // areas the player has never explored (both vanilla fog and CAI fog ignored).
                if (SuppressAllFog) { __result = false; return; }
                if (!Finder.Settings.FogOfWar_Enabled) return;
                Map map = GetMap(__instance);
                if (map == null) return;
                // Suppress only CAI-written fog while keeping genuine vanilla unexplored fog.
                // When UseVanillaUnexplored is ON, the vanilla FogGrid also contains bits that
                // CAI wrote for CAIFogged cells. If the cell is CAIFogged (explored but currently
                // in war fog) we override __result back to false so incidents can land there.
                // VanillaUnexplored cells are untouched — incidents are still blocked there.
                if (SuppressCAIFogOnly)
                {
                    if (__result
                        && Finder.Settings.FogOfWar_UseVanillaUnexplored
                        && !IsGravshipLandingCached()
                        && !(Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome))
                    {
                        var fogComp = map.GetComp_Fast<MapComponent_FogGrid>();
                        if (fogComp?.fogState != null && index >= 0 && index < fogComp.fogState.Length
                            && fogComp.fogState[index] == CellFogState.CAIFogged)
                        {
                            __result = false;
                        }
                    }
                    return;
                }
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
                if (SuppressAllFog) { __result = false; return; }
                if (!Finder.Settings.FogOfWar_Enabled) return;
                Map map = GetMap(__instance);
                if (map == null) return;
                if (SuppressCAIFogOnly)
                {
                    if (__result
                        && Finder.Settings.FogOfWar_UseVanillaUnexplored
                        && !IsGravshipLandingCached()
                        && !(Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome))
                    {
                        var fogComp = map.GetComp_Fast<MapComponent_FogGrid>();
                        if (fogComp?.fogState != null)
                        {
                            try
                            {
                                int idx = map.cellIndices.CellToIndex(c);
                                if (idx >= 0 && idx < fogComp.fogState.Length
                                    && fogComp.fogState[idx] == CellFogState.CAIFogged)
                                {
                                    __result = false;
                                }
                            }
                            catch { }
                        }
                    }
                    return;
                }
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
