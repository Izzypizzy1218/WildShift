using HarmonyLib;
using Verse;
using Verse.AI;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn_Thinker), "get_MainThinkTree")]
    public static class Patch_TransformedAnimalDraftThinkTree
    {
        public static void Postfix(Pawn_Thinker __instance, ref ThinkTreeDef __result)
        {
            Pawn animal = __instance != null ? __instance.pawn : null;
            if (animal != null
                && animal.Drafted
                && TransformUtility.IsTransformedAnimal(animal)
                && WildShiftDefOf.WildShift_TransformedDrafted != null)
            {
                __result = WildShiftDefOf.WildShift_TransformedDrafted;
            }
        }
    }
}
