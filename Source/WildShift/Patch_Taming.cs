using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // VERIFY: RimWorld 1.6 still routes generated pawns through GeneratePawn(PawnGenerationRequest).
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_GeneratedAnimalLatentShapeshifter
    {
        public static void Postfix(ref Pawn __result)
        {
            if (__result == null || !AnimalPool.IsEligible(__result))
            {
                return;
            }

            // Faction leaders are generated while the world is still being
            // created, before the player faction exists. Do not ask the static
            // Faction.OfPlayer accessor until the faction manager has one.
            FactionManager factionManager = Find.FactionManager;
            Faction playerFaction = factionManager != null ? factionManager.OfPlayer : null;
            if (playerFaction == null || __result.Faction == playerFaction)
            {
                return;
            }

            float chance = WildShiftMod.Settings != null ? WildShiftMod.Settings.fieldShapeshifterChance : 0.03f;
            if (chance <= 0f || !Rand.Chance(chance))
            {
                return;
            }

            if (__result.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker) == null)
            {
                __result.health.AddHediff(HediffMaker.MakeHediff(WildShiftDefOf.WildShift_FieldShapeshifterMarker, __result));
            }
        }
    }

    // Normal taming does not require a Bond and is not blocked by past damage.
    // Medical-bond recruitment has a separate, player-harm-aware DoTend hook.
    [HarmonyPatch(typeof(InteractionWorker_RecruitAttempt), "Interacted")]
    public static class Patch_Taming
    {
        public static void Prefix(Pawn recipient, out bool __state)
        {
            __state = IsLatentWildShapeshifter(recipient)
                && recipient.Faction != Faction.OfPlayer;
        }

        public static void Postfix(Pawn recipient, bool __state)
        {
            if (!__state || recipient == null || recipient.Faction != Faction.OfPlayer)
            {
                return;
            }

            Pawn animal = recipient;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                RevealTamedShapeshifter(animal);
            });
        }

        internal static bool IsLatentWildShapeshifter(Pawn pawn)
        {
            return pawn != null
                && !pawn.Dead
                && AnimalPool.IsEligible(pawn)
                && !TransformUtility.IsTransformedAnimal(pawn)
                && pawn.health != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker) != null;
        }

        internal static void RevealTamedShapeshifter(Pawn animal)
        {
            // All acquisition routes share the lock, including normal taming
            // and treatment callbacks, not just manual button actions.
            if (!FormTransferUtility.TryBeginReturn(animal)) return;
            try { RevealTamedShapeshifterCore(animal); }
            finally { FormTransferUtility.EndReturn(animal); }
        }

        private static void RevealTamedShapeshifterCore(Pawn animal)
        {
            if (animal == null || animal.Destroyed || animal.Dead || !animal.Spawned
                || !animal.RaceProps.Animal || animal.Faction != Faction.OfPlayer || TransformUtility.IsTransformedAnimal(animal))
            {
                return;
            }

            GameComponent_AwakeningRecovery recovery = Current.Game?.GetComponent<GameComponent_AwakeningRecovery>();
            if (recovery == null) return;
            if (!recovery.TryClearRollback(animal))
            {
                Messages.Message("WildShift_AwakenCleanupFailed".Translate(), animal, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (recovery.IsCompleted(animal))
            {
                // The human already exists. Retry only unloading/removing the
                // old body, even after save/load or death of that human.
                FinishAwakeningCleanup(animal);
                return;
            }

            Map map = animal.Map;
            IntVec3 cell = animal.Position;
            Rot4 rotation = animal.Rotation;
            PawnKindDef animalKind = animal.kindDef;
            string animalLabel = animal.LabelShortCap;
            bool wasSelected = Find.Selector != null && Find.Selector.IsSelected(animal);

            Pawn human = null;
            try
            {
                var request = AnimalFormGender.CreateHumanRequest(animal);
                human = PawnGenerator.GeneratePawn(request);
                if (human != null && !AnimalFormGender.MatchesHumanRequest(human, request))
                {
                    Log.Error("[WildShift] Another generation hook replaced the selected awakening race; preserving the original animal.");
                    recovery.RollBack(animal, human);
                    return;
                }
                // Alternative races are generated without the colony faction
                // to avoid racial replacement, then join before map placement.
                if (human != null && human.Faction != Faction.OfPlayer) human.SetFaction(Faction.OfPlayer);
                if (human == null || TransformUtility.AddOrGetShapeshifter(human, animalKind) == null)
                {
                    recovery.RollBack(animal, human);
                    return;
                }
                GeneticsCompatibility.Remember(animal, human);
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Tamed shapeshifter generation failed; keeping the animal: " + ex);
                recovery.RollBack(animal, human);
                return;
            }

            // Like normal transformation, commit destination placement before
            // removing the only original body. Spawn callbacks may fail either
            // before or after registration; TrySpawn distinguishes those cases.
            if (!FormTransferUtility.TrySpawn(human, cell, map))
            {
                recovery.RollBack(animal, human);
                return;
            }
            // Generate/place first; keep all belongings safe before committing
            // removal of the animal. Failed/partial drops permit a later retry.
            if (!AwakeningPossessions.TryDropAll(animal))
            {
                recovery.RollBack(animal, human);
                Messages.Message("WildShift_AwakenDropFailed".Translate(), animal, MessageTypeDefOf.RejectInput, false);
                return;
            }
            // Commit before invoking arbitrary destruction callbacks. A failed
            // cleanup cannot turn this animal into a fresh awakening candidate.
            recovery.Complete(animal);
            if (!FormTransferUtility.DestroyEmptyForm(animal))
                Messages.Message("WildShift_AwakenCleanupFailed".Translate(), animal, MessageTypeDefOf.RejectInput, false);
            human.Rotation = rotation;

            if (wasSelected)
            {
                Find.Selector.Deselect(animal);
                Find.Selector.Select(human);
            }

            Messages.Message(
                "WildShift_MessageTamedReveal".Translate(animalLabel, animalKind.LabelCap),
                human,
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private static void FinishAwakeningCleanup(Pawn animal)
        {
            if (!AwakeningPossessions.TryDropAll(animal))
                Messages.Message("WildShift_AwakenDropFailed".Translate(), animal, MessageTypeDefOf.RejectInput, false);
            else if (!FormTransferUtility.DestroyEmptyForm(animal))
                Messages.Message("WildShift_AwakenCleanupFailed".Translate(), animal, MessageTypeDefOf.RejectInput, false);
        }
    }
}
