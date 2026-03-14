using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace CombatAI.Patches
{
    public static class OverlaySuppressor_Patch
    {

        private static bool Thing_DrawGUIOverlay_Prefix(Thing __instance)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return true;
                if (__instance is Pawn pawn && pawn.Spawned && !pawn.Destroyed)
                {
                    var grid = pawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                    if (grid != null && grid.IsFogged(pawn.Position))
                        return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning("ISMA: Thing_DrawGUIOverlay_Prefix exception: " + e);
            }
            return true;
        }

        private static bool ThingComp_DrawGUIOverlay_Prefix(ThingComp __instance)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return true;
                var parent = __instance?.parent;
                if (parent is Pawn pawn && pawn.Spawned && !pawn.Destroyed)
                {
                    var grid = pawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                    if (grid != null && grid.IsFogged(pawn.Position))
                        return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning("ISMA: ThingComp_DrawGUIOverlay_Prefix exception: " + e);
            }
            return true;
        }
    }
}
