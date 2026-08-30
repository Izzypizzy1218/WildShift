using RimWorld;
using Verse;

namespace WildShift
{
    public class IncidentWorker_ShapeshifterJoin : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
            {
                return false;
            }

            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            if (AnimalPool.RandomEligibleKind() == null)
            {
                return false;
            }

            if (HasPlayerShapeshifter(map) && (WildShiftMod.Settings == null || !WildShiftMod.Settings.allowAdditionalJoiners))
            {
                return false;
            }

            return true;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            PawnKindDef animalKind = AnimalPool.RandomEligibleKind();
            if (animalKind == null)
            {
                Messages.Message("WildShift_MessageNoEligibleAnimals".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            TransformUtility.AddOrGetShapeshifter(pawn, animalKind);

            IntVec3 cell;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out cell, map, CellFinder.EdgeRoadChance_Friendly))
            {
                cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 10);
            }

            GenSpawn.Spawn(pawn, cell, map);
            Find.LetterStack.ReceiveLetter(
                "WildShift_LetterJoinLabel".Translate(),
                "WildShift_LetterJoinText".Translate(pawn.LabelShortCap, animalKind.LabelCap),
                LetterDefOf.PositiveEvent,
                pawn);
            return true;
        }

        public static bool HasPlayerShapeshifter(Map map)
        {
            return LoneBeastkinUtility.HasAnyPlayerShapeshifter();
        }
    }
}
