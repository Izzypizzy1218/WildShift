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
            if (__result == null || __result.Faction == Faction.OfPlayer)
            {
                return;
            }

            if (!AnimalPool.IsEligible(__result))
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

    // VERIFY: Taming success in 1.6 is detected here by observing the animal's faction switch to Faction.OfPlayer.
    [HarmonyPatch(typeof(Pawn), "SetFaction")]
    public static class Patch_Taming
    {
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (__instance == null || newFaction != Faction.OfPlayer || __instance.Dead)
            {
                return;
            }

            if (!AnimalPool.IsEligible(__instance) || TransformUtility.IsTransformedAnimal(__instance))
            {
                return;
            }

            Hediff marker = __instance.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker);
            if (marker == null)
            {
                return;
            }

            Pawn animal = __instance;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                RevealTamedShapeshifter(animal);
            });
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
