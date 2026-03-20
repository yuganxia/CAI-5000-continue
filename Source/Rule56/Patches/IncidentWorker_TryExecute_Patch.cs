using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;

namespace CombatAI.Patches
{
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow))]
    public static class IncidentWorker_CanFireNow_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(out bool __state)
        {
            __state = false;
            if (!Finder.Settings.FogOfWar_Enabled) return;
            FogGrid_IsFogged_Patch.PushSuppressCAIFogOnly();
            FogGrid_IsFogged_Patch.PushIgnoreVanillaUnexploredOnMapEdge();
            __state = true;
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                FogGrid_IsFogged_Patch.PopIgnoreVanillaUnexploredOnMapEdge();
                FogGrid_IsFogged_Patch.PopSuppressCAIFogOnly();
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class IncidentWorker_TryExecute_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(out bool __state)
        {
            __state = false;
            if (!Finder.Settings.FogOfWar_Enabled) return;
            FogGrid_IsFogged_Patch.PushSuppressCAIFogOnly();
            FogGrid_IsFogged_Patch.PushIgnoreVanillaUnexploredOnMapEdge();
            __state = true;
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                FogGrid_IsFogged_Patch.PopIgnoreVanillaUnexploredOnMapEdge();
                FogGrid_IsFogged_Patch.PopSuppressCAIFogOnly();
            }
            return __exception;
        }
    }

    /// <summary>
    /// Siege blueprint placement happens inside LordToil ticks — well after IncidentWorker.TryExecute
    /// has returned — so the IncidentWorker patch above cannot protect it.  We patch
    /// SiegeBlueprintPlacer's private CanPlaceBlueprintAt instead, which is the exact call-site
    /// that goes through GenConstruct.CanPlaceBlueprintAt → center.Fogged(map).
    ///
    /// With SuppressCAIFogOnly active, IsFogged returns false for CAIFogged cells (player has
    /// visited but CAI currently obscures them) while still returning true for VanillaUnexplored
    /// cells (player has never seen them), preserving the original "no blueprints in unexplored
    /// territory" behaviour.
    /// </summary>
    public static class SiegeBlueprintPlacer_CanPlaceBlueprint_Patch
    {
        private static readonly MethodBase _target = AccessTools.Method(typeof(SiegeBlueprintPlacer), "CanPlaceBlueprintAt");

        public static void Register(Harmony harmony)
        {
            if (_target == null) return;
            harmony.Patch(
                _target,
                prefix:    new HarmonyMethod(typeof(SiegeBlueprintPlacer_CanPlaceBlueprint_Patch), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(SiegeBlueprintPlacer_CanPlaceBlueprint_Patch), nameof(Finalizer)));
        }

        public static void Prefix(out bool __state)
        {
            __state = false;
            if (!Finder.Settings.FogOfWar_Enabled) return;
            FogGrid_IsFogged_Patch.PushSuppressCAIFogOnly();
            __state = true;
        }

        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) FogGrid_IsFogged_Patch.PopSuppressCAIFogOnly();
            return __exception;
        }
    }
}
