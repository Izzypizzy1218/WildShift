using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WildShift
{
    public static class TransformUtility
    {
        public static HediffComp_Shapeshifter AddOrGetShapeshifter(Pawn pawn, PawnKindDef assignedKind)
        {
            if (pawn == null)
            {
                return null;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(WildShiftDefOf.WildShift_Shapeshifter, pawn);
                pawn.health.AddHediff(hediff);
            }

            HediffComp_Shapeshifter comp = hediff.TryGetComp<HediffComp_Shapeshifter>();
            if (comp == null)
            {
                Log.Error("[WildShift] Shapeshifter hediff is missing HediffComp_Shapeshifter.");
                return null;
            }

            if (assignedKind != null)
            {
                comp.assignedKind = assignedKind;
            }

            comp.EnsureAssignedKind();
            comp.RemoveLegacyAbilities();
            return comp;
        }

        public static bool IsShapeshifter(Pawn pawn)
        {
            return pawn != null
                && pawn.health != null
                && pawn.health.hediffSet != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter) != null;
        }

        public static bool IsTransformedAnimal(Pawn pawn)
        {
            return TryGetTransformedComp(pawn) != null;
        }

        public static void FaceMeleeTargetForAnimalForm(Pawn animal, Thing target)
        {
            if (animal == null
                || animal.Dead
                || !animal.Spawned
                || target == null
                || !target.Spawned
                || target.Map != animal.Map)
            {
                return;
            }

            int horizontalOffset = target.Position.x - animal.Position.x;
            if (horizontalOffset > 0)
            {
                animal.Rotation = Rot4.East;
            }
            else if (horizontalOffset < 0)
            {
                animal.Rotation = Rot4.West;
            }
            else if (animal.rotationTracker != null)
            {
                animal.rotationTracker.FaceCell(target.Position);
            }
        }

        public static bool TryOrderMeleeAttack(Pawn animal, LocalTargetInfo target)
        {
            if (animal == null
                || animal.Dead
                || !animal.Spawned
                || !animal.Drafted
                || animal.jobs == null
                || !target.IsValid
                || target.Thing == animal)
            {
                return false;
            }

            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.playerForced = true;
            job.killIncappedTarget = true;
            return animal.jobs.TryTakeOrderedJob(job);
        }

        public static HediffComp_Transformed TryGetTransformedComp(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Transformed);
            return hediff != null ? hediff.TryGetComp<HediffComp_Transformed>() : null;
        }

        public static bool TransformToAnimal(Pawn human, PawnKindDef kind)
        {
            string reason;
            if (!CanTransformToAnimal(human, kind, out reason))
            {
                if (!reason.NullOrEmpty())
                {
                    Messages.Message(reason, human, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            Map map = human.Map;
            IntVec3 cell = human.Position;
            Rot4 rotation = human.Rotation;
            Faction faction = human.Faction;
            string humanLabel = human.LabelShortCap;
            bool wasSelected = Find.Selector != null && Find.Selector.IsSelected(human);
            bool wasDrafted = human.Drafted;

            Pawn animal;
            try
            {
                animal = PawnGenerator.GeneratePawn(kind, faction);
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Failed to generate animal form " + kind + ": " + ex);
                return false;
            }

            if (animal == null)
            {
                Log.Error("[WildShift] PawnGenerator returned null for animal form " + kind + ".");
                return false;
            }

            if (human.Name != null)
            {
                animal.Name = new NameSingle(human.Name.ToStringShort, false);
            }

            if (faction != null && animal.Faction != faction)
            {
                animal.SetFaction(faction);
            }

            ClearGeneratedAnimalHealth(animal);

            Hediff transformed = HediffMaker.MakeHediff(WildShiftDefOf.WildShift_Transformed, animal);
            animal.health.AddHediff(transformed);

            HediffComp_Transformed comp = transformed.TryGetComp<HediffComp_Transformed>();
            if (comp == null)
            {
                Log.Error("[WildShift] Transformed hediff is missing HediffComp_Transformed.");
                animal.Destroy(DestroyMode.Vanish);
                return false;
            }

            comp.Store(human);
            GenSpawn.Spawn(animal, cell, map);
            animal.Rotation = rotation;
            EnsureTransformedAnimalControl(animal, true);
            Patch_GameEnder.InvalidateCache();

            if (wasDrafted && animal.drafter != null)
            {
                animal.drafter.Drafted = true;
            }

            if (wasSelected)
            {
                Find.Selector.Deselect(human);
                Find.Selector.Select(animal);
            }

            Messages.Message(
                "WildShift_MessageTransformed".Translate(humanLabel, kind.LabelCap),
                animal,
                MessageTypeDefOf.PositiveEvent,
                false);
            return true;
        }

        public static void ClearGeneratedAnimalHealth(Pawn animal)
        {
            ClearGeneratedAnimalHealth(animal, null);
        }

        public static void ClearGeneratedAnimalHealth(Pawn animal, HediffDef hediffDefToKeep)
        {
            if (animal == null || animal.health == null || animal.health.hediffSet == null)
            {
                return;
            }

            List<Hediff> hediffs = new List<Hediff>(animal.health.hediffSet.hediffs);
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff != null)
                {
                    if (hediffDefToKeep != null && hediff.def == hediffDefToKeep)
                    {
                        continue;
                    }

                    animal.health.RemoveHediff(hediff);
                }
            }
        }

        public static void EnsureTransformedAnimalControl(Pawn animal, bool transformedKnown = false)
        {
            if (animal == null
                || animal.Dead
                || animal.Faction != Faction.OfPlayer
                || (!transformedKnown && !IsTransformedAnimal(animal)))
            {
                return;
            }

            if (animal.playerSettings == null)
            {
                animal.playerSettings = new Pawn_PlayerSettings(animal);
            }

            if (animal.drafter == null)
            {
                animal.drafter = new Pawn_DraftController(animal);
            }
        }

        public static Pawn RevertToHuman(Pawn animal)
        {
            if (animal == null)
            {
                return null;
            }

            HediffComp_Transformed comp = TryGetTransformedComp(animal);
            if (comp == null)
            {
                Log.Warning("[WildShift] Tried to revert a pawn without transformed storage.");
                return null;
            }

            Pawn human = comp.ReleaseStoredPawn();
            if (human == null)
            {
                Log.Error("[WildShift] Transformed pawn had no stored human to release.");
                return null;
            }

            Patch_GameEnder.InvalidateCache();

            Map map = animal.Map;
            IntVec3 cell = animal.Position;
            Rot4 rotation = animal.Rotation;
            Faction faction = animal.Faction;
            bool wasSelected = Find.Selector != null && Find.Selector.IsSelected(animal);

            if (animal.Spawned)
            {
                animal.DeSpawn(DestroyMode.Vanish);
            }

            if (!animal.Destroyed)
            {
                animal.Destroy(DestroyMode.Vanish);
            }

            if (faction != null && human.Faction != faction)
            {
                human.SetFaction(faction);
            }

            GenSpawn.Spawn(human, cell, map);
            human.Rotation = rotation;

            if (human.jobs != null)
            {
                human.jobs.StopAll(false);
            }

            if (wasSelected)
            {
                Find.Selector.Deselect(animal);
                Find.Selector.Select(human);
            }

            Messages.Message(
                "WildShift_MessageReverted".Translate(human.LabelShortCap),
                human,
                MessageTypeDefOf.PositiveEvent,
                false);
            return human;
        }

        private static bool CanTransformToAnimal(Pawn human, PawnKindDef kind, out string reason)
        {
            reason = null;

            if (human == null || human.Dead || !human.Spawned || human.Map == null)
            {
                reason = "WildShift_ShiftDisabledBadPawn".Translate();
                return false;
            }

            if (IsTransformedAnimal(human))
            {
                reason = "WildShift_ShiftDisabledTransformed".Translate();
                return false;
            }

            if (kind == null || !AnimalPool.IsEligible(kind))
            {
                reason = "WildShift_ShiftDisabledNoKind".Translate();
                return false;
            }

            return true;
        }
    }
}
