using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;
namespace CombatAI.Patches
{
    public static class Pawn_PathFollower_Patch
    {

        private static bool WalkableBy(IntVec3 cell, Map map, Pawn pawn)
        {
            if (Finder.Settings.Debug)
            {
                if (map != null)
                {
                    return cell.WalkableBy(map, pawn);
                }
                return true;
            }
            return true;
        }

        [HarmonyPatch(typeof(Pawn_PathFollower), "SetupMoveIntoNextCell")]
        private static class Pawn_PathFollower__Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var from = AccessTools.Method(typeof(GenGrid), "WalkableBy", new[] { typeof(IntVec3), typeof(Map), typeof(Pawn) });
                var to = AccessTools.Method(typeof(Pawn_PathFollower_Patch), nameof(WalkableBy));
                if (from != null && to != null)
                {
                    return instructions.MethodReplacer(from, to);
                }
                return instructions;
            }
        }
    }
}
