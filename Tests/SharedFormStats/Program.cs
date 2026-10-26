using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

internal static class Program
{
    private static int checks;
    private static string mode;

    private sealed class RecursiveActiveGene : Gene
    {
        public Hediff carrier;
        public override bool Active { get { HediffStage unused = carrier.CurStage; return true; } }
    }

    // A mod's condition can inspect the current body and query another stat.
    // No game API is replaced: native StatWorker calls this native extension.
    private sealed class PositionAffecter : ConditionalStatAffecter
    {
        public Hediff carrier;
        public override string Label { get { return "position condition"; } }
        public override bool Applies(StatRequest req)
        {
            if (carrier != null) { HediffStage unused = carrier.CurStage; }
            return req.HasThing && req.Thing.Position.x >= 5;
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 4) return 2;
        mode = args[3];
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (string dir in new[] { args[0], args[1] })
            {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { Run(args[0], args[2]); Console.WriteLine("PASS: " + checks + " native shared-stat checks (" + mode + ")."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string game, string scratch)
    {
        string profile = Path.Combine(scratch, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Config"));
        Directory.CreateDirectory(Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, profile);
        AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(game, "Data"));
        File.WriteAllText(Path.Combine(profile, "Config", "ModsConfig.xml"), "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        var prefs = AccessTools.Field(typeof(Prefs), "data");
        prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
        Current.Game = Bare<Game>();
        Current.Game.tickManager = Bare<TickManager>();
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(Current.Game.tickManager, 100);
        WildShiftDefOf.WildShift_Transformed = new HediffDef { defName = "WildShift_Transformed", label = "shapeshifter" };
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, true);
        Console.WriteLine("Shared-stat implementation: " + typeof(SharedFormStats).Assembly.Location);
        if (mode != "baseline")
        {
            var harmony = new Harmony("wildshift.tests.shared-stats");
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hediff), "CurStage"), postfix: new HarmonyMethod(typeof(Patch_SharedFormStatStage), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(StatWorker), "GetValue", new[] { typeof(Thing), typeof(bool), typeof(int) }), prefix: new HarmonyMethod(typeof(Patch_SharedFormStatCache), "Prefix"));
        }
        TestStats();
        if (mode != "baseline") TestConditionalStats();
        AccessTools.Method(typeof(Log), "DrainOffThreadMessages").Invoke(null, null);
        foreach (LogMessage message in Log.Messages)
            if (message.type == LogMessageType.Error) throw new InvalidOperationException("Native game error: " + message.text);
        Check(true, "native game log contains no errors");
        Console.WriteLine("Scope: real 1.7 Gene.Active, Trait.Suppressed, Hediff stage and StatWorker APIs with isolated pawn subgraphs; no game launch or graphical verification.");
    }

    private static Pawn MakePawn(int id, bool human)
    {
        var pawn = Bare<Pawn>();
        pawn.thingIDNumber = id;
        pawn.stackCount = 1;
        pawn.def = new ThingDef { defName = "StatPawn" + id, category = ThingCategory.Pawn,
            race = new RaceProperties { intelligence = human ? Intelligence.Humanlike : Intelligence.Animal,
                lifeStageAges = new List<LifeStageAge> { new LifeStageAge { def = new LifeStageDef { defName = "Adult" } } } } };
        AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, new FleshTypeDef { defName = "StatFlesh" });
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)-1);
        pawn.health = Bare<Pawn_HealthTracker>();
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(pawn.health, pawn);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AccessTools.Field(typeof(Pawn_AgeTracker), "cachedLifeStageIndex").SetValue(pawn.ageTracker, 0);
        if (human) { pawn.story = Bare<Pawn_StoryTracker>(); pawn.story.traits = new TraitSet(pawn); }
        return pawn;
    }

    private static HediffWithComps Mark(Pawn animal, Pawn human)
    {
        var carrier = new HediffWithComps { pawn = animal, def = WildShiftDefOf.WildShift_Transformed };
        var holder = new HediffComp_Transformed { parent = carrier, props = new HediffCompProperties_Transformed() };
        carrier.comps = new List<HediffComp> { holder };
        animal.health.hediffSet.hediffs.Add(carrier);
        ((ThingOwner<Pawn>)holder.GetDirectlyHeldThings()).InnerListForReading.Add(human);
        human.holdingOwner = holder.GetDirectlyHeldThings();
        return carrier;
    }

    private static StatModifier Mod(StatDef stat, float value) { return new StatModifier { stat = stat, value = value }; }

    private static Gene AddGene(Pawn pawn, GeneDef def)
    {
        if (pawn.genes == null) pawn.genes = new Pawn_GeneTracker(pawn);
        var gene = new Gene { pawn = pawn, def = def };
        pawn.genes.Endogenes.Add(gene);
        AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes").SetValue(pawn.genes, null);
        return gene;
    }

    private static Trait AddTrait(Pawn pawn, StatDef stat, float offset, float factor)
    {
        if (pawn.story == null) { pawn.story = Bare<Pawn_StoryTracker>(); pawn.story.traits = new TraitSet(pawn); }
        var def = new TraitDef { defName = "StatTrait" + pawn.thingIDNumber, degreeDatas = new List<TraitDegreeData> {
            new TraitDegreeData { degree = 0, label = "stat trait", statOffsets = new List<StatModifier> { Mod(stat, offset) }, statFactors = new List<StatModifier> { Mod(stat, factor) } } } };
        var trait = new Trait(def) { pawn = pawn };
        pawn.story.traits.allTraits.Add(trait);
        return trait;
    }

    private static float Value(StatWorker worker, Pawn pawn) { return worker.GetValueUnfinalized(StatRequest.For(pawn)); }
    private static float CachedValue(StatWorker worker, Pawn pawn) { return (float)AccessTools.Method(typeof(StatWorker), "GetValue", new[] { typeof(Thing), typeof(bool), typeof(int) }).Invoke(worker, new object[] { pawn, true, 10000 }); }

    private static void TestStats()
    {
        var stat = new StatDef { defName = "SharedStat", label = "shared stat", defaultBaseValue = 10, neverDisabled = true, noSkillFactor = 1, cacheable = true };
        var worker = new StatWorker(); worker.InitSetStat(stat); worker.SetCacheability(false);
        Pawn human = MakePawn(1, true), animal = MakePawn(2, false), ordinary = MakePawn(3, false);
        var carrier = Mark(animal, human);
        // Native animal health has its own +3 and x2; the human's +1000 injury/
        // equipment-like modifier is intentionally not part of the projection.
        var animalCondition = new Hediff { pawn = animal, def = new HediffDef { defName = "AnimalCondition", label = "animal condition",
            stages = new List<HediffStage> { new HediffStage { statOffsets = new List<StatModifier> { Mod(stat, 3) }, statFactors = new List<StatModifier> { Mod(stat, 2) } } } } };
        animal.health.hediffSet.hediffs.Add(animalCondition);
        human.health.hediffSet.hediffs.Add(new Hediff { pawn = human, def = new HediffDef { stages = new List<HediffStage> { new HediffStage { statOffsets = new List<StatModifier> { Mod(stat, 1000) } } } } });
        var geneDef = new GeneDef { defName = "StatGene", statOffsets = new List<StatModifier> { Mod(stat, 2) }, statFactors = new List<StatModifier> { Mod(stat, 1.5f) } };
        Gene gene = AddGene(human, geneDef);
        Trait trait = AddTrait(human, stat, 1, 2);
        if (mode == "baseline")
        {
            Near(Value(worker, animal), 26, "baseline native animal has only its own health modifiers");
            Check(Math.Abs(Value(worker, animal) - 96) > .001f, "baseline reproduces missing human pure-stat sharing");
            return;
        }
        Near(Value(worker, animal), (10 + 3 + 2 + 1) * 2 * 1.5f * 2, "native offsets precede native and inherited factors");
        Near(Value(worker, ordinary), 10, "ordinary animal unchanged");
        Near(Value(worker, human), (10 + 1000 + 2 + 1) * 1.5f * 2, "original human is not projected a second time");
        Pawn sapientOriginal = MakePawn(10, true), sapientForm = MakePawn(11, true), unmarkedHuman = MakePawn(12, true);
        Mark(sapientForm, sapientOriginal);
        Gene sapientGene = AddGene(sapientOriginal, geneDef);
        AddTrait(sapientOriginal, stat, 1, 2);
        Near(CachedValue(worker, sapientForm), 39, "marked Humanlike sapient form shares the stored original's stat effects");
        sapientGene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(CachedValue(worker, sapientForm), 22, "Humanlike sapient form refreshes native cached stats on source changes");
        AddGene(unmarkedHuman, geneDef);
        AddTrait(unmarkedHuman, stat, 1, 2);
        Near(CachedValue(worker, unmarkedHuman), 39, "unmarked Humanlike pawn retains its ordinary native stat calculation");
        Check(carrier.CurStage != null && carrier.CurStage.statOffsets.GetStatOffsetFromList(stat) == 3, "native tooltip stage exposes the same shared offset");
        Near(carrier.CurStage.statFactors.GetStatFactorFromList(stat), 3, "native tooltip stage exposes the same shared factor");
        string explanation = worker.GetExplanationUnfinalized(StatRequest.For(animal), ToStringNumberSense.Absolute);
        Check(explanation.IndexOf("shapeshifter", StringComparison.OrdinalIgnoreCase) >= 0, "native stat explanation includes the projected carrier");
        Check(ReferenceEquals(carrier.CurStage, carrier.CurStage), "unchanged input reuses stage allocation");
        Check(gene.pawn == human && trait.pawn == human && animal.genes == null && animal.story == null, "gene and trait ownership is unchanged");
        Check(human.health.hediffSet.hediffs.Count == 1 && animal.health.hediffSet.hediffs.Count == 2, "no human hediff or condition object is copied");

        // Real native override/suppression getters, including same-tick changes.
        gene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(Value(worker, animal), (10 + 3 + 1) * 2 * 2, "overridden gene stops sharing immediately");
        gene.overriddenByGene = null;
        trait.suppressedByTrait = true;
        Near(Value(worker, animal), (10 + 3 + 2) * 2 * 1.5f, "suppressed trait stops sharing immediately");
        trait.suppressedByTrait = false;
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, false);
        Near(Value(worker, animal), (10 + 3 + 1) * 2 * 2, "gene sharing follows native Biotech activation");
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, true);

        // Native animal genes already supply their modifiers; share each effect once.
        Gene nativeGene = AddGene(animal, geneDef);
        Near(Value(worker, animal), 96, "native animal gene is not applied twice");
        nativeGene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(Value(worker, animal), 96, "inactive animal gene does not suppress inherited active gene");
        nativeGene.overriddenByGene = null;
        Trait nativeTrait = new Trait(trait.def) { pawn = animal };
        animal.story = Bare<Pawn_StoryTracker>(); animal.story.traits = new TraitSet(animal); animal.story.traits.allTraits.Add(nativeTrait);
        Near(Value(worker, animal), 96, "native animal trait is not applied twice");

        Near(CachedValue(worker, animal), 96, "native temporary cache is primed");
        gene.overriddenByGene = new Gene { def = new GeneDef() };
        nativeGene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(CachedValue(worker, animal), 56, "source change invalidates native temporary cache in same tick");
        var immutableStat = new StatDef { defName = "ImmutableSharedStat", defaultBaseValue = 5, immutable = true, neverDisabled = true, noSkillFactor = 1 };
        var immutableWorker = new StatWorker(); immutableWorker.InitSetStat(immutableStat); immutableWorker.SetCacheability(true);
        Gene otherGene = AddGene(human, new GeneDef { defName = "OtherGene", statOffsets = new List<StatModifier> { Mod(immutableStat, 7) } });
        Near(CachedValue(immutableWorker, animal), 12, "native immutable cache is primed");
        otherGene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(CachedValue(immutableWorker, animal), 5, "source change invalidates native immutable cache");
        gene.overriddenByGene = null; nativeGene.overriddenByGene = null;

        // Carrier reuse must see the new exact original, even during one tick.
        var holder = TransformUtility.TryGetTransformedComp(animal);
        Pawn replacement = MakePawn(4, true);
        holder.ReleaseStoredPawn();
        ((ThingOwner<Pawn>)holder.GetDirectlyHeldThings()).InnerListForReading.Add(replacement); replacement.holdingOwner = holder.GetDirectlyHeldThings();
        Near(Value(worker, animal), 96, "reused body retains only native animal effects when new original is unmodified");
        nativeTrait.suppressedByTrait = true; nativeGene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(Value(worker, animal), 26, "reused body does not retain prior human snapshot");
        holder.ReleaseStoredPawn();
        Near(Value(worker, animal), 26, "empty carrier shares nothing");
        ((ThingOwner<Pawn>)holder.GetDirectlyHeldThings()).InnerListForReading.Add(human); human.holdingOwner = holder.GetDirectlyHeldThings();
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(human.health, PawnHealthState.Dead);
        Near(Value(worker, animal), 26, "dead original shares nothing");
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(human.health, PawnHealthState.Mobile);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(animal.health, PawnHealthState.Dead);
        Check(carrier.CurStage == null, "dead animal has no projected stage");
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(animal.health, PawnHealthState.Mobile);
        var recursion = new RecursiveActiveGene { pawn = human, carrier = carrier, def = new GeneDef { defName = "RecursiveActive", statOffsets = new List<StatModifier> { Mod(stat, 4), Mod(stat, 100) } } };
        human.genes.Endogenes.Add(recursion); AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes").SetValue(human.genes, null);
        Near(Value(worker, animal), 120, "recursive custom Active getter terminates and gene duplicate list uses native first entry");
        Gene zero = AddGene(human, new GeneDef { defName = "ZeroFactor", statFactors = new List<StatModifier> { Mod(stat, 0) } });
        Near(Value(worker, animal), 0, "zero factor is applied without division or infinity");
        zero.overriddenByGene = new Gene { def = new GeneDef() };
        Near(Value(worker, animal), 120, "deactivating zero factor restores normal value");
        Gene negative = AddGene(human, new GeneDef { defName = "NegativeOffset", statOffsets = new List<StatModifier> { Mod(stat, -50) } });
        Near(Value(worker, animal), -180, "negative inherited offset retains native unfinalized arithmetic");
        negative.overriddenByGene = new Gene { def = new GeneDef() };
        human.story.traits.allTraits.Remove(trait);
        Near(Value(worker, animal), 57, "removing source trait invalidates the same-tick snapshot");
        var baselineStage = new HediffStage { painFactor = .25f, statOffsets = new List<StatModifier> { Mod(stat, 5) }, statFactors = new List<StatModifier> { Mod(stat, .5f) } };
        carrier.def.stages = new List<HediffStage> { baselineStage };
        Near(Value(worker, animal), 36, "plain existing carrier stats compose at the native arithmetic stages");
        Check(carrier.CurStage != baselineStage && carrier.CurStage.painFactor == .25f, "non-stat carrier fields are preserved on a separate stage");
        Near(baselineStage.statOffsets[0].value, 5, "shared offset does not mutate the carrier definition");
        Near(baselineStage.statFactors[0].value, .5f, "shared factor does not mutate the carrier definition");
        carrier.def.stages = null;
    }

    private static Apparel Clothing(Pawn pawn, int id)
    {
        if (pawn.apparel == null) pawn.apparel = new Pawn_ApparelTracker(pawn);
        var clothing = new Apparel { thingIDNumber = id, stackCount = 1, def = new ThingDef {
            defName = "StatClothing" + id, apparel = new ApparelProperties { countsAsClothingForNudity = true } } };
        pawn.apparel.WornApparel.Add(clothing);
        return clothing;
    }

    private static void TestConditionalStats()
    {
        var stat = new StatDef { defName = "ConditionalSharedStat", label = "conditional shared stat", defaultBaseValue = 10,
            neverDisabled = true, noSkillFactor = 1, cacheable = true };
        var worker = new StatWorker(); worker.InitSetStat(stat); worker.SetCacheability(false);
        Pawn human = MakePawn(20, true), animal = MakePawn(21, false), ordinary = MakePawn(22, false);
        Hediff carrier = Mark(animal, human);
        var clothed = new ConditionalStatAffecter_Clothed { statOffsets = new List<StatModifier> { Mod(stat, -4) },
            statFactors = new List<StatModifier> { Mod(stat, .8f) } };
        var unclothed = new ConditionalStatAffecter_Unclothed { statOffsets = new List<StatModifier> { Mod(stat, 3), Mod(stat, 100) },
            statFactors = new List<StatModifier> { Mod(stat, 1.2f), Mod(stat, 100) } };
        var def = new GeneDef { defName = "ConditionalSharedGene", statOffsets = new List<StatModifier> { Mod(stat, 2) },
            statFactors = new List<StatModifier> { Mod(stat, 1.5f) },
            conditionalStatAffecters = new List<ConditionalStatAffecter> { clothed, unclothed } };
        Gene gene = AddGene(human, def);
        Clothing(human, 200);
        Check(clothed.Applies(StatRequest.For(human)) && !clothed.Applies(StatRequest.For(animal)), "native clothing condition distinguishes stored human and active animal");
        Near(CachedValue(worker, animal), 27, "unclothed positive conditional uses active animal, static and conditional first-entry arithmetic");
        Near(carrier.CurStage.statOffsets.GetStatOffsetFromList(stat), 5, "conditional tooltip stage contains the active offset");
        Near(carrier.CurStage.statFactors.GetStatFactorFromList(stat), 1.8f, "conditional tooltip stage contains the active factor");
        string explanation = worker.GetExplanationUnfinalized(StatRequest.For(animal), ToStringNumberSense.Absolute);
        Check(explanation.IndexOf("shapeshifter", StringComparison.OrdinalIgnoreCase) >= 0, "native explanation includes active conditional projection");
        HediffStage unchanged = carrier.CurStage;
        Check(ReferenceEquals(unchanged, carrier.CurStage), "unchanged conditions reuse the stage allocation");
        Apparel armor = Clothing(animal, 201);
        Near(CachedValue(worker, animal), 9.6f, "same-tick apparel change invalidates native cache and applies conditional penalty");
        Near(carrier.CurStage.statOffsets.GetStatOffsetFromList(stat), -2, "conditional tooltip immediately replaces bonus with penalty");
        Near(carrier.CurStage.statFactors.GetStatFactorFromList(stat), 1.2f, "conditional tooltip immediately replaces factor");
        animal.apparel.WornApparel.Remove(armor);
        Near(CachedValue(worker, animal), 27, "same-tick undressing restores the conditional bonus");
        gene.overriddenByGene = new Gene { def = new GeneDef() };
        Near(CachedValue(worker, animal), 10, "suppressed source gene removes static and conditional modifiers");
        gene.overriddenByGene = null;
        Gene duplicate = AddGene(human, def);
        Near(CachedValue(worker, animal), 27, "duplicate source GeneDef shares conditional effects once");
        Gene native = AddGene(animal, def);
        Near(CachedValue(worker, animal), 27, "same native active GeneDef excludes duplicate conditional projection");
        animal.apparel.WornApparel.Add(armor);
        Near(CachedValue(worker, animal), 9.6f, "same native gene's conditional transition invalidates native cache");
        native.overriddenByGene = new Gene { def = new GeneDef() };
        Near(CachedValue(worker, animal), 9.6f, "inactive native gene allows original conditional projection");
        gene.overriddenByGene = new Gene { def = new GeneDef() };
        duplicate.overriddenByGene = new Gene { def = new GeneDef() };
        Near(CachedValue(worker, animal), 10, "all suppressed duplicate source genes share nothing");
        Near(CachedValue(worker, ordinary), 10, "ordinary animal receives no conditional sharing");

        var immutable = new StatDef { defName = "ConditionalImmutableStat", defaultBaseValue = 5, immutable = true,
            neverDisabled = true, noSkillFactor = 1 };
        var immutableWorker = new StatWorker(); immutableWorker.InitSetStat(immutable); immutableWorker.SetCacheability(true);
        var position = new PositionAffecter { carrier = carrier,
            statOffsets = new List<StatModifier> { Mod(immutable, -2) }, statFactors = new List<StatModifier> { Mod(immutable, .5f) } };
        human.Position = new IntVec3(10, 0, 10); animal.Position = new IntVec3(1, 0, 1);
        Gene positionGene = AddGene(human, new GeneDef { defName = "ModPositionGene",
            conditionalStatAffecters = new List<ConditionalStatAffecter> { position } });
        Near(CachedValue(immutableWorker, animal), 5, "mod conditional is evaluated against active body position rather than stored human");
        animal.Position = new IntVec3(6, 0, 1);
        Near(CachedValue(immutableWorker, animal), 1.5f, "same-tick environmental condition change clears native immutable cache with reentrancy guard");
        animal.Position = new IntVec3(1, 0, 1);
        Near(CachedValue(immutableWorker, animal), 5, "same-tick environmental condition removal restores native immutable value");
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, false);
        animal.Position = new IntVec3(6, 0, 1);
        Near(CachedValue(immutableWorker, animal), 5, "Biotech deactivation excludes conditional gene effects");
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, true);
        Near(CachedValue(immutableWorker, animal), 1.5f, "Biotech reactivation restores current conditional state");
        Check(positionGene.pawn == human && animal.genes.Endogenes.Count == 1 && human.genes.Endogenes.Count == 3,
            "conditional bridge keeps gene ownership and native trackers separate");
        TestNativeSunlight();
    }

    private static void TestNativeSunlight()
    {
        // An isolated native map graph exercises the actual vanilla sunlight
        // predicate, roof grid and sky glow, without replacing any methods or
        // claiming that the full game has initialized/spawned these pawns.
        var map = Bare<Map>(); map.info = new MapInfo { Size = new IntVec3(10, 1, 10) };
        map.cellIndices = new CellIndices(map); map.roofGrid = new RoofGrid(map); map.skyManager = Bare<SkyManager>();
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { map });
        var glow = AccessTools.Field(typeof(SkyManager), "curSkyGlowInt"); glow.SetValue(map.skyManager, 1f);
        Pawn human = MakePawn(30, true), animal = MakePawn(31, false);
        animal.Position = new IntVec3(2, 0, 2);
        Mark(animal, human);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(animal, (sbyte)0);
        try
        {
            var stat = new StatDef { defName = "SunlightSharedStat", defaultBaseValue = 10, cacheable = true,
                neverDisabled = true, noSkillFactor = 1 };
            var worker = new StatWorker(); worker.InitSetStat(stat); worker.SetCacheability(false);
            var sunlight = new ConditionalStatAffecter_InSunlight { statOffsets = new List<StatModifier> { Mod(stat, -2) },
                statFactors = new List<StatModifier> { Mod(stat, .8f) } };
            AddGene(human, new GeneDef { defName = "NativeSunlightGene", conditionalStatAffecters = new List<ConditionalStatAffecter> { sunlight } });
            Check(!sunlight.Applies(StatRequest.For(human)) && sunlight.Applies(StatRequest.For(animal)), "native sunlight predicate observes active map body, not off-map original");
            Near(CachedValue(worker, animal), 6.4f, "native sunlight penalty has both conditional offset and factor");
            RoofDef[] roofs = (RoofDef[])AccessTools.Field(typeof(RoofGrid), "roofGrid").GetValue(map.roofGrid);
            roofs[map.cellIndices.CellToIndex(animal.Position)] = new RoofDef { defName = "StatTestRoof" };
            Near(CachedValue(worker, animal), 10, "same-tick native roof change removes sunlight penalty from cached stat");
            roofs[map.cellIndices.CellToIndex(animal.Position)] = null;
            glow.SetValue(map.skyManager, .05f);
            Near(CachedValue(worker, animal), 10, "native night sky suppresses sunlight penalty");
            glow.SetValue(map.skyManager, 1f);
            Near(CachedValue(worker, animal), 6.4f, "same-tick native daylight transition reapplies cached sunlight penalty");
        }
        finally
        {
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(animal, (sbyte)-1);
            AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map>());
        }
    }

    private static T Bare<T>() where T : class { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    private static void Near(float actual, float expected, string label) { Check(Math.Abs(actual - expected) < .001f, label + " (" + actual + " vs " + expected + ")"); }
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
}
