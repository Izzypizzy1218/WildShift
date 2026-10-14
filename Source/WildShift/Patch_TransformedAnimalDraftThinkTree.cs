using HarmonyLib;
using Verse;
using Verse.AI;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn_Thinker), "get_ConstantThinkTree")]
    public static class Patch_TransformedEnemyConstantThinkTree
    {
        public static void Postfix(Pawn_Thinker __instance, ref ThinkTreeDef __result)
        {
            Pawn animal = __instance != null ? __instance.pawn : null;
            if (animal != null && animal.lord != null && (EnemyShapeshifterUtility.IsCombatForm(animal) || SoldShapeshifterRevenge.IsPackAnimal(animal)))
                __result = WildShiftDefOf.WildShift_TransformedEnemyConstant;
        }
    }

    [HarmonyPatch(typeof(Pawn_Thinker), "get_MainThinkTree")]
    public static class Patch_TransformedAnimalDraftThinkTree
    {
        public static void Postfix(Pawn_Thinker __instance, ref ThinkTreeDef __result)
        {
            Pawn animal = __instance != null ? __instance.pawn : null;
            if (animal != null && animal.lord != null && (EnemyShapeshifterUtility.IsCombatForm(animal) || SoldShapeshifterRevenge.IsPackAnimal(animal)))
            {
                __result = WildShiftDefOf.WildShift_TransformedEnemy;
                return;
            }
            if (animal != null
                && animal.Drafted
                && animal.RaceProps != null
                && animal.RaceProps.Animal
                && TransformUtility.IsTransformedAnimal(animal)
                && WildShiftDefOf.WildShift_TransformedDrafted != null)
            {
                __result = WildShiftDefOf.WildShift_TransformedDrafted;
            }
        }
    }
}
