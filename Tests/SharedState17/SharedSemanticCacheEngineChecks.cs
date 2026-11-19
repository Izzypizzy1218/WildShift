using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Use real native caches, a real transform and long-lived stat
    // reads. The observer only counts DirtyCache calls; it does not query a
    // stage, stat, capacity, or modifier predicate.
    public sealed class SharedSemanticStatCapacityWorker : PawnCapacityWorker
    {
        public static PawnCapacityDef Capacity;
        public static bool PersonalScope, CachingBefore, CachingAfter;
        public override bool CanHaveCapacity(BodyDef body) { return true; }
        public override float CalculateCapacityLevel(HediffSet set, List<PawnCapacityUtility.CapacityImpactor> impactors = null)
        {
            if (!PersonalScope) return set.pawn.GetStatValue(StatDefOf.MoveSpeed, cacheStaleAfterTicks:10000);
            using (new SharedFormThoughts.QueryScope(set.pawn, null))
            {
                CachingBefore = IsCaching(set.pawn);
                float value = set.pawn.GetStatValue(StatDefOf.MoveSpeed, cacheStaleAfterTicks:10000);
                CachingAfter = IsCaching(set.pawn);
                return value;
            }
        }
        private static bool IsCaching(Pawn pawn)
        {
            object map = AccessTools.Field(typeof(PawnCapacitiesHandler), "cachedCapacityLevels").GetValue(pawn.health.capacities);
            object entry = AccessTools.Method(map.GetType(), "get_Item", new[] { typeof(PawnCapacityDef) }).Invoke(map, new object[] { Capacity });
            return AccessTools.Field(entry.GetType(), "status").GetValue(entry).ToString() == "Caching";
        }
    }


    public static class SharedSemanticCacheEngineChecks
    {
        private static Pawn watched;
        private static int dirtyCalls;
        public static void ObserveDirty(Pawn ___pawn) { if (___pawn == watched) dirtyCalls++; }

        public sealed class ToggleAffecter : ConditionalStatAffecter
        {
            public static bool Enabled;
            public override string Label { get { return "semantic cache fixture"; } }
            public override bool Applies(StatRequest request) { return Enabled; }
        }

        public static void Run(Map map, Action<bool,string> check)
        {
            // The fixture XML registers this before any native DefMap exists.
            var capacity = DefDatabase<PawnCapacityDef>.GetNamed("WildShift_TestSemanticStatCapacity");

            SharedSemanticStatCapacityWorker.Capacity = capacity;
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn:true, canGeneratePawnRelations:false, mustBeCapableOfViolence:true,
                fixedGender:Gender.Male, fixedBiologicalAge:25f, forcedXenotype:XenotypeDefOf.Baseliner));
            human.Name = new NameSingle("SharedSemanticCache");
            human.health.RemoveAllHediffs();
            foreach (Trait trait in human.story.traits.allTraits.ToList()) human.story.traits.RemoveTrait(trait);
            foreach (Gene gene in human.genes.GenesListForReading.ToList()) human.genes.RemoveGene(gene);
            human.apparel.DestroyAll(); human.equipment.DestroyAllEquipment();
            GenSpawn.Spawn(human, CellFinder.StandableCellNear(map.Center,map,12f), map);
            Ideo originalIdeo = human.Ideo;
            Ideo empty = new Ideo();
            AccessTools.Field(typeof(Pawn_IdeoTracker), "ideo").SetValue(human.ideo, empty);
            Pawn body = null;
            var observer = new Harmony("wildshift.tests.semantic-cache-observer");
            observer.Patch(AccessTools.Method(typeof(HediffSet), "DirtyCache"), prefix:new HarmonyMethod(typeof(SharedSemanticCacheEngineChecks), nameof(ObserveDirty)));
            try
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human, kind);
                Require(TransformUtility.TransformToAnimal(human,kind), "semantic-cache/real native transform succeeds", check);
                body = SharedFormIdentity.ActiveFormFor(human);
                Require(body != null, "semantic-cache/active form is registered", check);
                watched = body;
                Pawn_IdeoTracker nativeIdeo = body.ideo;
                Hediff carrier = TransformUtility.TryGetTransformedComp(body).parent;
                float consciousness = body.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                float pain = body.health.hediffSet.PainTotal;
                HediffStage stage = carrier.CurStage;
                int tick = Find.TickManager.TicksGame;
                dirtyCalls = 0;
                bool stable = true;
                for (int i = 0; i < 20; i++)
                {
                    using (new SharedFormThoughts.QueryScope(body,null))
                        stable &= Near(body.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),consciousness)
                            && Near(body.health.hediffSet.PainTotal,pain);
                    stable &= Near(body.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),consciousness)
                        && Near(body.health.hediffSet.PainTotal,pain);
                }
                check(stable && dirtyCalls == 0 && Find.TickManager.TicksGame == tick && body.ideo == nativeIdeo,
                    "semantic-cache/20 empty-ideology personal query cycles preserve native capacity/pain caches with zero full health invalidations");
                check(ReferenceEquals(stage,carrier.CurStage),
                    "semantic-cache/no-op personal context preserves an absent or unchanged carrier stage");

                float baseSpeed = body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                float baseCapacity = body.health.capacities.GetLevel(capacity);
                Precept bonus = new Precept { def=new PreceptDef { defName="WildShift_TestSemanticPrecept",
                    statOffsets=new List<StatModifier> { new StatModifier { stat=StatDefOf.MoveSpeed, value=.25f } } } };
                empty.PreceptsListForReading.Add(bonus);
                dirtyCalls = 0;
                check(body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000)>baseSpeed+.2f
                    && body.health.capacities.GetLevel(capacity)>baseCapacity+.2f && dirtyCalls>0,
                    "semantic-cache/real precept stat addition refreshes both a long-lived stat and a custom stat-dependent capacity in the same tick");
                using (new SharedFormThoughts.QueryScope(body,null))
                    check(Near(body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000),baseSpeed+.25f),
                        "semantic-cache/native ideology contribution replaces its nonzero projection without losing or duplicating the stat");
                empty.PreceptsListForReading.Remove(bonus);
                check(Near(body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000),baseSpeed)
                    && Near(body.health.capacities.GetLevel(capacity),baseCapacity),
                    "semantic-cache/precept removal repairs the same native stat and custom capacity caches immediately");

                // The native ideology can differ from the source ideology.
                // Equal projected lists alone must not hide a real native stat
                // change when a personal query temporarily replaces that view.
                Ideo physicalIdeo = new Ideo(); physicalIdeo.PreceptsListForReading.Add(bonus);
                body.ideo = new Pawn_IdeoTracker(body);
                AccessTools.Field(typeof(Pawn_IdeoTracker),"ideo").SetValue(body.ideo,physicalIdeo);
                body.health.hediffSet.DirtyCache();
                body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:0);
                float physicalCapacity = body.health.capacities.GetLevel(capacity);
                using (new SharedFormThoughts.QueryScope(body,null))
                    check(physicalCapacity>baseCapacity+.2f && Near(body.health.capacities.GetLevel(capacity),baseCapacity),
                        "semantic-cache/real native-only ideology stat change still invalidates a dependent capacity");
                check(Near(body.health.capacities.GetLevel(capacity),physicalCapacity),
                    "semantic-cache/restoring the native ideology restores its stat-dependent capacity in the same tick");
                body.ideo = nativeIdeo;
                body.health.hediffSet.DirtyCache();
                body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:0);

                var condition = new ToggleAffecter { statOffsets=new List<StatModifier> { new StatModifier { stat=StatDefOf.MoveSpeed,value=.5f } } };
                Precept conditional = new Precept { def=new PreceptDef { defName="WildShift_TestSemanticConditional" } };
                empty.PreceptsListForReading.Add(conditional);
                ToggleAffecter.Enabled = false;
                float disabled = body.health.capacities.GetLevel(capacity);
                // Leave the list length and PreceptDef identity intact while
                // changing that definition from static to environmental.
                conditional.def.conditionalStatAffecters = new List<ConditionalStatAffecter> { condition };
                ToggleAffecter.Enabled = true;
                check(body.health.capacities.GetLevel(capacity)>disabled+.4f,
                    "semantic-cache/conditional precept stat activation retains conservative immediate capacity invalidation");
                ToggleAffecter.Enabled = false;
                check(Near(body.health.capacities.GetLevel(capacity),disabled),
                    "semantic-cache/conditional precept stat deactivation retains the same-tick native cache contract");
                ToggleAffecter.Enabled = true;
                body.health.capacities.GetLevel(capacity);
                conditional.def.conditionalStatAffecters.Clear();
                check(Near(body.health.capacities.GetLevel(capacity),disabled),
                    "semantic-cache/live removal of a matching precept's last conditional effect refreshes the same-tick capacity cache");
                ToggleAffecter.Enabled = false;
                empty.PreceptsListForReading.Remove(conditional);
                body.health.capacities.GetLevel(capacity);

                // Inspiration offsets apply after gene factors; ideology
                // offsets apply before them. Equal flat modifier numbers do
                // not make these native categories interchangeable.
                Gene orderingGene = human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("WildShift_TestSemanticMoveFactor"),false);
                InspirationHandler handler = body.mindState.inspirationHandler;
                Inspiration previousInspiration = handler.CurState;
                body.ideo = human.ideo;
                var inspiration = new Inspiration { pawn=body, def=new InspirationDef { defName="WildShift_TestSemanticInspiration",
                    statOffsets=new List<StatModifier> { new StatModifier { stat=StatDefOf.MoveSpeed,value=1f } } } };
                var orderedBonus = new Precept { def=new PreceptDef { defName="WildShift_TestSemanticOrderedPrecept",
                    statOffsets=new List<StatModifier> { new StatModifier { stat=StatDefOf.MoveSpeed,value=1f } } } };
                try
                {
                    AccessTools.Field(typeof(InspirationHandler),"curState").SetValue(handler,inspiration);
                    float lateOffset = body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                    float lateCapacity = body.health.capacities.GetLevel(capacity);
                    AccessTools.Field(typeof(InspirationHandler),"curState").SetValue(handler,null);
                    empty.PreceptsListForReading.Add(orderedBonus);
                    float earlyOffset = body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                    check(earlyOffset>lateOffset+.1f && Near(lateCapacity,lateOffset)
                        && Near(body.health.capacities.GetLevel(capacity),earlyOffset),
                        "semantic-cache/moving an equal +1 from native inspiration to ideology under gene x2 refreshes both the long-lived stat and dependent capacity");
                }
                finally
                {
                    empty.PreceptsListForReading.Remove(orderedBonus);
                    AccessTools.Field(typeof(InspirationHandler),"curState").SetValue(handler,previousInspiration);
                    body.ideo = nativeIdeo;
                    human.genes.RemoveGene(orderingGene);
                }
                body.health.capacities.GetLevel(capacity);

                Apparel marine = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmor"));
                Apparel recon = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_ArmorRecon"));
                try
                {
                    human.apparel.Wear(marine,false);
                    float oldItem = body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                    float oldItemCapacity = body.health.capacities.GetLevel(capacity);
                    human.apparel.Remove(marine);
                    human.apparel.Wear(recon,false);
                    float newItem = body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                    check(Math.Abs(newItem-oldItem)>.1f && Near(oldItemCapacity,oldItem)
                        && Near(body.health.capacities.GetLevel(capacity),newItem),
                        "semantic-cache/replacing native-visible armor with a different equipped movement bonus refreshes the stat and dependent capacity in the same tick");
                }
                finally
                {
                    if (human.apparel.WornApparel.Contains(marine)) human.apparel.Remove(marine);
                    if (human.apparel.WornApparel.Contains(recon)) human.apparel.Remove(recon);
                    if (!marine.Destroyed) marine.Destroy(DestroyMode.Vanish);
                    if (!recon.Destroyed) recon.Destroy(DestroyMode.Vanish);
                }
                body.health.capacities.GetLevel(capacity);

                SharedSemanticStatCapacityWorker.PersonalScope = true;
                SharedSemanticStatCapacityWorker.CachingBefore = SharedSemanticStatCapacityWorker.CachingAfter = false;
                body.health.capacities.Notify_CapacityLevelsDirty();
                dirtyCalls = 0;
                body.health.capacities.GetLevel(capacity);
                check(SharedSemanticStatCapacityWorker.CachingBefore && SharedSemanticStatCapacityWorker.CachingAfter && dirtyCalls == 0,
                    "semantic-cache/nested personal context and stat reads preserve the native capacity recursion guard");
                SharedSemanticStatCapacityWorker.PersonalScope = false;

                Hediff injury = body.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Crush"),body.RaceProps.body.corePart);
                injury.Severity = 3f;
                float injuryPain = body.health.hediffSet.PainTotal;
                GeneDef painDef = DefDatabase<GeneDef>.AllDefsListForReading.First(def=>def.painFactor>0f && def.painFactor<1f
                    && def.geneClass.Assembly==typeof(Gene).Assembly);
                dirtyCalls = 0;
                Gene painGene = human.genes.AddGene(painDef,false);
                check(injuryPain>0f && body.health.hediffSet.PainTotal<injuryPain && dirtyCalls>0,
                    "semantic-cache/real source pain-gene addition still invalidates and updates native pain immediately");
                human.genes.RemoveGene(painGene);
                check(Near(body.health.hediffSet.PainTotal,injuryPain),
                    "semantic-cache/source pain-gene removal restores the same-tick native pain cache");

                float moving = body.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                BodyPartRecord leg = human.RaceProps.body.AllParts.First(part=>part.def==BodyPartDefOf.Leg);
                dirtyCalls = 0;
                human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("BionicLeg"),leg);
                check(body.health.capacities.GetLevel(PawnCapacityDefOf.Moving)>moving+.01f && dirtyCalls>0,
                    "semantic-cache/real source anatomy changes still invalidate native health and capacity caches");
                check(Find.TickManager.TicksGame==tick,"semantic-cache/all cache transitions occur without advancing a game tick");
            }
            finally
            {
                watched = null; SharedSemanticStatCapacityWorker.PersonalScope = false; ToggleAffecter.Enabled = false;
                observer.Unpatch(AccessTools.Method(typeof(HediffSet),"DirtyCache"),HarmonyPatchType.All,observer.Id);
                if (body != null && SharedFormIdentity.HumanFor(body)==human) TransformUtility.RevertToHuman(body,false);
                AccessTools.Field(typeof(Pawn_IdeoTracker),"ideo").SetValue(human.ideo,originalIdeo);
                if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool Near(float a,float b) { return Math.Abs(a-b)<.001f; }
        private static void Require(bool ok,string label,Action<bool,string> check)
        { check(ok,label); if(!ok)throw new InvalidOperationException(label); }
    }
}
