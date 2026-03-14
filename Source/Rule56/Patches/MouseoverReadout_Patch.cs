using System;
using HarmonyLib;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    public static class MouseoverReadout_Patch
    {
        public static bool Prefix(MouseoverReadout __instance)
        {
            try
            {
                if (!Finder.Settings.FogOfWar_Enabled) return true;
                var map = Find.CurrentMap;
                if (map == null) return true;
                var comp = map.GetComp_Fast<CombatAI.MapComponent_FogGrid>();
                if (comp == null) return true;
                IntVec3 mouseCell = UI.MouseCell();
                if (!mouseCell.IsValid) return true;
                return !comp.IsFogged(mouseCell);
            }
            catch (Exception e)
            {
                Log.Warning("ISMA: MouseoverReadout_Patch exception: " + e);
                return true;
            }
        }
    }
}
