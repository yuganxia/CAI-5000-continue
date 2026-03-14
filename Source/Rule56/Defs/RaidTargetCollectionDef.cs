using System.Collections.Generic;
using System.Linq;
using Verse;
using CombatAI.Compatibility;
namespace CombatAI
{
    public class RaidTargetCollectionDef : Def
    {
        private string       packageId;
        private List<string> targets = new List<string>();

        [Unsaved(allowLoading: false)]
        public List<ThingDef> targetDefs;

        public void PostPostLoad()
        {
            targets    ??= new List<string>();
            targetDefs ??= new List<ThingDef>();
            targetDefs.Clear();
            if (!packageId.NullOrEmpty())
            {
                packageId = packageId.ToLower();
                if (!LoadedModManager.RunningMods.Any(m => ModContentPackCompat.GetPackageIdPlayerFacing(m).ToLower() == packageId || m.PackageId == packageId))
                {
                    return;
                }
            }
            foreach (string defName in targets)
            {
                if (DefDatabaseCompat.TryGetByName<ThingDef>(defName, out ThingDef def))
                {
                    targetDefs.Add(def);
                    Log.Message($"ISMA: {def} added to raid targets.");
                }
                else
                {
                    Log.Warning($"ISMA: {defName} part of {packageId} not found!");
                }
            }
        }

        public bool Initialized
        {
            get => targetDefs != null;
        }
    }
}
