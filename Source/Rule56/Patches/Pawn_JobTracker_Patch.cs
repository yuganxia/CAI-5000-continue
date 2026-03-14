using System.Collections.Generic;
using CombatAI.Comps;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace CombatAI.Patches
{
    public class Pawn_JobTracker_Patch
    {
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        public static class Pawn_JobTracker_TryTakeOrderedJob_Patch
        {
            public static void Postfix(Pawn_JobTracker __instance)
            {
                var __pawn = CombatAI.Compatibility.CompatHelpers.GetPawn(__instance);
                if (__pawn != null && __pawn.Faction.IsPlayerSafe() && __pawn.GetComp<ThingComp_CombatAI>() is ThingComp_CombatAI comp)
                {
                    comp.forcedTarget = LocalTargetInfo.Invalid;
                }
            }
        }

        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
        public static class Pawn_StartJob_Patch
        {
            public static void Postfix(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, ThinkNode jobGiver, bool cancelBusyStances)
            {
                var __pawn2 = CombatAI.Compatibility.CompatHelpers.GetPawn(__instance);
                if (Finder.Settings.Debug_LogJobs && Finder.Settings.Debug && __pawn2 != null && newJob != null && __pawn2.GetComp<ThingComp_CombatAI>() is ThingComp_CombatAI comp)
                {
                    comp.jobLogs ??= new List<JobLog>();
                    if (!comp.jobLogs.Any(j => j.id == newJob.loadID))
                    {
                        comp.jobLogs.Insert(0, JobLog.For(__pawn2, newJob, "unknown"));
                    }
                }
            }
        }
    }
}
