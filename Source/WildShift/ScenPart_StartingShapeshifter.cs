using RimWorld;
using Verse;

namespace WildShift
{
    public class ScenPart_StartingShapeshifter : ScenPart
    {
        private PawnKindDef assignedKind;

        public PawnKindDef AssignedKind
        {
            get
            {
                if (assignedKind == null || !AnimalPool.IsEligible(assignedKind))
                {
                    assignedKind = AnimalPool.RandomEligibleKind();
                }

                return assignedKind;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref assignedKind, "assignedKind");
        }

        public override void Randomize()
        {
            base.Randomize();
            assignedKind = AnimalPool.RandomEligibleKind();
        }

        public override string Summary(Scenario scen)
        {
            return "WildShift_ScenPartSummary".Translate();
        }

        public override void PostGameStart()
        {
            base.PostGameStart();
            LoneBeastkinUtility.TryAssignStartingShapeshifter(AssignedKind);
        }
    }
}
