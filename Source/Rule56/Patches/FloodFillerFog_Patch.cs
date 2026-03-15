using HarmonyLib;
using Verse;
namespace CombatAI.Patches
{
    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    public static class FloodFillerFog_FloodUnfog_Patch
    {
        public static void Postfix(IntVec3 root, Map map)
        {
            if (!Finder.Settings.FogOfWar_Enabled) return;
            try
            {
                map?.GetComp_Fast<MapComponent_FogGrid>()?.ScheduleOnVanillaFloodUnfog();
            }
            catch { }
        }
    }
}
