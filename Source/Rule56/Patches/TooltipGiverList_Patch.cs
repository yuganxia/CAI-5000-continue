using HarmonyLib;
using Verse;

namespace CombatAI.Patches
{
	public static class TooltipGiverList_Patch
	{
		[HarmonyPatch(typeof(TooltipGiverList), "ShouldShowShotReport")]
		private static class TooltipGiverList_ShouldShowShotReport_Patch
		{
			public static bool Prefix(Thing t)
			{
				if (Finder.Settings.FogOfWar_Enabled && t is Pawn pawn && !t.Destroyed && t.Spawned)
				{
					Map map = pawn.Map;
					if (map != null)
					{
						var fog = map.GetComp_Fast<CombatAI.MapComponent_FogGrid>();
						if (fog != null)
						{
							return !fog.IsFogged(pawn.Position);
						}
					}
				}
				return true;
			}
		}
	}
}
