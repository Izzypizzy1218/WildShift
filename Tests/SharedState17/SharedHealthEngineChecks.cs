using System;
using System.Collections.Generic;
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
                ImmediateSourceCache(map, check);
                NativeExpandedEffects(map, check);
                ExpiredSourceAfterReturn(map, check);
                NativeDeathRefusal(map, check);
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
                && (!SharedFormBiology.SharesNeeds(animal) || animal.needs.AllNeeds.OfType<Need_Chemical>()
                    .All(n => human.needs.AllNeeds.Contains(n))),
                "health/native healing creates no separately owned animal drug, addiction or chemical need");
            Require(TransformUtility.RevertToHuman(animal, false) == human,
                "health/healing probe returns to its exact original human", check);
            check(human.health.hediffSet.hediffs.Contains(originalScar)
                && ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(high.def), high),
                "health/return preserves the unhealed original scar and original drug object");
        }

        private static void ImmediateSourceCache(Map map, Action<bool,string> check)
        {
            Pawn human = NewHuman(map, "ExpandedImmediateCache", -15);
            foreach (Trait trait in human.story.traits.allTraits.ToList()) human.story.traits.RemoveTrait(trait);
            Pawn animal = Transform(map,human);
            int tick = Find.TickManager.TicksGame;
            float before = animal.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            Hediff charge = human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("NeuralSupercharge"));
            check(Near(animal.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness), before+.1f),
                "health/direct original-source buff addition invalidates cached animal consciousness in the same tick");
            human.health.RemoveHediff(charge);
            check(Near(animal.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),before),
                "health/direct original-source buff removal clears animal consciousness in the same tick");
            var flame = new DamageInfo(DamageDefOf.Flame,4f);
            float fire = animal.health.FactorForDamage(flame);
            Hediff metal = human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Metalblood"));
            check(Near(animal.health.FactorForDamage(flame),fire*4f),
                "health/direct original-source damage factor addition replaces cached animal factor immediately");
            human.health.RemoveHediff(metal);
            check(Near(animal.health.FactorForDamage(flame),fire),
                "health/direct original-source removal restores cached animal damage factor immediately");
            Hediff cut = animal.health.AddHediff(HediffDefOf.Cut,animal.RaceProps.body.corePart);
            cut.Severity=2f;
            float pain=animal.health.hediffSet.PainTotal;
            Hediff painstopper=human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Painstopper"),human.health.hediffSet.GetBrain());
            check(pain>0f && Near(animal.health.hediffSet.PainTotal,0f),
                "health/direct original implant addition updates cached animal pain immediately");
            human.health.RemoveHediff(painstopper);
            check(Near(animal.health.hediffSet.PainTotal,pain) && Find.TickManager.TicksGame==tick,
                "health/source implant removal restores pain with no game tick or stale projection");
            animal.health.RemoveHediff(cut);
            Require(TransformUtility.RevertToHuman(animal,false)==human,
                "health/immediate-cache probe returns its exact original",check);
        }

        private static void NativeExpandedEffects(Map map, Action<bool, string> check)
        {
            string[] items = { "JuggernautSerum", "MetalbloodSerum", "VoidsightSerum", "MindNumbSerum" };
            string[] effects = { "JuggernautSerum", "Metalblood", "Voidsight", "MindNumbSerum" };
            for (int i = 0; i < effects.Length; i++)
            {
                Pawn human = NewHuman(map, "ExpandedHealth" + effects[i], -18 + i * 2);
                foreach (Trait trait in human.story.traits.allTraits.ToList()) human.story.traits.RemoveTrait(trait);
                Pawn animal = Transform(map, human);
                StatDef melee = DefDatabase<StatDef>.GetNamed("MeleeDamageFactor");
                StatDef incoming = DefDatabase<StatDef>.GetNamed("IncomingDamageFactor");
                StatDef psychic = DefDatabase<StatDef>.GetNamed("PsychicSensitivity");
                float beforeMelee = animal.GetStatValue(melee), beforeIncoming = animal.GetStatValue(incoming);
                float beforePsychic = animal.GetStatValue(psychic);
                float beforeConsciousness = animal.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                var flame = new DamageInfo(DamageDefOf.Flame, 4f);
                float beforeFire = animal.health.FactorForDamage(flame);
                ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(items[i])).Ingested(animal, 0f);
                HediffDef def = DefDatabase<HediffDef>.GetNamed(effects[i]);
                Hediff source = human.health.hediffSet.GetFirstHediffOfDef(def);
                Require(source != null && source.pawn == human && SharedFormHealth.IsSupported(source),
                    "health/real serum ingestion is canonical and supported: " + effects[i], check);
                Hediff_SharedFormDrugEffect view = animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                    .Single(p => ReferenceEquals(p.source, source));
                HediffComp_DisappearsDisableable timer = source.TryGetComp<HediffComp_DisappearsDisableable>();
                Require(timer != null && ReferenceEquals(view.TryGetComp<HediffComp_DisappearsDisableable>(), timer)
                    && timer.parent == source, "health/serum exposes its exact canonical native component: " + effects[i], check);
                if (i == 0) check(Near(animal.GetStatValue(melee), beforeMelee * 1.5f),
                    "health/native Juggernaut ingestion gives animal melee damage x1.5");
                if (i == 1)
                {
                    check(Near(animal.GetStatValue(incoming), beforeIncoming * .5f),
                        "health/native Metalblood ingestion gives animal incoming damage x0.5");
                    check(Near(animal.health.FactorForDamage(flame), beforeFire * 4f),
                        "health/native Metalblood also retains animal fire vulnerability x4");
                }
                if (i == 2) check(Near(animal.GetStatValue(psychic), beforePsychic * 1.25f),
                    "health/native Voidsight ingestion gives animal psychic sensitivity x1.25");
                if (i == 3) check(Near(animal.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness), beforeConsciousness - .05f),
                    "health/native MindNumb ingestion retains consciousness penalty");
                int sourceId = source.loadID, age = source.ageTicks, remaining = timer.ticksToDisappear;
                int started = Find.TickManager.TicksGame;
                for (int tick = 0; tick < 30; tick++) Find.TickManager.DoSingleTick();
                int elapsed = Find.TickManager.TicksGame - started;
                check(source.ageTicks > age && source.ageTicks - age <= elapsed
                    && timer.ticksToDisappear < remaining && timer.ticksToDisappear >= remaining - elapsed,
                    "health/native serum timer and age advance once: " + effects[i]);
                remaining = timer.ticksToDisappear;
                Require(TransformUtility.RevertToHuman(animal, false) == human,
                    "health/serum returns to exact source human: " + effects[i], check);
                check(source.loadID == sourceId && source.pawn == human && timer.ticksToDisappear == remaining
                    && ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(def), source),
                    "health/return retains native serum identity and duration: " + effects[i]);
                Pawn reused = Transform(map, human);
                check(ReferenceEquals(reused, animal) && source.loadID == sourceId && timer.ticksToDisappear == remaining
                    && reused.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(p => ReferenceEquals(p.source, source)) == 1,
                    "health/repeated serum transformation has one source and one view: " + effects[i]);
                Current.Game.GetComponent<ExpandedSharedHealthPersistence>().Register(human, reused, source);
            }
            Pawn drinker = NewHuman(map, "ExpandedAlcohol", -9);
            foreach (Trait trait in drinker.story.traits.allTraits.ToList()) drinker.story.traits.RemoveTrait(trait);
            Hediff alcohol = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("AlcoholHigh"), drinker);
            alcohol.Severity = .75f;
            drinker.health.AddHediff(alcohol);
            Pawn drunk = Transform(map, drinker);
            check(SharedFormHealth.IsSupported(alcohol)
                && Near(drunk.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),
                    drinker.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness)),
                "health/native alcohol subclass applies its consciousness penalty to animal");
            for (int tick = 0; tick < 330 && !drinker.health.hediffSet.HasHediff(HediffDefOf.Hangover); tick++)
                Find.TickManager.DoSingleTick();
            Hediff hangover = drinker.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Hangover);
            check(hangover != null && hangover.pawn == drinker
                && drunk.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(p => ReferenceEquals(p.source, hangover)) == 1,
                "health/native alcohol interval creates one canonical hangover with one active view");
            Require(TransformUtility.RevertToHuman(drunk, false) == drinker,
                "health/alcohol regression leaves original alive", check);
        }

        private static void ExpiredSourceAfterReturn(Map map, Action<bool,string> check)
        {
            Pawn human = NewHuman(map,"ExpandedExpiredSource",-4);
            Hediff source = human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("WorkFocus"));
            HediffComp_Disappears timer = source.TryGetComp<HediffComp_Disappears>();
            Require(timer!=null,"health/expired-source regression uses a real native timed buff",check);
            timer.ticksToDisappear=600;
            Pawn body=Transform(map,human);
            BodyPartRecord leg=body.health.hediffSet.GetNotMissingParts().First(p=>p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore));
            Hediff physical=body.health.AddHediff(DefDatabase<HediffDef>.GetNamed("BionicLeg"),leg);
            string physicalId=physical.GetUniqueLoadID();
            check(body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(v=>ReferenceEquals(v.source,source)),
                "health/expired-source fixture begins with a real canonical buff projection");
            Require(TransformUtility.RevertToHuman(body,false)==human,
                "health/expired-source regression returns its original",check);
            check(!body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any()
                && body.health.hediffSet.hediffs.Contains(physical) && physical.pawn==body
                && timer.ticksToDisappear==600,
                "health/release removes only borrowed health views while preserving physical implant and source duration");
            timer.ticksToDisappear=1;
            for(int tick=0;tick<60&&human.health.hediffSet.hediffs.Contains(source);tick++)Find.TickManager.DoSingleTick();
            check(!human.health.hediffSet.hediffs.Contains(source)
                && !body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(),
                "health/buff expiration on restored human leaves no suspended-body dangling source view");
            Current.Game.GetComponent<ExpandedSharedHealthPersistence>().RegisterExpired(human,body,source.GetUniqueLoadID(),physicalId);
        }

        private static bool Near(float a, float b) { return Mathf.Abs(a - b) < .0001f; }

        private static void NativeDeathRefusal(Map map, Action<bool,string> check)
        {
            Pawn human = NewHuman(map, "ExpandedDeathRefusal", -6);
            string id = human.GetUniqueLoadID();
            var refusal = (Hediff_DeathRefusal)human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("DeathRefusal"));
            refusal.SetUseAmountDirect(2);
            Pawn animal = Transform(map, human);
            check(refusal.pawn == human && refusal.UsesLeft == 2,
                "health/death refusal retains one canonical original and both uses during transformation");
            float previous = WildShiftMod.Settings.deathChance;
            try
            {
                WildShiftMod.Settings.deathChance = 1f;
                animal.Kill(null);
            }
            finally { WildShiftMod.Settings.deathChance = previous; }
            Require(human.Dead && human.Corpse != null && human.Corpse.Spawned && refusal.pawn == human,
                "health/fatal animal death returns the exact original corpse with its native death refusal", check);
            Command_Action command = refusal.GetGizmos().OfType<Command_Action>().FirstOrDefault();
            Require(command != null, "health/original corpse exposes the actual native self-resurrection command", check);
            command.action();
            for (int tick = 0; tick < 900 && human.Dead; tick++) Find.TickManager.DoSingleTick();
            check(!human.Dead && human.Spawned && human.GetUniqueLoadID() == id
                && refusal.UsesLeft == 1 && refusal.pawn == human,
                "health/native self-resurrection restores the same original and consumes exactly one canonical use");
            check(TransformUtility.TryGetTransformedComp(animal)?.StoredPawn == null
                && !map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().Any(c => c.InnerPawn == human),
                "health/death refusal leaves no nested original or duplicate original corpse");
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

    public sealed class ExpandedSharedHealthPersistence : GameComponent
    {
        private List<Pawn> humans = new List<Pawn>(), bodies = new List<Pawn>();
        private List<Hediff> sources = new List<Hediff>();
        private List<int> ids = new List<int>(), remaining = new List<int>(), ages = new List<int>();
        private List<Pawn> expiredHumans = new List<Pawn>(), expiredBodies = new List<Pawn>();
        private List<string> expiredIds = new List<string>(), physicalIds = new List<string>();
        public ExpandedSharedHealthPersistence(Game game) { }
        public void Register(Pawn human, Pawn body, Hediff source)
        { humans.Add(human); bodies.Add(body); sources.Add(source); }
        public void RegisterExpired(Pawn human,Pawn body,string expiredId,string physicalId)
        {expiredHumans.Add(human);expiredBodies.Add(body);expiredIds.Add(expiredId);physicalIds.Add(physicalId);}
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                ids = sources.Select(s => s.loadID).ToList();
                remaining = sources.Select(s => s.TryGetComp<HediffComp_DisappearsDisableable>().ticksToDisappear).ToList();
                ages = sources.Select(s => s.ageTicks).ToList();
            }
            Scribe_Collections.Look(ref humans, "expandedHealthHumans", LookMode.Reference);
            Scribe_Collections.Look(ref bodies, "expandedHealthBodies", LookMode.Reference);
            Scribe_Collections.Look(ref sources, "expandedHealthSources", LookMode.Reference);
            Scribe_Collections.Look(ref ids, "expandedHealthIds", LookMode.Value);
            Scribe_Collections.Look(ref remaining, "expandedHealthRemaining", LookMode.Value);
            Scribe_Collections.Look(ref ages, "expandedHealthAges", LookMode.Value);
            Scribe_Collections.Look(ref expiredHumans,"expandedExpiredHumans",LookMode.Reference);
            Scribe_Collections.Look(ref expiredBodies,"expandedExpiredBodies",LookMode.Reference);
            Scribe_Collections.Look(ref expiredIds,"expandedExpiredIds",LookMode.Value);
            Scribe_Collections.Look(ref physicalIds,"expandedPhysicalIds",LookMode.Value);
        }
        public void Verify(Action<bool,string> check)
        {
            for (int i = 0; i < (sources?.Count ?? 0); i++)
            {
                Hediff source = sources[i];
                Pawn body = bodies[i], human = humans[i];
                check(source != null && source.loadID == ids[i] && source.pawn == human
                    && human.health.hediffSet.hediffs.Contains(source), "health/reload preserves exact serum source identity " + i);
                if (source == null) continue;
                HediffComp_DisappearsDisableable timer = source.TryGetComp<HediffComp_DisappearsDisableable>();
                check(timer != null && timer.parent == source && timer.ticksToDisappear == remaining[i]
                    && source.ageTicks == ages[i], "health/reload preserves exact serum timer and component owner " + i);
                Hediff_SharedFormDrugEffect view = body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                    .SingleOrDefault(v => ReferenceEquals(v.source, source));
                check(view != null && ReferenceEquals(view.TryGetComp<HediffComp_DisappearsDisableable>(), timer),
                    "health/reload reconstructs one non-owning serum component view " + i);
            }
            for(int i=0;i<(expiredHumans?.Count??0);i++)
            {
                Pawn human=expiredHumans[i],body=expiredBodies[i];
                check(human!=null&&body!=null&&AnimalFormStorage.IsStored(body)
                    && !human.health.hediffSet.hediffs.Any(h=>h.GetUniqueLoadID()==expiredIds[i])
                    && !body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(),
                    "health/reload has no suspended projection referencing the expired original source "+i);
                check(body!=null&&body.health.hediffSet.hediffs.Any(h=>h.GetUniqueLoadID()==physicalIds[i]&&h.pawn==body),
                    "health/reload retains actual cached-animal prosthesis after view-only cleanup "+i);
            }
        }
    }
}
