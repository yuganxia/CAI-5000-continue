using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    public static class PawnRenderer_Patch
    {
        private static bool Prefix(PawnRenderer __instance, Vector3 drawLoc)
        {
            var prPawn = CombatAI.Compatibility.CompatHelpers.GetPawn(__instance);
            if (Finder.Settings.FogOfWar_Enabled && prPawn != null && prPawn.Spawned)
            {
                var grid = prPawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                if (grid != null)
                {
                    return !grid.IsFogged(drawLoc.ToIntVec3());
                }
            }
            return true;
        }

        // Manual patcher: find and patch PawnRenderer.DrawPawn overloads
        public static void Patch()
        {
            try
            {
                var harmony = Finder.Harmony;
                var prefix = new HarmonyMethod(typeof(PawnRenderer_Patch).GetMethod(nameof(DrawPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                var type = typeof(PawnRenderer);
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var m in methods)
                {
                    if (m.Name == "DrawPawn")
                    {
                        Log.Message($"ISMA: Patching PawnRenderer.{m.Name} - {m}");
                        harmony.Patch(m, prefix: prefix);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("ISMA: PawnRenderer DrawPawn manual patch failed: " + e);
            }
        }

        // Generic prefix that inspects original args to determine cell and skip draw when fogged
        private static bool DrawPrefix(object __instance, object[] __args)
        {
            try
            {
                var prPawn = CombatAI.Compatibility.CompatHelpers.GetPawn(__instance);
                if (Finder.Settings.FogOfWar_Enabled && prPawn != null && prPawn.Spawned)
                {
                    var grid = prPawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                    if (grid != null)
                    {
                        IntVec3 cell = IntVec3.Invalid;
                        if (__args != null)
                        {
                            foreach (var a in __args)
                            {
                                if (a is Vector3 v)
                                {
                                    cell = v.ToIntVec3();
                                    break;
                                }
                                if (a is IntVec3 iv)
                                {
                                    cell = iv;
                                    break;
                                }
                                if (a is Thing t)
                                {
                                    cell = t.Position;
                                    break;
                                }
                            }
                        }
                        if (cell.IsValid)
                        {
                            return !grid.IsFogged(cell);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("ISMA: PawnRenderer DrawPrefix exception: " + e);
            }
            return true;
        }
    }
}
