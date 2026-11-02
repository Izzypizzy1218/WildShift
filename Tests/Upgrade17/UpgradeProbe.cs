using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WildShift;

namespace WildShiftUpgradeValidation
{
    // TEST ONLY. No Pawn tick/health/ownership method is patched. A copied old
    // save is loaded by the native autostart loader and paused in FinalizeInit.
    public sealed class UpgradeProbe : GameComponent
    {
        private bool scheduled, done;
        private string Root { get { return Path.Combine(GenFilePaths.SaveDataFolderPath, "UpgradeResults"); } }
        public UpgradeProbe(Game game) { }
        public override void FinalizeInit()
        {
            Find.TickManager.Pause();
            if (scheduled) return;
            scheduled = true;
            LongEventHandler.ExecuteWhenFinished(Run);
        }
        private void Run()
        {
            if (done) return;
            done = true;
            Directory.CreateDirectory(Root);
            int phase = 0;
            var checks = new List<string>();
            try
            {
                Find.TickManager.Pause();
                XDocument input = XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath, "upgrade-inputs.xml"));
                phase = int.Parse(Value(input, "Phase"), CultureInfo.InvariantCulture);
                Require(Value(input, "GameVersion") == VersionControl.CurrentVersionString, "actual native game version", checks);
                Require(SamePath(Value(input, "GameAssemblyPath"), typeof(Pawn).Assembly.Location), "actual native game assembly path", checks);
                Require(Value(input, "GameAssemblySHA256") == Hash(typeof(Pawn).Assembly.Location), "actual native game assembly hash", checks);
                Require(SamePath(Value(input, "WildShiftPath"), typeof(TransformUtility).Assembly.Location), "actual production DLL path", checks);
                Require(Value(input, "WildShiftSHA256") == Hash(typeof(TransformUtility).Assembly.Location), "actual production DLL hash", checks);
                Require(Value(input, "FixtureSHA256") == Hash(typeof(UpgradeProbe).Assembly.Location), "actual fixture DLL hash", checks);
                string[] requested = input.Root.Element("Mods").Elements("li").Select(e => e.Value.ToLowerInvariant()).ToArray();
                string[] actual = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId.ToLowerInvariant()).ToArray();
                Require(requested.SequenceEqual(actual), "actual loaded mod IDs and order", checks);
                int initialTick = Find.TickManager.TicksGame;
                XDocument initial = Capture();
                initial.Save(Path.Combine(Root, "phase-" + phase + "-loaded.xml"));
                if (phase == 0)
                {
                    string cycleId = Value(input, "CycleTargetId", false);
                    if (!string.IsNullOrEmpty(cycleId)) Cycle(cycleId, checks);
                    XDocument expected = Capture();
                    expected.Save(Path.Combine(Root, "expected-reload.xml"));
                    Require(Find.TickManager.TicksGame == initialTick, "probe does not advance native ticks", checks);
                    GameDataSaveLoader.SaveGame("autostart");
                    Require(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")), "native isolated resave exists", checks);
                }
                else if (phase == 1)
                {
                    XDocument expected = XDocument.Load(Path.Combine(Root, "expected-reload.xml"));
                    Require(XNode.DeepEquals(expected.Root, initial.Root), "native fresh-process snapshot matches before resave", checks);
                    Require(Find.TickManager.TicksGame == initialTick, "reload probe does not advance native ticks", checks);
                }
                else throw new InvalidOperationException("Unexpected probe phase " + phase);
                WriteResult(input, phase, true, checks, null);
            }
            catch (Exception error)
            {
                checks.Add("FAIL: " + error.Message);
                WriteResult(null, phase, false, checks, error.ToString());
            }
            finally
            {
                GenerationTrace.Save(Path.Combine(Root, "phase-" + phase + "-generation-trace.xml"));
                Application.Quit();
            }
        }
        private static string Value(XDocument input, string name, bool required = true)
        {
            XElement node = input.Root.Element(name);
            if (node == null && required) throw new InvalidOperationException("Missing input " + name);
            return node != null ? node.Value : null;
        }
        private static bool SamePath(string expected, string actual)
        {
            return string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase);
        }
        private static void Require(bool ok, string label, List<string> checks)
        {
            checks.Add((ok ? "PASS: " : "FAIL: ") + label);
            if (!ok) throw new InvalidOperationException(label);
        }
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        private static string Id(ILoadReferenceable obj) { return obj != null ? obj.GetUniqueLoadID() : ""; }
        private static string Number(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static List<Pawn> Pawns()
        {
            var queue = new Queue<Pawn>(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToList());
            if (Find.WorldObjects != null)
                foreach (Caravan caravan in Find.WorldObjects.Caravans)
                    foreach (Pawn pawn in caravan.PawnsListForReading) queue.Enqueue(pawn);
            var seen = new HashSet<Pawn>();
            while (queue.Count != 0)
            {
                Pawn pawn = queue.Dequeue();
                if (pawn == null || !seen.Add(pawn)) continue;
                Pawn original = TransformUtility.TryGetTransformedComp(pawn)?.StoredPawn;
                if (original != null) queue.Enqueue(original);
                Pawn stored = TransformUtility.TryGetShapeshifterComp(pawn)?.FormStorage.StoredForm;
                if (stored != null) queue.Enqueue(stored);
            }
            return seen.OrderBy(p => Id(p), StringComparer.Ordinal).ToList();
        }
        private static bool Relevant(Pawn pawn)
        {
            return TransformUtility.TryGetShapeshifterComp(pawn) != null || TransformUtility.TryGetTransformedComp(pawn) != null;
        }
        private static XElement ThingState(Thing thing, string slot)
        {
            return new XElement("Thing", new XAttribute("id", Id(thing)), new XAttribute("slot", slot),
                new XAttribute("def", thing.def.defName), new XAttribute("stuff", thing.Stuff?.defName ?? ""),
                new XAttribute("count", thing.stackCount), new XAttribute("hp", thing.HitPoints));
        }
        private static XElement PawnState(Pawn pawn)
        {
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(pawn);
            HediffComp_Shapeshifter shifter = TransformUtility.TryGetShapeshifterComp(pawn);
            var state = new XElement("Pawn", new XAttribute("id", Id(pawn)),
                new XAttribute("def", pawn.def.defName), new XAttribute("kind", pawn.kindDef?.defName ?? ""),
                new XAttribute("name", pawn.Name?.ToStringFull ?? ""), new XAttribute("dead", pawn.Dead),
                new XAttribute("destroyed", pawn.Destroyed), new XAttribute("downed", pawn.Downed),
                new XAttribute("spawned", pawn.Spawned), new XAttribute("faction", Id(pawn.Faction)),
                new XAttribute("holder", pawn.ParentHolder?.GetType().FullName ?? ""),
                new XAttribute("map", pawn.MapHeld?.uniqueID.ToString(CultureInfo.InvariantCulture) ?? ""),
                new XAttribute("original", Id(comp?.StoredPawn)), new XAttribute("cachedForm", Id(shifter?.FormStorage.StoredForm)),
                new XAttribute("assignedKind", shifter?.assignedKind?.defName ?? ""),
                new XAttribute("generatedHealthSanitized", comp == null ? "" : Convert.ToString(AccessTools.Field(typeof(HediffComp_Transformed), "generatedHealthSanitized").GetValue(comp), CultureInfo.InvariantCulture)));
            state.Add(new XElement("Health", pawn.health.hediffSet.hediffs.OrderBy(h => Id(h), StringComparer.Ordinal).Select(h =>
                new XElement("Hediff", new XAttribute("id", Id(h)), new XAttribute("def", h.def.defName),
                    new XAttribute("class", h.GetType().FullName), new XAttribute("severity", Number(h.Severity)),
                    new XAttribute("age", h.ageTicks), new XAttribute("part", h.Part == null ? -1 : pawn.RaceProps.body.AllParts.IndexOf(h.Part)),
                    new XAttribute("disappearsTicks", h.TryGetComp<HediffComp_Disappears>()?.ticksToDisappear.ToString(CultureInfo.InvariantCulture) ?? "")))));
            var things = new List<XElement>();
            if (pawn.apparel != null) things.AddRange(pawn.apparel.WornApparel.Select(t => ThingState(t, "apparel")));
            if (pawn.equipment != null) things.AddRange(pawn.equipment.AllEquipmentListForReading.Select(t => ThingState(t, "equipment")));
            if (pawn.inventory != null) things.AddRange(pawn.inventory.innerContainer.Select(t => ThingState(t, "inventory")));
            if (pawn.carryTracker?.CarriedThing != null) things.Add(ThingState(pawn.carryTracker.CarriedThing, "carried"));
            state.Add(new XElement("Possessions", things.OrderBy(t => (string)t.Attribute("id"), StringComparer.Ordinal)));
            state.Add(new XElement("Skills", pawn.skills?.skills.OrderBy(s => s.def.defName, StringComparer.Ordinal).Select(s =>
                new XElement("Skill", new XAttribute("def", s.def.defName), new XAttribute("level", s.levelInt), new XAttribute("observedLevel", s.Level),
                    new XAttribute("passion", s.passion), new XAttribute("xp", Number(s.xpSinceLastLevel)),
                    new XAttribute("dailyXp", Number(s.xpSinceMidnight))))));
            state.Add(new XElement("Abilities", pawn.abilities?.AllAbilitiesForReading.OrderBy(a => Id(a), StringComparer.Ordinal).Select(a =>
                new XElement("Ability", new XAttribute("id", Id(a)), new XAttribute("def", a.def.defName),
                    new XAttribute("caster", Id(a.pawn)), new XAttribute("cooldown", a.CooldownTicksRemaining),
                    new XAttribute("charges", a.RemainingCharges)))));
            state.Add(new XElement("Genes", pawn.genes?.GenesListForReading.OrderBy(g => Id(g), StringComparer.Ordinal).Select(g =>
                new XElement("Gene", new XAttribute("id", Id(g)), new XAttribute("def", g.def.defName), new XAttribute("active", g.Active),
                    new XAttribute("resourceValue", g is Gene_Resource resource ? Number(resource.Value) : "")))));
            return state;
        }
        private static XDocument Capture()
        {
            List<Pawn> all = Pawns();
            List<Pawn> relevant = all.Where(Relevant).ToList();
            if (relevant.Count == 0) throw new InvalidOperationException("No WildShift pawns found; this save cannot establish migration coverage.");
            string[] ids = relevant.Select(Id).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new InvalidOperationException("Duplicate relevant pawn load IDs.");
            return new XDocument(new XElement("Snapshot", new XAttribute("tick", Find.TickManager.TicksGame),
                new XElement("Pawns", relevant.Select(PawnState)),
                new XElement("Caravans", Find.WorldObjects.Caravans.OrderBy(c => Id(c), StringComparer.Ordinal).Select(c =>
                    new XElement("Caravan", new XAttribute("id", Id(c)), c.PawnsListForReading.OrderBy(p => Id(p), StringComparer.Ordinal).Select(p => new XElement("Pawn", Id(p))))))));
        }
        private static void Cycle(string id, List<string> checks)
        {
            Pawn pawn = Pawns().SingleOrDefault(p => Id(p) == id || p.ThingID == id);
            Require(pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed && pawn.Faction?.IsPlayer == true, "explicit cycle target is a living player pawn on a map", checks);
            Pawn original = TransformUtility.TryGetTransformedComp(pawn)?.StoredPawn;
            if (original != null)
            {
                PawnKindDef kind = TransformUtility.TryGetTransformedComp(pawn).sourceKind ?? pawn.kindDef;
                Require(TransformUtility.RevertToHuman(pawn, false) == original, "old active form returns the exact original", checks);
                Require(TransformUtility.TransformToAnimal(original, kind), "original shifts through production path", checks);
                Require(SharedFormIdentity.ActiveFormFor(original) == pawn, "old active body is reused exactly", checks);
            }
            else
            {
                PawnKindDef kind = TransformUtility.TryGetShapeshifterComp(pawn)?.assignedKind;
                Require(kind != null && TransformUtility.TransformToAnimal(pawn, kind), "old human uses its assigned production form", checks);
                Pawn form = SharedFormIdentity.ActiveFormFor(pawn);
                Require(form != null && TransformUtility.RevertToHuman(form, false) == pawn, "new form returns exact old human", checks);
            }
        }
        private void WriteResult(XDocument input, int phase, bool passed, List<string> checks, string error)
        {
            var result = new XDocument(new XElement("Result", new XAttribute("phase", phase), new XAttribute("statePassed", passed),
                new XElement("GameVersion", VersionControl.CurrentVersionString),
                new XElement("ProductionPath", typeof(TransformUtility).Assembly.Location),
                new XElement("ProductionSHA256", Hash(typeof(TransformUtility).Assembly.Location)),
                new XElement("SourceSerializedBaselineCompared", false),
                new XElement("Checks", checks.Select(c => new XElement("li", c))),
                new XElement("Exception", error ?? "")));
            result.Save(Path.Combine(Root, "phase-" + phase + "-result.xml"));
        }
    }
}
