using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
namespace CombatAI.Patches
{
    public static class Selector_Patch
    {
        // Manual patcher: find and patch Selector methods named "Select" or "SelectInternal"
        public static void Patch()
        {
            try
            {
                var harmony = Finder.Harmony;
                var prefix = new HarmonyMethod(typeof(Selector_Patch).GetMethod(nameof(SelectPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                var type = typeof(Selector);
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var m in methods)
                {
                    if (m.Name == "Select" || m.Name == "SelectInternal")
                    {
                        Log.Message($"ISMA: Patching Selector.{m.Name} - {m}");
                        harmony.Patch(m, prefix: prefix);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("ISMA: Selector_Patch.Patch failed: " + e);
            }
        }

        // Prefix receives generic args; if selecting a Pawn in fog, cancel selection
        private static bool SelectPrefix(object __instance, object[] __args)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return true;
                if (__args == null) return true;
                foreach (var a in __args)
                {
                    if (a is Pawn pawn && !pawn.Destroyed && pawn.Spawned)
                    {
                        var grid = pawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                        if (grid != null && grid.IsFogged(pawn.Position))
                        {
                            return false; // skip original: prevent selection
                        }
                    }
                    else if (a is Thing t && t is Pawn p && !p.Destroyed && p.Spawned)
                    {
                        var grid = p.Map?.GetComp_Fast<MapComponent_FogGrid>();
                        if (grid != null && grid.IsFogged(p.Position))
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("ISMA: Selector_Patch.SelectPrefix exception: " + e);
            }
            return true;
        }
    }
}
