using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Native ability effect application and the native final ingest
    // toil are invoked directly; targeting UI, walking and chewing are outside
    // this helper. No native method or production result is replaced.
    public static class SharedBuffEngineChecks
    {
        public static void Run(Map map, Action<bool, string> check)
        {
            if (map == null || check == null) throw new ArgumentNullException();
            BrainBoundOfficialEffects(map, check);
            NativeFoodFinalization(map, check);
            NativeInvisibilityAndCommands(map, check);
        }

        private static void BrainBoundOfficialEffects(Map map, Action<bool, string> check)
        {
            Pawn caster = NewHuman(map, "SharedBuffCaster", -20);
            Pawn human = NewHuman(map, "SharedBuffBrain", -24);
            HediffDef workDef = DefDatabase<HediffDef>.GetNamed("WorkDrive");
            HediffDef preachDef = DefDatabase<HediffDef>.GetNamed("PreachHealth");
            BodyPartRecord humanBrain = human.health.hediffSet.GetBrain();
            Require(humanBrain != null, "buff/original has a real native brain", check);

            // Real Ideology ability XML has onlyBrain=true. Applying its native
            // effect, rather than manufacturing a Part-null lookalike, catches
            // accidental exclusion of these actual official effects.
            ApplyNativeEffect(caster, "WorkDrive", human);
            Hediff work = human.health.hediffSet.GetFirstHediffOfDef(workDef);
            Require(work != null && work.Part == humanBrain && SharedFormHealth.IsSupported(work),
                "buff/actual WorkDrive effect is approved while bound to the original brain", check);
            var workTimer = work.TryGetComp<HediffComp_Disappears>();
            Require(workTimer != null && workTimer.ticksToDisappear > 0,
                "buff/native WorkDrive ability configures its real duration", check);

            // Physical implants remain attached to the preserved human. The
            // reviewed brain-buff exception must not become anatomy copying.
            BodyPartRecord arm = human.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "Arm");
            HediffDef implantDef = DefDatabase<HediffDef>.GetNamed("BionicArm");
            Hediff implant = human.health.AddHediff(implantDef, arm);
            int beforeTransform = workTimer.ticksToDisappear;
            Pawn animal = Transform(map, human);
            check(work.Part == humanBrain && work.pawn == human
                    && human.health.hediffSet.hediffs.Contains(work)
                    && workTimer.ticksToDisappear == beforeTransform,
                "buff/transform preserves the exact original brain effect and configured duration");
            Hediff_SharedFormDrugEffect projection = animal.health.hediffSet.hediffs
                .OfType<Hediff_SharedFormDrugEffect>().Single(p => ReferenceEquals(p.source, work));
            check(projection.Part == null && projection.pawn == animal
                    && !animal.health.hediffSet.hediffs.Contains(work)
                    && ReferenceEquals(animal.health.hediffSet.GetFirstHediffOfDef(workDef), work),
                "buff/animal gets a whole-body view while native lookup returns the brain-bound source");
            check(implant.pawn == human && implant.Part == arm
                    && human.health.hediffSet.hediffs.Contains(implant)
                    && !animal.health.hediffSet.hediffs.Any(h => h.def == implantDef)
                    && SharedFormHealth.IsSupported(implant)
                    && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Count(p => ReferenceEquals(p.source, implant)) == 1,
                "buff/human bionic implant stays attached to its owner while one systemic effect view is shared");

            // Invoke the native effect component directly to inspect active-
            // body part mapping. This is not a claim that the vanilla ability's
            // target selector permits animals.
            ApplyNativeEffect(caster, "PreachHealth", animal);
            Hediff preach = human.health.hediffSet.GetFirstHediffOfDef(preachDef);
            Require(preach != null && preach.pawn == human && preach.Part == humanBrain,
                "buff/native PreachHealth effect on an active form maps only to the original brain", check);
            check(!animal.health.hediffSet.hediffs.Any(h => h.def == preachDef)
                    && animal.health.hediffSet.HasHediff(preachDef)
                    && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Count(p => ReferenceEquals(p.source, preach)) == 1,
                "buff/active native brain effect has one canonical owner and one animal projection");
            ApplyNativeEffect(caster, "PreachHealth", animal);
            Hediff replacement = human.health.hediffSet.GetFirstHediffOfDef(preachDef);
            check(replacement != null && !ReferenceEquals(replacement, preach)
                    && !human.health.hediffSet.hediffs.Contains(preach)
                    && human.health.hediffSet.hediffs.Count(h => h.def == preachDef) == 1
                    && !animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Any(p => ReferenceEquals(p.source, preach)),
                "buff/native replaceExisting removes the old canonical brain effect without stale views");

            int sourceAge = work.ageTicks;
            int startedAt = Find.TickManager.TicksGame;
            int waited = 0;
            while (work.ageTicks == sourceAge && waited < 60 && animal.Spawned && !animal.Dead)
            {
                Find.TickManager.DoSingleTick();
                waited++;
            }
            int elapsed = Find.TickManager.TicksGame - startedAt;
            Require(work.ageTicks > sourceAge,
                "buff/official timed source progresses through actual native animal scheduling", check);
            check(work.ageTicks - sourceAge <= elapsed && workTimer.ticksToDisappear <= beforeTransform
                    && workTimer.ticksToDisappear >= beforeTransform - elapsed,
                "buff/official source age and disappearance advance at most once per actual game tick");
            int timerAtReturn = workTimer.ticksToDisappear;
            Require(TransformUtility.RevertToHuman(animal, false) == human,
                "buff/official brain-effect form returns through production", check);
            check(ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(workDef), work)
                    && work.Part == humanBrain && workTimer.ticksToDisappear == timerAtReturn,
                "buff/return preserves original brain effect identity and remaining duration");
            Pawn reused = Transform(map, human);
            check(ReferenceEquals(reused, animal) && workTimer.ticksToDisappear == timerAtReturn
                    && reused.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Count(p => ReferenceEquals(p.source, work)) == 1,
                "buff/repeated transform reuses the body without refreshing or doubling its original buff");
            Require(TransformUtility.RevertToHuman(reused, false) == human,
                "buff/brain probe leaves its original alive on the map", check);
        }

        private static void NativeFoodFinalization(Map map, Action<bool, string> check)
        {
            Pawn human = NewHuman(map, "SharedBuffFood", -28);
            Pawn animal = Transform(map, human);
            HediffDef buffDef = DefDatabase<HediffDef>.GetNamed("WildShift_TestSharedFoodBuff");
            Thing meal = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WildShift_TestSharedBuffMeal"));
            GenSpawn.Spawn(meal, animal.Position, map);
            Require(!meal.def.IsDrug && SharedFormBuffs.IsSupportedDef(buffDef),
                "buff/engine meal is non-drug food with a reviewed native opt-in outcome", check);
            Require(animal.needs.food != null && human.needs.food != null,
                "buff/both preserved bodies retain their actual native food trackers", check);
            animal.needs.food.CurLevel = animal.needs.food.MaxLevel * 0.1f;
            float actorBefore = animal.needs.food.CurLevel;
            float originalBefore = human.needs.food.CurLevel;
            float nutrition = FoodUtility.NutritionForEater(animal, meal);
            float expected = Mathf.Min(animal.needs.food.MaxLevel, actorBefore + nutrition);
            float recordBefore = animal.records.GetValue(RecordDefOf.NutritionEaten);

            // Setup a real native Job target. Execute only the native final
            // ingestion action so walking/AI delays do not influence this check.
            // It calls Thing.Ingested, consumes the item, increases actor food
            // and records NutritionEaten itself; the helper does none of those.
            Job previousJob = animal.jobs.curJob;
            try
            {
                animal.jobs.curJob = JobMaker.MakeJob(JobDefOf.Ingest, meal);
                Toil finalize = Toils_Ingest.FinalizeIngest(animal, TargetIndex.A);
                finalize.actor = animal;
                finalize.initAction();
            }
            finally
            {
                animal.jobs.curJob = previousJob;
            }
            check(meal.Destroyed && Mathf.Abs(animal.needs.food.CurLevel - expected) < 0.00001f,
                "buff/native final ingest consumes the food and grants nutrition to the active animal");
            check((SharedFormBiology.SharesNeeds(animal)
                        ? ReferenceEquals(human.needs.food, animal.needs.food) && Mathf.Abs(human.needs.food.CurLevel - expected) < .00001f
                        : Mathf.Approximately(human.needs.food.CurLevel, originalBefore))
                    && Mathf.Abs(animal.records.GetValue(RecordDefOf.NutritionEaten) - recordBefore - nutrition) < 0.00001f,
                "buff/food uses the one canonical shared hunger state when enabled and keeps the native eater record");
            Hediff source = human.health.hediffSet.GetFirstHediffOfDef(buffDef);
            Require(source != null && source.pawn == human && source.Part == null,
                "buff/full native food ingestion stores its effect on the original human", check);
            check(human.health.hediffSet.hediffs.Count(h => h.def == buffDef) == 1
                    && !animal.health.hediffSet.hediffs.Any(h => h.def == buffDef)
                    && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Count(p => ReferenceEquals(p.source, source)) == 1,
                "buff/full native meal produces one source and one distinct active-body projection");
            HediffComp_Disappears timer = source.TryGetComp<HediffComp_Disappears>();
            Require(timer != null && timer.ticksToDisappear > 0,
                "buff/native meal source owns its configured disappearance timer", check);
            int remaining = timer.ticksToDisappear;
            Require(TransformUtility.RevertToHuman(animal, false) == human,
                "buff/food form returns through production", check);
            Pawn reused = Transform(map, human);
            check(ReferenceEquals(reused, animal)
                    && ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(buffDef), source)
                    && timer.ticksToDisappear == remaining
                    && reused.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Count(p => ReferenceEquals(p.source, source)) == 1,
                "buff/repeated food-form returns preserve the same canonical source and timer");
            Require(TransformUtility.RevertToHuman(reused, false) == human,
                "buff/food probe leaves its original alive on the map", check);
        }

        private static void NativeInvisibilityAndCommands(Map map, Action<bool,string> check)
        {
            Pawn caster = NewHuman(map, "ExpandedCommandCaster", -20);
            Pawn recipient = NewHuman(map, "ExpandedCommandRecipient", -23);
            Pawn casterBody = Transform(map, caster), recipientBody = Transform(map, recipient);
            ApplyNativeEffect(caster, "Invisibility", recipientBody);
            Hediff invisible = recipient.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("PsychicInvisibility"));
            Require(invisible != null && invisible.pawn == recipient,
                "buff/native invisibility effect on animal has one canonical original source", check);
            HediffComp_Invisibility comp = invisible.TryGetComp<HediffComp_Invisibility>();
            Hediff_SharedFormDrugEffect view = recipientBody.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                .Single(p => ReferenceEquals(p.source, invisible));
            check(comp != null && ReferenceEquals(view.TryGetComp<HediffComp_Invisibility>(), comp)
                && comp.Pawn == recipientBody && !comp.PsychologicallyVisible,
                "buff/native invisibility component uses active animal and actual invisibility state");
            ApplyNativeEffect(caster, "CombatCommand", casterBody);
            for (int tick = 0; tick < 20 && !recipient.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("CombatCommandBuff")); tick++)
                Find.TickManager.DoSingleTick();
            HediffDef buffDef = DefDatabase<HediffDef>.GetNamed("CombatCommandBuff");
            Hediff buff = recipient.health.hediffSet.GetFirstHediffOfDef(buffDef);
            Require(buff != null && buff.pawn == recipient,
                "buff/native command aura reaches transformed human recipient", check);
            HediffComp_Link link = buff.TryGetComp<HediffComp_Link>();
            check(link != null && link.other == caster && !link.CompShouldRemove
                && recipientBody.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(p => ReferenceEquals(p.source,buff)) == 1,
                "buff/command source link retains canonical identity and one active-body view");
            IntVec3 far = map.Center + new IntVec3(24,0,0);
            casterBody.Position = far.InBounds(map) && far.Standable(map) ? far : CellFinder.RandomClosewalkCellNear(far,map,3);
            for (int tick=0;tick<60 && recipient.health.hediffSet.HasHediff(buffDef);tick++) Find.TickManager.DoSingleTick();
            check(!recipient.health.hediffSet.HasHediff(buffDef)
                && !recipientBody.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(p => p.source == buff),
                "buff/moving actual command source out of range removes canonical recipient buff and view");
            Require(TransformUtility.RevertToHuman(casterBody,false)==caster
                && TransformUtility.RevertToHuman(recipientBody,false)==recipient,
                "buff/invisibility and command probes return both exact humans",check);
        }

        private static void ApplyNativeEffect(Pawn caster, string name, Pawn target)
        {
            var ability = new Ability(caster, DefDatabase<AbilityDef>.GetNamed(name));
            var effect = ability.CompOfType<CompAbilityEffect_GiveHediff>();
            if (effect == null) throw new InvalidOperationException("Native ability lacks GiveHediff: " + name);
            effect.Apply(new LocalTargetInfo(target), LocalTargetInfo.Invalid);
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
                throw new InvalidOperationException("Shared-buff fixture production transformation failed.");
            return map.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == human);
        }

        private static void Require(bool passed, string label, Action<bool, string> check)
        {
            check(passed, label);
            if (!passed) throw new InvalidOperationException("Shared-buff fixture prerequisite: " + label);
        }
    }
}
