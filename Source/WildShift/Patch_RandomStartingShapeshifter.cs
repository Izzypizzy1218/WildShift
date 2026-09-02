using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(StartingPawnUtility), "NewGeneratedStartingPawn")]
    public static class Patch_RandomStartingShapeshifter
    {
        private const float RandomStartingShapeshifterChance = 0.01f;

        public static void Postfix(ref Pawn __result)
        {
            Pawn pawn = __result;
            if (pawn == null
                || pawn.RaceProps == null
                || !pawn.RaceProps.Humanlike
                || LoneBeastkinUtility.IsActiveScenario()
                || TransformUtility.IsShapeshifter(pawn))
            {
                return;
            }

            if (!Rand.Chance(RandomStartingShapeshifterChance))
            {
                return;
            }

            PawnKindDef animalKind = AnimalPool.RandomEligibleKind();
            if (animalKind != null)
            {
                TransformUtility.AddOrGetShapeshifter(pawn, animalKind);
            }
        }
    }
}
