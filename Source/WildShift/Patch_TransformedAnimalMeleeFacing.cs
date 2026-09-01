using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn_RotationTracker), "UpdateRotation")]
    public static class Patch_TransformedAnimalMeleeFacing
    {
        public static void Postfix(Pawn_RotationTracker __instance, Pawn ___pawn)
        {
            Pawn animal = ___pawn;
            if (__instance == null
                || animal == null
                || !animal.Spawned
                || animal.Dead
                || animal.drafter == null
                || !animal.Drafted
                || animal.RaceProps == null
                || !animal.RaceProps.Animal
                || animal.pather == null
                || animal.pather.Moving
                || !TransformUtility.IsTransformedAnimal(animal))
            {
                return;
            }

            LocalTargetInfo target = LocalTargetInfo.Invalid;
            Stance_Busy busy = animal.stances != null
                ? animal.stances.curStance as Stance_Busy
                : null;

            if (busy != null
                && busy.verb != null
                && busy.verb.verbProps != null
                && busy.verb.verbProps.IsMeleeAttack
                && busy.focusTarg.IsValid)
            {
                target = busy.focusTarg;
            }
            else if (animal.CurJobDef == JobDefOf.AttackMelee
                && animal.CurJob != null
                && animal.CurJob.targetA.IsValid)
            {
                target = animal.CurJob.targetA;
            }

            if (target.IsValid)
            {
                // Drafted pawns normally fall back to Rot4.South between busy
                // stances. Transformed animals are draftable, so that vanilla
                // fallback caused a one-frame southward snap between attacks.
                // Reapply the current melee target after vanilla rotation logic.
                __instance.FaceTarget(target);
            }
        }
    }
}
