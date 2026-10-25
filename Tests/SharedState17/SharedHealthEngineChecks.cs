using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. This helper changes test data and invokes native production
    // operations; it does not patch, replace or simulate any gameplay result.
    public static class SharedHealthEngineChecks
    {
        public static void Run(Map map, Action<bool, string> check)
        {
            if (map == null || check == null) throw new ArgumentNullException();
            float oldDeathChance = WildShiftMod.Settings.deathChance;
            try
            {
                WildShiftMod.Settings.deathChance = 0f;
                OrdinaryBodyDeath(map, check);
                FatalDrug(map, check);
                PermanentWoundHealing(map, check);
            }
            finally
            {
                WildShiftMod.Settings.deathChance = oldDeathChance;
            }
        }

        private static void OrdinaryBodyDeath(Map map, Action<bool, string> check)
        {
            Pawn human = NewHuman(map, "SharedHealthOrdinary", -8);
            Pawn animal = Transform(map, human);
            string animalId = animal.GetUniqueLoadID();
            check(!SharedFormHealth.IsSharedLethalDeath(animal),
                "health/ordinary animal death is outside a shared drug death scope");
            animal.Kill(null);
            check(!human.Dead && human.Spawned && human.Map == map,
                "health/native animal Kill at zero death chance restores its living original");
            check(!animal.Dead && !animal.Destroyed && AnimalFormStorage.IsStored(animal)
                && TransformUtility.TryGetTransformedComp(animal)?.StoredPawn == null
                && TransformUtility.TryGetShapeshifterComp(human).FormStorage.StoredForm == animal,
                "health/ordinary death preserves the same cached animal without nesting its human");
            check(map.mapPawns.AllPawnsSpawned.Count(p => p.GetUniqueLoadID() == human.GetUniqueLoadID()) == 1,
                "health/ordinary death has exactly one active original human");
            Pawn reused = Transform(map, human);
            check(ReferenceEquals(reused, animal) && reused.GetUniqueLoadID() == animalId,
                "health/ordinary death cache can reactivate the exact same animal body");
            Require(TransformUtility.RevertToHuman(reused, false) == human,
                "ordinary death probe returns its reusable form", check);
        }

        private static void FatalDrug(Map map, Action<bool, string> check)
        {
            Pawn human = NewHuman(map, "SharedHealthFatalDrug", -12);
            Pawn animal = Transform(map, human);
            string humanId = human.GetUniqueLoadID();
            string animalId = animal.GetUniqueLoadID();
            HediffDef fatalDef = DefDatabase<HediffDef>.GetNamed("WildShift_TestSharedLethalState");
            Hediff sample = HediffMaker.MakeHediff(fatalDef, human);
            Require(SharedFormHealth.IsSupported(sample),
                "health/startup fixture drug definition is present in production supported-drug discovery", check);
            Thing dose = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WildShift_TestSharedLethalDrug"));
            dose.Ingested(animal, 0f);
            check(human.Dead && human.GetUniqueLoadID() == humanId,
                "health/native ingestion of a lethal drug kills the exact original despite zero body death chance");
            check(TransformUtility.TryGetTransformedComp(animal)?.StoredPawn == null
                && SharedFormIdentity.HumanFor(animal) == null,
                "health/lethal ingestion leaves no dead original stranded in the active form");
            check(!animal.Spawned && !animal.Destroyed && AnimalFormStorage.IsStored(animal)
                && animal.GetUniqueLoadID() == animalId,
                "health/lethal ingestion retains one inactive animal body owned by the original");
            check(!map.mapPawns.AllPawnsSpawned.Any(p => p.GetUniqueLoadID() == humanId)
                && map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                    .Count(c => c.InnerPawn.GetUniqueLoadID() == humanId) == 1,
                "health/lethal ingestion creates exactly one original corpse and no active human copy");
            check(!map.mapPawns.AllPawnsSpawned.Any(p => p.GetUniqueLoadID() == animalId),
                "health/lethal ingestion leaves no duplicate active animal");
            check(!animal.health.hediffSet.hediffs.Any(h => h.def.defName == "WildShift_TestSharedTrailingDrugState"
                    || h.def.defName == "DrugOverdose" || h.def.defName == "LuciferiumAddiction")
                && !animal.needs.AllNeeds.OfType<Need_Chemical>().Any(),
                "health/post-lethal native drug callbacks do not mutate the inactive cached animal");
        }

        private static void PermanentWoundHealing(Map map, Action<bool, string> check)
        {
            Pawn human = NewHuman(map, "SharedHealthHealing", -16);
            Hediff originalScar = Scar(human);
            ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Luciferium")).Ingested(human, 0f);
            Hediff high = human.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("LuciferiumHigh"));
            HediffComp_HealPermanentWounds healing = high.TryGetComp<HediffComp_HealPermanentWounds>();
            Require(healing != null, "health/native luciferium owns its permanent-wound healing component", check);
            Pawn animal = Transform(map, human);
            Hediff animalScar = Scar(animal);
            Hediff_SharedFormDrugEffect projection = animal.health.hediffSet.hediffs
                .OfType<Hediff_SharedFormDrugEffect>().Single(p => ReferenceEquals(p.source, high));
            check(high.pawn == human && projection.pawn == animal && !ReferenceEquals(high, projection)
                && !animal.health.hediffSet.hediffs.Contains(high),
                "health/healing source and projection have distinct native hediff owners");
            check(animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                    .Count(p => ReferenceEquals(p.source, high)) == 1,
                "health/active body has one projection of the canonical luciferium source");
            // Test setup only: remove the 15-30-day wait without replacing the
            // native comp tick, random wound selection or HealthUtility.Cure.
            AccessTools.Field(typeof(HediffComp_HealPermanentWounds), "ticksToHeal").SetValue(healing, 1);
            int sourceAge = high.ageTicks;
            float originalSeverity = originalScar.Severity;
            int startedAt = Find.TickManager.TicksGame;
            int waited = 0;
            // Native 1.7 animal TickInterval is coalesced at a 15-tick rate.
            // One DoSingleTick may only perform the per-tick work. Observe the
            // first real source interval instead of assuming it ran already.
            while (high.ageTicks == sourceAge && waited < 60 && animal.Spawned && !animal.Dead)
            {
                Find.TickManager.DoSingleTick();
                waited++;
            }
            int gameElapsed = Find.TickManager.TicksGame - startedAt;
            int sourceElapsed = high.ageTicks - sourceAge;
            Require(sourceElapsed > 0,
                "health/native scheduled drug interval arrives within 60 actual game ticks", check);
            check(!animal.health.hediffSet.hediffs.Contains(animalScar),
                "health/native luciferium comp heals the active animal's permanent scar");
            check(human.health.hediffSet.hediffs.Contains(originalScar)
                && Mathf.Approximately(originalScar.Severity, originalSeverity),
                "health/the same healing event does not also heal the inactive human scar");
            check(high.pawn == human && human.health.hediffSet.hediffs.Contains(high)
                && ReferenceEquals(high.TryGetComp<HediffComp_HealPermanentWounds>(), healing)
                && (int)AccessTools.Field(typeof(HediffComp_HealPermanentWounds), "ticksToHeal").GetValue(healing) > 1,
                "health/healing keeps and resets the one native timer on the original source");
            check(sourceElapsed <= gameElapsed,
                "health/first scheduled interval advances canonical drug once (" + sourceElapsed
                + " source ticks for " + gameElapsed + " actual game ticks)");
            check(!animal.health.hediffSet.hediffs.Any(h => h.def.defName == "LuciferiumHigh"
                    || h.def.defName == "LuciferiumAddiction")
                && !animal.needs.AllNeeds.OfType<Need_Chemical>().Any(),
                "health/native healing creates no separate animal drug, addiction or chemical need");
            Require(TransformUtility.RevertToHuman(animal, false) == human,
                "health/healing probe returns to its exact original human", check);
            check(human.health.hediffSet.hediffs.Contains(originalScar)
                && ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(high.def), high),
                "health/return preserves the unhealed original scar and original drug object");
        }

        private static Pawn NewHuman(Map map, string name, int offset)
        {
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                fixedGender: Gender.Male, forcedXenotype: XenotypeDefOf.Baseliner));
            human.Name = new NameSingle(name);
            human.health.RemoveAllHediffs();
            IntVec3 requested = map.Center + new IntVec3(offset, 0, 0);
            IntVec3 cell = requested.InBounds(map) && requested.Standable(map)
                ? requested : CellFinder.RandomClosewalkCellNear(map.Center, map, 6);
            GenSpawn.Spawn(human, cell, map);
            return human;
        }

        private static Pawn Transform(Map map, Pawn human)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
            TransformUtility.AddOrGetShapeshifter(human, kind);
            if (!TransformUtility.TransformToAnimal(human, kind))
                throw new InvalidOperationException("Shared-health fixture production transformation failed.");
            return map.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == human);
        }

        private static Hediff Scar(Pawn pawn)
        {
            BodyPartRecord limb = pawn.health.hediffSet.GetNotMissingParts()
                .First(p => p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore));
            Hediff scar = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, limb);
            scar.Severity = 1f;
            pawn.health.AddHediff(scar, limb);
            HediffComp_GetsPermanent permanence = scar.TryGetComp<HediffComp_GetsPermanent>();
            if (permanence == null) throw new InvalidOperationException("Native Cut has no permanent-scar component.");
            permanence.isPermanentInt = true;
            permanence.permanentDamageThreshold = 9999f;
            permanence.SetPainCategory(PainCategory.Painless);
            return scar;
        }

        private static void Require(bool passed, string label, Action<bool, string> check)
        {
            check(passed, label);
            if (!passed) throw new InvalidOperationException("Shared-health fixture prerequisite: " + label);
        }
    }
}
