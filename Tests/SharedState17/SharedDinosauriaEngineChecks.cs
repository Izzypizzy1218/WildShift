using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: observe the real mod loader and production form generation.
    // The installed Dinosauria XML is neither copied into WildShift nor reloaded here.
    public static class SharedDinosauriaEngineChecks
    {
        private const string Package = "spincrus.dinosauria";

        public static void Run(Map map, Action<bool,string> check)
        {
            ModContentPack pack = LoadedModManager.RunningModsListForReading
                .SingleOrDefault(m => m.PackageId == Package);
            if (pack == null)
            {
                check(true, "dinosauria/optional pack is absent; provenance checks skipped");
                return;
            }

            string version = VersionControl.CurrentVersionStringWithoutBuild;
            string selected = Path.Combine(pack.RootDir, version);
            Require(Directory.Exists(selected), "dinosauria/current installed pack contains " + version + " content", check);
            check(pack.foldersToLoadDescendingOrder.Any(p => SamePath(p, selected))
                && !pack.foldersToLoadDescendingOrder.Any(p => IsOtherVersion(p, version)),
                "dinosauria/native loader selects the current version folder: "
                    + string.Join(";", pack.foldersToLoadDescendingOrder));

            List<PawnKindDef> loaded = DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(k => k.modContentPack == pack).ToList();
            Require(loaded.Count > 0, "dinosauria/native database contains pack-owned pawn kinds", check);
            foreach (PawnKindDef kind in loaded)
            {
                check(kind.race != null && kind.race.modContentPack == pack
                    && ReferenceEquals(DefDatabase<PawnKindDef>.GetNamed(kind.defName), kind)
                    && ReferenceEquals(DefDatabase<ThingDef>.GetNamed(kind.race.defName), kind.race),
                    "dinosauria/loaded kind and race have canonical current-pack ownership: " + kind.defName);
            }
            var eligible = new HashSet<PawnKindDef>(AnimalPool.EligibleAnimals().Where(k => k.modContentPack == pack));
            check(eligible.Count > 0 && eligible.SetEquals(loaded.Where(AnimalPool.IsEligible)),
                "dinosauria/form pool contains the eligible loaded definitions without a version-specific whitelist");

            PawnKindDef triceratops = loaded.Single(k => k.defName == "Triceratops");
            Require(eligible.Contains(triceratops), "dinosauria/loaded Triceratops is available as a form", check);
            GenerateAndReuse(map, triceratops, check);
        }

        private static void GenerateAndReuse(Map map, PawnKindDef kind, Action<bool,string> check)
        {
            Pawn human = null, ordinary = null, active = null;
            try
            {
                human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                    fixedGender: Gender.Male, fixedBiologicalAge: 25f, forcedXenotype: XenotypeDefOf.Baseliner));
                human.Name = new NameSingle("DinosauriaCurrentVersion");
                human.health.RemoveAllHediffs();
                foreach (Trait trait in human.story.traits.allTraits.ToList()) human.story.traits.RemoveTrait(trait);
                foreach (Gene gene in human.genes.GenesListForReading.ToList()) human.genes.RemoveGene(gene);
                foreach (Apparel apparel in human.apparel.WornApparel.ToList()) human.apparel.Remove(apparel);
                human.equipment.DestroyAllEquipment();
                GenSpawn.Spawn(human, CellFinder.StandableCellNear(map.Center, map, 12f), map);
                Require(human.drafter != null, "dinosauria/native spawn initializes the person's draft controller", check);
                human.drafter.Drafted = true;

                ordinary = PawnGenerator.GeneratePawn(AnimalFormGender.CreateRequest(human, kind));
                ordinary.health.RemoveAllHediffs();
                ordinary.ageTracker.AgeBiologicalTicks = 2L * GenDate.TicksPerYear;
                TransformUtility.AddOrGetShapeshifter(human, kind);
                Require(TransformUtility.TransformToAnimal(human, kind),
                    "dinosauria/production transformation generates the current loaded Triceratops", check);
                active = SharedFormIdentity.ActiveFormFor(human);
                Require(active != null, "dinosauria/production transformation retains its exact original human", check);
                active.ageTracker.AgeBiologicalTicks = ordinary.ageTracker.AgeBiologicalTicks;
                CheckBody(active, ordinary, kind, "fresh", check);

                Pawn originalBody = active;
                Require(TransformUtility.RevertToHuman(active, false) == human,
                    "dinosauria/return preserves the original person", check);
                active = null;
                Require(TransformUtility.TransformToAnimal(human, kind),
                    "dinosauria/cached form reactivates through production transformation", check);
                active = SharedFormIdentity.ActiveFormFor(human);
                check(active == originalBody, "dinosauria/the preserved form is reused without replacing its identity");
                CheckBody(active, ordinary, kind, "cached", check);
            }
            finally
            {
                if (human != null && !human.Destroyed)
                {
                    Pawn form = SharedFormIdentity.ActiveFormFor(human);
                    if (form != null) TransformUtility.RevertToHuman(form, false);
                    if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
                }
                if (ordinary != null && !ordinary.Destroyed) ordinary.Destroy(DestroyMode.Vanish);
            }
        }

        private static void CheckBody(Pawn body, Pawn ordinary, PawnKindDef kind, string phase, Action<bool,string> check)
        {
            Require(body != null, "dinosauria/" + phase + " active form exists", check);
            check(ReferenceEquals(body.kindDef, kind) && ReferenceEquals(body.def, kind.race)
                && ReferenceEquals(body.RaceProps, kind.race.race)
                && ReferenceEquals(body.RaceProps.body, DefDatabase<BodyDef>.GetNamed(kind.race.race.body.defName)),
                "dinosauria/" + phase + " form uses current canonical kind, race, properties and anatomy");
            check(ReferenceEquals(body.def.statBases, kind.race.statBases)
                && ReferenceEquals(body.def.tools, kind.race.tools)
                && ReferenceEquals(body.def.comps, kind.race.comps)
                && kind.lifeStages.Contains(body.ageTracker.CurKindLifeStage),
                "dinosauria/" + phase + " full stat/tool/comp definitions and life stage come from the active definition");
            check(body.AllComps.Count == ordinary.AllComps.Count
                && body.AllComps.All(c => kind.race.comps.Contains(c.props)),
                "dinosauria/" + phase + " instantiated native components match the current race definition");
            var tools = new HashSet<Tool>(body.VerbTracker.AllVerbs.Where(v => v.tool != null).Select(v => v.tool));
            var normalTools = new HashSet<Tool>(ordinary.VerbTracker.AllVerbs.Where(v => v.tool != null).Select(v => v.tool));
            check(tools.Count > 0 && tools.SetEquals(normalTools) && tools.All(t => kind.race.tools.Contains(t)),
                "dinosauria/" + phase + " native attack verbs use the same active-definition tools as an ordinary Triceratops");
            foreach (StatDef stat in new[] { StatDefOf.MoveSpeed, StatDefOf.ArmorRating_Sharp, StatDefOf.ArmorRating_Blunt })
            {
                float transformed = body.GetStatValue(stat), normal = ordinary.GetStatValue(stat);
                check(Math.Abs(transformed - normal) < .001f,
                    "dinosauria/" + phase + " clean baseline " + stat.defName + " matches native same-kind animal: "
                        + transformed.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            check(Math.Abs(body.BodySize - ordinary.BodySize) < .001f
                && Math.Abs(body.HealthScale - ordinary.HealthScale) < .001f,
                "dinosauria/" + phase + " body size and health scale match the current native species/life stage");
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsOtherVersion(string folder, string version)
        {
            string name = new DirectoryInfo(folder).Name;
            return (name == "1.6" || name == "1.7") && name != version;
        }
        private static void Require(bool value, string label, Action<bool,string> check)
        {
            check(value, label);
            if (!value) throw new InvalidOperationException(label);
        }
    }
}
