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

            if (Find.TickManager != null && Find.TickManager.TicksGame % 60 == 0)
            {
                CheckJoinIncidentMtb();
            }
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

        private static void CheckJoinIncidentMtb()
        {
            if (WildShiftMod.Settings == null || Find.Maps == null)
            {
                return;
            }

            float baseMtbDays = WildShiftMod.Settings.joinMtbDaysWithoutShifter;
            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                if (map == null || !map.IsPlayerHome)
                {
                    continue;
                }

                bool alreadyHasShapeshifter = IncidentWorker_ShapeshifterJoin.HasPlayerShapeshifter(map);
                if (alreadyHasShapeshifter && !WildShiftMod.Settings.allowAdditionalJoiners)
                {
                    continue;
                }

                float effectiveMtbDays = alreadyHasShapeshifter ? baseMtbDays * 20f : baseMtbDays;
                if (!Rand.MTBEventOccurs(effectiveMtbDays, GenDate.TicksPerDay, 60f))
                {
                    continue;
                }

                IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, map);
                if (WildShiftDefOf.WildShift_ShapeshifterJoin.Worker.TryExecute(parms))
                {
                    break;
                }
            }
        }
    }

    public static class LoneBeastkinUtility
    {
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

            TransformUtility.AddOrGetShapeshifter(pawnToMark, kind);
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
