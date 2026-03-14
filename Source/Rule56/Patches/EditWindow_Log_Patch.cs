using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using LudeonTK;

namespace CombatAI.Patches
{
	[HarmonyPatch(typeof(EditWindow_Log), "DoWindowContents")]
	public static class EditWindow_Log_Patch
	{
		public static void Postfix(EditWindow_Log __instance)
		{
			DoCAIWidgets();
		}

		private static void DoCAIWidgets()
		{
			if (Current.ProgramState != ProgramState.Playing)
			{
				return;
			}
			try
			{
				var row = new WidgetRow(10f, 10f);
				if (!Finder.Settings.Debug_LogJobs)
				{
					UnityEngine.GUI.color = Color.green;
					if (row.ButtonText("Enable CAI Job Logging", "Enables CAI job logging used for debugging. WARNING: This is really bad for performance!"))
					{
						Finder.Settings.Debug = true;
						Finder.Settings.Debug_LogJobs = true;
						Messages.Message("WARNING: Please remember to disable job logging.", MessageTypeDefOf.CautionInput);
					}
				}
				else
				{
					UnityEngine.GUI.color = Color.red;
					if (row.ButtonText("Disable CAI Job Logging", "Disables CAI job logging used for debugging."))
					{
						Finder.Settings.Debug = false;
						Finder.Settings.Debug_LogJobs = false;
					}
				}
				UnityEngine.GUI.color = Color.white;
			}
			catch (Exception) { }
		}
	}
}
