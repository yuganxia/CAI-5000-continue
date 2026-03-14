using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace CombatAI.Patches
{
    public static class LabelSuppressor_Patch
    {
        // Common method names that may draw name labels / GUI overlays for pawns
        private static readonly string[] CandidateNames = new[]
        {
            "DrawLabel", "DrawPawnLabel", "DrawGUIOverlay", "DrawName", "DoNameLabel", "DrawPawnGUI", "DrawPawnOverlay", "DrawNameLabel", "DrawPawnLabelAt", "DrawHeadLabel"
        };

        public static void Patch()
        {
            try
            {
                var harmony = Finder.Harmony;
                var prefix = new HarmonyMethod(typeof(LabelSuppressor_Patch).GetMethod(nameof(LabelPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                        foreach (var t in types)
                        {
                            MethodInfo[] methods;
                            try { methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); } catch { continue; }
                            foreach (var m in methods)
                            {
                                if (!CandidateNames.Contains(m.Name))
                                    continue;
                                // Only patch methods that accept a Pawn parameter to avoid suppressing labels for non-pawn things
                                var ps = m.GetParameters();
                                bool hasPawnParam = ps.Any(p => p.ParameterType == typeof(Pawn) || typeof(Pawn).IsAssignableFrom(p.ParameterType));
                                if (!hasPawnParam)
                                    continue;
                                try
                                {
                                    Log.Message($"ISMA: Attempting to patch label method {t.FullName}.{m.Name}");
                                    harmony.Patch(m, prefix: prefix);
                                }
                                catch (Exception e)
                                {
                                    Log.Warning($"ISMA: Failed to patch {t.FullName}.{m.Name}: {e.Message}");
                                }
                            }
                        }
                }
            }
            catch (Exception e)
            {
                Log.Error("ISMA: LabelSuppressor_Patch.Patch failed: " + e);
            }
        }

        // Generic prefix: if target pawn (from args or instance) is in fog, skip original
        private static bool LabelPrefix(object __instance, object[] __args)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return true;

                Pawn pawn = null;
                if (__instance is Pawn p) pawn = p;
                else if (__instance is Thing t && t is Pawn tp) pawn = tp;

                if (pawn == null && __args != null)
                {
                    foreach (var a in __args)
                    {
                        if (a is Pawn ap)
                        {
                            pawn = ap; break;
                        }
                        if (a is Thing at && at is Pawn apt)
                        {
                            pawn = apt; break;
                        }
                        if (a is Vector3 v)
                        {
                            // cannot map to pawn from Vector3
                        }
                    }
                }

                if (pawn != null && pawn.Spawned && !pawn.Destroyed)
                {
                    var grid = pawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                    if (grid != null && grid.IsFogged(pawn.Position))
                    {
                        return false; // skip drawing label
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("ISMA: LabelSuppressor_Patch.LabelPrefix exception: " + e);
            }
            return true;
        }
    }
}
