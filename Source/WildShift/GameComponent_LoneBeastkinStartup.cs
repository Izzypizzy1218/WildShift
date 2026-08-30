using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WildShift
{
    public class GameComponent_LoneBeastkinStartup : GameComponent
    {
        private bool checkedStartingPawn;

        public GameComponent_LoneBeastkinStartup(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref checkedStartingPawn, "checkedStartingPawn", false);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (checkedStartingPawn)
            {
                return;
            }

            if (!LoneBeastkinUtility.IsActiveScenario())
            {
                checkedStartingPawn = true;
                return;
            }

            if (Find.TickManager == null
                || Find.TickManager.TicksGame < 30
                || Find.TickManager.TicksGame % 30 != 0)
            {
                return;
            }

            if (LoneBeastkinUtility.HasAnyPlayerShapeshifter())
            {
                checkedStartingPawn = true;
                return;
            }

            List<Pawn> pawns = new List<Pawn>();
            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                List<Pawn> colonists = Find.Maps[mapIndex].mapPawns.FreeColonistsSpawned;
                for (int pawnIndex = 0; pawnIndex < colonists.Count; pawnIndex++)
                {
                    Pawn pawn = colonists[pawnIndex];
                    if (pawn != null && !pawn.Dead)
                    {
                        pawns.Add(pawn);
                    }
                }
            }

            Pawn pawnToMark = pawns.RandomElementWithFallback();
            if (pawnToMark == null)
            {
                return;
            }

            PawnKindDef kind = AnimalPool.RandomEligibleKind();
            if (kind == null)
            {
                return;
            }

            TransformUtility.AddOrGetShapeshifter(pawnToMark, kind);
            checkedStartingPawn = true;
            Log.Message("[WildShift] Marked " + pawnToMark.LabelShortCap + " as the Lone Beastkin starter.");
        }
    }

    public static class LoneBeastkinUtility
    {
        public static bool IsActiveScenario()
        {
            return Current.Game != null
                && Current.Game.Scenario != null
                && Current.Game.Scenario.name == "\uACE0\uB3C5\uD55C \uC218\uC778";
        }

        public static bool HasAnyPlayerShapeshifter()
        {
            if (Find.Maps == null)
            {
                return false;
            }

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                IReadOnlyList<Pawn> pawns = Find.Maps[mapIndex].mapPawns.AllPawnsSpawned;
                for (int pawnIndex = 0; pawnIndex < pawns.Count; pawnIndex++)
                {
                    Pawn pawn = pawns[pawnIndex];
                    if (pawn != null
                        && pawn.Faction == Faction.OfPlayer
                        && (TransformUtility.IsShapeshifter(pawn) || TransformUtility.IsTransformedAnimal(pawn)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
