using CombatAI.Comps;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace CombatAI.Patches
{
	public static class JobDriver_Wait_Patch
	{
		private static readonly System.Reflection.FieldInfo fStartTick = AccessTools.Field(typeof(JobDriver), "startTick");
		private static readonly System.Reflection.FieldInfo fVerbCurrentTarget = AccessTools.Field(typeof(Verb), "currentTarget");
		[HarmonyPatch(typeof(JobDriver_Wait), "MakeNewToils")]
		private static class JobDriver_Wait_MakeNewToils_Patch
		{
			public static void Postfix(JobDriver_Wait __instance)
			{
				if (__instance.job.Is(JobDefOf.Wait_Combat) && (!__instance.pawn.Faction.IsPlayerSafe() || (__instance.pawn.GetComp<ThingComp_CombatAI>()?.IsAIAutoControlled ?? false)))
				{
					if (__instance.job.targetC.IsValid)
					{
						__instance.rotateToFace = TargetIndex.C;
					}
					__instance.AddFailCondition(() =>
					{
						int startTickVal = 0;
						try { startTickVal = (int)(fStartTick?.GetValue(__instance) ?? 0); } catch { }
						if (!__instance.pawn.IsHashIntervalTick(30) || GenTicks.TicksGame - startTickVal < 30)
						{
							return false;
						}
						Verb verb = __instance.job.verbToUse ?? __instance.pawn.CurrentEffectiveVerb;
						if (verb == null || verb.WarmingUp || verb.Bursting || (__instance.pawn.Faction.IsPlayerSafe() && !(__instance.pawn.GetComp<ThingComp_CombatAI>()?.IsAIAutoControlled ?? false)))
						{
							// just skip the fail check if something is not right.
							return false;
						}
						LocalTargetInfo target = LocalTargetInfo.Invalid;
						try
						{
							var cur = fVerbCurrentTarget?.GetValue(verb);
							if (cur is LocalTargetInfo lti && lti.IsValid) target = lti;
						}
						catch { }
						if (!target.IsValid) target = __instance.pawn.mindState?.enemyTarget ?? LocalTargetInfo.Invalid;
						if (target.IsValid)
						{
							if (target.Thing is Pawn { Dead: false, Downed: false } pawn)
							{
								if (verb.CanHitTarget(PawnPathUtility.GetMovingShiftedPosition(pawn, 60)))
								{
									return false;
								}
							}
							else if (verb.CanHitTarget(target))
							{
								return false;
							}
							return true;
						}
						return __instance.job.endIfCantShootTargetFromCurPos;
					});
				}
			}
		}
	}
}
