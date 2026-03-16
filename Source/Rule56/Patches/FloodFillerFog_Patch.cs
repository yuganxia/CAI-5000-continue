using HarmonyLib;
using Verse;
namespace CombatAI.Patches
{
    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    public static class FloodFillerFog_FloodUnfog_Patch
    {
        // Prefix: temporarily strip CAI-written fog bits from the native fogGrid so
        // that PassCheck (which reads fogGridDirect.IsSet() directly, bypassing our
        // IsFogged patch) does not flood-unfog cells that are fogged by CAI war fog.
        public static void Prefix(Map map)
        {
            if (!Finder.Settings.FogOfWar_Enabled) return;
            try
            {
                map?.GetComp_Fast<MapComponent_FogGrid>()?.ClearCAIFogBitsForSave();
            }
            catch { }
        }

        // Postfix: restore CAI fog bits, then let ScheduleOnVanillaFloodUnfog re-sync
        // the fogState with whatever vanilla actually revealed.
        public static void Postfix(IntVec3 root, Map map)
        {
            if (!Finder.Settings.FogOfWar_Enabled) return;
            try
            {
                var comp = map?.GetComp_Fast<MapComponent_FogGrid>();
                if (comp == null) return;
                comp.RestoreCAIFogBitsAfterSave();
                comp.ScheduleOnVanillaFloodUnfog();
            }
            catch { }
        }
    }
}
