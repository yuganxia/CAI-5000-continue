using System;
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
            if (!Finder.Settings.FogOfWar_UseVanillaUnexplored) return;

            FogGrid_IsFogged_Patch.PushSuppressDeepFogForVanillaChecks();
            FogGrid_IsFogged_Patch.PushIgnoreVanillaUnexploredOnMapEdge();
            __state = true;
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                FogGrid_IsFogged_Patch.PopIgnoreVanillaUnexploredOnMapEdge();
                FogGrid_IsFogged_Patch.PopSuppressDeepFogForVanillaChecks();
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
            if (!Finder.Settings.FogOfWar_UseVanillaUnexplored) return;

            FogGrid_IsFogged_Patch.PushSuppressDeepFogForVanillaChecks();
            FogGrid_IsFogged_Patch.PushIgnoreVanillaUnexploredOnMapEdge();
            __state = true;
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                FogGrid_IsFogged_Patch.PopIgnoreVanillaUnexploredOnMapEdge();
                FogGrid_IsFogged_Patch.PopSuppressDeepFogForVanillaChecks();
            }
            return __exception;
        }
    }
}
