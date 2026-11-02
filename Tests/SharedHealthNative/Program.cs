using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using WildShift;

internal static class Program
{
    private static readonly HashSet<Pawn> fixtures = new HashSet<Pawn>();
    private static int assertions;
    internal static Pawn observedStagePawn;
    internal static bool observedLethalScope;

    private static int Main(string[] args)
    {
        if (args.Length != 3) return 2;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { args[0], args[1] })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { Run(args[0], args[2]); Console.WriteLine("PASS: " + assertions + " shared-health native checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string gameDir, string scratch)
    {
        string productionPath = typeof(SharedFormHealth).Assembly.Location;
        Console.WriteLine("Tested production assembly: " + productionPath);
        Console.WriteLine("Production SHA256: " + BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(productionPath))).Replace("-", ""));
        Console.WriteLine("Mode: production DLL reference; production source is not linked into the test assembly.");
        string profile = Path.Combine(scratch, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Config"));
        Directory.CreateDirectory(Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, profile);
        AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(gameDir, "Data"));
        File.WriteAllText(Path.Combine(profile, "Config", "ModsConfig.xml"), "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        var prefs = AccessTools.Field(typeof(Prefs), "data");
        prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
        var game = Bare<Game>();
        game.tickManager = Bare<TickManager>();
        game.uniqueIDsManager = new UniqueIDsManager();
        game.playSettings = new PlaySettings();
        AccessTools.Field(typeof(Game), "worldInt").SetValue(game, Bare<World>());
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
        Current.Game = game;
        Find.World.worldPawns = new WorldPawns();
        Find.World.worldObjects = new WorldObjectsHolder();
        SetTick(1000);

        // Native body/UI state transitions require an actual map. Only those
        // unrelated boundaries are suppressed in this isolated graph fixture;
        // hediff ownership, native drug comps and chemical needs remain real.
        var boundary = new Harmony("wildshift.tests.shared-health.boundaries");
        boundary.Patch(AccessTools.Method(typeof(HediffSet), "DirtyCache"),
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipFixtureCache)));
        boundary.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange"),
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipFixtureHealthState)));
        boundary.Patch(AccessTools.Method(typeof(Pawn_NeedsTracker), "AddOrRemoveNeedsAsAppropriate"),
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipFixtureNeedSet)));
        var production = new Harmony("wildshift.tests.shared-health.production");
        production.PatchAll(typeof(SharedFormHealth).Assembly);

        var stage = new HediffStage { painFactor = 0.8f, capMods = new List<PawnCapacityModifier> { new PawnCapacityModifier { capacity = PawnCapacityDefOf.Moving, offset = 0.05f } } };
        var highDef = new HediffDef { defName = "FixtureLuciferiumHigh", label = "fixture luciferium", hediffClass = typeof(Hediff_High), initialSeverity = 1, maxSeverity = 1,
            stages = new List<HediffStage> { stage }, comps = new List<HediffCompProperties> { new HediffCompProperties_Disappears { disappearsAfterTicks = new IntRange(600,600) } } };
        var needDef = new NeedDef { defName = "FixtureChemical", needClass = typeof(Need_Chemical), fallPerDay = 0.15f, developmentalStageFilter = DevelopmentalStage.Adult };
        var addictionDef = new HediffDef { defName = "FixtureAddiction", label = "fixture addiction", hediffClass = typeof(Hediff_Addiction), chemicalNeed = needDef, initialSeverity = 1,
            stages = new List<HediffStage> { new HediffStage(), new HediffStage { painOffset = 0.4f } } };
        var toleranceDef = new HediffDef { defName = "FixtureTolerance", label = "fixture tolerance", hediffClass = typeof(Hediff), initialSeverity = 0.18f,
            maxSeverity = 1, stages = new List<HediffStage> { new HediffStage() } };
        var healingHighDef = new HediffDef { defName = "FixtureLegacyHealingHigh", label = "fixture healing high", hediffClass = typeof(Hediff_High), initialSeverity = 1,
            maxSeverity = 1, stages = new List<HediffStage> { stage },
            comps = new List<HediffCompProperties> { new HediffCompProperties_HealPermanentWounds() } };
        var visualHighDef = new HediffDef { defName = "FixtureLegacyEffecterHigh", label = "fixture visual high", hediffClass = typeof(Hediff_High), initialSeverity = 1,
            maxSeverity = 1, stages = new List<HediffStage> { stage },
            comps = new List<HediffCompProperties> { new HediffCompProperties_Effecter() } };
        var chemical = new ChemicalDef { defName = "FixtureLuciferium", addictionHediff = addictionDef, toleranceHediff = toleranceDef };
        var drugDef = new ThingDef { defName = "FixtureDrug", category = ThingCategory.Item, comps = new List<CompProperties> { new CompProperties_Drug { chemical = chemical, addictiveness = 1, needLevelOffset = 0.9f } }, ingestible = new IngestibleProperties { drugCategory = DrugCategory.Medical,
            outcomeDoers = new List<IngestionOutcomeDoer> {
                new IngestionOutcomeDoer_GiveHediff { hediffDef = highDef, severity = 1 },
                new IngestionOutcomeDoer_GiveHediff { hediffDef = healingHighDef, severity = 1 },
                new IngestionOutcomeDoer_GiveHediff { hediffDef = visualHighDef, severity = 1 }
            } } };
        var projectionDef = new HediffDef { defName = SharedFormHealth.ProjectionDefName, label = "shared state", hediffClass = typeof(Hediff_SharedFormDrugEffect), initialSeverity = 1 };
        foreach (var def in new[] { highDef, addictionDef, toleranceDef, healingHighDef, visualHighDef, projectionDef }) DefDatabase<HediffDef>.Add(def);
        DefDatabase<ThingDef>.Add(drugDef);
        DefDatabase<ChemicalDef>.Add(chemical);
        DefDatabase<NeedDef>.Add(needDef);

        Pawn human = Pawn(201, Intelligence.Humanlike), animal = Pawn(202, Intelligence.Animal);
        var chemicalNeed = new Need_Chemical(human) { def = needDef, CurLevel = 0.5f };
        var needs = new List<Need> { chemicalNeed };
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(human.needs, needs);
        var addiction = (Hediff_Addiction)HediffMaker.MakeHediff(addictionDef, human);
        human.health.hediffSet.hediffs.Add(addiction);
        var high = HediffMaker.MakeHediff(highDef, human);
        human.health.hediffSet.hediffs.Add(high);
        var disappears = high.TryGetComp<HediffComp_Disappears>();
        var comp = Mark(animal);
        Check(comp.TryStore(human), "Native ThingOwner stores the original human once");
        SharedFormIdentity.Initialize(animal);
        SharedFormHealth.Initialize(animal);
        var projection = animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Single(x => x.source == high);
        Check(projection != high && projection.pawn == animal && high.pawn == human, "Projection and source have separate ownership");
        Check(object.ReferenceEquals(projection.CurStage, high.CurStage), "Projection reads original immutable stage definition");
        Check(projection.CapMods.Single().offset == 0.05f && projection.PainFactor == 0.8f, "Native projection exposes source capacity and pain effects");
        Check(projection.def.chemicalNeed == null && animal.needs.AllNeeds.Count == 0, "Projection does not create a duplicate chemical need");
        SharedFormHealth.Refresh(animal);
        Check(animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count() == 2, "Refreshing is idempotent per source hediff");
        Check(!SharedFormHealth.IsSharedLethalDeath(animal), "Ordinary injury outside shared processing is not marked as drug death");

        int initial = disappears.ticksToDisappear;
        SetTick(1001);
        SharedFormHealth.Tick(animal);
        Check(disappears.ticksToDisappear == initial - 1, "Original native disappearance timer advances once");
        SharedFormHealth.Tick(animal);
        human.health.HealthTick();
        Check(disappears.ticksToDisappear == initial - 1, "Repeated calls and native hidden-human tick cannot double advance timer");
        SetTick(1150);
        float before = chemicalNeed.CurLevel;
        SharedFormHealth.TickInterval(animal, 150);
        Check(chemicalNeed.CurLevel == before, "Unexposed world pawn retains native chemical-need freeze");

        // Establish native caravan membership. The original is held through a
        // HediffComp, which native GetCaravan cannot traverse by itself.
        var caravan = new Caravan();
        ((HashSet<WorldObject>)AccessTools.Field(typeof(WorldObjectsHolder), "worldObjectsHashSet").GetValue(Find.WorldObjects)).Add(caravan);
        caravan.pawns.InnerListForReading.Add(animal);
        animal.holdingOwner = caravan.pawns;
        Check(animal.GetCaravan() == caravan && human.GetCaravan() == null, "Native caravan lookup reproduces hidden-holder boundary");
        SetTick(1300);
        SharedFormHealth.TickInterval(animal, 150);
        Check(Math.Abs(chemicalNeed.CurLevel - (before - 0.15f * 150 / 60000)) < 0.000001f, "Chemical need advances at native rate in caravan");
        float after = chemicalNeed.CurLevel;
        SharedFormHealth.TickInterval(animal, 150);
        Check(chemicalNeed.CurLevel == after, "Duplicate interval in caravan does not consume drug need twice");
        Check(high.ageTicks == 300, "Original source age advances by elapsed intervals only");
        chemicalNeed.CurLevel = 0;
        Check(addiction.CurStageIndex == 1, "Native addiction enters withdrawal stage");
        SharedFormHealth.Refresh(animal);
        Check(animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Single(x => x.source == addiction).PainOffset == 0.4f, "Animal receives live native withdrawal stage");

        var dependencyDef = new HediffDef { defName = "FixtureGeneticDrugNeed", label = "genetic need", hediffClass = typeof(Hediff_ChemicalDependency), initialSeverity = 0, maxSeverity = 12, lethalSeverity = 12,
            stages = new List<HediffStage> { new HediffStage() }, comps = new List<HediffCompProperties> { new HediffCompProperties_SeverityPerDay { severityPerDay = 0.2f, minAge = 13 } } };
        human.genes = Bare<Pawn_GeneTracker>();
        human.genes.pawn = human;
        var dependencyGene = new Gene_ChemicalDependency { pawn = human, def = new GeneDef { defName = "FixtureDependencyGene", chemical = chemical } };
        AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes").SetValue(human.genes, new List<Gene> { dependencyGene });
        var dependency = (Hediff_ChemicalDependency)HediffMaker.MakeHediff(dependencyDef, human);
        dependency.chemical = chemical;
        human.health.hediffSet.hediffs.Add(dependency);
        dependency.PostAdd(null);
        SharedFormHealth.Refresh(animal);
        Check(SharedFormHealth.IsSupported(dependency), "Native genetic dependency is supported without a duplicate gene");
        for (int tick = 1301; tick <= 1500; tick++) { SetTick(tick); SharedFormHealth.TickInterval(animal, 1); }
        Check(Math.Abs(dependency.Severity - 0.2f * 200 / 60000) < 0.000001f, "Native genetic-dependency severity comp progresses while transformed");

        var drug = new ThingWithComps { def = drugDef, stackCount = 1 };
        drug.InitializeComps();
        dependencyGene.Notify_IngestedThing(drug, 1);
        Check(dependency.Severity == 0 && dependencyGene.lastIngestedTick == 1500, "Native dependency reset targets original hediff and clock");
        Pawn recipient = animal;
        Pawn state;
        Patch_SharedDrugBeforeIngestion.Prefix(ref recipient, out state);
        try { Check(recipient == human && state == animal, "Drug component recipient redirects without changing physical ingester"); }
        finally { Patch_SharedDrugBeforeIngestion.Finalizer(state); }
        recipient = animal;
        Patch_SharedDrugOutcome.Prefix((IngestionOutcomeDoer_GiveHediff)drugDef.ingestible.outcomeDoers[0], ref recipient, drug, out state);
        try { Check(recipient == human, "Native drug GiveHediff outcome selects original body"); }
        finally { Patch_SharedDrugOutcome.Finalizer(state); }
        recipient = animal;
        Patch_SharedDrugOutcome.Prefix(new IngestionOutcomeDoer_GiveHediff(), ref recipient, new Thing { def = new ThingDef { ingestible = new IngestibleProperties() } }, out state);
        Check(recipient == animal && state == null, "Ordinary food outcome remains on active animal");
        drug.TryGetComp<CompDrug>().PrePostIngested(animal);
        Check(chemicalNeed.CurLevel > 0.8f && animal.needs.AllNeeds.Count == 0,
            "Actual native CompDrug refills original chemical need without animal addiction copy");
        ((IngestionOutcomeDoer_GiveHediff)drugDef.ingestible.outcomeDoers[0]).DoIngestionOutcome(animal, drug, 1);
        Check(human.health.hediffSet.hediffs.Count(x => x.def == highDef) == 1
            && !animal.health.hediffSet.hediffs.Any(x => x is Hediff_High)
            && disappears.ticksToDisappear == initial,
            "Actual native drug outcome merges dose into original timer without animal high copy");

        stage.hediffGivers = new List<HediffGiver> { new RecordingGiver() };
        try { projection.TickInterval(60); }
        catch (StageProbeException) { }
        Check(observedStagePawn == animal && observedLethalScope, "Native stage events run on active animal within precise shared-death scope");
        Check(high.pawn == human && !SharedFormHealth.IsSharedLethalDeath(animal), "Exceptional stage event keeps original ownership and releases lethal scope");
        stage.hediffGivers = null;
        var part = new BodyPartRecord { def = new BodyPartDef { defName = "FixtureLeg" } };
        var partDrug = new Hediff_High { pawn = human, def = highDef, Part = part };
        Check(!SharedFormHealth.IsSupported(partDrug), "Body-part-bound hediff is excluded");
        Check(!SharedFormHealth.IsSupported(new CustomDrug { pawn = human, def = highDef }), "Arbitrary custom hediff behavior is excluded");
        human.health.hediffSet.hediffs.Remove(high);
        SharedFormHealth.Refresh(animal);
        Check(!animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(x => x.source == high), "Removed original effect leaves no stale animal projection");
        comp.ReleaseStoredPawn();
        caravan.pawns.InnerListForReading.Remove(animal);
        animal.holdingOwner = null;
        var dormant = new AnimalFormStorage(human);
        Check(dormant.TryStore(animal, new PawnKindDef { defName = "FixtureCachedKind", race = animal.def })
            && AnimalFormStorage.IsStored(animal), "Native production holder caches the animal after its original is released");
        ((IngestionOutcomeDoer_GiveHediff)drugDef.ingestible.outcomeDoers[0]).DoIngestionOutcome(animal, drug, 1);
        Check(!animal.health.hediffSet.hediffs.Any(h => h.def == highDef),
            "Inactive cached animal rejects the remaining native drug ingestion outcome");
        RunBuffRegressions();
        RunLegacyDrugRegressions(scratch, highDef, toleranceDef, addictionDef, needDef, healingHighDef, visualHighDef);
        Console.WriteLine("Scope: actual native hediff ownership/comps/chemical needs and isolated caravan holder graph; full map/UI health transitions and need-set rebuilding are fixture boundaries. Full ingestion/return/death/save lifecycle requires the engine fixture.");
    }

    private static void RunLegacyDrugRegressions(string scratch, HediffDef highDef, HediffDef toleranceDef,
        HediffDef addictionDef, NeedDef needDef, HediffDef healingHighDef, HediffDef visualHighDef)
    {
        Console.WriteLine("Legacy migration scope: native pre-bridge animal-owned drug Hediffs and chemical need. No production outcome is mocked; native health/need UI boundaries remain isolated.");
        SetTick(4000);
        Pawn human = Pawn(501, Intelligence.Humanlike), animal = Pawn(502, Intelligence.Animal);
        var high = HediffMaker.MakeHediff(highDef, animal);
        var tolerance = HediffMaker.MakeHediff(toleranceDef, animal);
        var addiction = HediffMaker.MakeHediff(addictionDef, animal);
        var healingHigh = HediffMaker.MakeHediff(healingHighDef, animal);
        var visualHigh = HediffMaker.MakeHediff(visualHighDef, animal);
        var healingClock = AccessTools.Field(typeof(HediffComp_HealPermanentWounds), "ticksToHeal");
        healingClock.SetValue(healingHigh.TryGetComp<HediffComp_HealPermanentWounds>(), 731);
        high.Severity = 0.4f;
        high.ageTicks = 67;
        var timer = high.TryGetComp<HediffComp_Disappears>();
        timer.ticksToDisappear = 237;
        tolerance.Severity = 0.18f;
        tolerance.ageTicks = 11;
        addiction.ageTicks = 31;
        // Native Scribe puts these exact objects in the loaded animal's lists.
        // Reconstruct that pre-bridge state before establishing the human link,
        // so the new ingestion redirection cannot pre-migrate the fixture.
        animal.health.hediffSet.hediffs.AddRange(new[] { high, tolerance, addiction, healingHigh, visualHigh });
        var chemicalNeed = new Need_Chemical(animal) { def = needDef, CurLevel = 0.37f };
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(animal.needs, new List<Need> { chemicalNeed });
        Check(ReferenceEquals(((Hediff_Addiction)addiction).Need, chemicalNeed)
            && ReferenceEquals(chemicalNeed.AddictionHediff, addiction),
            "Legacy addiction and chemical-need properties are read before migration to expose any stale body cache");
        var anatomical = HediffMaker.MakeHediff(highDef, animal,
            new BodyPartRecord { def = new BodyPartDef { defName = "FixtureLegacyDrugLeg" } });
        var custom = new CustomDrug { pawn = animal, def = highDef };
        animal.health.hediffSet.hediffs.AddRange(new Hediff[] { anatomical, custom });
        Check(SharedFormHealth.IsSupported(high) && SharedFormHealth.IsSupported(tolerance)
            && SharedFormHealth.IsSupported(addiction),
            "Legacy migration fixture uses three supported native drug classes and registered chemical definitions");
        Check(!SharedFormHealth.IsSupported(anatomical) && !SharedFormHealth.IsSupported(custom),
            "Legacy migration fixture keeps body-bound and custom drug controls outside the supported bridge");
        var holder = Mark(animal);
        Check(holder.TryStore(human), "Legacy drug fixture owns the exact original through the native transformed holder");
        SharedFormIdentity.Initialize(animal);
        Rand.PushState(34901);
        int expectedRandom;
        try
        {
            // Each neutral native projection runs Hediff.PostMake, which draws
            // causesNoPain even with chance zero. Those four native creations
            // are expected; chemical-need reconstruction must add no extra roll.
            HediffDef projectionDef = DefDatabase<HediffDef>.GetNamed(SharedFormHealth.ProjectionDefName);
            for (int i = 0; i < 4; i++) HediffMaker.MakeHediff(projectionDef, animal);
            expectedRandom = Rand.Int;
        }
        finally { Rand.PopState(); }
        Rand.PushState(34901);
        int observedRandom;
        try { SharedFormHealth.Initialize(animal); observedRandom = Rand.Int; }
        finally { Rand.PopState(); }

        var failures = new List<string>();
        Action<bool, string> verify = delegate(bool passed, string message)
        {
            if (passed) Check(true, message);
            else { failures.Add(message); Console.WriteLine("FAIL: " + message); }
        };
        verify(observedRandom == expectedRandom,
            "Legacy need reconstruction adds no random rolls beyond native neutral projection creation");
        verify(high.pawn == human && human.health.hediffSet.hediffs.Contains(high)
            && !animal.health.hediffSet.hediffs.Contains(high),
            "Legacy animal-only high migrates as the exact canonical human-owned object");
        verify(tolerance.pawn == human && human.health.hediffSet.hediffs.Contains(tolerance)
            && addiction.pawn == human && human.health.hediffSet.hediffs.Contains(addiction)
            && !animal.health.hediffSet.hediffs.Contains(tolerance) && !animal.health.hediffSet.hediffs.Contains(addiction),
            "Legacy native tolerance and addiction migrate without leftover animal-owned copies");
        verify(timer.ticksToDisappear == 237 && high.ageTicks == 67 && Math.Abs(high.Severity - 0.4f) < 0.000001f
            && tolerance.ageTicks == 11 && Math.Abs(tolerance.Severity - 0.18f) < 0.000001f && addiction.ageTicks == 31,
            "Legacy drug migration does not replay PostAdd, reroll severity, reset duration or advance age");
        verify(human.needs.TryGetNeed(needDef) != null
            && Math.Abs(human.needs.TryGetNeed(needDef).CurLevel - 0.37f) < 0.000001f
            && animal.needs.TryGetNeed(needDef) == null,
            "Legacy native chemical need retains its saved level under one canonical human tracker");
        verify(animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == high) == 1
            && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == tolerance) == 1
            && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == addiction) == 1,
            "Legacy drug migration exposes exactly one active-body projection for each canonical source");
        verify(anatomical.pawn == animal && custom.pawn == animal
            && animal.health.hediffSet.hediffs.Contains(anatomical) && animal.health.hediffSet.hediffs.Contains(custom)
            && !human.health.hediffSet.hediffs.Contains(anatomical) && !human.health.hediffSet.hediffs.Contains(custom),
            "Legacy migration leaves unsupported anatomy-bound and custom drug objects local");
        verify(visualHigh.pawn == animal && animal.health.hediffSet.hediffs.Contains(visualHigh)
            && !human.health.hediffSet.hediffs.Contains(visualHigh),
            "A native visual component outside the reviewed migration set remains on its existing body");
        verify(healingHigh.pawn == human && human.health.hediffSet.hediffs.Contains(healingHigh)
            && !animal.health.hediffSet.hediffs.Contains(healingHigh)
            && healingHigh.TryGetComp<HediffComp_HealPermanentWounds>().Pawn == human
            && (int)healingClock.GetValue(healingHigh.TryGetComp<HediffComp_HealPermanentWounds>()) == 731,
            "Native luciferium-style healing component follows its exact moved Hediff without a cached animal or reset clock");
        Need migratedNeed = human.needs.TryGetNeed(needDef);
        verify(migratedNeed != null && AccessTools.Field(typeof(Need), "pawn").GetValue(migratedNeed) == human
            && ReferenceEquals(((Hediff_Addiction)addiction).Need, migratedNeed)
            && ReferenceEquals(((Need_Chemical)migratedNeed).AddictionHediff, addiction),
            "Moved native addiction dynamically resolves the new human chemical need after prior animal-side property reads");
        SharedFormHealth.Initialize(animal);
        verify(human.health.hediffSet.hediffs.Count(h => h.def == highDef) == 1
            && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == high) == 1
            && timer.ticksToDisappear == 237,
            "Repeated legacy drug initialization is idempotent with no duplicate source or timer refresh");

        // A separate native graph covers an already-canonical matching dose.
        // The original lasts longer than the old animal dose, so retaining the
        // existing canonical timer does not prescribe a contested merge policy.
        Pawn existingHuman = Pawn(503, Intelligence.Humanlike), existingAnimal = Pawn(504, Intelligence.Animal);
        var canonical = HediffMaker.MakeHediff(highDef, existingHuman);
        var extra = HediffMaker.MakeHediff(highDef, existingAnimal);
        canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 511;
        extra.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 237;
        canonical.Severity = 0.2f; canonical.ageTicks = 144;
        extra.Severity = 0.4f; extra.ageTicks = 67;
        existingHuman.health.hediffSet.hediffs.Add(canonical);
        existingAnimal.health.hediffSet.hediffs.Add(extra);
        var existingHolder = Mark(existingAnimal);
        Check(existingHolder.TryStore(existingHuman), "Matching legacy dose fixture retains its existing canonical original");
        SharedFormIdentity.Initialize(existingAnimal);
        SharedFormHealth.Initialize(existingAnimal);
        verify(existingHuman.health.hediffSet.hediffs.Count(h => h.def == highDef) == 1
            && existingHuman.health.hediffSet.hediffs.Contains(canonical)
            && !existingAnimal.health.hediffSet.hediffs.Any(h => h.def == highDef)
            && existingAnimal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == canonical) == 1,
            "Matching legacy animal dose cannot remain as a second physical effect beside its canonical projection");
        verify(canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 511,
            "An existing longer canonical high is not shortened or reset by legacy-dose initialization");
        verify(Math.Abs(canonical.Severity - 0.4f) < 0.000001f && canonical.ageTicks == 144,
            "Matching legacy doses retain original identity and age while taking maximum severity instead of summing a new dose");
        SharedFormHealth.Initialize(existingAnimal);
        verify(canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 511
            && Math.Abs(canonical.Severity - 0.4f) < 0.000001f && canonical.ageTicks == 144,
            "Reinitializing merged legacy doses cannot grow severity or refresh duration");

        Pawn lowerHuman = Pawn(505, Intelligence.Humanlike), lowerAnimal = Pawn(506, Intelligence.Animal);
        var shorter = HediffMaker.MakeHediff(highDef, lowerHuman);
        var longer = HediffMaker.MakeHediff(highDef, lowerAnimal);
        shorter.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 127;
        longer.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 237;
        shorter.Severity = 0.6f; shorter.ageTicks = 44;
        longer.Severity = 0.2f; longer.ageTicks = 101;
        var humanAddiction = HediffMaker.MakeHediff(addictionDef, lowerHuman);
        var animalAddiction = HediffMaker.MakeHediff(addictionDef, lowerAnimal);
        lowerHuman.health.hediffSet.hediffs.AddRange(new[] { shorter, humanAddiction });
        lowerAnimal.health.hediffSet.hediffs.AddRange(new[] { longer, animalAddiction });
        var lowerNeed = new Need_Chemical(lowerHuman) { def = needDef, CurLevel = 0.12f };
        var higherNeed = new Need_Chemical(lowerAnimal) { def = needDef, CurLevel = 0.37f };
        var humanFood = new Need_Food(lowerHuman) { def = new NeedDef { defName = "FixtureLegacyHumanFood", needClass = typeof(Need_Food) } };
        var animalFood = new Need_Food(lowerAnimal) { def = new NeedDef { defName = "FixtureLegacyAnimalFood", needClass = typeof(Need_Food) } };
        humanFood.CurLevel = 0.55f; animalFood.CurLevel = 0.6f;
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(lowerHuman.needs, new List<Need> { lowerNeed, humanFood });
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(lowerAnimal.needs, new List<Need> { higherNeed, animalFood });
        var lowerHolder = Mark(lowerAnimal);
        Check(lowerHolder.TryStore(lowerHuman), "Existing lower chemical-need fixture preserves its canonical original");
        SharedFormIdentity.Initialize(lowerAnimal);
        SharedFormHealth.Initialize(lowerAnimal);
        verify(lowerHuman.health.hediffSet.hediffs.Contains(shorter)
            && !lowerAnimal.health.hediffSet.hediffs.Contains(longer)
            && shorter.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 237
            && Math.Abs(shorter.Severity - 0.6f) < 0.000001f && shorter.ageTicks == 101,
            "A longer saved animal timer survives without summing severity, resetting age or replacing the original source ID");
        verify(ReferenceEquals(lowerHuman.needs.TryGetNeed(needDef), lowerNeed)
            && Math.Abs(lowerNeed.CurLevel - 0.12f) < 0.000001f && lowerAnimal.needs.TryGetNeed(needDef) == null,
            "Combining saved native chemical needs retains the lower canonical level instead of refilling it");
        verify(lowerHuman.needs.food == humanFood && lowerAnimal.needs.food == animalFood
            && Math.Abs(humanFood.CurLevel - 0.55f) < 0.000001f && Math.Abs(animalFood.CurLevel - 0.6f) < 0.000001f,
            "Native tracker field rebinding preserves unrelated original and physical-animal nutrition needs");
        SharedFormHealth.Initialize(lowerAnimal);
        verify(Math.Abs(lowerNeed.CurLevel - 0.12f) < 0.000001f
            && shorter.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 237,
            "Repeated native chemical reconciliation cannot refill need or extend the saved maximum timer");
        var olderWithdrawal = HediffMaker.MakeHediff(addictionDef, lowerAnimal);
        lowerAnimal.health.hediffSet.hediffs.Add(olderWithdrawal);
        lowerAnimal.needs.AllNeeds.Add(new Need_Chemical(lowerAnimal) { def = needDef, CurLevel = 0f });
        SharedFormHealth.Initialize(lowerAnimal);
        verify(ReferenceEquals(lowerHuman.needs.TryGetNeed(needDef), lowerNeed)
            && lowerNeed.CurLevel == 0f
            && ReferenceEquals(((Hediff_Addiction)humanAddiction).Need, lowerNeed)
            && ((Hediff_Addiction)humanAddiction).CurStageIndex == 1
            && lowerAnimal.needs.TryGetNeed(needDef) == null,
            "A lower saved animal level retains withdrawal without rebinding the canonical need to its obsolete body");

        Pawn excludedHuman = Pawn(507, Intelligence.Humanlike), excludedAnimal = Pawn(508, Intelligence.Animal);
        var customNeed = new CustomChemicalNeed(excludedAnimal) { def = needDef, CurLevel = 0.31f };
        var customNeedAddiction = HediffMaker.MakeHediff(addictionDef, excludedAnimal);
        var dependencyDef = new HediffDef { defName = "FixtureLegacyGeneDependency", hediffClass = typeof(Hediff_ChemicalDependency), initialSeverity = 0.5f,
            maxSeverity = 1, stages = new List<HediffStage> { new HediffStage() } };
        var cravingDef = new HediffDef { defName = "FixtureLegacyHemogenCraving", hediffClass = typeof(Hediff_HemogenCraving), initialSeverity = 0.3f,
            maxSeverity = 1, stages = new List<HediffStage> { new HediffStage() } };
        var dependency = (Hediff_ChemicalDependency)HediffMaker.MakeHediff(dependencyDef, excludedAnimal);
        dependency.chemical = DefDatabase<ChemicalDef>.AllDefsListForReading.Single(c => c.addictionHediff == addictionDef);
        var craving = HediffMaker.MakeHediff(cravingDef, excludedAnimal);
        excludedAnimal.health.hediffSet.hediffs.AddRange(new[] { customNeedAddiction, dependency, craving });
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(excludedAnimal.needs, new List<Need> { customNeed });
        var excludedHolder = Mark(excludedAnimal);
        Check(excludedHolder.TryStore(excludedHuman), "Unsupported legacy need/gene fixture stores an original without a source gene");
        SharedFormIdentity.Initialize(excludedAnimal);
        SharedFormHealth.Initialize(excludedAnimal);
        verify(customNeedAddiction.pawn == excludedAnimal && customNeed.CurLevel == 0.31f
            && ReferenceEquals(excludedAnimal.needs.TryGetNeed(needDef), customNeed)
            && excludedHuman.needs.TryGetNeed(needDef) == null,
            "A custom chemical-need subclass and its consuming addiction remain body-local without state loss");
        verify(dependency.pawn == excludedAnimal && craving.pawn == excludedAnimal
            && excludedAnimal.health.hediffSet.hediffs.Contains(dependency) && excludedAnimal.health.hediffSet.hediffs.Contains(craving)
            && !excludedHuman.health.hediffSet.hediffs.Contains(dependency) && !excludedHuman.health.hediffSet.hediffs.Contains(craving),
            "Animal gene-dependent deficiency and hemogen craving are not adopted without their original-human source gene");

        Pawn healingHuman = Pawn(509, Intelligence.Humanlike), healingAnimal = Pawn(510, Intelligence.Animal);
        var canonicalHealing = HediffMaker.MakeHediff(healingHighDef, healingHuman);
        var legacyHealing = HediffMaker.MakeHediff(healingHighDef, healingAnimal);
        int canonicalHealingId = canonicalHealing.loadID;
        var canonicalHealingComp = canonicalHealing.TryGetComp<HediffComp_HealPermanentWounds>();
        var legacyHealingComp = legacyHealing.TryGetComp<HediffComp_HealPermanentWounds>();
        healingClock.SetValue(canonicalHealingComp, 600000);
        healingClock.SetValue(legacyHealingComp, 731);
        healingHuman.health.hediffSet.hediffs.Add(canonicalHealing);
        healingAnimal.health.hediffSet.hediffs.Add(legacyHealing);
        var healingHolder = Mark(healingAnimal);
        Check(healingHolder.TryStore(healingHuman), "Paired healing-clock fixture owns its original canonical drug source");
        SharedFormIdentity.Initialize(healingAnimal);
        SharedFormHealth.Initialize(healingAnimal);
        int earlierLegacyClock = (int)healingClock.GetValue(canonicalHealingComp);
        verify(earlierLegacyClock == 731 && canonicalHealing.loadID == canonicalHealingId
            && healingHuman.health.hediffSet.hediffs.Contains(canonicalHealing)
            && !healingAnimal.health.hediffSet.hediffs.Contains(legacyHealing),
            "Merging paired luciferium-style highs preserves the earlier pending animal heal and canonical source identity");
        verify(canonicalHealingComp.Pawn == healingHuman && canonicalHealing.ageTicks == 0
            && healingAnimal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == canonicalHealing) == 1,
            "Paired healing-clock adoption creates one view without ticking, dosing or healing during initialization");
        SharedFormHealth.Initialize(healingAnimal);
        verify((int)healingClock.GetValue(canonicalHealingComp) == 731,
            "Reinitialization retains the merged pending heal without restarting its native interval");

        // A later old-animal clock must likewise never delay a nearer heal on
        // the already-canonical source. Preserve exactly one pending interval.
        healingClock.SetValue(canonicalHealingComp, 317);
        var laterLegacyHealing = HediffMaker.MakeHediff(healingHighDef, healingAnimal);
        healingClock.SetValue(laterLegacyHealing.TryGetComp<HediffComp_HealPermanentWounds>(), 800000);
        healingAnimal.health.hediffSet.hediffs.Add(laterLegacyHealing);
        SharedFormHealth.Initialize(healingAnimal);
        int earlierCanonicalClock = (int)healingClock.GetValue(canonicalHealingComp);
        verify(earlierCanonicalClock == 317 && canonicalHealing.loadID == canonicalHealingId
            && !healingAnimal.health.hediffSet.hediffs.Contains(laterLegacyHealing),
            "A later saved animal healing clock cannot postpone the earlier canonical heal");
        SharedFormHealth.Initialize(healingAnimal);
        verify((int)healingClock.GetValue(canonicalHealingComp) == 317
            && healingHuman.health.hediffSet.hediffs.Count(h => h.def == healingHighDef) == 1
            && healingAnimal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == canonicalHealing) == 1,
            "Repeated paired healing reconciliation keeps one original, one projection and one unchanged native clock");
        File.WriteAllText(Path.Combine(scratch, "legacy-drug-observed.json"), System.Text.Json.JsonSerializer.Serialize(new {
            Scope = "Native isolated pre-bridge ownership graphs, no full-engine or actual-old-save assertion",
            AnimalOnly = new {
                HumanHediffs = human.health.hediffSet.hediffs.Select(h => h.def.defName).ToArray(),
                AnimalHediffs = animal.health.hediffSet.hediffs.Select(h => h.GetType().Name + ":" + h.def.defName).ToArray(),
                HumanChemicalNeeds = human.needs.AllNeeds.OfType<Need_Chemical>().Select(n => n.CurLevel).ToArray(),
                AnimalChemicalNeeds = animal.needs.AllNeeds.OfType<Need_Chemical>().Select(n => n.CurLevel).ToArray(),
                HighOwner = high.pawn == human ? "human" : high.pawn == animal ? "animal" : "other",
                HighTimer = timer.ticksToDisappear, HighAge = high.ageTicks
            },
            MatchingDose = new {
                HumanHighCount = existingHuman.health.hediffSet.hediffs.Count(h => h.def == highDef),
                AnimalNativeHighCount = existingAnimal.health.hediffSet.hediffs.Count(h => h.def == highDef),
                ProjectionCount = existingAnimal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == canonical),
                CanonicalTimer = canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear
            },
            PairedHealing = new {
                CanonicalSourceID = canonicalHealingId,
                EarlierLegacyClock = earlierLegacyClock,
                EarlierCanonicalClock = earlierCanonicalClock,
                FinalClock = (int)healingClock.GetValue(canonicalHealingComp)
            },
            FailedExpectations = failures.ToArray()
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count != 0) throw new InvalidOperationException("FAIL: " + failures.Count + " legacy native-drug migration expectations; see legacy-drug-observed.json.");
    }

    private static HediffDef BuffDef(string name, bool optIn = true)
    {
        var def = new HediffDef {
            defName = name, label = name, hediffClass = typeof(HediffWithComps), initialSeverity = 1, maxSeverity = 1,
            stages = new List<HediffStage> { new HediffStage { painFactor = 0.9f } },
            comps = new List<HediffCompProperties> {
                new HediffCompProperties_Disappears { disappearsAfterTicks = new IntRange(600, 600) },
                new HediffCompProperties_SeverityPerDay { severityPerDay = -2 }
            }
        };
        if (optIn) def.modExtensions = new List<DefModExtension> { new SharedFormBuffExtension() };
        DefDatabase<HediffDef>.Add(def);
        return def;
    }

    private static void RunBuffRegressions()
    {
        Console.WriteLine("Additional scope: explicit native timed-buff opt-in, native health Add/Has/Get/Remove and native food outcome. Complete Thing.Ingested nutrition remains an engine-fixture check.");
        SetTick(2000);
        var buffDef = BuffDef("FixtureOptInTimedBuff");
        Pawn human = Pawn(301, Intelligence.Humanlike), animal = Pawn(302, Intelligence.Animal);
        var holder = Mark(animal);
        Check(holder.TryStore(human), "Buff fixture stores a distinct canonical original");
        SharedFormIdentity.Initialize(animal);
        SharedFormHealth.Initialize(animal);

        var effect = (HediffWithComps)HediffMaker.MakeHediff(buffDef, animal);
        animal.health.AddHediff(effect);
        Check(effect.pawn == human && human.health.hediffSet.hediffs.Contains(effect)
            && !animal.health.hediffSet.hediffs.Contains(effect),
            "Actual native AddHediff routes an approved whole-body buff to one original owner");
        Check(human.health.hediffSet.hediffs.Count(h => h.def == buffDef) == 1
            && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == effect) == 1,
            "Approved buff produces exactly one distinct animal projection");
        Check(animal.health.hediffSet.HasHediff(buffDef)
            && ReferenceEquals(animal.health.hediffSet.GetFirstHediffOfDef(buffDef), effect),
            "Native HasHediff and GetFirstHediffOfDef expose the canonical approved source");

        var timer = effect.TryGetComp<HediffComp_Disappears>();
        int timerBefore = timer.ticksToDisappear;
        int ageBefore = effect.ageTicks;
        float severityBefore = effect.Severity;
        SetTick(2001);
        SharedFormHealth.Tick(animal);
        SharedFormHealth.Tick(animal);
        human.health.HealthTick();
        Check(timer.ticksToDisappear == timerBefore - 1,
            "Approved native buff timer advances once despite duplicate actor and hidden-human ticks");
        // Native SeverityModifierBase applies a 200-tick hash step rather
        // than a fractional change on every smaller health interval.
        SetTick(2200);
        SharedFormHealth.TickInterval(animal, 200);
        SharedFormHealth.TickInterval(animal, 200);
        Check(effect.ageTicks == ageBefore + 200
            && Math.Abs(effect.Severity - (severityBefore - 2f * 200 / 60000)) < 0.000001f,
            "Approved buff age and native severity timer advance once at original elapsed rate");

        // This isolated graph does not load Core XML. Supply the native stat
        // definition that Need_Food.SetInitialLevel uses to clear its cache.
        if (StatDefOf.MaxNutrition == null)
            StatDefOf.MaxNutrition = new StatDef { defName = "FixtureMaxNutrition", defaultBaseValue = 1, workerClass = typeof(StatWorker) };
        var foodNeed = new Need_Food(animal) { def = new NeedDef { defName = "FixtureActorFood", needClass = typeof(Need_Food) } };
        foodNeed.CurLevel = 0.4f;
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(animal.needs, new List<Need> { foodNeed });
        float foodBefore = foodNeed.CurLevel;
        var outcome = new IngestionOutcomeDoer_GiveHediff { hediffDef = buffDef, severity = 0.1f };
        var food = new Thing { def = new ThingDef { defName = "FixtureBuffFood", category = ThingCategory.Item,
            ingestible = new IngestibleProperties { outcomeDoers = new List<IngestionOutcomeDoer> { outcome } } }, stackCount = 1 };
        Check(!food.def.IsDrug, "Food regression uses a genuinely non-drug native ingestible");
        outcome.DoIngestionOutcome(animal, food, 1);
        Check(human.health.hediffSet.hediffs.Count(h => h.def == buffDef) == 1
            && ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(buffDef), effect)
            && !animal.health.hediffSet.hediffs.Any(h => h.def == buffDef),
            "Actual native non-drug food GiveHediff merges into canonical original without an animal copy");
        Check(ReferenceEquals(animal.needs.TryGetNeed<Need_Food>(), foodNeed)
            && foodNeed.CurLevel == foodBefore && human.needs.TryGetNeed<Need_Food>() == null,
            "Food outcome routing leaves physical actor and its nutrition need untouched");

        animal.health.RemoveHediff(animal.health.hediffSet.GetFirstHediffOfDef(buffDef));
        Check(!human.health.hediffSet.hediffs.Contains(effect)
            && !animal.health.hediffSet.HasHediff(buffDef)
            && !animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(h => h.source == effect),
            "Actual native RemoveHediff removes the canonical buff and clears presence and projection");

        var unregistered = BuffDef("FixtureUnregisteredTimedBuff", false);
        Check(!SharedFormBuffs.IsSupportedDef(unregistered), "An arbitrary unregistered native timed buff is rejected");
        var unknown = HediffMaker.MakeHediff(unregistered, animal);
        animal.health.AddHediff(unknown);
        Check(unknown.pawn == animal && animal.health.hediffSet.hediffs.Contains(unknown)
            && !human.health.hediffSet.hediffs.Contains(unknown),
            "Unregistered native AddHediff retains ordinary animal ownership");

        var part = new BodyPartRecord { def = new BodyPartDef { defName = "FixtureBuffLeg" } };
        var anatomical = HediffMaker.MakeHediff(buffDef, animal, part);
        Check(!SharedFormBuffs.IsSupported(anatomical)
            && !SharedFormBuffs.AllowsPart(buffDef, animal, part),
            "Generic buff opt-in cannot opt anatomy-bound effects into sharing");
        Check(!SharedFormBuffs.IsSupported(new CustomBuff { def = buffDef, pawn = animal }),
            "A custom runtime hediff subclass cannot inherit native buff approval");
        var badCompDef = BuffDef("FixtureOptInCustomComp");
        badCompDef.comps.Add(new HediffCompProperties { compClass = typeof(CustomBuffComp) });
        Check(!SharedFormBuffs.IsSupportedDef(badCompDef), "Explicit opt-in still rejects an arbitrary custom comp");
        var mentalDef = BuffDef("FixtureOptInMentalTimer");
        ((HediffCompProperties_Disappears)mentalDef.comps[0]).requiredMentalState = new MentalStateDef();
        Check(!SharedFormBuffs.IsSupportedDef(mentalDef), "A native timer tied to mental state is not a context-free buff");
        var woundDef = BuffDef("FixtureOptInAnatomicalRemoval");
        ((HediffCompProperties_Disappears)woundDef.comps[0]).leaveFreshWounds = false;
        Check(!SharedFormBuffs.IsSupportedDef(woundDef), "A native timer with anatomy-dependent removal is rejected");

        var changedInstance = (HediffWithComps)HediffMaker.MakeHediff(buffDef, animal);
        changedInstance.comps.Add(new CustomBuffComp { parent = changedInstance, props = new HediffCompProperties { compClass = typeof(CustomBuffComp) } });
        Check(!SharedFormBuffs.IsSupported(changedInstance), "Runtime injected custom comp is rejected even when its Def is native");
        animal.health.AddHediff(changedInstance);
        Check(changedInstance.pawn == animal && animal.health.hediffSet.hediffs.Contains(changedInstance)
            && !human.health.hediffSet.hediffs.Contains(changedInstance),
            "Native AddHediff validates the actual instance before redirecting ownership");

        SetTick(3000);
        Pawn legacyHuman = Pawn(401, Intelligence.Humanlike), legacyAnimal = Pawn(402, Intelligence.Animal);
        var legacy = (HediffWithComps)HediffMaker.MakeHediff(buffDef, legacyAnimal);
        legacyAnimal.health.AddHediff(legacy);
        var legacyTimer = legacy.TryGetComp<HediffComp_Disappears>();
        var legacySeverity = legacy.TryGetComp<HediffComp_SeverityPerDay>();
        legacyTimer.ticksToDisappear = 237;
        legacySeverity.severityPerDay = -0.12345f;
        legacy.ageTicks = 67;
        var legacyHolder = Mark(legacyAnimal);
        Check(legacyHolder.TryStore(legacyHuman), "Legacy buff fixture preserves its existing animal body");
        SharedFormIdentity.Initialize(legacyAnimal);
        SharedFormHealth.Initialize(legacyAnimal);
        Check(ReferenceEquals(legacyHuman.health.hediffSet.GetFirstHediffOfDef(buffDef), legacy)
            && legacy.pawn == legacyHuman && !legacyAnimal.health.hediffSet.hediffs.Contains(legacy),
            "Initialization adopts the exact legacy native buff with only one owning body");
        Check(legacyTimer.ticksToDisappear == 237 && legacy.ageTicks == 67
            && legacySeverity.severityPerDay == -0.12345f,
            "Legacy adoption does not replay PostAdd, reroll native severity, refresh duration or advance age");
        SharedFormHealth.Initialize(legacyAnimal);
        Check(legacyHuman.health.hediffSet.hediffs.Count(h => h.def == buffDef) == 1
            && legacyAnimal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => h.source == legacy) == 1
            && legacyTimer.ticksToDisappear == 237,
            "Repeated initialization leaves legacy state and its projection idempotent");

        legacyHolder.ReleaseStoredPawn();
        var dormant = new AnimalFormStorage(legacyHuman);
        Check(dormant.TryStore(legacyAnimal, new PawnKindDef { defName = "FixtureCachedBuffKind", race = legacyAnimal.def })
            && AnimalFormStorage.IsStored(legacyAnimal), "Food-tail fixture uses a real production inactive animal holder");
        outcome.DoIngestionOutcome(legacyAnimal, food, 1);
        Check(!legacyAnimal.health.hediffSet.hediffs.Any(h => h.def == buffDef)
            && legacyHuman.health.hediffSet.hediffs.Contains(legacy) && legacyTimer.ticksToDisappear == 237,
            "Cached inactive animal rejects a trailing registered non-drug food effect");
    }

    private static bool SkipFixtureCache(Pawn ___pawn) { return !fixtures.Contains(___pawn); }
    private static bool SkipFixtureHealthState(Pawn ___pawn) { return !fixtures.Contains(___pawn); }
    private static bool SkipFixtureNeedSet(Pawn ___pawn) { return !fixtures.Contains(___pawn); }
    private static void SetTick(int tick) { AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(Find.TickManager, tick); }
    private static T Bare<T>() where T : class { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAIL: " + message); assertions++; Console.WriteLine("OK: " + message); }

    private static Pawn Pawn(int id, Intelligence intelligence)
    {
        var pawn = Bare<Pawn>();
        pawn.thingIDNumber = id;
        pawn.stackCount = 1;
        pawn.def = new ThingDef { defName = "FixturePawn" + id, category = ThingCategory.Pawn, race = new RaceProperties { intelligence = intelligence,
            lifeStageAges = new List<LifeStageAge> { new LifeStageAge { minAge = 0, def = new LifeStageDef { defName = "FixtureAdult", bodySizeFactor = 1, developmentalStage = DevelopmentalStage.Adult } } } } };
        AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, new FleshTypeDef { defName = "FixtureFlesh" });
        fixtures.Add(pawn);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)-1);
        pawn.health = Bare<Pawn_HealthTracker>();
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(pawn.health, pawn);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.needs = Bare<Pawn_NeedsTracker>();
        AccessTools.Field(typeof(Pawn_NeedsTracker), "pawn").SetValue(pawn.needs, pawn);
        AccessTools.Field(typeof(Pawn_NeedsTracker), "needs").SetValue(pawn.needs, new List<Need>());
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AccessTools.Field(typeof(Pawn_AgeTracker), "ageBiologicalTicksInt").SetValue(pawn.ageTracker, 20L * 3600000);
        AccessTools.Field(typeof(Pawn_AgeTracker), "cachedLifeStageIndex").SetValue(pawn.ageTracker, 0);
        return pawn;
    }

    private static HediffComp_Transformed Mark(Pawn pawn)
    {
        var hediff = new HediffWithComps { pawn = pawn, def = new HediffDef { defName = "WildShift_Transformed" } };
        var comp = new HediffComp_Transformed { parent = hediff, props = new HediffCompProperties_Transformed() };
        hediff.comps = new List<HediffComp> { comp };
        pawn.health.hediffSet.hediffs.Add(hediff);
        WildShiftDefOf.WildShift_Transformed = hediff.def;
        return comp;
    }
}

internal sealed class CustomDrug : Hediff_High { }
internal sealed class CustomChemicalNeed : Need_Chemical { public CustomChemicalNeed(Pawn pawn) : base(pawn) { } }
internal sealed class CustomBuff : HediffWithComps { }
internal sealed class CustomBuffComp : HediffComp { }
internal sealed class StageProbeException : Exception { }
internal sealed class RecordingGiver : HediffGiver
{
    public override void OnIntervalPassed(Pawn pawn, Hediff cause)
    {
        Program.observedStagePawn = pawn;
        Program.observedLethalScope = SharedFormHealth.IsSharedLethalDeath(pawn);
        throw new StageProbeException();
    }
}
