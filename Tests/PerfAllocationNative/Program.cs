using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WildShift;

internal static class Program
{
    private static int checks;
    private static readonly AccessTools.FieldRef<TickManager, int> ticks = AccessTools.FieldRefAccess<TickManager, int>("ticksGameInt");
    private static readonly FieldInfo geneCache = AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes");
    private sealed class Measurement
    {
        public string Name { get; set; }
        public int Calls { get; set; }
        public long AllocatedBytes { get; set; }
        public double BytesPerCall { get; set; }
        public double Milliseconds { get; set; }
        public int Gen0Collections { get; set; }
    }
    private sealed class Pair { public Pawn Human, Animal; public Ability First; public AbilityDef Def; public Gene SourceGene; }

    private static int Main(string[] args)
    {
        if (args.Length != 4) return 2;
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (string dir in new[] { args[0], args[1] }) {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { Run(args[0], args[2], args[3] == "candidate"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string gameDir, string output, bool candidate)
    {
        Directory.CreateDirectory(output);
        Setup(gameDir, output);
        var harmony = new Harmony("wildshift.tests.perf-allocation.native");
        foreach (Type type in typeof(SharedFormAbilities).Assembly.GetTypes())
            if (type.Name.StartsWith("Patch_SharedAbility", StringComparison.Ordinal)
                || type.Name.StartsWith("Patch_SharedHemogen", StringComparison.Ordinal))
                harmony.CreateClassProcessor(type).Patch();

        Action<Pawn> tick = (Action<Pawn>)Delegate.CreateDelegate(typeof(Action<Pawn>),
            AccessTools.Method(typeof(SharedFormAbilities), "TickHemogen"));
        var plain = new Pair[50];
        for (int i = 0; i < plain.Length; i++) plain[i] = NewPair(10000 + i * 2, false);
        var rich = NewPair(20000, true);
        TestResourceClock(rich, tick);
        TestAbilityLookup(rich);

        var measures = new List<Measurement>();
        const int calls = 200000;
        // Delegates are bound once outside measured loops; Reflection.Invoke,
        // JSON, disk IO and fixture construction are never measured as mod work.
        measures.Add(Measure("TickHemogen/no-resource/50-bodies", calls,
            i => { ticks(Find.TickManager) = 100000 + i / plain.Length; tick(plain[i % plain.Length].Animal); }));

        Action refresh = (Action)Delegate.CreateDelegate(typeof(Action), rich.Animal.abilities,
            AccessTools.Method(typeof(SharedPawnAbilityTracker), "Refresh"));
        measures.Add(Measure("TrackerRefresh/one-native-gene-ability", calls,
            i => { ticks(Find.TickManager) = 200000 + i; refresh(); }));

        Func<AbilityDef, Ability> lookup = (Func<AbilityDef, Ability>)Delegate.CreateDelegate(typeof(Func<AbilityDef, Ability>),
            rich.Animal.abilities, AccessTools.Method(typeof(SharedPawnAbilityTracker), "BorrowedAbility"));
        ticks(Find.TickManager) = 500000;
        refresh();
        measures.Add(Measure("BorrowedLookup/stable-tick", calls, i => { if (lookup(rich.Def) == null) throw new Exception("Missing borrowed ability"); }));

        object buff = TestDuplicateBuff(output, candidate);
        if (candidate)
            foreach (Measurement measurement in measures)
                Check(measurement.BytesPerCall < 8.0, measurement.Name + " has no recurring closure/predicate allocation");
        string production = typeof(SharedFormAbilities).Assembly.Location;
        var result = new {
            Passed = true, Mode = candidate ? "candidate" : "baseline", Checks = checks,
            ProductionPath = production,
            ProductionSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(production))),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            GameAssemblySHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Pawn).Assembly.Location))),
            Measurements = measures, DuplicateBuff = buff,
            Scope = "Isolated actual-native pawn graphs and prebound delegates; allocation microprobe, not initialized-game TPS or a whole-colony memory result."
        };
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Measurement Measure(string name, int count, Action<int> action)
    {
        for (int i = 0; i < 20000; i++) action(i);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) action(i);
        long duration = Stopwatch.GetTimestamp() - start;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return new Measurement { Name = name, Calls = count, AllocatedBytes = bytes,
            BytesPerCall = (double)bytes / count, Milliseconds = (double)duration * 1000 / Stopwatch.Frequency,
            Gen0Collections = GC.CollectionCount(0) - gen0 };
    }

    private static void Setup(string gameDir, string output)
    {
        string profile = Path.Combine(output, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Config"));
        Directory.CreateDirectory(Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, profile);
        AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(gameDir, "Data"));
        File.WriteAllText(Path.Combine(profile, "Config", "ModsConfig.xml"), "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        FieldInfo prefs = AccessTools.Field(typeof(Prefs), "data"); prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
        LanguageDatabase.activeLanguage = new LoadedLanguage("English");
        AccessTools.Field(typeof(LoadedLanguage), "workerInt").SetValue(LanguageDatabase.activeLanguage, new LanguageWorker_English());
        AccessTools.Field(typeof(LoadedLanguage), "dataIsLoaded").SetValue(LanguageDatabase.activeLanguage, true);
        Current.Game = Bare<Game>();
        Current.Game.tickManager = Bare<TickManager>();
        Current.Game.uniqueIDsManager = new UniqueIDsManager();
        Current.Game.questManager = new QuestManager();
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, true);
        ticks(Find.TickManager) = 1000;
        WildShiftDefOf.WildShift_Transformed = new HediffDef { defName = "WildShift_Transformed", label = "transformed" };
    }

    private static Pair NewPair(int id, bool ability)
    {
        var pair = new Pair { Human = Pawn(id, true), Animal = Pawn(id + 1, false) };
        var marker = new HediffWithComps { pawn = pair.Animal, def = WildShiftDefOf.WildShift_Transformed };
        var holder = new HediffComp_Transformed { parent = marker, props = new HediffCompProperties_Transformed() };
        marker.comps = new List<HediffComp> { holder }; pair.Animal.health.hediffSet.hediffs.Add(marker);
        ((ThingOwner<Pawn>)holder.GetDirectlyHeldThings()).InnerListForReading.Add(pair.Human);
        pair.Human.holdingOwner = holder.GetDirectlyHeldThings();
        if (ability) {
            pair.Def = new AbilityDef { defName = "PerfNativeAbility", label = "ability", verbProperties = new VerbProperties { verbClass = typeof(Verb_CastAbility), isPrimary = true, range = 12f }, cooldownTicksRange = new IntRange(100, 100) };
            DefDatabase<AbilityDef>.Add(pair.Def);
            pair.First = new Ability(pair.Human, pair.Def);
            pair.Human.abilities.abilities.Add(pair.First);
            pair.Human.abilities.abilities.Add(new Ability(pair.Human, pair.Def));
            pair.SourceGene = new Gene { pawn = pair.Human, def = new GeneDef { defName = "PerfAbilityGene", abilities = new List<AbilityDef> { pair.Def } } };
            pair.Human.genes = new Pawn_GeneTracker(pair.Human);
            pair.Human.genes.Endogenes.Add(pair.SourceGene); geneCache.SetValue(pair.Human.genes, null);
        }
        SharedFormIdentity.Initialize(pair.Animal);
        SharedFormAbilities.Initialize(pair.Animal);
        return pair;
    }

    private static void TestAbilityLookup(Pair pair)
    {
        Check(pair.Animal.abilities.GetAbility(pair.Def) == pair.First, "Indexed lookup retains the first matching canonical source");
        Check(pair.Human.abilities.abilities[0] == pair.First && !pair.Animal.abilities.abilities.Contains(pair.First), "Ability remains deep-owned only by the human");
        pair.First.StartCooldown(300); pair.First.maxCharges = 3; pair.First.RemainingCharges = 1;
        pair.SourceGene.OverrideBy(new Gene { pawn = pair.Human, def = new GeneDef { defName = "PerfSuppressor" } });
        Check(pair.Animal.abilities.GetAbility(pair.Def) == null && pair.First.pawn == pair.Human, "Same-tick gene suppression revokes the exact borrowed source");
        pair.SourceGene.OverrideBy(null);
        Check(pair.Animal.abilities.GetAbility(pair.Def) == pair.First && pair.First.RemainingCharges == 1 && pair.First.CooldownTicksRemaining == 300,
            "Same-tick reactivation restores the exact source without resetting charges/cooldown");
    }

    private static void TestResourceClock(Pair pair, Action<Pawn> tick)
    {
        var resource = new Gene_Hemogen { pawn = pair.Human, def = new GeneDef { defName = "PerfHemogen", resourceLossPerDay = .2f } };
        resource.Reset(); resource.Value = .7f;
        // Isolate the bridge clock's source replacement. A separate native
        // Gene_HemogenDrain intentionally caches its resource object and would
        // confound manual graph replacement. Combined native drain behavior
        // remains covered by the existing 49-check shared-ability suite.
        pair.Human.genes.Endogenes.Add(resource); geneCache.SetValue(pair.Human.genes, null);
        ticks(Find.TickManager) = 1100; tick(pair.Animal);
        Check(Near(resource.Value, .7f), "New native resource source does not receive retrospective drain");
        ticks(Find.TickManager) = 1200; tick(pair.Animal);
        float expected = .7f - .2f * 100 / 60000;
        Check(Near(resource.Value, expected), "Established native resource advances by exactly the elapsed ticks");
        tick(pair.Animal);
        Check(Near(resource.Value, expected), "Repeated same-tick calls cannot double drain");
        var replacement = new Gene_Hemogen { pawn = pair.Human, def = resource.def }; replacement.Reset(); replacement.Value = .6f;
        pair.Human.genes.Endogenes.Remove(resource); pair.Human.genes.Endogenes.Add(replacement); geneCache.SetValue(pair.Human.genes, null);
        ticks(Find.TickManager) = 1300; tick(pair.Animal);
        Check(Near(replacement.Value, .6f), "Replacing the resource object resets its clock without stale-source drain");
        ticks(Find.TickManager) = 1400; tick(pair.Animal);
        Check(Near(replacement.Value, .6f - .2f * 100 / 60000), "Replacement resource resumes native drain after the reset");
        pair.Human.genes.Endogenes.Remove(replacement); geneCache.SetValue(pair.Human.genes, null);
        ticks(Find.TickManager) = 1500; tick(pair.Animal);
        Check(Near(replacement.Value, .6f - .2f * 100 / 60000), "Removing hemogen leaves the detached source untouched");
        pair.Human.genes.Endogenes.Add(replacement); geneCache.SetValue(pair.Human.genes, null);
        ticks(Find.TickManager) = 1600; tick(pair.Animal);
        Check(Near(replacement.Value, .6f - .2f * 100 / 60000), "Reappearing hemogen has no catch-up drain for its absence");
        SharedFormAbilities.Release(pair.Animal);
        ticks(Find.TickManager) = 5000; SharedFormAbilities.Initialize(pair.Animal); tick(pair.Animal);
        Check(Near(replacement.Value, .6f - .2f * 100 / 60000), "Return and reinitialization discard the dormant interval");
        pair.Human.genes.Endogenes.Remove(replacement); geneCache.SetValue(pair.Human.genes, null);
    }

    private static object TestDuplicateBuff(string output, bool candidate)
    {
        var boundary = new Harmony("wildshift.tests.perf-allocation.buff-cache-boundary");
        boundary.Patch(AccessTools.Method(typeof(HediffSet), "DirtyCache"), prefix: new HarmonyMethod(typeof(Program), nameof(SkipCache)));
        Pawn human = Pawn(30000, true), animal = Pawn(30001, false);
        var def = new HediffDef { defName = "PerfSavedBuff", label = "saved buff", hediffClass = typeof(HediffWithComps), initialSeverity = 1, maxSeverity = 5,
            comps = new List<HediffCompProperties> { new HediffCompProperties_SeverityPerDay { severityPerDay = -.1f },
                new HediffCompProperties_Disappears { disappearsAfterTicks = new IntRange(600, 600) } },
            modExtensions = new List<DefModExtension> { new SharedFormBuffExtension() } };
        DefDatabase<HediffDef>.Add(def);
        Hediff canonical = HediffMaker.MakeHediff(def, human), legacy = HediffMaker.MakeHediff(def, animal);
        canonical.Severity = .2f; canonical.ageTicks = 144; canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 80;
        legacy.Severity = .4f; legacy.ageTicks = 67; legacy.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 237;
        human.health.hediffSet.hediffs.Add(canonical); animal.health.hediffSet.hediffs.Add(legacy);
        string id = canonical.GetUniqueLoadID();
        SharedFormBuffs.AdoptExisting(animal, human);
        var result = new { Severity = canonical.Severity, Age = canonical.ageTicks,
            Timer = canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear, CanonicalId = canonical.GetUniqueLoadID(),
            OneHumanOwner = human.health.hediffSet.hediffs.Count == 1 && canonical.pawn == human,
            AnimalSources = animal.health.hediffSet.hediffs.Count,
            ConservativeState = Near(canonical.Severity, .4f) && canonical.ageTicks == 144 && canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 237 };
        File.WriteAllText(Path.Combine(output, "duplicate-buff-observed.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Check(result.CanonicalId == id && result.OneHumanOwner && result.AnimalSources == 0, "Duplicate native buff retains its canonical ID and single ownership");
        if (candidate)
        {
            Check(result.ConservativeState, "Saved duplicate buff reconciles without adding a dose or resetting age");
            SharedFormBuffs.AdoptExisting(animal, human);
            Check(Near(canonical.Severity, .4f) && canonical.ageTicks == 144
                && canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 237
                && canonical.GetUniqueLoadID() == id, "Repeated saved-buff adoption is idempotent");
            TestBuffBranches(def);
        }
        return result;
    }

    private static void TestBuffBranches(HediffDef def)
    {
        Pawn human = Pawn(30100, true), animal = Pawn(30101, false);
        Hediff canonical = SavedBuff(def, human, .6f, 44, 237);
        Hediff legacy = SavedBuff(def, animal, .2f, 101, 80);
        SharedFormBuffs.AdoptExisting(animal, human);
        Check(Near(canonical.Severity, .6f) && canonical.ageTicks == 101
            && canonical.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 237,
            "Shorter/weaker old buff cannot reduce canonical potency or remaining timer");

        human = Pawn(30200, true); animal = Pawn(30201, false);
        legacy = SavedBuff(def, animal, .4f, 67, 237);
        string id = legacy.GetUniqueLoadID();
        SharedFormBuffs.AdoptExisting(animal, human);
        Check(human.health.hediffSet.hediffs.Single() == legacy && legacy.pawn == human
            && animal.health.hediffSet.hediffs.Count == 0 && legacy.GetUniqueLoadID() == id
            && legacy.ageTicks == 67 && Near(legacy.Severity, .4f)
            && legacy.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 237,
            "Unique old native buff transfers its exact object, ID, age, severity and comp timer");

        human = Pawn(30300, true); animal = Pawn(30301, false);
        var custom = new UnreviewedBuff { pawn = human, def = def };
        human.health.hediffSet.hediffs.Add(custom);
        legacy = SavedBuff(def, animal, .4f, 67, 237);
        SharedFormBuffs.AdoptExisting(animal, human);
        Check(human.health.hediffSet.hediffs.Single() == custom
            && animal.health.hediffSet.hediffs.Single() == legacy && legacy.pawn == animal,
            "Unreviewed matching human source prevents consuming the animal's saved buff");

        human = Pawn(30400, true); animal = Pawn(30401, false);
        canonical = SavedBuff(def, human, .2f, 144, 80);
        Hediff second = SavedBuff(def, human, .3f, 10, 60);
        legacy = SavedBuff(def, animal, .4f, 67, 237);
        SharedFormBuffs.AdoptExisting(animal, human);
        Check(human.health.hediffSet.hediffs.Count == 2 && human.health.hediffSet.hediffs.Contains(canonical)
            && human.health.hediffSet.hediffs.Contains(second) && animal.health.hediffSet.hediffs.Single() == legacy
            && Near(canonical.Severity, .2f) && canonical.ageTicks == 144,
            "Ambiguous original duplicate buffs remain unchanged instead of selecting a saved authority");
    }

    private static Hediff SavedBuff(HediffDef def, Pawn pawn, float severity, int age, int timer)
    {
        Hediff effect = HediffMaker.MakeHediff(def, pawn);
        effect.Severity = severity; effect.ageTicks = age;
        effect.TryGetComp<HediffComp_Disappears>().ticksToDisappear = timer;
        pawn.health.hediffSet.hediffs.Add(effect);
        return effect;
    }

    public static bool SkipCache() { return false; }
    private static Pawn Pawn(int id, bool human)
    {
        Pawn pawn = Bare<Pawn>(); pawn.thingIDNumber = id; pawn.stackCount = 1;
        pawn.def = new ThingDef { defName = "PerfPawn" + id, category = ThingCategory.Pawn, race = new RaceProperties { intelligence = human ? Intelligence.Humanlike : Intelligence.Animal,
            lifeStageAges = new List<LifeStageAge> { new LifeStageAge { def = new LifeStageDef { defName = "Adult" } } } } };
        pawn.kindDef = new PawnKindDef { defName = "PerfKind" + id, race = pawn.def };
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)-1);
        pawn.health = Bare<Pawn_HealthTracker>();
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(pawn.health, pawn);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn); AccessTools.Field(typeof(Pawn_AgeTracker), "cachedLifeStageIndex").SetValue(pawn.ageTracker, 0);
        pawn.drafter = new Pawn_DraftController(pawn); AccessTools.Field(typeof(Pawn_DraftController), "draftedInt").SetValue(pawn.drafter, true);
        pawn.jobs = new Pawn_JobTracker(pawn); pawn.abilities = new Pawn_AbilityTracker(pawn);
        return pawn;
    }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .00001f; }
    private static T Bare<T>() where T : class { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    private static void Check(bool result, string label) { if (!result) throw new InvalidOperationException(label); checks++; }
}

internal sealed class UnreviewedBuff : HediffWithComps { }
