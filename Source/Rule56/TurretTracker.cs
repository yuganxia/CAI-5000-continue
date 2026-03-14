using System.Collections.Generic;
using RimWorld;
using Verse;
namespace CombatAI
{
    public class TurretTracker : MapComponent
    {
        public HashSet<Building_Turret> Turrets = new HashSet<Building_Turret>();

        public TurretTracker(Map map) : base(map)
        {
        }

        public void Register(Building_Turret t)
        {
            if (!Turrets.Contains(t))
            {
                Turrets.Add(t);
            }
            var comp = t.Map.GetComp_Fast<SightTracker>();
            if (comp != null) comp.Register(t);
        }

        public void DeRegister(Building_Turret t)
        {
            if (Turrets.Contains(t))
            {
                Turrets.Remove(t);
            }
            var comp = t.Map.GetComp_Fast<SightTracker>();
            if (comp != null) comp.DeRegister(t);
        }
    }
}
