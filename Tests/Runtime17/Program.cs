using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using WildShift;

internal static class Program
{
    private static int assertions;
    private const string Owner = "wildshift.mod";

    private static int Main(string[] args)
    {
        if (args.Length != 4) { Console.Error.WriteLine("Expected game directory, Harmony directory, mod directory, scratch directory."); return 2; }
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { args[0], args[1] })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            Run(args[0], args[2], args[3]);
            Console.WriteLine($"PASS: {assertions} assertions against installed RimWorld 1.7 assemblies.");
            Console.WriteLine("Scope: real Harmony registration/native APIs and isolated pawn subgraphs; not a full graphical game or third-party mod integration run.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string gameDir, string modDir, string scratch)
    {
        string profile = Path.Combine(scratch, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Config"));
        Directory.CreateDirectory(Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, profile);
        AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(profile, "EmptyMods"));
        AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(gameDir, "Data"));
        File.WriteAllText(Path.Combine(profile, "Config", "ModsConfig.xml"), "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>");
        var prefsData = AccessTools.Field(typeof(Prefs), "data");
        prefsData.SetValue(null, Activator.CreateInstance(prefsData.FieldType));
        Console.WriteLine("Game assembly: " + typeof(Pawn).Assembly.Location);
        Console.WriteLine("Mod assembly: " + typeof(TransformUtility).Assembly.Location);
        AuditXml(gameDir, modDir);
        var harmony = new Harmony(Owner);
        harmony.PatchAll(typeof(TransformUtility).Assembly);
        AnimalModCompatibility.Install(harmony);
        SapientAnimalsCompatibility.Install(harmony);
        AuditPatches();
        SetupGame();
        TestNativeControlAndStorage();
        TestNativeTemporaryMapRetention();
        TestStorageSerialization(scratch);
        TestCustomSettingsSerialization(scratch);
        AccessTools.Method(typeof(Log), "DrainOffThreadMessages").Invoke(null, null);
        File.WriteAllLines(Path.Combine(scratch, "native-log.txt"), Log.Messages.Select(m => m.type + ": " + m.text + "\n" + m.StackTrace));
        var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException("Native game logged errors: " + string.Join("\n", errors.Select(e => e.text)));
        Check(true, "Native log contains no errors");
    }

    private static void AuditPatches()
    {
        var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(Owner)).ToArray();
        var registered = patched.SelectMany(m => {
            var p = Harmony.GetPatchInfo(m);
            return p.Prefixes.Concat(p.Postfixes).Concat(p.Transpilers).Concat(p.Finalizers).Where(x => x.owner == Owner);
        }).Select(p => p.PatchMethod).ToHashSet();
        int classes = 0;
        foreach (var type in typeof(TransformUtility).Assembly.GetTypes().Where(t => t.IsDefined(typeof(HarmonyPatch), false)))
        {
            classes++;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => new[] { "Prefix", "Postfix", "Transpiler", "Finalizer" }.Contains(m.Name)))
                Check(registered.Contains(method), "Registered patch " + type.Name + "." + method.Name);
        }
        Check(classes >= 40, "All declared core patch classes audited");
        foreach (var method in patched)
        {
            var p = Harmony.GetPatchInfo(method);
            foreach (var patch in p.Prefixes.Concat(p.Postfixes).Where(x => x.owner == Owner))
            foreach (var parameter in patch.PatchMethod.GetParameters().Where(x => x.Name.StartsWith("___")))
            {
                var field = AccessTools.Field(method.DeclaringType, parameter.Name.Substring(3));
                var expected = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                Check(field != null && (expected == field.FieldType || expected.IsAssignableFrom(field.FieldType)),
                    "Injected native field " + method.DeclaringType.Name + "." + parameter.Name.Substring(3));
            }
        }
        var drag = Patch_TransformedDragSelection.FindTarget();
        Check(drag != null && registered.Contains(AccessTools.Method(typeof(Patch_TransformedDragSelection), "Postfix")), "Native drag priority predicate exists and is patched");
        Console.WriteLine($"Audited {classes} patch classes on {patched.Length} distinct native methods.");
    }

    private static void AuditXml(string gameDir, string modDir)
    {
        var docs = new List<XDocument>();
        foreach (string folder in new[] { "Defs", "About", "Languages", "Patches" })
        foreach (string path in Directory.GetFiles(Path.Combine(modDir, folder), "*.xml", SearchOption.AllDirectories))
        {
            docs.Add(XDocument.Load(path));
            Check(true, "Parse XML " + Path.GetRelativePath(modDir, path));
        }
        var names = new HashSet<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(gameDir, "Data", "Core", "Defs", "ThinkTreeDefs"), "*.xml", SearchOption.AllDirectories))
        foreach (var element in XDocument.Load(path).Descendants("ThinkTreeDef").Elements("defName")) names.Add(element.Value);
        var tree = XDocument.Load(Path.Combine(modDir, "Defs", "ThinkTreeDefs.xml"));
        foreach (var element in tree.Descendants("treeDef")) Check(names.Contains(element.Value), "Native think subtree " + element.Value);
        foreach (var attribute in tree.Descendants().Attributes("Class"))
        {
            Type type = typeof(Pawn).Assembly.GetType("Verse.AI." + attribute.Value) ?? typeof(Pawn).Assembly.GetType("RimWorld." + attribute.Value)
                ?? typeof(TransformUtility).Assembly.GetType(attribute.Value);
            Check(type != null && typeof(ThinkNode).IsAssignableFrom(type), "Think node class " + attribute.Value);
        }
        var defNames = docs.Where(d => d.Root?.Name.LocalName == "Defs").SelectMany(d => d.Root.Elements().Elements("defName")).Select(e => e.Value).ToArray();
        Check(defNames.Distinct().Count() == defNames.Length, "No duplicate mod def names");
        foreach (var doc in docs.Where(d => d.Root?.Name.LocalName == "LanguageData"))
            Check(doc.Root.Elements().Select(e => e.Name).Distinct().Count() == doc.Root.Elements().Count(), "No duplicate translation keys");
    }

    private static void SetupGame()
    {
        var game = Bare<Game>();
        game.tickManager = Bare<TickManager>();
        game.playSettings = new PlaySettings();
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, 1000);
        game.InitData = Bare<GameInitData>();
        game.InitData.playerFaction = Bare<Faction>();
        game.InitData.playerFaction.def = new FactionDef { defName = "RuntimePlayer", isPlayer = true };
        AccessTools.Field(typeof(Game), "worldInt").SetValue(game, Bare<World>());
        // Real native Map entries let Spawned/Map resolve normally without
        // generating or ticking a colony in this isolated object-graph test.
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { Bare<Map>() });
        Current.Game = game;
        WildShiftMod.Settings = new WildShiftSettings();
        WildShiftDefOf.WildShift_Transformed = new HediffDef { defName = "WildShift_Transformed" };
        WildShiftDefOf.WildShift_Shapeshifter = new HediffDef { defName = "WildShift_Shapeshifter" };
        WildShiftDefOf.WildShift_TransformedDrafted = new ThinkTreeDef { defName = "WildShift_TransformedDrafted" };
    }

    private static Pawn Pawn(int id, Intelligence intelligence, Faction faction)
    {
        var pawn = Bare<Pawn>();
        pawn.thingIDNumber = id;
        pawn.stackCount = 1;
        pawn.def = new ThingDef { defName = "RuntimePawn" + id + "Race", category = ThingCategory.Pawn, race = new RaceProperties { intelligence = intelligence } };
        AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, new FleshTypeDef { defName = "RuntimeFlesh" });
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)-1);
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(pawn, faction);
        pawn.health = Bare<Pawn_HealthTracker>();
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(pawn.health, pawn);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.mindState = Bare<Pawn_MindState>();
        pawn.mindState.mentalStateHandler = new MentalStateHandler(pawn);
        return pawn;
    }

    private static HediffComp_Transformed Mark(Pawn pawn)
    {
        var hediff = new HediffWithComps { pawn = pawn, def = WildShiftDefOf.WildShift_Transformed };
        var comp = new HediffComp_Transformed { parent = hediff, props = new HediffCompProperties_Transformed() };
        hediff.comps = new List<HediffComp> { comp };
        pawn.health.hediffSet.hediffs.Add(hediff);
        return comp;
    }

    private static void TestNativeControlAndStorage()
    {
        var human = Pawn(1, Intelligence.Humanlike, Faction.OfPlayer);
        var animal = Pawn(2, Intelligence.Animal, Faction.OfPlayer);
        var ordinary = Pawn(3, Intelligence.Animal, Faction.OfPlayer);
        var enemyFaction = Bare<Faction>();
        enemyFaction.def = new FactionDef { defName = "RuntimeEnemy" };
        var enemy = Pawn(4, Intelligence.Animal, enemyFaction);
        var comp = Mark(animal);
        var enemyComp = Mark(enemy);
        Check(TransformUtility.TryGetTransformedComp(animal) == comp, "Native hediff lookup finds transformed comp");
        Check(!TransformUtility.IsTransformedAnimal(ordinary), "Ordinary animal remains unmarked");
        Check(comp.TryStore(human), "Native ThingOwner stores original human");
        Check(comp.TryStore(human), "Storing same original is idempotent");
        Check(comp.StoredPawn == human && human.holdingOwner == comp.GetDirectlyHeldThings(), "Stored body is same instance with exactly one native owner");
        Check(comp.GetDirectlyHeldThings().Count == 1, "No duplicate body when storing twice");
        Check(!animal.IsColonistPlayerControlled, "Unspawned transformed animal is not player-controlled");
        Check(animal.drafter == null && animal.playerSettings == null, "Unspawned control query does not create trackers");
        foreach (var pawn in new[] { animal, ordinary, enemy })
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
        Check(animal.Spawned && animal.Map == Current.Game.Maps[0], "Native spawned control fixture resolves registered map");
        Check(animal.IsColonistPlayerControlled, "Actual patched player-control getter accepts transformed animal");
        Check(animal.drafter != null && animal.playerSettings != null, "Control getter initializes native trackers");
        Check(!ordinary.IsColonistPlayerControlled, "Ordinary animal is not player colonist-controlled");
        Check(!enemy.IsColonistPlayerControlled, "Enemy transformed animal is not player-controlled");
        AccessTools.Field(typeof(MentalStateHandler), "curStateInt").SetValue(animal.mindState.mentalStateHandler,
            new MentalState { pawn = animal, def = new MentalStateDef { defName = "RuntimeMentalState" } });
        Check(animal.InMentalState && animal.MentalStateDef != null, "Native mental-state fixture has active mental break");
        Check(!animal.IsColonistPlayerControlled, "Transformed animal in mental state is not player-controlled");
        Check(!animal.CanTakeOrder, "Native order eligibility rejects transformed animal in mental state");
        AccessTools.Field(typeof(MentalStateHandler), "curStateInt").SetValue(animal.mindState.mentalStateHandler, null);
        Check(animal.IsColonistPlayerControlled, "Transformed animal regains control after mental state ends");
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(animal.health, PawnHealthState.Down);
        Check(animal.IsColonistPlayerControlled, "Downed transformed classification retains native control semantics");
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(animal.health, PawnHealthState.Mobile);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(animal, (sbyte)-1);
        Check(!animal.IsColonistPlayerControlled && !animal.CanTakeOrder, "Despawned form loses player control and orders despite existing trackers");
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(animal, (sbyte)0);
        var nativeColonist = Pawn(5, Intelligence.Humanlike, Faction.OfPlayer);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(nativeColonist, (sbyte)0);
        Check(nativeColonist.IsColonistPlayerControlled, "Native true player-control result is preserved for normal human colonist");
        var drag = Patch_TransformedDragSelection.FindTarget();
        Check((bool)drag.Invoke(null, new object[] { animal }), "Actual native drag predicate prioritizes player form");
        Check(!(bool)drag.Invoke(null, new object[] { ordinary }), "Native drag predicate excludes ordinary animal");
        Check(!(bool)drag.Invoke(null, new object[] { enemy }), "Native drag predicate excludes enemy form");
        Check(animal.equipment == null && !FloatMenuUtility.UseRangedAttack(animal), "Actual patched ranged lookup handles unarmed animal without equipment tracker");
        Check(!TransformUtility.TryOrderMeleeAttack(animal, LocalTargetInfo.Invalid), "Invalid native melee target is rejected");
        Check(FormTransferUtility.TryBeginReturn(animal), "First return acquires guard");
        Check(!FormTransferUtility.TryBeginReturn(animal), "Second simultaneous return is blocked");
        FormTransferUtility.EndReturn(animal);
        Check(FormTransferUtility.TryBeginReturn(animal), "Return can be retried after guard release");
        FormTransferUtility.EndReturn(animal);
        Check(comp.ReleaseStoredPawn() == human && !comp.HasStoredPawn && human.holdingOwner == null, "Release returns exact original body and clears ownership");
        Check(comp.ReleaseStoredPawn() == null, "Second release cannot duplicate body");
        Check(!(bool)drag.Invoke(null, new object[] { animal }), "Empty animal shell loses drag priority");
    }

    private static readonly HashSet<MapPawns> temporaryMapFixturePawns = new HashSet<MapPawns>();
    private static readonly HashSet<Map> temporaryMapFixtures = new HashSet<Map>();

    private static bool NoNearbyCaravanForFixture(Pawn pawn, ref Caravan __result)
    {
        if (pawn?.Map == null || !temporaryMapFixtures.Contains(pawn.Map)) return true;
        __result = null;
        return false;
    }

    private static bool NoUnspawnedContainerPawnsForFixture(MapPawns __instance, ref List<Pawn> __result)
    {
        if (!temporaryMapFixturePawns.Contains(__instance)) return true;
        __result = new List<Pawn>();
        return false;
    }

    private static bool NoIncomingTransporterForFixture(Map map, ref bool __result)
    {
        if (!temporaryMapFixtures.Contains(map)) return true;
        __result = false;
        return false;
    }

    private static void TestNativeTemporaryMapRetention()
    {
        // Run the actual private colony predicate, public MapPawns getter and
        // Site removal method. This object graph has no path grid, containers or
        // incoming ships: those three ancillary boundaries are fixture-only
        // prefixes. No production retention/removal result is mocked.
        var fixtureHarmony = new Harmony("wildshift.tests.temporary-map-boundaries");
        var colonyPredicate = AccessTools.Method(typeof(MapPawns), "IsValidColonyPawn");
        var productionHarmony = new Harmony(Owner);
        var productionPostfixes = Harmony.GetPatchInfo(colonyPredicate)?.Postfixes.Where(p => p.owner == Owner).ToArray()
            ?? Array.Empty<Patch>();
        var oldMaps = Current.Game.Maps.ToList();
        var oldPocketMaps = Find.World.pocketMaps;
        var site = Bare<Site>();
        site.parts = new List<SitePart>();
        site.def = new WorldObjectDef { defName = "RuntimeTemporarySite" };
        var otherSite = Bare<Site>();
        otherSite.parts = new List<SitePart>();
        otherSite.def = site.def;
        var map = Bare<Map>();
        map.info = new MapInfo { parent = site, Size = new IntVec3(40, 1, 40) };
        map.listerThings = new ListerThings(ListerThingsUse.Global);
        map.mapPawns = new MapPawns(map);
        var otherMap = Bare<Map>();
        otherMap.info = new MapInfo { parent = otherSite, Size = new IntVec3(40, 1, 40) };
        otherMap.listerThings = new ListerThings(ListerThingsUse.Global);
        otherMap.mapPawns = new MapPawns(otherMap);
        var mapPawns = (List<Pawn>)AccessTools.Field(typeof(MapPawns), "pawnsSpawned").GetValue(map.mapPawns);
        var otherMapPawns = (List<Pawn>)AccessTools.Field(typeof(MapPawns), "pawnsSpawned").GetValue(otherMap.mapPawns);
        var original = Pawn(301, Intelligence.Humanlike, Faction.OfPlayer);
        var form = Pawn(302, Intelligence.Animal, Faction.OfPlayer);
        var ordinary = Pawn(303, Intelligence.Animal, Faction.OfPlayer);
        var comp = Mark(form);
        var enemyFaction = Bare<Faction>();
        enemyFaction.def = new FactionDef { defName = "RuntimeTemporaryMapEnemy" };
        Func<Pawn, bool> valid = p => (bool)colonyPredicate.Invoke(null, new object[] { p });
        bool productionUnpatched = false;
        try
        {
            AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { map, otherMap });
            Find.World.pocketMaps = new List<PocketMapParent>();
            temporaryMapFixtures.UnionWith(new[] { map, otherMap });
            temporaryMapFixturePawns.UnionWith(new[] { map.mapPawns, otherMap.mapPawns });
            fixtureHarmony.Patch(AccessTools.Method(typeof(CaravanExitMapUtility), "FindCaravanToJoinFor"),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoNearbyCaravanForFixture)));
            fixtureHarmony.Patch(AccessTools.PropertyGetter(typeof(MapPawns), "AllPawnsUnspawned"),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoUnspawnedContainerPawnsForFixture)));
            fixtureHarmony.Patch(AccessTools.Method(typeof(TransporterUtility), "IncomingTransporterPreventingMapRemoval"),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoIncomingTransporterForFixture)));
            Check(comp.TryStore(original), "Temporary-site fixture stores exact native original");
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(form, (sbyte)0);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(ordinary, (sbyte)0);
            mapPawns.Add(form);
            Check(site.Map == map && otherSite.Map == otherMap, "Native Site lookup resolves distinct registered fixture maps");

            productionHarmony.Unpatch(colonyPredicate, HarmonyPatchType.Postfix, Owner);
            productionUnpatched = true;
            Check(valid(original) && !valid(form), "Unpatched native colony predicate accepts human but misses its animal form");
            Check(!map.mapPawns.AnyPawnBlockingMapRemoval,
                "Regression reproduced: unpatched native removal getter misses sole transformed colonist");
            Check(site.ShouldRemoveMapNow(out bool baselineRemoveSite) && baselineRemoveSite,
                "Regression reproduced: native Site would remove map containing sole transformed colonist");
            foreach (var patch in productionPostfixes)
                productionHarmony.Patch(colonyPredicate, postfix: new HarmonyMethod(patch.PatchMethod));
            productionUnpatched = false;

            Check(valid(form), "Patched native colony predicate accepts living player form with original");
            Check(map.mapPawns.AnyPawnBlockingMapRemoval, "Actual MapPawns getter retains sole transformed colonist's map");
            Check(!site.ShouldRemoveMapNow(out bool removeProtectedSite) && !removeProtectedSite,
                "Actual Site removal method retains protected map and world object");
            Check(!form.IsColonist && form.RaceProps.Animal, "Retention does not globally relabel animal as human colonist");
            Check(valid(original), "Native human colonist retention remains true");
            Check(!valid(ordinary), "Ordinary livestock does not gain colony retention");

            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(form.health, PawnHealthState.Down);
            Check(valid(form) && map.mapPawns.AnyPawnBlockingMapRemoval, "Downed living form still retains its temporary map");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(form.health, PawnHealthState.Mobile);
            AccessTools.Field(typeof(MentalStateHandler), "curStateInt").SetValue(form.mindState.mentalStateHandler,
                new MentalState { pawn = form, def = new MentalStateDef { defName = "RuntimeRetentionMentalBreak" } });
            Check(valid(form) && map.mapPawns.AnyPawnBlockingMapRemoval, "Mental-state form retains map even though direct control is restricted");
            AccessTools.Field(typeof(MentalStateHandler), "curStateInt").SetValue(form.mindState.mentalStateHandler, null);
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(original.health, PawnHealthState.Down);
            Check(valid(form), "Downed stored colonist retains native colony eligibility");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(original.health, PawnHealthState.Mobile);

            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(form.health, PawnHealthState.Dead);
            Check(!valid(form) && !map.mapPawns.AnyPawnBlockingMapRemoval, "Dead animal form does not falsely retain map");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(form.health, PawnHealthState.Mobile);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(form, (sbyte)-2);
            Check(!valid(form), "Destroyed animal form does not falsely retain map");
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(form, (sbyte)0);
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(original.health, PawnHealthState.Dead);
            Check(!valid(form) && !map.mapPawns.AnyPawnBlockingMapRemoval, "Dead stored human does not falsely retain map");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(original.health, PawnHealthState.Mobile);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(original, (sbyte)-2);
            Check(!valid(form), "Destroyed stored original does not gain retention");
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(original, (sbyte)-1);
            AccessTools.Field(typeof(Thing), "factionInt").SetValue(form, enemyFaction);
            Check(!valid(form) && !map.mapPawns.AnyPawnBlockingMapRemoval, "Hostile animal form does not retain player's map");
            AccessTools.Field(typeof(Thing), "factionInt").SetValue(form, null);
            Check(!valid(form), "Released animal form does not retain player's map");
            AccessTools.Field(typeof(Thing), "factionInt").SetValue(form, Faction.OfPlayer);
            AccessTools.Field(typeof(Thing), "factionInt").SetValue(original, enemyFaction);
            Check(!valid(form), "Stored non-player human does not gain colony retention");
            AccessTools.Field(typeof(Thing), "factionInt").SetValue(original, Faction.OfPlayer);
            form.guest = Bare<Pawn_GuestTracker>();
            AccessTools.Field(typeof(Pawn_GuestTracker), "pawn").SetValue(form.guest, form);
            AccessTools.Field(typeof(Pawn_GuestTracker), "hostFactionInt").SetValue(form.guest, enemyFaction);
            form.guest.guestStatusInt = GuestStatus.Prisoner;
            Check(!valid(form), "Captured or hosted animal form cannot retain player map through hidden colonist");
            form.guest = null;
            original.guest = Bare<Pawn_GuestTracker>();
            AccessTools.Field(typeof(Pawn_GuestTracker), "pawn").SetValue(original.guest, original);
            original.guest.guestStatusInt = GuestStatus.Slave;
            Check(original.IsSlave && original.IsColonist, "Native secure-slave fixture qualifies as colony pawn");
            Check(valid(form), "Secure player slave original retains same eligibility as native human");
            AccessTools.Field(typeof(Pawn_GuestTracker), "releasedInt").SetValue(original.guest, true);
            Check(!original.IsColonist && !valid(form), "Unsecured slave original does not gain retention through animal form");
            original.guest = null;
            Check(comp.ReleaseStoredPawn() == original && !valid(form), "Empty shell loses retention after original is released");
            Check(comp.TryStore(original) && valid(form), "Restoring original permits valid retention again");

            mapPawns.Clear();
            otherMapPawns.Add(form);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(form, (sbyte)1);
            Check(!map.mapPawns.AnyPawnBlockingMapRemoval && otherMap.mapPawns.AnyPawnBlockingMapRemoval,
                "Transformed colonist retains only its actual map, not other temporary maps");
            Check(site.ShouldRemoveMapNow(out bool removeDepartedSite) && removeDepartedSite,
                "Native Site cleanup is allowed after last transformed colonist leaves");
            otherMapPawns.Clear();
            Check(!otherMap.mapPawns.AnyPawnBlockingMapRemoval, "No stale retention cache remains when final form leaves");
            mapPawns.Add(ordinary);
            Check(!map.mapPawns.AnyPawnBlockingMapRemoval && site.ShouldRemoveMapNow(out _),
                "Ordinary livestock alone does not block native Site cleanup");
            Console.WriteLine("Temporary-map native fixture: actual colony predicate, removal getter and Site method; nearby-caravan lookup, unspawned-container enumeration and incoming transporters are modeled absent. This fixture does not execute a full transformation or map lifecycle.");
        }
        finally
        {
            if (productionUnpatched)
                foreach (var patch in productionPostfixes)
                    productionHarmony.Patch(colonyPredicate, postfix: new HarmonyMethod(patch.PatchMethod));
            foreach (var method in Harmony.GetAllPatchedMethods().Where(m =>
                Harmony.GetPatchInfo(m).Owners.Contains(fixtureHarmony.Id)).ToArray())
                fixtureHarmony.Unpatch(method, HarmonyPatchType.All, fixtureHarmony.Id);
            temporaryMapFixtures.Clear();
            temporaryMapFixturePawns.Clear();
            AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, oldMaps);
            Find.World.pocketMaps = oldPocketMaps;
        }
    }

    private static void TestStorageSerialization(string scratch)
    {
        // A native Pawn with a minimal identity subgraph is sufficient to test
        // the production deep ThingOwner serializer. This is not a colony save.
        var pawn = Pawn(71, Intelligence.Humanlike, null);
        pawn.health = null;
        pawn.mindState = null;
        pawn.Name = new NameSingle("Preserved original");
        pawn.kindDef = new PawnKindDef { defName = "RuntimeOriginalKind", race = pawn.def };
        DefDatabase<ThingDef>.Add(pawn.def);
        DefDatabase<PawnKindDef>.Add(pawn.kindDef);
        var snapshot = new StorageSnapshot();
        Check(snapshot.comp.TryStore(pawn), "Serialization fixture stores native Pawn original");
        snapshot.comp.enemyCombatForm = true;
        snapshot.comp.enemyReturnPending = true;
        string path = Path.Combine(scratch, "body-storage-roundtrip.xml");
        Scribe.saver.InitSaving(path, "RuntimeStorage");
        Scribe_Deep.Look(ref snapshot, "snapshot");
        Scribe.saver.FinalizeSaving();
        Check(XDocument.Load(path).Descendants("innerList").Elements("li").Count() == 1, "Native save writes one deeply stored body");
        StorageSnapshot loaded = null;
        Scribe.loader.InitLoading(path);
        Scribe_Deep.Look(ref loaded, "snapshot");
        Scribe.loader.FinalizeLoading();
        Check(loaded?.comp.StoredPawn != null, "Native Scribe reload restores stored Pawn");
        Check(loaded.comp.StoredPawn.thingIDNumber == 71 && loaded.comp.StoredPawn.Name.ToStringFull == "Preserved original", "Reload preserves original body ID and name");
        Check(loaded.comp.StoredPawn.kindDef == pawn.kindDef && loaded.comp.StoredPawn.def == pawn.def, "Reload resolves original kind and race definitions");
        Check(loaded.comp.StoredPawn.holdingOwner == loaded.comp.GetDirectlyHeldThings() && loaded.comp.GetDirectlyHeldThings().Count == 1, "Reload restores one native body owner");
        Check(loaded.comp.enemyCombatForm && loaded.comp.enemyReturnPending, "Reload preserves enemy form and queued return flags");
        Pawn restored = loaded.comp.StoredPawn;
        Check(loaded.comp.ReleaseStoredPawn() == restored && !loaded.comp.HasStoredPawn && restored.holdingOwner == null, "Reloaded body releases without duplication");
    }

    private static void TestCustomSettingsSerialization(string scratch)
    {
        var human = Pawn(91, Intelligence.Humanlike, null);
        human.def.defName = "RuntimeCustomRace";
        var smallAnimal = Pawn(92, Intelligence.Animal, null);
        smallAnimal.def.race.baseBodySize = 0.2f;
        smallAnimal.def.race.predator = false;
        var cat = new PawnKindDef { defName = "Cat", label = "cat", race = smallAnimal.def };
        DefDatabase<ThingDef>.Add(smallAnimal.def);
        DefDatabase<PawnKindDef>.Add(cat);
        var snapshot = new CustomSettingsSnapshot();
        Check(!snapshot.settings.customRacialFormsEnabled && snapshot.settings.customRacialForms.Count == 0,
            "Native settings default to existing form rules");
        snapshot.settings.customRacialFormsEnabled = true;
        snapshot.settings.customRacialForms.Add(new CustomRacialFormRule {
            targetDefName = human.def.defName, chance = 1f,
            animalKindDefNames = new List<string> { "Cat", "AbsentOptionalAnimal" }
        });
        snapshot.settings.customRacialForms.Add(new CustomRacialFormRule {
            xenotype = true, targetDefName = "AbsentOptionalXenotype", chance = 0.35f,
            animalKindDefNames = new List<string> { "AbsentOptionalAnimal" }
        });
        WildShiftMod.Settings = snapshot.settings;
        bool customChosen;
        Check(RacialAnimalForms.Choose(human, null, out customChosen) == cat && customChosen,
            "Native custom assignment resolves valid saved kind names and skips absent animals");
        snapshot.comp.SetAssignedKind(cat, true);
        string path = Path.Combine(scratch, "custom-rules-roundtrip.xml");
        Scribe.saver.InitSaving(path, "CustomRules");
        Scribe_Deep.Look(ref snapshot, "snapshot");
        Scribe.saver.FinalizeSaving();
        CustomSettingsSnapshot loaded = null;
        Scribe.loader.InitLoading(path);
        Scribe_Deep.Look(ref loaded, "snapshot");
        Scribe.loader.FinalizeLoading();
        Check(loaded?.settings != null && loaded.settings.customRacialFormsEnabled,
            "Native Scribe reload preserves opt-in toggle");
        Check(loaded.settings.customRacialForms.Count == 2
            && loaded.settings.customRacialForms[0].targetDefName == human.def.defName
            && loaded.settings.customRacialForms[1].xenotype
            && Math.Abs(loaded.settings.customRacialForms[1].chance - 0.35f) < 0.0001f,
            "Native Scribe reload preserves race/xenotype rules and probability");
        Check(loaded.settings.customRacialForms[0].animalKindDefNames.SequenceEqual(new[] { "Cat", "AbsentOptionalAnimal" })
            && loaded.settings.customRacialForms[1].targetDefName == "AbsentOptionalXenotype",
            "Native Scribe keeps absent mod names without unresolved Def references");
        Check(loaded.comp.assignedKind == cat && loaded.comp.customAssignedKind == cat,
            "Native Scribe reload preserves exact custom form provenance");
        string legacyPath = Path.Combine(scratch, "custom-rules-legacy.xml");
        var legacyXml = XDocument.Load(path);
        foreach (var element in legacyXml.Descendants().Where(e => new[] {
            "customRacialFormsEnabled", "customRacialForms", "customAssignedKind"
        }.Contains(e.Name.LocalName)).ToArray()) element.Remove();
        legacyXml.Save(legacyPath);
        CustomSettingsSnapshot legacy = null;
        Scribe.loader.InitLoading(legacyPath);
        Scribe_Deep.Look(ref legacy, "snapshot");
        Scribe.loader.FinalizeLoading();
        Check(legacy?.settings != null && !legacy.settings.customRacialFormsEnabled
            && legacy.settings.customRacialForms.Count == 0,
            "Legacy settings without custom keys retain disabled defaults");
        Check(legacy.comp.assignedKind == cat && legacy.comp.customAssignedKind == null,
            "Legacy save does not invent custom form provenance");
        loaded.settings.customRacialFormsEnabled = false;
        loaded.settings.customRacialForms.Clear();
        WildShiftMod.Settings = loaded.settings;
        Check(loaded.comp.AllowsAssignedKind(cat),
            "Existing custom small form remains valid after rules are disabled and removed");
        loaded.settings.Reset();
        Check(!loaded.settings.customRacialFormsEnabled && loaded.settings.customRacialForms.Count == 0,
            "Reset restores original assignment settings");
    }

    private static T Bare<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        assertions++;
        Console.WriteLine("OK: " + description);
    }
}

public sealed class StorageSnapshot : IExposable
{
    public HediffComp_Transformed comp = new HediffComp_Transformed();
    public void ExposeData() => comp.CompExposeData();
}

public sealed class CustomSettingsSnapshot : IExposable
{
    public WildShiftSettings settings = new WildShiftSettings();
    public HediffComp_Shapeshifter comp = new HediffComp_Shapeshifter();
    public void ExposeData()
    {
        Scribe_Deep.Look(ref settings, "settings");
        comp.CompExposeData();
    }
}
