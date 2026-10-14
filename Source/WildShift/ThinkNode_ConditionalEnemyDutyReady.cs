using Verse;
using Verse.AI;

namespace WildShift
{
    public class ThinkNode_ConditionalEnemyDutyReady : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            // Vanilla ConditionalHasLordDuty checks the Lord's AssignsDuties,
            // not the pawn's actual duty. A pending downed return has a Lord
            // but deliberately no duty until the safe-point body swap.
            return pawn != null && pawn.lord != null && pawn.mindState != null
                && pawn.mindState.duty != null;
        }
    }
}
