using CombatAI.Comps;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CombatAI.Patches
{
	public static class FloatMenuMakerMap_Patch
	{
		[HarmonyPatch]
		public static class FloatMenuMakerMap_PawnGotoAction_Patch
		{
			public static MethodInfo TargetMethod()
			{
				// Try exact match first
				var mi = AccessTools.Method(typeof(FloatMenuMakerMap), "PawnGotoAction", new[] { typeof(IntVec3), typeof(Pawn), typeof(IntVec3) });
				if (mi != null) return mi;
				// Fallback: search any method named PawnGotoAction with 3 parameters
				foreach (var m in typeof(FloatMenuMakerMap).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (m.Name != "PawnGotoAction") continue;
					var ps = m.GetParameters();
					if (ps.Length == 3) return m;
				}
				// As last resort, return a harmless local method so Harmony doesn't throw; patch will be effectively a no-op.
				return typeof(FloatMenuMakerMap_PawnGotoAction_Patch).GetMethod(nameof(Noop), BindingFlags.Static | BindingFlags.NonPublic);
			}

			private static void Noop(IntVec3 clickCell, Pawn pawn, IntVec3 dest) { }

			public static void Postfix(IntVec3 clickCell, Pawn pawn, IntVec3 dest)
			{
				if (pawn == null) return;
				if (pawn.Faction.IsPlayerSafe())
				{
					var comp = pawn.GetComp<ThingComp_CombatAI>();
					if (comp != null)
					{
						comp.forcedTarget = LocalTargetInfo.Invalid;
					}
				}
			}
		}
	}
}
