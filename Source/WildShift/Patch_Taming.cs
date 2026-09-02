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

            float chance = WildShiftMod.Settings != null ? WildShiftMod.Settings.fieldShapeshifterChance : 0.01f;
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

        private static bool IsLatentWildShapeshifter(Pawn pawn)
        {
            return pawn != null
                && !pawn.Dead
                && AnimalPool.IsEligible(pawn)
                && !TransformUtility.IsTransformedAnimal(pawn)
                && pawn.health != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker) != null;
        }

        private static void RevealTamedShapeshifter(Pawn animal)
        {
            if (animal == null || animal.Destroyed || animal.Dead || !animal.Spawned)
            {
                return;
            }

            Map map = animal.Map;
            IntVec3 cell = animal.Position;
            Rot4 rotation = animal.Rotation;
            PawnKindDef animalKind = animal.kindDef;
            string animalLabel = animal.LabelShortCap;
            bool wasSelected = Find.Selector != null && Find.Selector.IsSelected(animal);

            Pawn human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            TransformUtility.AddOrGetShapeshifter(human, animalKind);

            animal.DeSpawn(DestroyMode.Vanish);
            if (!animal.Destroyed)
            {
                animal.Destroy(DestroyMode.Vanish);
            }

            GenSpawn.Spawn(human, cell, map);
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
    }
}
