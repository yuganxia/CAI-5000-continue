using RimWorld;
using Verse;
using Verse.AI;
namespace CombatAI
{
    public class ThinkNode_ConditionalRaider : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return pawn.Map.ParentFaction != null && pawn.HostileTo(pawn.Map.ParentFaction);
        }
    }
}
