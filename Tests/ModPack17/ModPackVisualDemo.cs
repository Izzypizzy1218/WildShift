using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using WildShift;

namespace WildShiftModPackVisual
{
    [StaticConstructorOnStartup]
    public static class Boot
    {
        public static readonly string Output = Path.Combine(GenFilePaths.SaveDataFolderPath, "ModPackVisualResults");
        static Boot()
        {
            Directory.CreateDirectory(Output);
            Application.runInBackground = true;
            new Harmony("wildshift.tests.modpackvisual17").Patch(
                AccessTools.Method(typeof(Root_Play), "SetupForQuickTestPlay"),
                postfix: new HarmonyMethod(typeof(Boot), nameof(SmallMap)));
        }
        public static void SmallMap() { Find.GameInitData.mapSize = 75; }
    }

    // Isolated quicktest only. This makes native test subjects and pauses for
    // real user/UI actions. It never changes production behavior or exits the
    // game, and it does not mark rendering as passed from an apparel tracker.
    public sealed class VisualDemo : GameComponent
    {
        private bool ready, prepared;
        private int frames;
        private List<Pawn> subjects = new List<Pawn>();
        private List<string> names = new List<string>();
        private string status = "준비 중";
        public VisualDemo(Game game) { }
        public override void FinalizeInit() { ready = true; }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref prepared, "visualPrepared");
            Scribe_Collections.Look(ref subjects, "visualSubjects", LookMode.Reference);
            Scribe_Collections.Look(ref names, "visualSubjectNames", LookMode.Value);
        }
        public override void GameComponentUpdate()
        {
            if (!ready || prepared || LongEventHandler.AnyEventNowOrWaiting || ++frames < 90) return;
            prepared = true;
            try
            {
                Find.TickManager.Pause();
                Setup(Find.CurrentMap);
                WriteEvidence("prepared");
                status = "준비 완료 · 화면 검증 대기";
                File.WriteAllText(Path.Combine(Boot.Output, "ready.txt"), DateTime.UtcNow.ToString("O"));
            }
            catch (Exception error)
            {
                status = "준비 실패 · error.txt 확인";
                File.WriteAllText(Path.Combine(Boot.Output, "error.txt"), error.ToString());
                Log.Error("[WildShift visual fixture] " + error);
            }
        }

        private void Setup(Map map)
        {
            if (map == null) throw new InvalidOperationException("Quicktest has no map.");
            foreach (IntVec3 cell in CellRect.CenteredOn(map.Center, 17))
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (!(thing is Pawn) && thing.def.destroyable) thing.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell);
            }
            PawnKindDef ratkin = DefDatabase<PawnKindDef>.GetNamed("RatkinColonist");
            PawnKindDef kiiro = DefDatabase<PawnKindDef>.GetNamed("Kiiro_Colonist");
            Pair(map, ratkin, "Warg", "랫킨 와르그", 6);
            Pair(map, ratkin, "Thrumbo", "랫킨 트럼보", 0);
            Pawn human = NewHuman(map, kiiro, "키이로 캣민트", -6, -7);
            TransformUtility.AddOrGetShapeshifter(human, DefDatabase<PawnKindDef>.GetNamed("Cougar"));
            Track(human, "키이로: 캣민트 먹고 변신");
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Kiiro_CannedMintedCatFood"));
            food.stackCount = 3;
            GenSpawn.Spawn(food, map.Center + new IntVec3(-3, 0, -7), map);
            human.needs.food.CurLevel = human.needs.food.MaxLevel * .25f;
            Pawn target = NewHuman(map, PawnKindDefOf.Colonist, "능력 시험 대상", 9, -7);
            Track(target, "능력 시험 대상 · 자동 공격 없음");
            Pawn wild = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("WildBoar"));
            wild.Name = new NameSingle("전투 함성 대상 멧돼지");
            GenSpawn.Spawn(wild, map.Center + new IntVec3(11, 0, 1), map);
            Track(wild, "야생 멧돼지 · 전투 함성 대상");
            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
            GenSpawn.Spawn(wall, map.Center + new IntVec3(7, 0, 9), map);
            foreach (Pawn pawn in subjects)
            {
                Pawn current = CurrentPawn(pawn);
                if (current?.Spawned != true) continue;
                current.Rotation = Rot4.East;
                current.pather?.StopDead();
                current.jobs?.EndCurrentJob(JobCondition.InterruptForced, false);
                PawnUtility.ForceWait(current, 60000, null);
            }
            Find.CameraDriver.SetRootPosAndSize(map.Center.ToVector3Shifted(), 15f);
            Select(subjects[0]);
        }

        private void Pair(Map map, PawnKindDef humanKind, string animalKind, string label, int row)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(animalKind);
            Pawn original = NewHuman(map, humanKind, label, 3, row);
            AddNativeGenes(original);
            ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Luciferium")).Ingested(original, 0f);
            TransformUtility.AddOrGetShapeshifter(original, kind);
            if (!TransformUtility.TransformToAnimal(original, kind))
                throw new InvalidOperationException("Production transform failed: " + label);
            Pawn form = SharedFormIdentity.ActiveFormFor(original);
            if (form == null) throw new InvalidOperationException("No canonical active form: " + label);
            Track(original, label + " · 변신자");
            Pawn ordinary = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedGender: Gender.Male));
            ordinary.Name = new NameSingle("일반 " + animalKind);
            GenSpawn.Spawn(ordinary, map.Center + new IntVec3(-5, 0, row), map);
            Track(ordinary, "일반 " + animalKind + " · 비교군");
            foreach (Pawn animal in new[] { form, ordinary })
            {
                ThingDef plateDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PlateArmor_Animal");
                if (plateDef == null) continue; // Baseline 75-mod profile has no optional armor pack.
                if (animal.apparel == null) throw new InvalidOperationException("Armor pack loaded without apparel tracker: " + animal);
                Apparel plate = (Apparel)ThingMaker.MakeThing(plateDef, ThingDefOf.Steel);
                animal.apparel.Wear(plate, false, false);
            }
        }

        private static Pawn NewHuman(Map map, PawnKindDef kind, string name, int x, int z)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                fixedGender: Gender.Female));
            pawn.Name = new NameSingle(name);
            // Keep race/gene/mod-owned state for a genuine installed-pack case.
            // Only random generated injuries are removed from these subjects.
            foreach (Hediff injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                pawn.health.RemoveHediff(injury);
            IntVec3 cell = map.Center + new IntVec3(x, 0, z);
            GenSpawn.Spawn(pawn, cell.Standable(map) ? cell : CellFinder.RandomClosewalkCellNear(cell, map, 4), map);
            return pawn;
        }
        private static void AddNativeGenes(Pawn pawn)
        {
            if (pawn.genes == null) throw new InvalidOperationException("Demo human has no native gene tracker.");
            foreach (string name in new[] { "FireSpew", "AnimalWarcall", "Hemogenic", "HemogenDrain", "Coagulate", "Bloodfeeder" })
            {
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(name);
                if (def != null && !pawn.genes.HasActiveGene(def)) pawn.genes.AddGene(def, false);
            }
            Gene_Hemogen hemogen = SharedFormAbilities.HemogenFor(pawn);
            if (hemogen != null) hemogen.Value = .7f;
        }
        private void Track(Pawn pawn, string name) { subjects.Add(pawn); names.Add(name); }
        private static Pawn CurrentPawn(Pawn pawn) { return SharedFormIdentity.ActiveFormFor(pawn) ?? pawn; }
        private static void Select(Pawn pawn)
        {
            Pawn current = CurrentPawn(pawn);
            if (current?.Spawned != true) return;
            Find.Selector.ClearSelection();
            Find.Selector.Select(current);
        }
        public override void GameComponentOnGUI()
        {
            if (!prepared || Current.Game == null || Find.CurrentMap == null) return;
            float x = UI.screenWidth - 325f;
            Rect box = new Rect(x, 130, 315, 95 + subjects.Count * 31);
            Widgets.DrawMenuSection(box);
            Widgets.Label(new Rect(x + 8, 136, 300, 25), "WildShift 실제 모드팩 화면 검증");
            Widgets.Label(new Rect(x + 8, 160, 300, 25), status);
            for (int i = 0; i < subjects.Count; i++)
                if (Widgets.ButtonText(new Rect(x + 8, 190 + i * 31, 298, 28), names[i])) Select(subjects[i]);
            if (Widgets.ButtonText(new Rect(x + 8, 194 + subjects.Count * 31, 298, 26), "현재 상태 기록 · 판정은 화면에서"))
                WriteEvidence("manual-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        }
        private void WriteEvidence(string name)
        {
            var lines = new List<string> { "label\tactivePawn\trace\tsourceRace\tapparel\tabilities\thealth" };
            for (int i = 0; i < subjects.Count; i++)
            {
                Pawn pawn = CurrentPawn(subjects[i]);
                lines.Add(names[i] + "\t" + pawn.GetUniqueLoadID() + "\t" + pawn.def.defName + "\t"
                    + (SharedFormIdentity.HumanFor(pawn)?.def.defName ?? "self") + "\t"
                    + string.Join("|", pawn.apparel?.WornApparel.Select(a => a.def.defName) ?? Enumerable.Empty<string>()) + "\t"
                    + string.Join("|", pawn.abilities?.AllAbilitiesForReading.Select(a => a.def.defName) ?? Enumerable.Empty<string>()) + "\t"
                    + string.Join("|", pawn.health.hediffSet.hediffs.Select(h => h.Label)));
            }
            File.WriteAllLines(Path.Combine(Boot.Output, name + ".tsv"), lines);
            File.WriteAllLines(Path.Combine(Boot.Output, "loaded-mods.tsv"), LoadedModManager.RunningModsListForReading
                .Select(m => m.PackageIdPlayerFacing + "\t" + m.RootDir));
            File.WriteAllText(Path.Combine(Boot.Output, "production-dll.txt"), typeof(TransformUtility).Assembly.Location);
        }
    }
}
