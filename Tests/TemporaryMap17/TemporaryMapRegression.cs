using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WildShift;

namespace WildShiftTemporaryMapValidation
{
    [StaticConstructorOnStartup]
    public static class Boot
    {
        public static readonly string Output = Path.Combine(GenFilePaths.SaveDataFolderPath, "TemporaryMapResults");
        static Boot()
        {
            Directory.CreateDirectory(Output);
            Application.runInBackground = true;
            new Harmony("wildshift.tests.temporarymap17").Patch(
                AccessTools.Method(typeof(Root_Play), "SetupForQuickTestPlay"),
                postfix: new HarmonyMethod(typeof(Boot), nameof(SmallMap)));
            File.AppendAllText(Path.Combine(Output, "loaded-inputs.txt"),
                "WildShift=" + typeof(TransformUtility).Assembly.Location + "\n"
                + "WildShiftSHA256=" + Hash(typeof(TransformUtility).Assembly.Location) + "\n"
                + "Fixture=" + typeof(Boot).Assembly.Location + "\n"
                + "FixtureSHA256=" + Hash(typeof(Boot).Assembly.Location) + "\n");
        }
        public static void SmallMap() { Find.GameInitData.mapSize = 75; }
        public static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }

    // Run only inside the initialized game. Setup uses native quicktest world
    // and map generation, then attaches a native Site/Camp and enters it via
    // CaravanEnterMapUtility. No map/world/pawn/game-ending methods are mocked.
    public sealed class Regression : GameComponent
    {
        private const int RetentionTicks = 600;
        private bool ready, done;
        private int frames, stage, checks, failures;
        private string originalId, originalName, mapKind;
        private Map testMap;
        private MapParent camp;
        private Pawn human, form;
        private readonly List<string> results = new List<string>();

        public Regression(Game game) { }
        public override void FinalizeInit() { ready = true; }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "temporaryMapStage");
            Scribe_Values.Look(ref checks, "temporaryMapChecks");
            Scribe_Values.Look(ref originalId, "temporaryMapOriginalId");
            Scribe_Values.Look(ref originalName, "temporaryMapOriginalName");
            Scribe_Values.Look(ref mapKind, "temporaryMapKind");
            Scribe_References.Look(ref testMap, "temporaryMapMap");
            Scribe_References.Look(ref camp, "temporaryMapCamp");
            Scribe_References.Look(ref human, "temporaryMapHuman");
            Scribe_References.Look(ref form, "temporaryMapForm");
        }

        public override void GameComponentUpdate()
        {
            if (!ready || done || LongEventHandler.AnyEventNowOrWaiting || ++frames < 45) return;
            try
            {
                Find.TickManager.Pause();
                if (stage == 0) FirstPass(); else ReloadPass();
            }
            catch (Exception error)
            {
                failures++;
                results.Add("EXCEPTION: " + error);
                Finish("exception", false, false);
            }
        }

        private void FirstPass()
        {
            SetupCampAndEnter();
            Check(TransformUtility.TransformToAnimal(human, DefDatabase<PawnKindDef>.GetNamed("Warg")),
                "production TransformToAnimal succeeds for the sole caravan colonist");
            form = testMap.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == human);
            Check(!human.Spawned && form.Spawned && TransformUtility.TryGetTransformedComp(form).StoredPawn == human,
                "the same human is stored in the active animal form");
            Check(testMap.mapPawns.FreeColonistsSpawnedCount == 0,
                "retention is tested without a human colonist left on the map");
            Check(testMap.mapPawns.AnyPawnBlockingMapRemoval,
                "native map-removal predicate recognizes the transformed colonist");
            bool removeWorld;
            Check(!camp.ShouldRemoveMapNow(out removeWorld), "native temporary map refuses cleanup while the form remains");

            TickNative(RetentionTicks);
            bool retained = Find.Maps.Contains(testMap) && camp.HasMap && form.Spawned;
            Check(retained, "temporary map and form remain after 600 actual game ticks");
            Check(!HasCaravanLostLetter(), "no native Caravan lost letter after transformation");
            Check(!Find.GameEnder.gameEnding && !HasGameOverLetter(), "no Game Over after transformation");
            if (!retained)
            {
                // The old production DLL takes the real map-deinitialization
                // path. Preserve that evidence rather than only failing on a
                // synthetic predicate assertion or continuing with null maps.
                TickNative(RetentionTicks);
                bool observed = HasCaravanLostLetter() && HasGameOverLetter();
                Finish("first-pass", false, observed);
                return;
            }

            Check(TransformUtility.TryGetTransformedComp(form).StoredPawn == human && human.GetUniqueLoadID() == originalId,
                "the retained form still owns the exact original human");
            Check(human.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Silver) == 17,
                "human inventory survives the transformation and native ticks");
            Check(failures == 0, "all first-pass prerequisites passed before saving");
            if (failures != 0) { Finish("first-pass", false, false); return; }
            stage = 1;
            File.WriteAllLines(Path.Combine(Boot.Output, "phase-0-checks.txt"), results);
            GameDataSaveLoader.SaveGame("autostart");
            Check(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")), "full native Game save was written");
            File.WriteAllText(Path.Combine(Boot.Output, "reload-ready.txt"), "ready");
            Finish("save-ready", failures == 0, false);
        }

        private void ReloadPass()
        {
            Check(testMap != null && camp != null && human != null && form != null,
                "full-game reload resolves the map, temporary parent, form and original human references");
            if (testMap == null || camp == null || human == null || form == null)
                throw new InvalidOperationException("Reload lost a required native reference.");
            Check(testMap.Parent == camp && !testMap.IsPlayerHome && camp.HasMap,
                "reloaded map retains its native temporary parent");
            Check(!(camp is Site) || (((Site)camp).parts.Count > 0 && !string.IsNullOrWhiteSpace(camp.Label)),
                "reloaded Site retains a valid native SitePart and resolves its native label");
            Check(form.Spawned && !human.Spawned && TransformUtility.TryGetTransformedComp(form)?.StoredPawn == human,
                "native reload keeps one active animal owning its human");
            Check(human.GetUniqueLoadID() == originalId && human.Name.ToStringShort == originalName,
                "native reload preserves the original human identity and name");
            TickNative(RetentionTicks);
            Check(Find.Maps.Contains(testMap) && camp.HasMap && form.Spawned,
                "reloaded temporary map and form remain after 600 more native ticks");
            Check(!Find.GameEnder.gameEnding && !HasCaravanLostLetter() && !HasGameOverLetter(),
                "native reload and ticks do not cause caravan loss or game ending");

            Pawn returned = TransformUtility.RevertToHuman(form, false);
            Check(returned == human && human.Spawned && human.GetUniqueLoadID() == originalId,
                "production reversion restores that same saved human to the temporary map");
            Check(form.Destroyed && human.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Silver) == 17,
                "return removes the empty form without duplicating or losing inventory");

            Caravan caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(new[] { human }, Faction.OfPlayer,
                camp.Tile, camp.Tile, PlanetTile.Invalid, false);
            Check(caravan != null && caravan.Spawned && caravan.ContainsPawn(human)
                && WorldPawnsUtility.IsWorldPawn(human) && !human.Spawned,
                "native caravan departure preserves the original human as a world-registered member");
            bool removeWorld;
            Check(!testMap.mapPawns.AnyPawnBlockingMapRemoval && camp.ShouldRemoveMapNow(out removeWorld),
                "native temporary map becomes removable after the real colonist departs");
            camp.CheckRemoveMapNow();
            Check(!Find.Maps.Contains(testMap) && !camp.HasMap,
                "native cleanup actually removes the now-empty temporary map");
            TickNative(RetentionTicks);
            Check(caravan != null && !caravan.Destroyed && caravan.ContainsPawn(human)
                && human.GetUniqueLoadID() == originalId, "departed caravan survives native cleanup and ticks");
            Check(!Find.GameEnder.gameEnding && !HasCaravanLostLetter() && !HasGameOverLetter(),
                "legitimate empty-map cleanup does not lose the departing caravan");
            Finish("complete", failures == 0, false);
        }

        private void SetupCampAndEnter()
        {
            testMap = Find.CurrentMap;
            if (testMap == null) throw new InvalidOperationException("Native quicktest supplied no initialized map.");
            foreach (Pawn pawn in testMap.mapPawns.AllPawnsSpawned.ToList())
            {
                pawn.DeSpawn();
                if (pawn.Faction == Faction.OfPlayer) pawn.SetFaction(null);
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            foreach (Thing thing in testMap.listerThings.AllThings.ToList())
                if (thing.def.destroyable) thing.Destroy();
            foreach (IntVec3 cell in testMap.AllCells)
            {
                testMap.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                testMap.fogGrid.Unfog(cell);
            }

            MapParent oldParent = testMap.Parent;
            if (!GenCommandLine.TryGetCommandLineArg("TemporaryMapKind", out mapKind)) mapKind = "Site";
            if (mapKind != "Site" && mapKind != "Camp") throw new InvalidOperationException("TemporaryMapKind must be Site or Camp.");
            if (mapKind == "Site")
            {
                // A bare Site with no SitePart has an invalid Label and emits
                // native errors during real save/load cross-reference work.
                // Use the real site factory, with an ancient-complex part
                // when Ideology is active and a Core resource-site fallback.
                SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("AncientComplex")
                    ?? SitePartDefOf.PreciousLump;
                camp = SiteMaker.MakeSite(part, oldParent.Tile, null, false, 0f);
            }
            else
            {
                camp = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Camp);
                camp.Tile = oldParent.Tile;
                camp.SetFaction(Faction.OfPlayer);
            }
            testMap.info.parent = camp;
            oldParent.Destroy();
            Find.WorldObjects.Add(camp);
            Check(camp.Map == testMap && testMap.Parent == camp && !testMap.IsPlayerHome,
                "initialized quicktest map is attached to a real native " + mapKind);
            Check(!(camp is Site) || (((Site)camp).parts.Count > 0 && !string.IsNullOrWhiteSpace(camp.Label)),
                "native Site factory supplies a valid labeled SitePart");

            human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true));
            human.Name = new NameSingle("TemporaryMapOriginal");
            originalId = human.GetUniqueLoadID();
            originalName = human.Name.ToStringShort;
            TransformUtility.AddOrGetShapeshifter(human, DefDatabase<PawnKindDef>.GetNamed("Warg"));
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = 17;
            if (!human.inventory.innerContainer.TryAdd(silver)) throw new InvalidOperationException("Could not seed inventory.");
            Caravan incoming = CaravanMaker.MakeCaravan(new[] { human }, Faction.OfPlayer, camp.Tile, true);
            Check(incoming.ContainsPawn(human) && WorldPawnsUtility.IsWorldPawn(human),
                "native incoming caravan owns and world-registers the original human");
            CaravanEnterMapUtility.Enter(incoming, testMap, p => testMap.Center);
            Check(human.Spawned && human.Map == testMap && incoming.Destroyed,
                "native CaravanEnterMapUtility enters the sole colonist into the temporary map");
            Check(testMap.mapPawns.FreeColonistsSpawnedCount == 1
                && PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction.Count(p => p.IsFreeColonist) == 1,
                "the test has exactly one playable free human colonist and no home-map backup");
            Find.GameEnder.CheckOrUpdateGameOver();
            Check(!Find.GameEnder.gameEnding, "native game ending is clear before transformation");
        }

        private void TickNative(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i % 60 == 0) Find.GameEnder.CheckOrUpdateGameOver();
            }
            Find.GameEnder.CheckOrUpdateGameOver();
        }
        private bool HasCaravanLostLetter()
        {
            string expected = "LetterLabelPawnsLostBecauseMapClosed_Caravan".Translate();
            return Find.LetterStack.LettersListForReading.Any(l => l.Label.ToString() == expected);
        }
        private bool HasGameOverLetter()
        {
            return Find.LetterStack.LettersListForReading.Any(l => l.def == LetterDefOf.GameEnded);
        }
        private void Check(bool passed, string label)
        {
            checks++;
            if (!passed) failures++;
            string line = (passed ? "PASS: " : "FAIL: ") + label;
            results.Add(line);
            File.AppendAllText(Path.Combine(Boot.Output, "checks.txt"), line + "\n");
        }
        private void Finish(string phase, bool passed, bool baselineDefectObserved)
        {
            done = true;
            var summary = new
            {
                Phase = phase, MapKind = mapKind, Passed = passed, Checks = checks, Failures = failures,
                SiteParts = camp is Site ? ((Site)camp).parts.Select(p => p.def.defName).ToArray() : new string[0],
                BaselineDefectObserved = baselineDefectObserved,
                MapRetained = testMap != null && Find.Maps.Contains(testMap),
                CaravanLostLetter = HasCaravanLostLetter(), GameEnding = Find.GameEnder.gameEnding,
                GameOverLetter = HasGameOverLetter(),
                OriginalHumanId = originalId,
                WildShiftPath = typeof(TransformUtility).Assembly.Location,
                WildShiftSHA256 = Boot.Hash(typeof(TransformUtility).Assembly.Location),
                FixturePath = typeof(Boot).Assembly.Location,
                FixtureSHA256 = Boot.Hash(typeof(Boot).Assembly.Location),
                Mods = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray(),
                Results = results.ToArray(),
                Scope = "Actual initialized 1.7 quicktest world and map, native Site/Camp parent, native caravan entry/exit, production transformation/reversion, real ticks and full native save/reload. Fixture setup reparents the generated map; this is not a visual or third-party mod test."
            };
            File.WriteAllText(Path.Combine(Boot.Output, "phase-" + (stage == 0 ? "0" : phase == "save-ready" ? "0" : "1") + ".json"),
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            if (phase == "complete") File.WriteAllText(Path.Combine(Boot.Output, "complete.txt"), passed ? "passed" : "failed");
            Application.Quit();
        }
    }
}
