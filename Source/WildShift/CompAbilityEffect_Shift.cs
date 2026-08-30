using RimWorld;
using Verse;

namespace WildShift
{
    public class CompProperties_AbilityShift : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityShift()
        {
            compClass = typeof(CompAbilityEffect_Shift);
        }
    }

    public class CompAbilityEffect_Shift : CompAbilityEffect
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent != null ? parent.pawn : null;
            if (caster == null)
            {
                return;
            }

            HediffComp_Shapeshifter comp = GetShapeshifterComp(caster);
            if (comp == null)
            {
                return;
            }

            PawnKindDef kind = comp.assignedKind;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                TransformUtility.TransformToAnimal(caster, kind);
            });
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent != null ? parent.pawn : null;
            HediffComp_Shapeshifter comp = GetShapeshifterComp(caster);
            return caster != null
                && comp != null
                && comp.assignedKind != null
                && !TransformUtility.IsTransformedAnimal(caster);
        }

        public override bool GizmoDisabled(out string reason)
        {
            Pawn caster = parent != null ? parent.pawn : null;
            if (caster == null)
            {
                reason = "WildShift_ShiftDisabledBadPawn".Translate();
                return true;
            }

            if (TransformUtility.IsTransformedAnimal(caster))
            {
                reason = "WildShift_ShiftDisabledTransformed".Translate();
                return true;
            }

            HediffComp_Shapeshifter comp = GetShapeshifterComp(caster);
            if (comp == null || comp.assignedKind == null)
            {
                reason = "WildShift_ShiftDisabledNoKind".Translate();
                return true;
            }

            reason = null;
            return false;
        }

        private static HediffComp_Shapeshifter GetShapeshifterComp(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
            return hediff != null ? hediff.TryGetComp<HediffComp_Shapeshifter>() : null;
        }
    }
}
