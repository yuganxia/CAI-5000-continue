using System;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;
using CombatAI.Compatibility;

namespace CombatAI.Patches
{
	public static class PathFinder_Patch
	{
		// Debug flags (used by debug UI)
		public static bool FlashSearch { get; set; }
		public static bool FlashSapperPath { get; set; }

		// We register this patch at runtime to avoid Harmony trying to patch a non-existent overload.
		public static void Patch(HarmonyLib.Harmony harmony)
		{
			try
			{
				var pfType = typeof(PathFinder);
				System.Reflection.MethodInfo target = null;
				foreach (var m in pfType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
				{
					if (m.Name != "FindPath") continue;
					var ps = m.GetParameters();
					if (ps.Length >= 2 && ps[0].ParameterType == typeof(IntVec3) && ps[1].ParameterType == typeof(LocalTargetInfo))
					{
						target = m;
						break;
					}
				}
				if (target == null) return;
				var postfix = new HarmonyLib.HarmonyMethod(typeof(PathFinder_Patch).GetMethod(nameof(PostfixMethod), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
				harmony.Patch(target, postfix: postfix);
			}
			catch (Exception) { }
		}

		// Postfix executed when FindPath runs (best-effort visualization when debug flag enabled)
		private static void PostfixMethod(object __instance, IntVec3 start, LocalTargetInfo dest)
		{
			try
			{
				if (!FlashSearch) return;
				var mapField = __instance.GetType().GetField("map", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				Map map = null;
				if (mapField != null) map = mapField.GetValue(__instance) as Map;
				if (map == null) return;
				IntVec3 destCell = dest.HasThing ? dest.Thing.Position : dest.Cell;
				var path = PathFinderCompat.FindPath(map, start, destCell, null);
				if (path == null) return;
				var nodes = path.GetNodes();
				if (nodes == null) return;
				for (int i = 0; i < nodes.Count; i++)
				{
					map.debugDrawer.FlashCell(nodes[i], Mathf.Clamp01((float)i / Math.Max(1, nodes.Count)), $"{nodes.Count - i}", 30);
				}
			}
			catch { }
			finally
			{
				FlashSearch = false;
			}
		}
	}
}
