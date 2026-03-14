using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch]
    public static class Silhouette_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SilhouetteUtility), nameof(SilhouetteUtility.ShouldDrawSilhouette))]
        public static bool Prefix(Thing thing, ref bool __result)
        {
            try
            {
                if (thing == null || thing.Map == null) return true;
                // If CombatAI fog of war is enabled, suppress silhouettes for things that are fogged
                var fog = thing.Map.GetComp_Fast<MapComponent_FogGrid>();
                if (fog != null && Finder.Settings.FogOfWar_Enabled && fog.IsFogged(thing.Position))
                {
                    __result = false;
                    return false; // skip original
                }
            }
            catch
            {
                // swallow errors and fall back to vanilla
            }
            return true; // run original
        }
    }
}
