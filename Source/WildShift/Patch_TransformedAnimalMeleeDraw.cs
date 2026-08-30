using HarmonyLib;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn_DrawTracker), "Notify_MeleeAttackOn")]
    public static class Patch_TransformedAnimalMeleeDraw
    {
        public static bool Prefix(Pawn ___pawn, Thing Target)
        {
            if (!TransformUtility.IsTransformedAnimal(___pawn))
            {
                return true;
            }

            TransformUtility.FaceMeleeTargetForAnimalForm(___pawn, Target);

            // Generated animal forms briefly jump south when either RimWorld's
            // default melee jitter or our old replacement jitter is played. Keep
            // the hit/facing but suppress positional melee jitter completely.
            return false;
        }
    }
}
