using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WildShift
{
    public static class TransformUtility
    {
        public static HediffComp_Shapeshifter AddOrGetShapeshifter(Pawn pawn, PawnKindDef assignedKind)
        {
            return AddOrGetShapeshifter(pawn, assignedKind, false);
        }

        public static HediffComp_Shapeshifter AddOrGetShapeshifter(Pawn pawn, PawnKindDef assignedKind, bool useRacialAffinity)
        {
            if (pawn == null)
            {
                return null;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
            bool isNew = hediff == null;
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(WildShiftDefOf.WildShift_Shapeshifter, pawn);
            }

            HediffComp_Shapeshifter comp = hediff.TryGetComp<HediffComp_Shapeshifter>();
            if (comp == null)
            {
                Log.Error("[WildShift] Shapeshifter hediff is missing HediffComp_Shapeshifter.");
                return null;
            }

            // Initialize before AddHediff invokes CompPostPostAdd, avoiding a
            // discarded random roll. Automatic assignment never replaces an
            // existing pawn's form; taming can still supply an explicit form.
            if (isNew && useRacialAffinity)
            {
                comp.assignedKind = RacialAnimalForms.Choose(pawn, assignedKind);
            }
            else if (!useRacialAffinity && assignedKind != null)
            {
                comp.assignedKind = assignedKind;
            }

            if (isNew)
            {
                pawn.health.AddHediff(hediff);
            }

            comp.EnsureAssignedKind();
            comp.RemoveLegacyAbilities();
            return comp;
        }

        public static bool IsShapeshifter(Pawn pawn)
        {
            return TryGetShapeshifterComp(pawn) != null;
        }

        public static bool IsTransformedAnimal(Pawn pawn)
        {
            return TryGetTransformedComp(pawn) != null;
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

        public static HediffComp_Shapeshifter TryGetShapeshifterComp(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
            return hediff != null ? hediff.TryGetComp<HediffComp_Shapeshifter>() : null;
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
            if (human.playerSettings != null && animal.playerSettings != null)
            {
                animal.playerSettings.displayOrder = human.playerSettings.displayOrder;
            }
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

            if (Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
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

                    if (ShouldRemoveGeneratedHealthCondition(hediff))
                    {
                        animal.health.RemoveHediff(hediff);
                    }
                }
            }
        }

        private static bool ShouldRemoveGeneratedHealthCondition(Hediff hediff)
        {
            if (hediff is Hediff_Injury || hediff is Hediff_MissingPart)
            {
                return true;
            }

            HediffDef def = hediff.def;
            return def != null
                && def.isBad
                && (def.chronic || def.isInfection || def.tendable || def.makesSickThought);
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

        public static Pawn RevertToHuman(Pawn animal, bool sendMessage = true)
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

            Pawn human = comp.StoredPawn;
            if (human == null)
            {
                Log.Error("[WildShift] Transformed pawn had no stored human to release.");
                return null;
            }

            Patch_GameEnder.InvalidateCache();

            Map map = animal.MapHeld;
            IntVec3 cell = animal.PositionHeld;
            Rot4 rotation = animal.Rotation;
            Faction faction = animal.Faction;
            bool wasSelected = Find.Selector != null && Find.Selector.IsSelected(animal);

            // A released animal has no faction. The stored human must follow
            // that state too, otherwise killing a released form can restore a
            // player colonist from an animal that was no longer in the colony.
            if (human.Faction != faction)
            {
                human.SetFaction(faction);
            }

            if (animal.playerSettings != null && human.playerSettings != null)
            {
                human.playerSettings.displayOrder = animal.playerSettings.displayOrder;
            }

            if (animal.Spawned)
            {
                human = comp.ReleaseStoredPawn();
                if (human == null)
                {
                    return null;
                }

                animal.DeSpawn(DestroyMode.Vanish);
                if (!animal.Destroyed)
                {
                    animal.Destroy(DestroyMode.Vanish);
                }

                GenSpawn.Spawn(human, cell, map);
            }
            else if (!TryReplaceInHoldingOwner(animal, human))
            {
                if (!TryReplaceWorldPawn(animal, comp, ref human))
                {
                    Log.Error("[WildShift] Could not replace an unspawned transformed animal in its holder or world-pawn registry.");
                    return null;
                }
            }

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

            if (Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }

            if (sendMessage)
            {
                Messages.Message(
                    "WildShift_MessageReverted".Translate(human.LabelShortCap),
                    human,
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }

            return human;
        }

        private static bool TryReplaceInHoldingOwner(Pawn animal, Pawn human)
        {
            IThingHolder parentHolder = animal.ParentHolder;
            ThingOwner owner = parentHolder != null ? parentHolder.GetDirectlyHeldThings() : null;
            if (owner == null || !owner.Contains(animal))
            {
                return false;
            }

            owner.Remove(animal);
            if (!owner.TryAddOrTransfer(human, false))
            {
                owner.TryAdd(animal, false);
                return false;
            }

            if (!animal.Destroyed)
            {
                animal.Destroy(DestroyMode.Vanish);
            }

            return true;
        }

        private static bool TryReplaceWorldPawn(Pawn animal, HediffComp_Transformed comp, ref Pawn human)
        {
            if (Find.WorldPawns == null || !WorldPawnsUtility.IsWorldPawn(animal))
            {
                return false;
            }

            human = comp.ReleaseStoredPawn();
            if (human == null)
            {
                return false;
            }

            Find.WorldPawns.RemovePawn(animal);
            Find.WorldPawns.PassToWorld(human, PawnDiscardDecideMode.KeepForever);
            if (!animal.Destroyed)
            {
                animal.Destroy(DestroyMode.Vanish);
            }

            return true;
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

            if (kind == null || !RacialAnimalForms.IsAllowed(human, kind))
            {
                reason = "WildShift_ShiftDisabledNoKind".Translate();
                return false;
            }

            return true;
        }
    }
}
