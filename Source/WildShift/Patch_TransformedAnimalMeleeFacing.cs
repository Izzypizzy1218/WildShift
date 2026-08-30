using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Patch_TransformedAnimalMeleeFacing
    {
        public static void Postfix(Verb_MeleeAttack __instance)
        {
            if (__instance == null)
            {
                return;
            }

            Pawn animal = __instance.CasterPawn;
            if (!TransformUtility.IsTransformedAnimal(animal))
            {
                return;
            }

            Thing target = __instance.CurrentTarget.Thing;
            TransformUtility.FaceMeleeTargetForAnimalForm(animal, target);
        }
    }
}
