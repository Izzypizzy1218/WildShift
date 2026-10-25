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

namespace WildShiftSharedStateValidation
{
    [StaticConstructorOnStartup]
    public static class Boot
    {
        public static readonly string Output = Path.Combine(GenFilePaths.SaveDataFolderPath, "SharedStateResults");
        static Boot()
        {
            Directory.CreateDirectory(Output);
            Application.runInBackground = true;
            new Harmony("wildshift.tests.sharedstate17").Patch(AccessTools.Method(typeof(Root_Play), "SetupForQuickTestPlay"),
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

    public sealed class FixtureImplantProperties : HediffCompProperties
    {
        public FixtureImplantProperties() { compClass = typeof(FixtureImplantComp); }
    }

    public sealed class FixtureImplantComp : HediffComp
    {
        public int value;
        public override void CompExposeData() { Scribe_Values.Look(ref value, "fixtureState", 0); }
    }

    // TEST ONLY: no native gameplay method is replaced. All state operations
    // use native pawns, ingestion, skills, health, ticks, caravans and saves.
    public sealed class Regression : GameComponent
    {
        private bool ready, done;
        private int frames, stage, processPhase, failures, implantPartIndex, activeSavedDrugAge;
        private Pawn original, companion, form;
        private Hediff implant;
        private Caravan caravan;
        private string originalId, formId, implantId, originalName, activeSavedDrugId;
        private float learnedXp;
        private readonly List<string> results = new List<string>();
        public Regression(Game game) { }

        public override void FinalizeInit() { ready = true; processPhase = stage; }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "sharedStateStage", 0);
            Scribe_Values.Look(ref originalId, "sharedOriginalId");
            Scribe_Values.Look(ref formId, "sharedFormId");
            Scribe_Values.Look(ref implantId, "sharedImplantId");
            Scribe_Values.Look(ref originalName, "sharedOriginalName");
            Scribe_Values.Look(ref implantPartIndex, "sharedImplantPartIndex", -1);
            Scribe_Values.Look(ref learnedXp, "sharedLearnedXp", 0f);
            Scribe_Values.Look(ref activeSavedDrugId, "sharedActiveDrugId");
            Scribe_Values.Look(ref activeSavedDrugAge, "sharedActiveDrugAge", 0);
            Scribe_References.Look(ref original, "sharedOriginal");
            Scribe_References.Look(ref companion, "sharedCompanion");
            Scribe_References.Look(ref form, "sharedForm");
            Scribe_References.Look(ref implant, "sharedImplant");
            Scribe_References.Look(ref caravan, "sharedCaravan");
        }
        public override void GameComponentUpdate()
        {
            if (!ready || done || LongEventHandler.AnyEventNowOrWaiting || ++frames < 45) return;
            try
            {
                Find.TickManager.Pause();
                if (stage == 0) FirstPass();
                else if (stage == 1) ReloadPass();
                else if (stage == 2) ActiveReloadPass();
                else throw new InvalidOperationException("Unexpected shared-state fixture stage " + stage);
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
            Require(ModsConfig.RoyaltyActive && ModsConfig.IdeologyActive && ModsConfig.BiotechActive
                && ModsConfig.AnomalyActive && ModsConfig.OdysseyActive, "all five DLCs are active in the native game");
            Map map = Find.CurrentMap;
            Require(map != null, "quicktest initializes a native map");
            ClearMap(map);
            original = NewHuman(map, "SharedOriginal", 0, Gender.Male);
            companion = NewHuman(map, "SharedCompanion", -3, Gender.Female);
            originalId = original.GetUniqueLoadID();
            originalName = original.Name.ToStringFull;
            original.relations.AddDirectRelation(PawnRelationDefOf.Spouse, companion);
            original.needs.mood.thoughts.memories.TryGainMemory(Married(), companion);
            Require(HasMemory(), "native spouse memory is seeded on the original human");
            SkillRecord skill = original.skills.GetSkill(SkillDefOf.Melee);
            skill.Level = 8;
            skill.passion = Passion.Major;
            skill.xpSinceLastLevel = 200f;
            skill.xpSinceMidnight = 0f;
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = 17;
            Require(original.inventory.innerContainer.TryAdd(silver), "original inventory carries exactly 17 silver");
            Dose(original);
            Require(Drug(original) != null && ChemicalNeed() != null, "native luciferium ingestion seeds canonical drug and chemical need");
            Require(SharedFormHealth.IsSupported(Drug(original)), "native luciferium effect is supported by the production bridge");
            TransformUtility.AddOrGetShapeshifter(original, Kind());
            Require(TransformUtility.TransformToAnimal(original, Kind()), "production transformation creates the first active animal form");
            form = Body(map);
            formId = form.GetUniqueLoadID();
            Check(SharedFormIdentity.HumanFor(form) == original && SharedFormIdentity.ActiveFormFor(original) == form,
                "production identity lookups resolve the exact original and active form");
            Check(form.skills is SharedPawnSkillTracker && ReferenceEquals(form.skills.GetSkill(SkillDefOf.Melee), skill),
                "native animal skill lookup returns the canonical original skill record");
            form.skills.Learn(SkillDefOf.Melee, 100f, direct: true, ignoreLearnRate: true);
            learnedXp = skill.xpSinceLastLevel;
            Check(Near(learnedXp, 300f) && skill.Level == 8 && skill.passion == Passion.Major,
                "native animal learning changes the original XP once while retaining level and passion");
            CheckIdentity();
            CheckPureStats();
            CheckProjections();

            List<BodyPartRecord> legs = form.RaceProps.body.AllParts.Where(p => p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)).ToList();
            Require(legs.Count >= 3, "animal body has distinct limbs for implant, wound and missing-part probes");
            implant = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("WildShift_TestStatefulAnimalImplant"), form, legs[0]);
            ((HediffWithComps)implant).TryGetComp<FixtureImplantComp>().value = 731;
            form.health.AddHediff(implant, legs[0]);
            implantId = implant.GetUniqueLoadID();
            implantPartIndex = form.RaceProps.body.AllParts.IndexOf(implant.Part);
            form.TakeDamage(new DamageInfo(DamageDefOf.Cut, 2f, 999f, -1f, null, legs[1]));
            form.health.AddHediff(HediffDefOf.MissingBodyPart, legs[2]);
            HealthUtility.AdjustSeverity(form, HediffDefOf.BloodLoss, .15f);
            Require(form.health.hediffSet.hediffs.Any(h => h is Hediff_Injury)
                && form.health.hediffSet.hediffs.Any(h => h.def == HediffDefOf.BloodLoss), "native animal wound and blood loss are seeded");
            Pawn returned = TransformUtility.RevertToHuman(form, false);
            Require(returned == original && original.Spawned, "production return restores the same human on the map");
            Check(!form.Destroyed && AnimalFormStorage.IsStored(form)
                && TransformUtility.TryGetTransformedComp(form)?.StoredPawn == null,
                "inactive animal is stored without owning the active human or a deep-save cycle");
            Check(TransformUtility.TryGetShapeshifterComp(original).FormStorage.StoredForm == form,
                "human's production holder owns exactly the same cached animal");
            Check(form.health.hediffSet.hediffs.Contains(implant) && ImplantState() == 731,
                "cached animal retains the exact stateful implant object before saving");
            Check(form.health.hediffSet.hediffs.Any(h => h is Hediff_Injury), "injury remains on the dormant animal until the next transformation");
            CheckIdentity();
            Check(Near(original.skills.GetSkill(SkillDefOf.Melee).xpSinceLastLevel, learnedXp), "return preserves canonical learned XP");
            Check(Drug(original) != null && ChemicalNeed() != null, "return keeps canonical luciferium and its chemical need");
            Check(Inventory() == 17, "return preserves the original silver inventory");
            Require(failures == 0, "initial shared-state checks pass before saving");
            stage = 1;
            GameDataSaveLoader.SaveGame("autostart");
            Check(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")), "native save contains the active human and deep-stored cached animal");
            Finish("save-ready");
        }

        private void ReloadPass()
        {
            Require(original != null && companion != null && form != null && implant != null,
                "fresh-process native reload resolves original, spouse, cached body and implant cross-references");
            Check(original.GetUniqueLoadID() == originalId && form.GetUniqueLoadID() == formId,
                "native reload preserves both exact pawn IDs");
            Check(implant.GetUniqueLoadID() == implantId && form.health.hediffSet.hediffs.Contains(implant)
                && implant.Part == form.RaceProps.body.AllParts[implantPartIndex] && ImplantState() == 731,
                "native reload preserves implant ID, exact body part and custom serialized state 731");
            Require(original.Spawned && AnimalFormStorage.IsStored(form)
                && TransformUtility.TryGetTransformedComp(form)?.StoredPawn == null,
                "native reload retains the acyclic active-human and cached-animal ownership");
            CheckIdentity();
            Check(Near(original.skills.GetSkill(SkillDefOf.Melee).xpSinceLastLevel, learnedXp)
                && original.skills.GetSkill(SkillDefOf.Melee).passion == Passion.Major, "native reload preserves learned XP and passion");
            Require(TransformUtility.TransformToAnimal(original, Kind()), "production transformation reuses the reloaded cached body");
            Check(Body(Find.CurrentMap) == form && form.GetUniqueLoadID() == formId && !AnimalFormStorage.IsStored(form),
                "active form is the same cached animal, not a newly generated replacement");
            Check(form.health.hediffSet.hediffs.Contains(implant) && ImplantState() == 731 && implant.Part == form.RaceProps.body.AllParts[implantPartIndex],
                "reactivation preserves the exact implanted part and component state");
            Check(!form.health.hediffSet.hediffs.Any(h => h is Hediff_Injury || h.def == HediffDefOf.BloodLoss),
                "reactivation clears ordinary wounds and blood loss");
            Check(!form.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Any(h => !UnderImplant(h.Part)),
                "reactivation restores unrelated missing parts while respecting the installed prosthesis");
            Check(ReferenceEquals(form.skills.GetSkill(SkillDefOf.Melee), original.skills.GetSkill(SkillDefOf.Melee)),
                "reactivation rebinds native skill lookups to the original record");
            CheckIdentity();
            CheckProjections();
            CheckDrugTicks(300, "map");
            Need_Chemical need = ChemicalNeed();
            Require(need != null, "stored original chemical need remains available");
            need.CurLevel = .4f;
            Dose(form);
            Check(ChemicalNeed().CurLevel > .4f && original.health.hediffSet.hediffs.Count(h => h.def.defName == "LuciferiumAddiction") == 1,
                "native animal ingestion replenishes the original chemical need without duplicate addiction");
            Check(!form.health.hediffSet.hediffs.Any(h => h.def.defName == "LuciferiumHigh" || h.def.defName == "LuciferiumAddiction")
                && !form.needs.AllNeeds.OfType<Need_Chemical>().Any(), "animal ingestion does not create a separate animal drug or chemical need");
            CheckProjections();
            for (int i = 0; i < 2; i++)
            {
                Require(TransformUtility.RevertToHuman(form, false) == original, "repeated return restores the canonical human " + i);
                Check(AnimalFormStorage.IsStored(form) && TransformUtility.TryGetTransformedComp(form).StoredPawn == null,
                    "repeated return preserves one-way dormant ownership " + i);
                Require(TransformUtility.TransformToAnimal(original, Kind()), "repeated transformation succeeds " + i);
                Check(Body(Find.CurrentMap) == form && TransformUtility.TryGetShapeshifterComp(original).FormStorage.StoredForm == null,
                    "repeated transformation has one active body and no nested copy " + i);
            }
            Check(form.health.hediffSet.hediffs.Contains(implant) && ImplantState() == 731, "repeated cycles preserve the original stateful implant");
            SharedHealthEngineChecks.Run(Find.CurrentMap, Check);
            caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(new[] { companion, form }, Faction.OfPlayer,
                Find.CurrentMap.Tile, Find.CurrentMap.Tile, PlanetTile.Invalid, false);
            Require(caravan != null && caravan.Spawned && caravan.ContainsPawn(form), "native departure creates a caravan with the active form");
            CheckDrugTicks(300, "caravan");
            activeSavedDrugId = Drug(original).GetUniqueLoadID();
            activeSavedDrugAge = Drug(original).ageTicks;
            Require(failures == 0, "active shared-state and native health checks pass before saving");
            stage = 2;
            GameDataSaveLoader.SaveGame("autostart");
            Check(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")),
                "native active-form save contains the caravan, stored human and source drug/projection references");
            Finish("active-save-ready");
        }

        private void ActiveReloadPass()
        {
            Require(caravan != null && caravan.Spawned && original != null && companion != null && form != null && implant != null,
                "third-process native reload resolves the active caravan, both bodies, spouse and implant");
            Require(caravan.ContainsPawn(form) && caravan.ContainsPawn(companion) && !form.Spawned
                && TransformUtility.TryGetTransformedComp(form)?.StoredPawn == original,
                "active-form reload retains the exact original inside the caravan animal");
            Check(original.GetUniqueLoadID() == originalId && form.GetUniqueLoadID() == formId,
                "active-form reload preserves both original pawn IDs");
            Check(implant.GetUniqueLoadID() == implantId && form.health.hediffSet.hediffs.Contains(implant)
                && implant.Part == form.RaceProps.body.AllParts[implantPartIndex] && ImplantState() == 731,
                "active-form reload preserves implant ID, exact body part and custom state 731");
            Check(SharedFormIdentity.HumanFor(form) == original && SharedFormIdentity.ActiveFormFor(original) == form
                && TransformUtility.TryGetShapeshifterComp(original).FormStorage.StoredForm == null,
                "active-form reload rebinds identity lookups with one-way active ownership");
            Check(form.skills is SharedPawnSkillTracker
                && ReferenceEquals(form.skills.GetSkill(SkillDefOf.Melee), original.skills.GetSkill(SkillDefOf.Melee)),
                "active-form reload binds native skill lookups to one canonical human record");
            Hediff drug = Drug(original);
            Check(drug != null && drug.GetUniqueLoadID() == activeSavedDrugId && drug.pawn == original
                && drug.ageTicks >= activeSavedDrugAge && drug.ageTicks <= activeSavedDrugAge + 60,
                "active-form reload preserves the canonical source drug ID, owner and elapsed state");
            CheckProjections();
            Check(form.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                .Count(p => ReferenceEquals(p.source, drug)) == 1,
                "active-form reload resolves one projection reference to the exact canonical drug");
            CheckIdentity();
            CheckDrugTicks(300, "reloaded caravan");
            Require(TransformUtility.RevertToHuman(form, false) == original, "off-map production return restores the original human");
            Check(caravan.ContainsPawn(original) && !original.Spawned && WorldPawnsUtility.IsWorldPawn(original),
                "off-map return retains the exact world-registered caravan member");
            Check(AnimalFormStorage.IsStored(form) && !form.Destroyed && TransformUtility.TryGetTransformedComp(form).StoredPawn == null,
                "off-map return preserves the same cached body without an ownership cycle");
            Check(Inventory() == 17 && ImplantState() == 731, "caravan return preserves exact inventory and implant state");
            CheckIdentity();
            Finish("complete");
        }

        private void CheckPureStats()
        {
            TraitDef speed = DefDatabase<TraitDef>.GetNamed("SpeedOffset");
            foreach (Trait existing in original.story.traits.allTraits.Where(t => t.def == speed).ToList()) original.story.traits.RemoveTrait(existing);
            GeneDef quick = DefDatabase<GeneDef>.GetNamed("MoveSpeed_Quick");
            Gene existingGene = original.genes.GetGene(quick);
            if (existingGene != null) original.genes.RemoveGene(existingGene);
            float baseline = Speed();
            Gene added = original.genes.AddGene(quick, false);
            Check(Speed() > baseline + .01f, "adding the original's native quick-movement gene changes the animal stat immediately");
            original.genes.RemoveGene(added);
            Check(Near(Speed(), baseline), "removing the original gene restores the animal stat in the same tick");
            var trait = new Trait(speed, 1);
            original.story.traits.GainTrait(trait);
            Check(Speed() > baseline + .01f, "adding the original's native fast-walker trait changes the animal stat immediately");
            original.story.traits.RemoveTrait(trait);
            Check(Near(Speed(), baseline), "removing the original trait restores the animal stat in the same tick");
        }

        private void CheckDrugTicks(int count, string location)
        {
            Hediff drug = Drug(original);
            Need_Chemical need = ChemicalNeed();
            Require(drug != null && need != null, location + " has the canonical source drug and chemical need");
            int before = drug.ageTicks;
            float level = need.CurLevel;
            Thought_Memory memory = Memory();
            int memoryAge = memory != null ? memory.age : -1;
            TickNative(count);
            int elapsed = drug.ageTicks - before;
            Check(elapsed >= count - 50 && elapsed <= count + 50,
                location + " native ticks advance source drug once (" + elapsed + " for " + count + ")");
            Check(need.CurLevel < level && level - need.CurLevel < .01f, location + " native ticks decrease the original chemical need once");
            Check(memory != null && memory.age > memoryAge && memory.age - memoryAge <= count + 150,
                location + " native ticks progress the original memory without duplicate aging");
            CheckProjections();
        }

        private void CheckProjections()
        {
            List<Hediff> supported = original.health.hediffSet.hediffs.Where(SharedFormHealth.IsSupported).ToList();
            List<Hediff_SharedFormDrugEffect> projected = form.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().ToList();
            Check(projected.Count == supported.Count && projected.Select(p => p.source).Distinct().Count() == projected.Count
                && supported.All(h => projected.Any(p => p.source == h)), "active form has one disposable projection per canonical supported drug");
            Check(projected.All(p => p.pawn == form && p.source.pawn == original && !ReferenceEquals(p, p.source)),
                "drug projections keep source and animal health objects separate");
        }

        private void CheckIdentity()
        {
            Check(original.GetUniqueLoadID() == originalId && original.Name.ToStringFull == originalName, "canonical original identity and name are preserved");
            Check(original.relations.DirectRelationExists(PawnRelationDefOf.Spouse, companion) && HasMemory(),
                "canonical spouse relation and original memory are preserved");
        }

        private bool UnderImplant(BodyPartRecord part)
        {
            for (BodyPartRecord p = part; p != null; p = p.parent) if (p == implant.Part) return true;
            return false;
        }
        private int ImplantState() { return ((HediffWithComps)implant).TryGetComp<FixtureImplantComp>().value; }
        private int Inventory() { return original.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Silver); }
        private float Speed() { return form.GetStatValue(StatDefOf.MoveSpeed, cacheStaleAfterTicks: 10000); }
        private Thought_Memory Memory() { return original.needs.mood.thoughts.memories.Memories.FirstOrDefault(m => m.def == Married()); }
        private bool HasMemory() { return Memory() != null; }
        private Need_Chemical ChemicalNeed() { return original.needs.AllNeeds.OfType<Need_Chemical>().FirstOrDefault(n => n.def.defName == "Chemical_Luciferium"); }
        private static ThoughtDef Married() { return DefDatabase<ThoughtDef>.GetNamed("GotMarried"); }
        private static PawnKindDef Kind() { return DefDatabase<PawnKindDef>.GetNamed("Warg"); }
        private static Hediff Drug(Pawn human) { return human.health.hediffSet.hediffs.FirstOrDefault(h => h.def.defName == "LuciferiumHigh"); }
        private static bool Near(float a, float b) { return Math.Abs(a - b) < .002f; }
        private static void Dose(Pawn pawn) { ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Luciferium")).Ingested(pawn, 0f); }
        private Pawn Body(Map map) { return map.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == original); }
        private static Pawn NewHuman(Map map, string name, int offset, Gender gender)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                fixedGender: gender, forcedXenotype: XenotypeDefOf.Baseliner));
            pawn.Name = new NameSingle(name);
            GenSpawn.Spawn(pawn, map.Center + new IntVec3(offset, 0, 0), map);
            return pawn;
        }
        private static void ClearMap(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                pawn.DeSpawn();
                if (pawn.Faction == Faction.OfPlayer) pawn.SetFaction(null);
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            foreach (Thing thing in map.listerThings.AllThings.ToList()) if (thing.def.destroyable) thing.Destroy();
            foreach (IntVec3 cell in map.AllCells) { map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); map.fogGrid.Unfog(cell); }
        }
        private static void TickNative(int ticks) { for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick(); }
        private void Require(bool passed, string label) { Check(passed, label); if (!passed) throw new InvalidOperationException("Fixture prerequisite: " + label); }
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
            var data = new {
                Phase = phase, Passed = failures == 0, Failures = failures,
                WildShiftPath = typeof(TransformUtility).Assembly.Location, WildShiftSHA256 = Boot.Hash(typeof(TransformUtility).Assembly.Location),
                FixtureSHA256 = Boot.Hash(typeof(Boot).Assembly.Location),
                Mods = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray(), Results = results.ToArray(),
                Scope = "Actual initialized 1.7 game with all five DLCs, production transformations, native ingestion/learning/ticks/caravans, native death/scar-healing probes and three-process inactive/active full save reload. No gameplay result is mocked; no visual compatibility claim."
            };
            File.WriteAllText(Path.Combine(Boot.Output, "phase-" + processPhase + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            Application.Quit();
        }
    }
}
