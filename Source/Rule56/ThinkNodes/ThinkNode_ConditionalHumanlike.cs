using Verse;
using Verse.AI;
namespace CombatAI
{
    public class ThinkNode_ConditionalHumanlike : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return pawn.RaceProps.Humanlike;
        }
    }
}
