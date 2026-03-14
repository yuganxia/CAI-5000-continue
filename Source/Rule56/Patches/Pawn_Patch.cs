using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
namespace CombatAI.Patches
{
    public static class Pawn_Patch
    {
        internal static MapComponent_FogGrid fogThings;
        private static MapComponent_FogGrid fogOverlay;

        [HarmonyPatch(typeof(Pawn), "DrawAt")]
        private static class Pawn_DrawAt_Patch
        {
            public static bool Prefix(Pawn __instance, Vector3 drawLoc)
            {
                if (fogOverlay == null && __instance.Spawned)
                {
                    fogOverlay = __instance.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
                }
                return fogOverlay == null || (Finder.Settings.Debug || !fogOverlay.IsFogged(drawLoc.ToIntVec3()));
            }
        }

        // Mote draw patch moved to its own file Mote_Draw_Patch.cs

        [HarmonyPatch(typeof(Pawn), "DrawGUIOverlay")]
        private static class Pawn_DrawGUIOverlay_Patch
        {
            public static bool Prefix(Pawn __instance)
            {
                if (fogOverlay == null && __instance.Spawned)
                {
                    fogOverlay = __instance.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
                }
                return fogOverlay == null || (!fogOverlay.IsFogged(__instance.Position) && !Finder.Settings.Debug_DisablePawnGuiOverlay);
            }
        }

        [HarmonyPatch(typeof(DynamicDrawManager), "DrawDynamicThings")]
        private static class DynamicDrawManager_DrawDynamicThings_Patch
        {
            public static void Prefix(DynamicDrawManager __instance)
            {
                try
                {
                    Map map = null;
                    var f = __instance.GetType().GetField("map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null) map = f.GetValue(__instance) as Map;
                    else
                    {
                        var p = __instance.GetType().GetProperty("map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (p != null) map = p.GetValue(__instance) as Map;
                    }
                    fogThings = map?.GetComp_Fast<MapComponent_FogGrid>();
                }
                catch { fogThings = null; }
            }

            public static void Postfix()
            {
                fogThings = null;
            }
        }

        [HarmonyPatch(typeof(ThingOverlays), "ThingOverlaysOnGUI")]
        private static class ThingOverlays_ThingOverlaysOnGUI_Patch
        {
            public static void Prefix(ThingOverlays __instance)
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                fogOverlay = Find.CurrentMap?.GetComp_Fast<MapComponent_FogGrid>() ?? null;
            }

            public static void Postfix()
            {
                fogOverlay = null;
            }
        }
    }
}
