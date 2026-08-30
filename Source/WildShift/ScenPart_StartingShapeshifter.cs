using RimWorld;
using Verse;

namespace WildShift
{
    public class ScenPart_StartingShapeshifter : ScenPart
    {
        private PawnKindDef assignedKind;

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
    }
}
