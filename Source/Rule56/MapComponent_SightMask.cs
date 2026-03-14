using System;
using Verse;

namespace CombatAI
{
    // Compatibility shim for older saves that reference CombatAI.MapComponent_SightMask
    // Provides a minimal non-abstract MapComponent so Scribe can instantiate it during load.
    public class MapComponent_SightMask : MapComponent
    {
        public MapComponent_SightMask(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            // Intentionally minimal: swallow any data to maintain compatibility with older saves.
            try
            {
                base.ExposeData();
            }
            catch
            {
                // ignore any mismatched saved data
            }
        }
    }
}
