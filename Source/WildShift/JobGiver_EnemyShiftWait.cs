using RimWorld;
using Verse;
using Verse.AI;

namespace WildShift
{
    public class JobGiver_EnemyShiftWait : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // An unreachable colony is not a reason to wander, graze or emit
            // an idle-error job. Retry the native duty after a short combat wait.
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            job.expiryInterval = 60;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
