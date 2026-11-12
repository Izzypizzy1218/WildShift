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
    // TEST ONLY: native gene/need/trait state and the real transform path.
    public static class SharedBiologyEngineChecks
    {
        public static void Run(Map map, Action<bool, string> check)
        {
            if (map == null || check == null) throw new ArgumentNullException();
            CallbackGate(check);
            CanonicalMutationCaches(map, check);
            GeneFlagsAndClocks(map, check);
            NeedLevelsMetabolismAndTraits(map, check);
            WorkRestrictionsAndTraitPrice(map, check);
            FertilityAndConception(map, check);
        }

        private static void CanonicalMutationCaches(Map map, Action<bool,string> check)
        {
            Pawn human=NewHuman(map,"SharedBiologyMutations",-28);
            Pawn animal=Transform(map,human);
            GeneDef quick=DefDatabase<GeneDef>.GetNamed("MoveSpeed_Quick");
            GeneDef fingers=DefDatabase<GeneDef>.GetNamed("ElongatedFingers");
            TraitDef speed=DefDatabase<TraitDef>.GetNamed("SpeedOffset");
            // Keep a long-lived native cache across every operation. Neither
            // an elapsed tick nor an uncached query may repair these checks.
            int tick=Find.TickManager.TicksGame;
            float baseline=animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
            float manipulation=animal.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
            bool additions=true, removals=true, views=true;
            for(int i=0;i<4;i++)
            {
                Gene added=human.genes.AddGene(quick,false);
                // Direct lookup comes before any StatWorker query so the
                // source mutation must synchronize the view itself.
                views&=animal.genes.GenesListForReading.Contains(added);
                additions&=animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000)>baseline+.01f;
                human.genes.RemoveGene(added);
                views&=!animal.genes.GenesListForReading.Contains(added);
                removals&=Near(animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000),baseline);
                Trait trait=new Trait(speed,1);
                human.story.traits.GainTrait(trait);
                additions&=animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000)>baseline+.01f;
                human.story.traits.RemoveTrait(trait);
                removals&=Near(animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000),baseline);
            }
            check(additions&&removals&&Find.TickManager.TicksGame==tick,
                "biology/canonical gene and trait toggles invalidate the body's long-lived native stat cache four times in one tick");
            check(views,"biology/canonical gene addition and removal synchronize direct animal gene views before any stat query");
            Gene cap=human.genes.AddGene(fingers,false);
            float enhanced=animal.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
            human.genes.RemoveGene(cap);
            check(enhanced>manipulation+.09f&&Near(animal.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation),manipulation),
                "biology/canonical cap-mod removal clears native animal capacity caches in the same tick");
            Gene xeno=human.genes.AddGene(quick,true);
            float xenogeneSpeed=animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
            human.genes.ClearXenogenes();
            check(xenogeneSpeed>baseline+.01f&&!animal.genes.GenesListForReading.Contains(xeno)
                &&Near(animal.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000),baseline),
                "biology/canonical ClearXenogenes synchronizes membership and clears the same-tick native stat cache");
            if(TransformUtility.RevertToHuman(animal,false)!=human)
                throw new InvalidOperationException("Biology mutation probe did not return its original.");
        }

        private static void CallbackGate(Action<bool,string> check)
        {
            Gene unknown=new UnreviewedBiologyCallback { def=new GeneDef() };
            check(!SharedFormBiology.CanRunCallback(unknown,true),
                "biology/custom overridden callbacks require a reviewed explicit opt-in");
            unknown.def.modExtensions=new List<DefModExtension> {new SharedFormGeneCallbacksExtension()};
            check(SharedFormBiology.CanRunCallback(unknown,true),
                "biology/reviewed gene callback extension permits explicit active-body execution");
            Gene inherited=new InheritedNativeBiologyCallback { def=new GeneDef() };
            check(SharedFormBiology.CanRunCallback(inherited,true),
                "biology/mod subclass inheriting native clotting callback keeps native support");
        }

        private static void GeneFlagsAndClocks(Map map, Action<bool,string> check)
        {
            Pawn human = NewHuman(map, "SharedBiologyFlags", -24);
            foreach (string name in new[] { "ElongatedFingers", "Robust", "Deathless", "PerfectImmunity", "Ageless",
                "TotalHealing", "Superclotting", "StrongStomach", "DarkVision", "Neversleep", "DiseaseFree" })
                human.genes.AddGene(DefDatabase<GeneDef>.GetNamed(name), false);
            Gene[] originals = human.genes.GenesListForReading.ToArray();
            Hediff originalScar = Scar(human);
            Pawn animal = Transform(map, human);
            check(animal.genes != human.genes && originals.All(g => animal.genes.GenesListForReading.Contains(g))
                && originals.All(g => g.pawn == human),
                "biology/native gene view exposes the exact canonical genes with unchanged human owners");
            check(!SharedFormBiology.RawBodyGenesFor(animal).Any(g => originals.Contains(g)),
                "biology/native animal serialized gene lists do not own borrowed source genes");
            check(Near(animal.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation), 1.1f),
                "biology/native capMods applies original elongated-fingers manipulation110percent once");
            check(Near(animal.GetStatValue(StatDefOf.IncomingDamageFactor), .75f),
                "biology/native Robust remains x0.75 without a second stat projection");
            check(ReferenceEquals(animal.genes.GetFirstGeneOfType<Gene_Deathless>(), human.genes.GetFirstGeneOfType<Gene_Deathless>()),
                "biology/native deathless lookup returns the original stateful Gene object");
            check(Near(animal.genes.BiologicalAgeTickFactor, 0f),
                "biology/ageless uses original adult activation age for the younger animal shape");
            check(!animal.genes.AffectedByDarkness && animal.needs.rest == null && human.needs.rest == null,
                "biology/native darkness flag and no-sleep need removal follow the canonical genes");
            check(!animal.genes.HediffGiversCanGive(HediffDefOf.Carcinoma),
                "biology/native DiseaseFree blocks carcinoma generation on the active body");
            check(Near(animal.health.immunity.DiseaseContractChanceFactor(DefDatabase<HediffDef>.GetNamed("Flu")), 0f),
                "biology/native PerfectImmunity blocks contracting flu on the active animal");
            check(SharedFormBiology.SharesNeeds(animal) && ReferenceEquals(animal.needs.mood, human.needs.mood)
                && ReferenceEquals(animal.needs.food, human.needs.food),
                "biology/active body borrows the exact canonical mood and food needs");
            check(animal.mindState.mentalBreaker.CanDoRandomMentalBreaks == human.ageTracker.CurLifeStage.canDoRandomMentalBreaks,
                "biology/mental-break eligibility follows the person rather than the animal life-stage flag");

            BodyPartRecord torso = animal.RaceProps.body.corePart;
            Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, animal, torso);
            cut.Severity = 1f; animal.health.AddHediff(cut, torso);
            Hediff scar = Scar(animal);
            Gene_Healing healing = human.genes.GetFirstGeneOfType<Gene_Healing>();
            AccessTools.Field(typeof(Gene_Healing), "ticksToHeal").SetValue(healing, 1);
            int started = Find.TickManager.TicksGame, waited = 0;
            while (waited < 420 && animal.Spawned && !animal.Dead
                && (cut.Bleeding || animal.health.hediffSet.hediffs.Contains(scar)))
            { Find.TickManager.DoSingleTick(); waited++; }
            check(!cut.Bleeding && cut.IsTended(),
                "biology/canonical superclotting tends the active animal in native scheduled ticks");
            check(!animal.health.hediffSet.hediffs.Contains(scar)
                && human.health.hediffSet.hediffs.Contains(originalScar),
                "biology/canonical TotalHealing heals the active scar and leaves the inactive human scar alone");
            check(healing.pawn == human && originals.All(g => g.pawn == human)
                && (int)AccessTools.Field(typeof(Gene_Healing), "ticksToHeal").GetValue(healing) > 1,
                "biology/one original healing timer resets and all temporary gene contexts restore ownership");
            check(Find.TickManager.TicksGame - started <= 420,
                "biology/clotting and healing probes use at most420 actual native game ticks");
            Current.Game.GetComponent<ExpandedSharedBiologyPersistence>().Register(human, animal);
        }

        private static void NeedLevelsMetabolismAndTraits(Map map, Action<bool,string> check)
        {
            Pawn human = NewHuman(map, "SharedBiologyNeeds", -20);
            human.needs.food.CurLevelPercentage = .37f;
            human.needs.rest.CurLevelPercentage = .43f;
            Need_Food originalFood = human.needs.food;
            Need_Rest originalRest = human.needs.rest;
            Pawn animal = Transform(map, human);
            check(ReferenceEquals(animal.needs.food, originalFood) && ReferenceEquals(animal.needs.rest, originalRest)
                && Near(animal.needs.food.CurLevelPercentage, .37f) && Near(animal.needs.rest.CurLevelPercentage, .43f),
                "biology/initial transform preserves food/rest percentages on the same canonical need objects");
            float plain = animal.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
            human.story.traits.GainTrait(new Trait(DefDatabase<TraitDef>.GetNamed("Gourmand")));
            float gourmand = animal.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
            check(Near(gourmand, plain * 1.5f),
                "biology/native Gourmand hunger penalty x1.5 follows the shared trait view");
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("MeleeDamage_Strong"), false);
            int metabolism = human.genes.GenesListForReading.Where(g => !g.Overridden).Sum(g => g.def.biostatMet);
            float expected = gourmand * GeneTuning.MetabolismToFoodConsumptionFactorCurve.Evaluate(metabolism);
            check(Near(animal.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed), expected),
                "biology/native food consumption includes the canonical gene metabolic cost");
            animal.needs.food.CurLevelPercentage = .29f;
            animal.needs.rest.CurLevelPercentage = .31f;
            Pawn returned = TransformUtility.RevertToHuman(animal, false);
            check(returned == human && ReferenceEquals(human.needs.food, originalFood)
                && ReferenceEquals(human.needs.rest, originalRest)
                && Near(human.needs.food.CurLevelPercentage, .29f) && Near(human.needs.rest.CurLevelPercentage, .31f),
                "biology/return restores original need owners and keeps the consumed food/rest percentages");
            check(!ReferenceEquals(animal.needs.food, human.needs.food)
                && !(animal.story?.traits is SharedPawnTraitSet),
                "biology/cached animal restores its independent body needs and trait storage");
            Pawn again = Transform(map, human);
            human.story.traits.GainTrait(new Trait(DefDatabase<TraitDef>.GetNamed("BodyMastery")));
            check(human.needs.food == null && human.needs.rest == null && again.needs.food == null && again.needs.rest == null,
                "biology/native BodyMastery removes food/rest from both canonical and active need views");
            check(again.story.traits.allTraits.All(t => t.pawn == human)
                && again.story.traits.HasTrait(DefDatabase<TraitDef>.GetNamed("Gourmand")),
                "biology/direct trait queries preserve the human-owned trait objects");
            if (TransformUtility.RevertToHuman(again, false) != human)
                throw new InvalidOperationException("Biology needs probe did not return its original.");
        }

        private static void WorkRestrictionsAndTraitPrice(Map map, Action<bool,string> check)
        {
            Pawn human=NewHuman(map,"SharedBiologyRestrictions",-16);
            foreach(SkillRecord skill in human.skills.skills)skill.Level=20;
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Beauty_Beautiful"),false);
            Pawn animal=Transform(map,human);
            TraitDef voiceDef=DefDatabase<TraitDef>.GetNamed("AnnoyingVoice");
            float offset=voiceDef.DataAtDegree(0).marketValueFactorOffset;
            float before=PriceUtility.PawnQualityPriceFactor(animal);
            human.story.traits.GainTrait(new Trait(voiceDef));
            float after=PriceUtility.PawnQualityPriceFactor(animal);
            check(before+offset>.1f&&Near(after,before+offset),
                "biology/native pawn quality price applies the canonical trait offset exactly once without a second price patch");
            check(human.genes.GenesListForReading.Any(g=>g.def.defName=="Beauty_Beautiful")
                &&Near(animal.GetStatValue(StatDefOf.PawnBeauty),2f),
                "biology/native price beauty contribution uses the shared PawnBeauty stat, not genepack price factors");
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("ViolenceDisabled"),false);
            check(human.WorkTagIsDisabled(WorkTags.Violent)&&animal.WorkTagIsDisabled(WorkTags.Violent)
                &&(animal.CombinedDisabledWorkTags&WorkTags.Violent)!=0,
                "biology/the active animal cannot bypass the canonical violence-disabled gene restriction");
            BackstoryDef previous=human.story.Adulthood;
            BackstoryDef disabled=DefDatabase<BackstoryDef>.AllDefsListForReading.First(d=>d.workDisables!=WorkTags.None);
            human.story.Adulthood=disabled;
            human.Notify_DisabledWorkTypesChanged();
            SharedFormBiology.InvalidateSource(human);
            check((animal.CombinedDisabledWorkTags&disabled.workDisables)==disabled.workDisables,
                "biology/combined work tags include canonical backstory incapacities absent from the animal story");
            check(human.GetDisabledWorkTypes().All(w=>animal.GetDisabledWorkTypes().Contains(w))
                &&human.GetDisabledWorkTypes(true).All(w=>animal.GetDisabledWorkTypes(true).Contains(w)),
                "biology/native work-type queries preserve canonical permanent and temporary restrictions");
            human.story.Adulthood=previous;
            human.Notify_DisabledWorkTypesChanged();
            SharedFormBiology.InvalidateSource(human);
            if(TransformUtility.RevertToHuman(animal,false)!=human)
                throw new InvalidOperationException("Biology restrictions probe did not return its original.");
        }

        private static void FertilityAndConception(Map map, Action<bool,string> check)
        {
            Pawn ordinaryMale=NewAnimal(map,"Warg",Gender.Male,-12);
            Pawn female=NewAnimal(map,"Warg",Gender.Female,-10);
            int highSeed=SeedWithRoll(.60f,.70f),lowSeed=SeedWithRoll(.30f,.40f);
            MateSeeded(ordinaryMale,female,highSeed);
            check(!Pregnant(female)&&Near(SharedFormBiology.MatingChance(.5f,ordinaryMale,female),.5f),
                "biology/ordinary animal mating keeps the native fixed50percent chance");
            Pawn human=NewHuman(map,"SharedBiologyFertility",-8);
            Pawn male=Transform(map,human);
            HediffDef boostDef=new HediffDef
            {
                defName="WildShift_TestTemporaryFertility150",label="fixture fertility150",
                hediffClass=typeof(HediffWithComps),isBad=false,
                modExtensions=new List<DefModExtension>{new SharedFormBuffExtension()},
                stages=new List<HediffStage>{new HediffStage{statFactors=new List<StatModifier>
                    {new StatModifier{stat=StatDefOf.Fertility,value=1.5f}}}}
            };
            Hediff boost=HediffMaker.MakeHediff(boostDef,human);
            human.health.AddHediff(boost);
            try
            {
                check(Near(male.GetStatValue(StatDefOf.Fertility,true,0),1.5f)
                    &&Near(SharedFormBiology.MatingChance(.5f,male,female),.75f),
                    "biology/active-body Fertility uses original adult age and150percent fertility gives75percent conception");
                MateSeeded(male,female,highSeed);
                check(Pregnant(female),
                    "biology/native Mated roll above50butbelow75 succeeds with a transformed150percent fertile parent");
                ClearPregnancy(female);
            }
            finally
            {
                human.health.RemoveHediff(boost);
                SharedFormHealth.Refresh(male);
            }
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Fertile"),false);
            check(Near(male.GetStatValue(StatDefOf.Fertility,true,0),2f)
                &&Near(SharedFormBiology.MatingChance(.5f,male,female),1f),
                "biology/native Fertile gene doubles mating fertility once and conception chance clamps at100percent");
            human.genes.RemoveGene(human.genes.GetGene(DefDatabase<GeneDef>.GetNamed("Fertile")));
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Inbred"),false);
            check(Near(male.GetStatValue(StatDefOf.Fertility,true,0),.5f)
                &&Near(SharedFormBiology.MatingChance(.5f,male,female),.25f),
                "biology/native Inbred fertility penalty halves conception from50to25percent");
            MateSeeded(ordinaryMale,female,lowSeed);
            check(Pregnant(female),"biology/the control native mating succeeds for the same30to40percent roll");
            ClearPregnancy(female);
            MateSeeded(male,female,lowSeed);
            check(!Pregnant(female),"biology/native transformed mating rejects the same roll above its25percent fertility chance");
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Sterile"),false);
            check(male.Sterile()&&!PawnUtility.FertileMateTarget(male,female)
                &&Near(SharedFormBiology.MatingChance(.5f,male,female),0f),
                "biology/sterile canonical genes still block animal mate targeting and conception");
            if(TransformUtility.RevertToHuman(male,false)!=human)
                throw new InvalidOperationException("Biology fertility probe did not return its original.");

            Pawn eggHuman=NewHuman(map,"SharedBiologyEggFertility",-6);
            eggHuman.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Inbred"),false);
            Pawn rooster=Transform(map,eggHuman,"Alligator");
            Pawn hen=NewAnimal(map,"Alligator",Gender.Female,-4);
            CompEggLayer eggs=hen.TryGetComp<CompEggLayer>();
            if(eggs==null)throw new InvalidOperationException("Native eligible alligator fixture has no egg layer.");
            MateSeeded(rooster,hen,highSeed);
            check(!eggs.FullyFertilized,"biology/egg fertilization respects a transformed parent's50percent fertility penalty");
            MateSeeded(rooster,hen,lowSeed);
            check(eggs.FullyFertilized,"biology/a successful adjusted egg roll retains native CompEggLayer fertilization");
            if(TransformUtility.RevertToHuman(rooster,false)!=eggHuman)
                throw new InvalidOperationException("Biology egg probe did not return its original.");
        }

        private static int SeedWithRoll(float low,float high)
        {
            for(int seed=0;seed<10000;seed++)
            {
                Rand.PushState(seed);
                float roll;
                try{roll=Rand.Value;}finally{Rand.PopState();}
                if(roll>low&&roll<high)return seed;
            }
            throw new InvalidOperationException("No deterministic mating roll seed found.");
        }
        private static void MateSeeded(Pawn male,Pawn female,int seed)
        { Rand.PushState(seed);try{PawnUtility.Mated(male,female);}finally{Rand.PopState();} }
        private static bool Pregnant(Pawn p) { return p.health.hediffSet.HasHediff(HediffDefOf.Pregnant); }
        private static void ClearPregnancy(Pawn p)
        { foreach(Hediff h in p.health.hediffSet.hediffs.Where(h=>h.def==HediffDefOf.Pregnant).ToList())p.health.RemoveHediff(h); }
        private static Pawn NewAnimal(Map map,string kind,Gender gender,int offset)
        {
            Pawn p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind),Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,forceGenerateNewPawn:true,allowDead:false,allowDowned:false,
                fixedBiologicalAge:5f,fixedGender:gender,canGeneratePawnRelations:false));
            foreach(Hediff h in p.health.hediffSet.hediffs.ToList())p.health.RemoveHediff(h);
            GenSpawn.Spawn(p,CellFinder.StandableCellNear(map.Center+new IntVec3(offset,0,0),map,8f),map);
            return p;
        }

        private static Pawn NewHuman(Map map, string name, int offset)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
                Faction.OfPlayer, PawnGenerationContext.NonPlayer, forceGenerateNewPawn:true,
                allowDead:false, allowDowned:false, fixedBiologicalAge:25f, fixedChronologicalAge:25f,
                fixedGender:Gender.Male, forcedXenotype:XenotypeDefOf.Baseliner, canGeneratePawnRelations:false));
            foreach (Trait t in pawn.story.traits.allTraits.ToList()) pawn.story.traits.RemoveTrait(t);
            foreach (Gene g in pawn.genes.GenesListForReading.ToList()) pawn.genes.RemoveGene(g);
            foreach (Hediff h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
            pawn.Name = new NameSingle(name);
            GenSpawn.Spawn(pawn, CellFinder.StandableCellNear(map.Center+new IntVec3(offset,0,0),map,8f),map);
            return pawn;
        }
        private static Pawn Transform(Map map, Pawn human, string kindName="Warg")
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(kindName);
            HediffComp_Shapeshifter shifter=TransformUtility.AddOrGetShapeshifter(human,kind);
            if(!shifter.AllowsAssignedKind(kind))shifter.SetAssignedKind(kind,true);
            if (!TransformUtility.TransformToAnimal(human,kind)) throw new InvalidOperationException("Biology production transform failed.");
            return map.mapPawns.AllPawnsSpawned.Single(p=>TransformUtility.TryGetTransformedComp(p)?.StoredPawn==human);
        }
        private static Hediff Scar(Pawn pawn)
        {
            BodyPartRecord limb=pawn.health.hediffSet.GetNotMissingParts().First(p=>p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore));
            Hediff scar=HediffMaker.MakeHediff(HediffDefOf.Cut,pawn,limb);scar.Severity=1f;
            pawn.health.AddHediff(scar,limb);
            HediffComp_GetsPermanent permanence=scar.TryGetComp<HediffComp_GetsPermanent>();
            permanence.isPermanentInt=true;permanence.permanentDamageThreshold=9999f;permanence.SetPainCategory(PainCategory.Painless);
            return scar;
        }
        private static bool Near(float a,float b) { return Mathf.Abs(a-b)<.00001f; }
    }

    public sealed class UnreviewedBiologyCallback : Gene
    { public override void TickInterval(int delta) { } }
    public sealed class InheritedNativeBiologyCallback : Gene_Clotting { }

    public sealed class ExpandedSharedBiologyPersistence : GameComponent
    {
        private List<Pawn> humans=new List<Pawn>(),bodies=new List<Pawn>();
        private List<string> genes=new List<string>();
        private List<float> food=new List<float>(),rest=new List<float>();
        public ExpandedSharedBiologyPersistence(Game game) { }
        public void Register(Pawn human,Pawn animal) { humans.Add(human);bodies.Add(animal); }
        public override void ExposeData()
        {
            if (Scribe.mode==LoadSaveMode.Saving)
            {
                genes=humans.Select(h=>string.Join("|",h.genes.GenesListForReading.Select(g=>g.GetUniqueLoadID()))).ToList();
                // The canonical human owns personal needs. A legacy-input
                // fixture deliberately releases runtime views before saving,
                // leaving unrelated physical-body needs on the animal.
                food=humans.Select(p=>p.needs.food?.CurLevelPercentage ?? -1f).ToList();
                rest=humans.Select(p=>p.needs.rest?.CurLevelPercentage ?? -1f).ToList();
            }
            Scribe_Collections.Look(ref humans,"biologyHumans",LookMode.Reference);
            Scribe_Collections.Look(ref bodies,"biologyBodies",LookMode.Reference);
            Scribe_Collections.Look(ref genes,"biologyGeneIds",LookMode.Value);
            Scribe_Collections.Look(ref food,"biologyFoodPercentages",LookMode.Value);
            Scribe_Collections.Look(ref rest,"biologyRestPercentages",LookMode.Value);
        }
        public void Verify(Action<bool,string> check)
        {
            for(int i=0;i<humans.Count;i++)
            {
                Pawn h=humans[i],a=bodies[i];
                check(h!=null&&a!=null&&SharedFormIdentity.HumanFor(a)==h,
                    "biology/reload restores the exact original/active-body ownership "+i);
                if(h==null||a==null)continue;
                string ids=string.Join("|",h.genes.GenesListForReading.Select(g=>g.GetUniqueLoadID()));
                check(ids==genes[i]&&h.genes.GenesListForReading.All(g=>g.pawn==h),
                    "biology/reload keeps canonical gene IDs and human owners without copied genes "+i);
                check(h.genes.GenesListForReading.All(g=>a.genes.GenesListForReading.Contains(g))
                    && !SharedFormBiology.RawBodyGenesFor(a).Any(g=>h.genes.GenesListForReading.Contains(g)),
                    "biology/reload rebuilds a non-owning native gene read view "+i);
                check(ReferenceEquals(a.needs.food,h.needs.food)&&ReferenceEquals(a.needs.mood,h.needs.mood),
                    "biology/reload resolves one canonical food and mood object "+i);
                float loadedFood=a.needs.food?.CurLevelPercentage??-1f;
                float loadedRest=a.needs.rest?.CurLevelPercentage??-1f;
                check(Mathf.Abs(loadedFood-food[i])<.0001f&&Mathf.Abs(loadedRest-rest[i])<.0001f,
                    "biology/reload keeps food/rest fractions across the saved body owner context "+i
                    +" (food loaded="+loadedFood.ToString("R")+", saved="+food[i].ToString("R")
                    +"; rest loaded="+loadedRest.ToString("R")+", saved="+rest[i].ToString("R")+")");
            }
        }
    }
}
