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
    }

    private static T Bare<T>() where T : class { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    private static void Near(float actual, float expected, string label) { Check(Math.Abs(actual - expected) < .001f, label + " (" + actual + " vs " + expected + ")"); }
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
}
