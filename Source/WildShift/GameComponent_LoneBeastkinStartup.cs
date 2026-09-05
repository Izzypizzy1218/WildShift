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
            HandleStartingShapeshifter();
        }

        private void HandleStartingShapeshifter()
        {
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

            PawnKindDef kind = LoneBeastkinUtility.GetStartingAnimalKind();
            if (kind != null && LoneBeastkinUtility.TryAssignStartingShapeshifter(kind))
            {
                checkedStartingPawn = true;
            }
        }

    }

    public static class LoneBeastkinUtility
    {
        public static void EnsurePreviewStartingShapeshifter()
        {
            ScenPart_StartingShapeshifter part = TryGetStartingPart();
            if (part == null || Find.GameInitData == null)
            {
                return;
            }

            List<Pawn> candidates = Find.GameInitData.startingAndOptionalPawns;
            if (candidates == null || candidates.Count == 0 || Find.GameInitData.startingPawnCount <= 0)
            {
                return;
            }

            // The first pawn in the selected-starting-pawn section is the
            // designated shapeshifter. Keeping the marker on this exact slot
            // makes it visible before the player presses Start and lets it
            // follow the player's drag-and-drop selection.
            Pawn designated = candidates[0];
            if (designated == null)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn = candidates[i];
                if (pawn == null || pawn == designated)
                {
                    continue;
                }

                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }

            HediffComp_Shapeshifter comp = TransformUtility.TryGetShapeshifterComp(designated);
            if (comp == null)
            {
                TransformUtility.AddOrGetShapeshifter(designated, part.AssignedKind, true);
            }
        }

        public static bool IsActiveScenario()
        {
            return TryGetStartingPart() != null;
        }

        public static PawnKindDef GetStartingAnimalKind()
        {
            ScenPart_StartingShapeshifter part = TryGetStartingPart();
            return part != null ? part.AssignedKind : null;
        }

        public static bool TryAssignStartingShapeshifter(PawnKindDef kind)
        {
            if (kind == null || Find.Maps == null)
            {
                return false;
            }

            if (HasAnyPlayerShapeshifter())
            {
                return true;
            }

            List<Pawn> candidates = new List<Pawn>();
            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                List<Pawn> colonists = Find.Maps[mapIndex].mapPawns.FreeColonistsSpawned;
                for (int pawnIndex = 0; pawnIndex < colonists.Count; pawnIndex++)
                {
                    Pawn pawn = colonists[pawnIndex];
                    if (pawn != null && !pawn.Dead)
                    {
                        candidates.Add(pawn);
                    }
                }
            }

            Pawn pawnToMark = candidates.RandomElementWithFallback();
            if (pawnToMark == null)
            {
                return false;
            }

            TransformUtility.AddOrGetShapeshifter(pawnToMark, kind, true);
            Log.Message("[WildShift] Marked " + pawnToMark.LabelShortCap + " as the Lone Beastkin starter.");
            return true;
        }

        private static ScenPart_StartingShapeshifter TryGetStartingPart()
        {
            Scenario scenario = Current.Game != null ? Current.Game.Scenario : null;
            if (scenario == null)
            {
                return null;
            }

            foreach (ScenPart part in scenario.AllParts)
            {
                ScenPart_StartingShapeshifter startingPart = part as ScenPart_StartingShapeshifter;
                if (startingPart != null)
                {
                    return startingPart;
                }
            }

            return null;
        }

        public static bool HasAnyPlayerShapeshifter()
        {
            List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int pawnIndex = 0; pawnIndex < pawns.Count; pawnIndex++)
            {
                Pawn pawn = pawns[pawnIndex];
                if (pawn != null
                    && (TransformUtility.IsShapeshifter(pawn) || TransformUtility.IsTransformedAnimal(pawn)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
