using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WildShift;

namespace WildShiftReviewValidation
{
    [StaticConstructorOnStartup]
    public static class Boot
    {
        public static readonly string Output = Path.Combine(GenFilePaths.SaveDataFolderPath, "ReviewResults");
        static Boot()
        {
            Directory.CreateDirectory(Output);
            Application.runInBackground = true;
            new Harmony("wildshift.tests.review17").Patch(AccessTools.Method(typeof(Root_Play), "SetupForQuickTestPlay"),
                postfix: new HarmonyMethod(typeof(Boot), nameof(SmallMap)));
        }
        public static void SmallMap() { Find.GameInitData.mapSize = 75; }
        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }

    // TEST ONLY: actual initialized game, real production transformations,
    // native training degradation, Pawn.Kill, caravan lifecycle and full save.
    public sealed class Regression : GameComponent
    {
        private bool ready, done;
        private int frames, stage, failures;
        private Pawn original, form;
        private Caravan caravan;
        private string originalId;
        private readonly List<string> results = new List<string>();
        private readonly List<string> defects = new List<string>();
        public Regression(Game game) { }
        public override void FinalizeInit() { ready = true; }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "reviewStage");
            Scribe_Values.Look(ref originalId, "reviewOriginalId");
            Scribe_References.Look(ref original, "reviewOriginal");
            Scribe_References.Look(ref form, "reviewForm");
            Scribe_References.Look(ref caravan, "reviewCaravan");
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
                Finish("exception");
            }
        }
        private void FirstPass()
        {
            Map map = Find.CurrentMap;
            if (map == null) throw new InvalidOperationException("Quicktest supplied no native map.");
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                pawn.DeSpawn();
                if (pawn.Faction == Faction.OfPlayer) pawn.SetFaction(null);
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            foreach (Thing thing in map.listerThings.AllThings.ToList()) if (thing.def.destroyable) thing.Destroy();
            foreach (IntVec3 cell in map.AllCells)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }

            Pawn draftOriginal = NewHuman(map, "DraftOriginal", 0);
            TransformUtility.AddOrGetShapeshifter(draftOriginal, Kind());
            draftOriginal.drafter.Drafted = true;
            Require(TransformUtility.TransformToAnimal(draftOriginal, Kind()), "draft fixture transforms with production method");
            Pawn draftedForm = Body(map, draftOriginal);
            Require(draftedForm.Drafted, "native drafted human becomes a drafted animal");
            Pawn returned = TransformUtility.RevertToHuman(draftedForm, false);
            Require(returned == draftOriginal && returned.Spawned, "draft fixture returns the same human");
            bool draftPreserved = returned.Drafted;
            Check(draftPreserved, "drafted animal returns to a drafted human");
            if (!draftPreserved) defects.Add("draft-return");
            returned.drafter.Drafted = false;

            original = NewHuman(map, "TrainingOriginal", 3);
            originalId = original.GetUniqueLoadID();
            TransformUtility.AddOrGetShapeshifter(original, Kind());
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = 17;
            Require(original.inventory.innerContainer.TryAdd(silver), "original carries a unique inventory payload");
            Require(TransformUtility.TransformToAnimal(original, Kind()), "training fixture transforms with production method");
            form = Body(map, original);
            Require(!ModsConfig.OdysseyActive && form.RaceProps.specialTrainables.NullOrEmpty(),
                "Core-only Warg has no Odyssey special training to mask the regression");
            Require(form.training.HasLearned(TrainableDefOf.Tameness) && TrainableUtility.TamenessCanDecay(form),
                "native form generation and wildness meet actual training-decay conditions");
            Degrade(form);
            bool tame = form.Faction == Faction.OfPlayer && form.training.HasLearned(TrainableDefOf.Tameness);
            Check(tame, "ordinary transformed form retains tameness and player ownership");
            if (!tame) defects.Add("training-wildness");

            // Restore only the failed baseline fixture's setup, after recording
            // the actual native loss. This is not part of the production fix.
            if (form.Faction != Faction.OfPlayer) form.SetFaction(Faction.OfPlayer);
            form.training.Train(TrainableDefOf.Tameness, null, true);
            Pawn ordinaryAnimal = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind(), Faction.OfPlayer,
                forceGenerateNewPawn: true));
            GenSpawn.Spawn(ordinaryAnimal, map.Center + new IntVec3(6, 0, 0), map);
            ordinaryAnimal.training.Train(TrainableDefOf.Tameness, null, true);
            Degrade(ordinaryAnimal);
            Require(ordinaryAnimal.Faction == null && !ordinaryAnimal.training.HasLearned(TrainableDefOf.Tameness),
                "ordinary livestock still loses tameness through the same native calls");
            ordinaryAnimal.DeSpawn();
            Find.WorldPawns.PassToWorld(ordinaryAnimal, PawnDiscardDecideMode.KeepForever);

            Pawn companion = NewHuman(map, "CaravanCompanion", -3);
            caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(new[] { companion, form }, Faction.OfPlayer,
                map.Tile, map.Tile, PlanetTile.Invalid, false);
            Require(caravan.Spawned && caravan.ContainsPawn(companion) && caravan.ContainsPawn(form),
                "native departure creates a valid mixed human and animal-form caravan");
            Require(!caravan.IsOwner(form), "outside survival checks the form retains native animal trade classification");
            companion.Kill(null);
            bool survived = !caravan.Destroyed && caravan.Spawned && caravan.ContainsPawn(form);
            Check(survived, "caravan survives native death of its last ordinary human while a transformed colonist lives");
            if (!survived) defects.Add("mixed-caravan-loss");
            if (survived)
            {
                Check(!caravan.AllOwnersDowned && !caravan.AllOwnersHaveMentalBreak,
                    "mobile sane transformed colonist does not falsely immobilize the surviving caravan");
                Check(!caravan.IsOwner(form), "survival and mobility scope does not leak into ordinary owner/trade queries");
                TickNative(600);
                Check(caravan.Spawned && caravan.ContainsPawn(form) && TransformUtility.TryGetTransformedComp(form).StoredPawn == original,
                    "native world ticks retain caravan, active form and its exact original human");
            }
            if (failures != 0) { Finish("first-pass"); return; }
            stage = 1;
            GameDataSaveLoader.SaveGame("autostart");
            Check(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")), "native full game save is written");
            Finish("save-ready");
        }
        private void ReloadPass()
        {
            Require(caravan != null && original != null && form != null, "native reload resolves surviving caravan and both bodies");
            Check(caravan.Spawned && caravan.ContainsPawn(form) && !form.Spawned && TransformUtility.TryGetTransformedComp(form)?.StoredPawn == original,
                "native reload preserves exactly one caravan form holding its original human");
            Check(original.GetUniqueLoadID() == originalId, "reload preserves original human identity");
            Check(!caravan.AllOwnersDowned && !caravan.AllOwnersHaveMentalBreak && !caravan.IsOwner(form),
                "reloaded caravan retains scoped mobility protection without changing trade ownership");
            TickNative(600);
            Check(caravan.Spawned && caravan.ContainsPawn(form), "surviving caravan remains after further native ticks");
            Pawn restored = TransformUtility.RevertToHuman(form, false);
            Check(restored == original && form.Destroyed && caravan.ContainsPawn(original) && !original.Spawned
                && WorldPawnsUtility.IsWorldPawn(original), "off-map production return replaces form with the exact world-registered human");
            Check(!original.Drafted && original.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Silver) == 17,
                "off-map return stays undrafted and preserves the exact inventory");
            Check(caravan.IsOwner(original), "returned human has native caravan ownership");
            Finish("complete");
        }
        private static PawnKindDef Kind() { return DefDatabase<PawnKindDef>.GetNamed("Warg"); }
        private static Pawn NewHuman(Map map, string name, int offset)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            pawn.Name = new NameSingle(name);
            GenSpawn.Spawn(pawn, map.Center + new IntVec3(offset, 0, 0), map);
            return pawn;
        }
        private static Pawn Body(Map map, Pawn human)
        {
            return map.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == human);
        }
        private static void Degrade(Pawn pawn)
        {
            for (int i = 0; i < TrainableDefOf.Tameness.steps; i++)
            {
                pawn.training.Debug_MakeDegradeHappenSoon();
                pawn.training.TrainingTrackerTickRare();
            }
        }
        private static void TickNative(int ticks)
        {
            for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick();
        }
        private void Require(bool passed, string label)
        {
            Check(passed, label);
            if (!passed) throw new InvalidOperationException("Fixture prerequisite: " + label);
        }
        private void Check(bool passed, string label)
        {
            if (!passed) failures++;
            string line = (passed ? "PASS: " : "FAIL: ") + label;
            results.Add(line);
            File.AppendAllText(Path.Combine(Boot.Output, "checks.txt"), line + "\n");
        }
        private void Finish(string phase)
        {
            done = true;
            var data = new
            {
                Phase = phase, Passed = failures == 0, Failures = failures, BaselineDefectsObserved = defects.ToArray(),
                WildShiftPath = typeof(TransformUtility).Assembly.Location, WildShiftSHA256 = Boot.Hash(typeof(TransformUtility).Assembly.Location),
                FixtureSHA256 = Boot.Hash(typeof(Boot).Assembly.Location),
                Mods = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray(), Results = results.ToArray(),
                Scope = "Actual Core-only initialized game. Native degradation is accelerated using its debug scheduling method, without replacing native methods. Native Pawn.Kill, caravan departure, production transformation/return and full save/reload are used."
            };
            File.WriteAllText(Path.Combine(Boot.Output, "phase-" + (stage == 0 || phase == "save-ready" ? "0" : "1") + ".json"),
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            Application.Quit();
        }
    }
}
