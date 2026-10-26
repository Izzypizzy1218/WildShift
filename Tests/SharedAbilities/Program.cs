using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WildShift;

internal static class Program
{
    private static int checks;
    private sealed class UnknownResourceComp : AbilityComp { }
    private static int Main(string[] args)
    {
        if (args.Length != 3) return 2;
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (string directory in new[] { args[0], args[1] }) {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { Run(args[0], args[2]); Console.WriteLine("PASS: " + checks + " native shared-ability checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string gameDir, string scratch)
    {
        Directory.CreateDirectory(Path.Combine(scratch, "profile", "Config"));
        Directory.CreateDirectory(Path.Combine(scratch, "profile", "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, Path.Combine(scratch, "profile"));
        AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(scratch, "profile", "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(gameDir, "Data"));
        File.WriteAllText(Path.Combine(scratch, "profile", "Config", "ModsConfig.xml"), "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        FieldInfo prefs = AccessTools.Field(typeof(Prefs), "data");
        prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
        LanguageDatabase.activeLanguage = new LoadedLanguage("English");
        AccessTools.Field(typeof(LoadedLanguage), "workerInt").SetValue(LanguageDatabase.activeLanguage, new LanguageWorker_English());
        AccessTools.Field(typeof(LoadedLanguage), "dataIsLoaded").SetValue(LanguageDatabase.activeLanguage, true);
        LanguageDatabase.activeLanguage.keyedReplacements["AbilityDisabledNoHemogen"] = new LoadedLanguage.KeyedReplacement { value = "Not enough hemogen" };
        LanguageDatabase.activeLanguage.keyedReplacements["IsIncapableOfViolence"] = new LoadedLanguage.KeyedReplacement { value = "Incapable of violence" };
        Current.Game = Bare<Game>();
        Current.Game.tickManager = Bare<TickManager>();
        Current.Game.uniqueIDsManager = new UniqueIDsManager();
        Current.Game.questManager = new QuestManager();
        SetTick(1000);
        AccessTools.Field(typeof(ModsConfig), "biotechActive").SetValue(null, true);
        WildShiftDefOf.WildShift_Transformed = new HediffDef { defName = "WildShift_Transformed", label = "transformed" };
        var harmony = new Harmony("wildshift.tests.shared-abilities");
        foreach (Type type in typeof(SharedFormAbilities).Assembly.GetTypes())
            if (type.Name.StartsWith("Patch_SharedAbility", StringComparison.Ordinal) || type.Name.StartsWith("Patch_SharedHemogen", StringComparison.Ordinal))
                harmony.CreateClassProcessor(type).Patch();
        Console.WriteLine("Tested shared-ability assembly: " + typeof(SharedFormAbilities).Assembly.Location);
        Console.WriteLine("Production SHA256: " + BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(typeof(SharedFormAbilities).Assembly.Location))).Replace("-", ""));

        Pawn human = MakePawn(801, true), animal = MakePawn(802, false);
        Mark(animal, human);
        AbilityDef fireDef = MakeDef("FixtureFire", typeof(Verb_CastAbility));
        Ability fire = AddAbility(human, fireDef);
        Gene gene = AddGene(human, "FixtureFireGene", fireDef);
        Ability ownDuplicate = AddAbility(animal, fireDef);
        Ability own = AddAbility(animal, MakeDef("FixtureAnimal", typeof(Verb_CastAbility)));
        SharedFormIdentity.Initialize(animal);
        SharedFormAbilities.Initialize(animal);
        Check(animal.abilities is SharedPawnAbilityTracker, "Active body gets a native-tracker proxy");
        Check(human.abilities.abilities.Contains(fire) && !animal.abilities.abilities.Contains(fire), "Only original human deep-owns the canonical ability");
        Check(animal.abilities.GetAbility(fireDef) == fire, "Default native GetAbility resolves canonical human ability");
        Check(animal.abilities.AllAbilitiesForReading.Contains(fire), "Native ability list includes the exact source object");
        Check(!animal.abilities.AllAbilitiesForReading.Contains(ownDuplicate) && animal.abilities.abilities.Contains(ownDuplicate), "Duplicate animal-native def stays preserved but cannot bypass canonical cooldown");
        Check(animal.abilities.AllAbilitiesForReading.Contains(own), "Unrelated body-native abilities remain usable");
        Check(fire.pawn == animal && fire.verb.CasterPawn == animal, "Ability and all native verb casts originate at active animal");
        Job cast = fire.GetJob(new LocalTargetInfo(new IntVec3(8, 0, 9)), LocalTargetInfo.Invalid);
        Check(cast.ability == fire && cast.verbToUse == fire.verb, "Native cast job holds the same saved ability and active-body verb");
        var iterator = animal.abilities.AllAbilitiesForReading.GetEnumerator();
        Check(iterator.MoveNext(), "Merged native list can be enumerated");
        var unchanged = animal.abilities.AllAbilitiesForReading;
        Check(iterator.MoveNext(), "Nested unchanged tracker read does not invalidate native enumeration");
        fire.StartCooldown(500);
        fire.maxCharges = 3; fire.RemainingCharges = 1;
        SharedFormAbilities.Release(animal);
        Check(fire.pawn == human && fire.verb.CasterPawn == human, "Return restores human ability and verb caster");
        Check(fire.CooldownTicksRemaining == 500 && fire.RemainingCharges == 1, "Return preserves source cooldown and partial charges");
        SharedFormAbilities.Initialize(animal);
        Check(fire.CooldownTicksRemaining == 500 && fire.RemainingCharges == 1, "Repeated transformation cannot reset cooldown or refill charges");
        gene.overriddenByGene = new Gene { pawn = human, def = new GeneDef { defName = "Suppressor" } };
        SetTick(1001);
        Check(!animal.abilities.AllAbilitiesForReading.Contains(fire) && fire.pawn == human, "Overridden source gene revokes borrowed casting and restores caster");
        Check(animal.abilities.AllAbilitiesForReading.Contains(ownDuplicate), "Body-native ability remains when source gene is inactive");
        gene.overriddenByGene = null; SetTick(1002); SharedFormAbilities.Initialize(animal);
        Check(animal.abilities.GetAbility(fireDef) == fire, "Reactivated gene reuses original ability identity");
        gene.OverrideBy(new Gene { pawn = human, def = new GeneDef { defName = "SameTickSuppressor" } });
        Check(!fire.CanCast, "Same-tick source suppression prevents a stale queued cast immediately");
        Check(!animal.abilities.AllAbilitiesForReading.Contains(fire), "Native gene suppression invalidates the list within the same tick");
        gene.OverrideBy(null);
        Check(animal.abilities.GetAbility(fireDef) == fire, "Same-tick source reactivation rebinds without resetting the Ability");

        AbilityDef unknownDef = MakeDef("UnknownResource", typeof(Verb_CastAbility));
        unknownDef.comps.Add(new AbilityCompProperties { compClass = typeof(UnknownResourceComp) });
        Ability unknown = AddAbility(human, unknownDef);
        AddGene(human, "UnknownGene", unknownDef);
        SharedFormAbilities.Initialize(animal);
        Check(!SharedFormAbilities.IsSupported(unknown) && !animal.abilities.AllAbilitiesForReading.Contains(unknown), "Unreviewed custom resource callbacks are gated");
        Ability world = AddAbility(human, MakeDef("WorldOnly", typeof(Verb_CastAbility)));
        world.def.targetWorldCell = true;
        Check(!SharedFormAbilities.IsSupported(world), "World-targeting ability is outside local active-body casting scope");
        Check(!SharedFormAbilities.IsSupported(new Psycast(human, MakeDef("Psycast", typeof(Verb_CastAbility)))), "Royalty psychic resources are not silently borrowed");

        TestHumanViolence(human, animal);
        TestHemogen(human, animal);
        TestSingleTick(human, animal);
        TestSerialization(animal, fire, scratch);
        SharedFormAbilities.Release(animal);
        var holder = TransformUtility.TryGetTransformedComp(animal);
        holder.GetDirectlyHeldThings().Remove(human);
        Pawn newAnimal = MakePawn(803, false);
        newAnimal.abilities = animal.abilities;
        Mark(newAnimal, human);
        SharedFormIdentity.Initialize(newAnimal); SharedFormAbilities.Initialize(newAnimal);
        Check(newAnimal.abilities.GetAbility(fireDef) == fire && fire.pawn == newAnimal && fire.verb.CasterPawn == newAnimal, "Sapient body swap rebinds canonical ability to the new actual body");
        Check(own.pawn == newAnimal && own.verb.CasterPawn == newAnimal, "Sapient swap also migrates existing body's native ability caster");
        SharedFormAbilities.Release(animal);
        Check(fire.pawn == newAnimal, "Releasing old swapped body cannot detach the new body's canonical ability");
        SharedFormAbilities.Release(newAnimal);
        Check(fire.pawn == human, "Swapped body returns original ability to canonical human");
        Console.WriteLine("Scope: actual 1.7 Ability/Verb/Job/Gene/ThingOwner/Scribe APIs; isolated pawn graphs, no game launch or graphical claim.");
    }

    private static void TestHumanViolence(Pawn human, Pawn animal)
    {
        human.story = new Pawn_StoryTracker(human);
        human.story.Adulthood = new BackstoryDef { defName = "FixturePeacefulBackstory", workDisables = WorkTags.Violent };
        Check(human.WorkTagIsDisabled(WorkTags.Violent) && !animal.WorkTagIsDisabled(WorkTags.Violent),
            "Native human background forbids violence while beast's ordinary combat policy remains unchanged");
        foreach (string name in new[] { "Fire", "Spine", "Acid" })
        {
            AbilityDef def = MakeDef("Peaceful" + name, typeof(Verb_CastAbility));
            Ability ability = AddAbility(human, def); AddGene(human, name + "ConstraintGene", def);
            SharedFormAbilities.Initialize(animal);
            string reason;
            Check(ability.GizmoDisabled(out reason) && reason == "Incapable of violence",
                "Original human's violence disability disables borrowed " + name + " gizmo");
            Check(!ability.CanCast && !ability.Activate(LocalTargetInfo.Invalid, LocalTargetInfo.Invalid),
                "Queued and direct borrowed " + name + " activation cannot bypass original violence disability");
        }
        foreach (string name in new[] { "Foam", "Jump", "Bloodfeed", "Coagulate" })
        {
            AbilityDef def = MakeDef("Peaceful" + name, typeof(Verb_CastAbility));
            def.casterMustBeCapableOfViolence = false;
            Ability ability = AddAbility(human, def); AddGene(human, name + "AllowedGene", def);
            SharedFormAbilities.Initialize(animal);
            Check(ability.CanCast && ability.Activate(LocalTargetInfo.Invalid, LocalTargetInfo.Invalid),
                "Native nonviolent " + name + " ability remains available to peaceful original");
        }
        human.story.Adulthood = null;
        Check(!human.WorkTagIsDisabled(WorkTags.Violent), "Removing original disability restores native ability permission");
    }

    private static void TestHemogen(Pawn human, Pawn animal)
    {
        var hemogen = new Gene_Hemogen { pawn = human, def = new GeneDef { defName = "FixtureHemogen", resourceLossPerDay = 0.2f } };
        hemogen.Reset();
        human.genes.Endogenes.Add(hemogen);
        var drain = new Gene_HemogenDrain { pawn = human, def = new GeneDef { defName = "FixtureDrain", resourceLossPerDay = 0.4f } };
        human.genes.Endogenes.Add(drain);
        AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes").SetValue(human.genes, null);
        var def = MakeDef("FixtureLongjump", typeof(Verb_CastAbilityJump));
        def.comps.Add(new CompProperties_AbilityHemogenCost { hemogenCost = 0.05f });
        Ability jump = AddAbility(human, def);
        AddGene(human, "FixtureJumpGene", def);
        SharedFormAbilities.Initialize(animal);
        var cost = jump.CompOfType<CompAbilityEffect_HemogenCost>();
        Check(animal.genes == null && animal.abilities.GetAbility(def) == jump, "Hemogenic gene ability borrows source without sharing the genes tracker");
        hemogen.Value = 0.5f;
        string reason;
        Check(!cost.GizmoDisabled(out reason) && cost.AICanTargetNow(LocalTargetInfo.Invalid), "Native hemogen permission reads original resource while animal has no genes");
        GeneUtility.OffsetHemogen(animal, -0.1f, false);
        Check(Close(hemogen.Value, 0.4f), "Native ability cost updates original hemogen only");
        GeneUtility.OffsetHemogen(animal, 0.2f, false);
        Check(Close(hemogen.Value, 0.6f), "Bloodfeeding and blood packs replenish original hemogen");
        animal.jobs.jobQueue.EnqueueLast(new Job { verbToUse = jump.verb, ability = jump });
        hemogen.Value = 0.075f;
        Check(cost.GizmoDisabled(out reason), "Queued active-body casts reserve original hemogen and prevent overqueue");
        animal.jobs.jobQueue.Extract(animal.jobs.jobQueue[0].job);
        SetTick(1102);
        float before = hemogen.Value;
        animal.abilities.AbilitiesTick();
        Check(Close(hemogen.Value, before - 0.6f * 100f / 60000f), "Both native hemogen drain genes advance original resource exactly once");
        animal.abilities.AbilitiesTick(); hemogen.TickInterval(100); drain.TickInterval(100);
        Check(Close(hemogen.Value, before - 0.6f * 100f / 60000f), "Repeated ability ticks and native hidden-gene interval cannot double-drain");
    }

    private static void TestSingleTick(Pawn human, Pawn animal)
    {
        AbilityDef def = MakeDef("ChargeTick", typeof(Verb_CastAbility));
        def.charges = 3; def.cooldownPerCharge = true;
        def.cooldownTicksRange = new IntRange(0, 0);
        def.groupDef = new AbilityGroupDef { defName = "ChargeGroup", cooldownTicks = 1 };
        Ability ability = AddAbility(human, def); AddGene(human, "ChargeGene", def);
        SharedFormAbilities.Initialize(animal);
        ability.RemainingCharges = 0; ability.StartCooldown(0);
        SetTick(1103);
        animal.abilities.AbilitiesTick();
        Check(ability.RemainingCharges == 1, "Native cooldown-per-charge restores one charge in one real tick");
        human.abilities.AbilitiesTick(); animal.abilities.AbilitiesTick();
        Check(ability.RemainingCharges == 1, "Same original Ability cannot receive duplicate native ticks through two trackers");
    }

    private static void TestSerialization(Pawn animal, Ability borrowed, string scratch)
    {
        string file = Path.Combine(scratch, "proxy-save.xml");
        Scribe.saver.InitSaving(file, "sharedAbilities");
        ((IExposable)animal.abilities).ExposeData();
        Scribe.saver.FinalizeSaving();
        string xml = File.ReadAllText(file);
        Check(!xml.Contains("<Id>" + borrowed.Id + "</Id>"), "Actual native proxy Scribe does not deep-save canonical human Ability a second time");
        Check(xml.Contains("FixtureAnimal") && xml.Contains("bodyAbilities"), "Actual native proxy Scribe preserves animal-native tracker and abilities");
    }

    private static AbilityDef MakeDef(string name, Type verb)
    {
        var def = new AbilityDef { defName = name, label = name, verbProperties = new VerbProperties { verbClass = verb, isPrimary = true, range = 12f },
            cooldownTicksRange = new IntRange(100, 100), displayGizmoWhileUndrafted = true };
        DefDatabase<AbilityDef>.Add(def);
        return def;
    }
    private static Ability AddAbility(Pawn pawn, AbilityDef def)
    {
        if (pawn.abilities == null) pawn.abilities = new Pawn_AbilityTracker(pawn);
        var ability = new Ability(pawn, def);
        pawn.abilities.abilities.Add(ability); pawn.abilities.Notify_TemporaryAbilitiesChanged();
        return ability;
    }
    private static Gene AddGene(Pawn human, string name, AbilityDef def)
    {
        if (human.genes == null) human.genes = new Pawn_GeneTracker(human);
        var gene = new Gene { pawn = human, def = new GeneDef { defName = name, abilities = new List<AbilityDef> { def } } };
        human.genes.Endogenes.Add(gene);
        AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes").SetValue(human.genes, null);
        return gene;
    }
    private static Pawn MakePawn(int id, bool human)
    {
        Pawn pawn = Bare<Pawn>(); pawn.thingIDNumber = id; pawn.stackCount = 1;
        pawn.def = new ThingDef { defName = "AbilityPawn" + id, category = ThingCategory.Pawn,
            race = new RaceProperties { intelligence = human ? Intelligence.Humanlike : Intelligence.Animal,
                lifeStageAges = new List<LifeStageAge> { new LifeStageAge { def = new LifeStageDef { defName = "Adult" } } } } };
        pawn.kindDef = new PawnKindDef { defName = "AbilityPawnKind" + id, race = pawn.def };
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)-1);
        pawn.health = Bare<Pawn_HealthTracker>();
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(pawn.health, pawn);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AccessTools.Field(typeof(Pawn_AgeTracker), "cachedLifeStageIndex").SetValue(pawn.ageTracker, 0);
        pawn.drafter = new Pawn_DraftController(pawn);
        AccessTools.Field(typeof(Pawn_DraftController), "draftedInt").SetValue(pawn.drafter, true);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.abilities = new Pawn_AbilityTracker(pawn);
        return pawn;
    }
    private static void Mark(Pawn animal, Pawn human)
    {
        var hediff = new HediffWithComps { pawn = animal, def = WildShiftDefOf.WildShift_Transformed };
        var comp = new HediffComp_Transformed { parent = hediff, props = new HediffCompProperties_Transformed() };
        hediff.comps = new List<HediffComp> { comp }; animal.health.hediffSet.hediffs.Add(hediff);
        ((ThingOwner<Pawn>)comp.GetDirectlyHeldThings()).InnerListForReading.Add(human);
        human.holdingOwner = comp.GetDirectlyHeldThings();
    }
    private static void SetTick(int tick) { AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(Find.TickManager, tick); }
    private static bool Close(float a, float b) { return Math.Abs(a - b) < 0.00001f; }
    private static T Bare<T>() where T : class { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    private static void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks++; }
}
