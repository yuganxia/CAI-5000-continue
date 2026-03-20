using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    public static class PawnRenderer_Patch
    {
        // Compiled delegate — eliminates per-call reflection for reading PawnRenderer.pawn.
        // Discovered once at type-init; falls back to CompatHelpers if the field/property
        // cannot be found (e.g. future game version renames it).
        private static Func<PawnRenderer, Pawn> _getPawn;

        static PawnRenderer_Patch()
        {
            try
            {
                var type = typeof(PawnRenderer);
                var param = Expression.Parameter(typeof(PawnRenderer), "pr");

                // Try field first (RimWorld stores it as a field in most versions)
                var field = type.GetField("pawn",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(Pawn))
                {
                    _getPawn = Expression.Lambda<Func<PawnRenderer, Pawn>>(
                        Expression.Field(param, field), param).Compile();
                    return;
                }

                // Fall back to property
                var prop = type.GetProperty("pawn",
                               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
                        ?? type.GetProperty("Pawn",
                               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(Pawn))
                {
                    _getPawn = Expression.Lambda<Func<PawnRenderer, Pawn>>(
                        Expression.Property(param, prop), param).Compile();
                }
            }
            catch
            {
                // _getPawn stays null; GetPawnSafe falls back to CompatHelpers
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Pawn GetPawnSafe(PawnRenderer instance)
        {
            return _getPawn != null
                ? _getPawn(instance)
                : CombatAI.Compatibility.CompatHelpers.GetPawn(instance) as Pawn;
        }

        private static bool Prefix(PawnRenderer __instance, Vector3 drawLoc)
        {
            if (!Finder.Settings.FogOfWar_Enabled) return true;
            var prPawn = GetPawnSafe(__instance);
            if (prPawn != null && prPawn.Spawned)
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

        // Generic prefix that inspects original args to determine cell and skip draw when fogged.
        // __instance is typed as PawnRenderer so Harmony injects it directly without boxing.
        private static bool DrawPrefix(PawnRenderer __instance, object[] __args)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return true;
                var prPawn = GetPawnSafe(__instance);
                if (prPawn != null && prPawn.Spawned)
                {
                    var grid = prPawn.Map?.GetComp_Fast<MapComponent_FogGrid>();
                    if (grid != null)
                    {
                        IntVec3 cell = IntVec3.Invalid;
                        if (__args != null)
                        {
                            for (int i = 0; i < __args.Length; i++)
                            {
                                var a = __args[i];
                                if (a is Vector3 v)   { cell = v.ToIntVec3(); break; }
                                if (a is IntVec3 iv)  { cell = iv;            break; }
                                if (a is Thing t)     { cell = t.Position;    break; }
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
