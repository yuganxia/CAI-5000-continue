using System;
using HarmonyLib;
using UnityEngine;
using RimWorld;
using Verse;
namespace CombatAI.Patches
{
    public static class FleckMaker_Patch
    {
    	[HarmonyPatch(typeof(FleckMaker), nameof(FleckMaker.Static), new []{typeof(IntVec3), typeof(Map), typeof(FleckDef), typeof(float)})]
    	private static class FleckMaker_Static_Patch
    	{
    		// Original: only reveal ShotFlash spots; add debug logging for suspicious flecks
    		public static void Prefix(IntVec3 cell, Map map, FleckDef fleckDef, float scale)
    		{

				if (Finder.Settings.FogOfWar_Enabled && fleckDef == FleckDefOf.ShotFlash && cell != FleckMakerCE_Patch.Current)
				{
					if (map != null)
					{
						MapComponent_FogGrid grid = map.GetComp_Fast<MapComponent_FogGrid>();
						if (grid != null)
						{
							grid.RevealSpot(cell, Maths.Max(scale, 3f), Rand.Range(120, 240));
						}
					}
				}
    		}
    	}
	    
        [HarmonyPatch(typeof(FleckMaker), nameof(FleckMaker.ThrowMetaIcon))]
        private static class FleckMaker_ThrowMetaIcon_Patch
        {
            public static bool Prefix(IntVec3 cell, Map map)
            {
                return !Finder.Settings.FogOfWar_Enabled || !(map.GetComp_Fast<MapComponent_FogGrid>()?.IsFogged(cell) ?? false);
            }
        }

        [HarmonyPatch(typeof(FleckMaker), nameof(FleckMaker.PlaceFootprint))]
        private static class FleckMaker_PlaceFootprint_Patch
        {
            public static bool Prefix(IntVec3 loc, Map map)
            {
                return !Finder.Settings.FogOfWar_Enabled || !(map.GetComp_Fast<MapComponent_FogGrid>()?.IsFogged(loc) ?? false);
            }
        }

        [HarmonyPatch(typeof(FleckMaker), nameof(FleckMaker.ThrowBreathPuff))]
		private static class FleckMaker_ThrowBreathPuff_Patch
		{
			public static bool Prefix(IntVec3 loc, Map map)
			{
				if (!Finder.Settings.FogOfWar_Enabled) return true;
				if (map == null) return true;
				var comp = map.GetComp_Fast<MapComponent_FogGrid>();
				if (comp == null) return true;
				return !comp.IsFogged(loc);
			}
		}

		// Some callers use Vector3 overloads; patch that signature too and convert to IntVec3
		[HarmonyPatch(typeof(FleckMaker), nameof(FleckMaker.ThrowBreathPuff), new []{typeof(Vector3), typeof(Map), typeof(float), typeof(Vector3)})]
		private static class FleckMaker_ThrowBreathPuff_Vector_Patch
		{
			// use original parameter names to avoid Harmony parameter mapping issues
			public static bool Prefix(Vector3 loc, Map map, float throwAngle, Vector3 inheritVelocity)
			{
				IntVec3 cell = loc.ToIntVec3();
				if (!Finder.Settings.FogOfWar_Enabled) return true;
				if (map == null) return true;
				var comp = map.GetComp_Fast<MapComponent_FogGrid>();
				if (comp == null) return true;
				return !comp.IsFogged(cell);
			}
		}
    }
}
