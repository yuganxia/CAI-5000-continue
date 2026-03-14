using System;
using Verse;
namespace CombatAI.Patches
{
    public static class HarmonyPatches
    {
        public static void Initialize()
        {
            // queue patches
            LongEventHandler.QueueLongEvent(PatchAll, "CombatAI.Preparing", false, null);
            // manual patches
            MainMenuDrawer_Patch.Patch();
            PawnRenderer_Patch.Patch();
            Selector_Patch.Patch();
            LabelSuppressor_Patch.Patch();
        }

        private static void PatchAll()
        {
            Log.Message("ISMA: Applying patches");
            // Run attribute-based patches
            Finder.Harmony.PatchAll();
            // Register any runtime-only patches
            try
            {
                PathFinder_Patch.Patch(Finder.Harmony);
            }
            catch { }
        }
    }
}
