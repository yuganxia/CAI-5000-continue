using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CombatAI.Patches
{
    [HarmonyPatch]
    public static class Mote_Draw_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            // Match any instance Draw methods on Mote and MoteBubble to cover overloads
            foreach (var m in typeof(Mote).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "Draw") yield return m;
            }
            foreach (var m in typeof(MoteBubble).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "Draw") yield return m;
            }
            // Fallback: if nothing found, return a harmless local method so Harmony won't throw
            var noop = typeof(Mote_Draw_Patch).GetMethod(nameof(Noop), BindingFlags.Static | BindingFlags.NonPublic);
            if (noop != null) yield return noop;
        }

        public static bool Prefix(Mote __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }
                var fog = Pawn_Patch.fogThings;
                if (fog == null)
                {
                    return true;
                }
                // Ensure the fog component belongs to the same map as the mote.
                if (fog.map == null || __instance?.Map == null || fog.map != __instance.Map)
                {
                    return true;
                }
                try
                {
                    return !fog.IsFogged(__instance.Position);
                }
                catch
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        private static void Noop(Mote __instance) { }
    }
}
