using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Native ingestion, capacities and stats are evaluated directly.
    // An ordinary animal of the exact same race supplies the native control;
    // the fixture never replaces a gameplay method or a calculated result.
    public static class SharedPassiveHealthEngineChecks
    {
        private static readonly string[] statNames = { "MoveSpeed", "ImmunityGainSpeed", "InjuryHealingFactor" };

        public static void Run(Map map, Action<bool, string> check)
        {
            Pawn human = null, control = null;
            try
            {
                human = Human(map);
                var capacities = DefDatabase<PawnCapacityDef>.AllDefsListForReading
                    .Where(c => string.Equals(c.modContentPack?.PackageIdPlayerFacing, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.defName).ToList();
                Require(capacities.Count >= 11, "passive/native core capacity definitions are loaded", check);
                var stats = statNames.Select(n => DefDatabase<StatDef>.GetNamed(n)).ToList();
                var humanBefore = Capacities(human, capacities);

                Pawn body = Shift(map, human);
                string bodyId = body.GetUniqueLoadID();
                control = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Warg"), Faction.OfPlayer);
                control.health.RemoveAllHediffs();
                GenSpawn.Spawn(control, CellFinder.StandableCellNear(map.Center, map, 10f), map);
                Require(control.def == body.def && SharedFormIdentity.HumanFor(control) == null,
                    "passive/control is a real ordinary animal of the same native race", check);
                Hediff bodyCut = Cut(body), controlCut = Cut(control);
                var bodyBefore = Capacities(body, capacities);
                var controlBefore = Capacities(control, capacities);
                var bodyStatsBefore = Stats(body, stats);
                var controlStatsBefore = Stats(control, stats);
                float bodyPainBefore = body.health.hediffSet.PainTotal;
                float controlPainBefore = control.health.hediffSet.PainTotal;
                Require(bodyPainBefore > 0f && Near(bodyPainBefore, controlPainBefore),
                    "passive/native control and active form have matched nonzero physical pain", check);
                Require(TransformUtility.RevertToHuman(body, false) == human,
                    "passive/baseline form returns its exact original before native ingestion", check);

                Dose(human, "Luciferium");
                Dose(control, "Luciferium");
                HediffDef luciferium = DefDatabase<HediffDef>.GetNamed("LuciferiumHigh");
                Hediff source = human.health.hediffSet.GetFirstHediffOfDef(luciferium);
                Hediff nativeControlSource = control.health.hediffSet.GetFirstHediffOfDef(luciferium);
                Require(source != null && nativeControlSource != null && SharedFormHealth.IsSupported(source),
                    "passive/real native Luciferium ingestion creates supported human and ordinary-animal sources", check);
                var humanAfter = Capacities(human, capacities);
                string sourceId = source.GetUniqueLoadID();
                int sourceAge = source.ageTicks;
                body = Shift(map, human);
                Require(body.GetUniqueLoadID() == bodyId,
                    "passive/buffed comparison reuses the exact baseline animal body", check);
                // Entering a cached form deliberately resets physical wounds.
                bodyCut = Cut(body);
                var bodyAfter = Capacities(body, capacities);
                var controlAfter = Capacities(control, capacities);
                var bodyStatsAfter = Stats(body, stats);
                var controlStatsAfter = Stats(control, stats);

                foreach (PawnCapacityDef capacity in capacities)
                {
                    bool correct = Near(bodyAfter[capacity] - bodyBefore[capacity], controlAfter[capacity] - controlBefore[capacity]);
                    check(correct, "passive/Luciferium " + capacity.defName + " human " + Pair(humanBefore[capacity], humanAfter[capacity])
                        + "; form " + Pair(bodyBefore[capacity], bodyAfter[capacity])
                        + "; native animal " + Pair(controlBefore[capacity], controlAfter[capacity]));
                }
                Require(source.CurStage?.capMods != null && source.CurStage.capMods.Count == 7,
                    "passive/native Luciferium declares seven capacity modifiers", check);
                foreach (PawnCapacityModifier modifier in source.CurStage.capMods)
                    check(humanAfter[modifier.capacity] > humanBefore[modifier.capacity]
                        && controlAfter[modifier.capacity] > controlBefore[modifier.capacity]
                        && bodyAfter[modifier.capacity] > bodyBefore[modifier.capacity],
                        "passive/Luciferium actually strengthens human, native animal and active form: " + modifier.capacity.defName);
                for (int i = 0; i < stats.Count; i++)
                {
                    StatDef stat = stats[i];
                    Require(bodyStatsBefore[stat] > 0f && controlStatsBefore[stat] > 0f,
                        "passive/native positive stat baselines: " + stat.defName, check);
                    check(Near(bodyStatsAfter[stat] / bodyStatsBefore[stat], controlStatsAfter[stat] / controlStatsBefore[stat]),
                        "passive/Luciferium stat " + stat.defName + " form " + Pair(bodyStatsBefore[stat], bodyStatsAfter[stat])
                        + "; native animal " + Pair(controlStatsBefore[stat], controlStatsAfter[stat]));
                }
                check(Near(body.health.hediffSet.PainTotal, bodyPainBefore * .8f)
                    && Near(control.health.hediffSet.PainTotal, controlPainBefore * .8f),
                    "passive/Luciferium native pain factor 0.8 applies to both active and ordinary animal injuries");
                CheckOwner(human, body, source, sourceId, check);

                Require(TransformUtility.RevertToHuman(body, false) == human,
                    "passive/buffed form returns the exact original", check);
                check(ReferenceEquals(human.health.hediffSet.GetFirstHediffOfDef(luciferium), source)
                    && source.ageTicks == sourceAge,
                    "passive/return preserves original Luciferium object and does not advance its clock during queries");
                body = Shift(map, human);
                control.health.RemoveHediff(controlCut);
                CheckOwner(human, body, source, sourceId, check);
                var reused = Capacities(body, capacities);
                check(body.GetUniqueLoadID() == bodyId && capacities.All(c => Near(reused[c], bodyAfter[c])),
                    "passive/repeated transformation keeps the same body and exact passive capacity benefits");

                int tick = Find.TickManager.TicksGame;
                human.health.RemoveHediff(source);
                control.health.RemoveHediff(nativeControlSource);
                var removed = Capacities(body, capacities);
                check(capacities.All(c => Near(removed[c], bodyBefore[c]))
                    && !body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(p => ReferenceEquals(p.source, source)),
                    "passive/removing the canonical source clears all passive capacity benefits and its view in the same tick");
                AlcoholStages(body, human, control, capacities, check);
                check(Find.TickManager.TicksGame == tick,
                    "passive/removal and severity-stage cache checks require no intervening game tick");
                PersonaWeapon(map, check);
                PersonalStates(map, capacities, check);
            }
            finally
            {
                if (human != null && !human.Destroyed)
                {
                    Pawn active = SharedFormIdentity.ActiveFormFor(human);
                    if (active != null) TransformUtility.RevertToHuman(active, false);
                    if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
                }
                if (control != null && !control.Destroyed) control.Destroy(DestroyMode.Vanish);
            }
        }

        private static void AlcoholStages(Pawn body, Pawn human, Pawn control, List<PawnCapacityDef> capacities, Action<bool, string> check)
        {
            var baseline = Capacities(body, capacities);
            HediffDef def = DefDatabase<HediffDef>.GetNamed("AlcoholHigh");
            Hediff source = human.health.AddHediff(def);
            Hediff native = control.health.AddHediff(def);
            source.Severity = native.Severity = .30f;
            var mild = Capacities(body, capacities);
            var nativeMild = Capacities(control, capacities);
            check(capacities.All(c => Near(mild[c], nativeMild[c]))
                && mild[PawnCapacityDefOf.Consciousness] < baseline[PawnCapacityDefOf.Consciousness],
                "passive/native alcohol mild-stage penalties match the ordinary-animal control");
            source.Severity = native.Severity = .55f;
            var strong = Capacities(body, capacities);
            var nativeStrong = Capacities(control, capacities);
            check(capacities.All(c => Near(strong[c], nativeStrong[c]))
                && strong[PawnCapacityDefOf.Consciousness] < mild[PawnCapacityDefOf.Consciousness],
                "passive/changing canonical severity updates every native capacity to the stronger stage in the same tick");
            source.Severity = native.Severity = .30f;
            check(capacities.All(c => Near(body.health.capacities.GetLevel(c), mild[c])),
                "passive/lowering canonical severity restores the cached mild stage in the same tick");
            human.health.RemoveHediff(source);
            control.health.RemoveHediff(native);
            check(capacities.All(c => Near(body.health.capacities.GetLevel(c), baseline[c])),
                "passive/removing native alcohol clears its passive penalties immediately");
        }

        private static void PersonaWeapon(Map map, Action<bool, string> check)
        {
            Pawn human = Human(map);
            ThingWithComps weapon = null;
            try
            {
                human.Name = new NameSingle("PersonaPassiveOriginal");
                Pawn body = Shift(map, human);
                Cut(body);
                float painBefore = body.health.hediffSet.PainTotal;
                float movingBefore = body.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                StatDef recovery = DefDatabase<StatDef>.GetNamed("PsychicEntropyRecoveryRate");
                StatPart_Pain recoveryPain = recovery.parts.OfType<StatPart_Pain>().Single();
                float recoveryBefore = body.GetStatValue(recovery);
                float recoveryPainBefore = recoveryPain.PainFactor(body);
                float hungerBefore = body.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                Require(painBefore > 0f && hungerBefore > 0f,
                    "passive/persona baseline has real physical pain and native food consumption", check);
                Require(TransformUtility.RevertToHuman(body, false) == human,
                    "passive/persona baseline returns the exact original before native equipment", check);
                Cut(human);
                float humanMovingBefore = human.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                float humanRecoveryBefore = human.GetStatValue(recovery);
                float humanRecoveryPainBefore = recoveryPain.PainFactor(human);
                float humanHungerBefore = human.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                ThingDef weaponDef = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.comps != null
                    && d.comps.Any(c => c.compClass == typeof(CompBladelinkWeapon)));
                weapon = (ThingWithComps)ThingMaker.MakeThing(weaponDef, weaponDef.MadeFromStuff ? ThingDefOf.Plasteel : null);
                CompBladelinkWeapon coding = weapon.TryGetComp<CompBladelinkWeapon>();
                string[] names = { "NoPain", "SpeedBoost", "NeuralHeatRecoveryGain", "HungerMaker" };
                // Test setup only: place all four real native traits on one
                // real persona weapon; normal generation chooses fewer traits.
                coding.TraitsListForReading.Clear();
                foreach (string name in names) coding.TraitsListForReading.Add(DefDatabase<WeaponTraitDef>.GetNamed(name));
                human.equipment.AddEquipment(weapon);
                if (coding.CodedPawn != human) coding.CodeFor(human);
                var sources = names.Select(n => human.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed(n))).ToList();
                Require(sources.All(h => h != null && h.pawn == human) && coding.CodedPawn == human,
                    "passive/native persona equip and bonding create all four real canonical health effects", check);
                // Evaluate the native human oracle with its stat cache cleared.
                // The active-form checks below deliberately receive no manual
                // cache invalidation, so they still exercise production updates.
                recovery.Worker.ClearCacheForThing(human);
                float humanPain = human.health.hediffSet.PainTotal;
                float humanMoving = human.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                float humanRecovery = human.GetStatValue(recovery);
                float humanHunger = human.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                check(Near(humanPain, 0f), "passive/native persona human NoPain oracle pain=" + Number(humanPain));
                check(Near(humanMoving, humanMovingBefore + .15f),
                    "passive/native persona human SpeedBoost oracle " + Pair(humanMovingBefore, humanMoving));
                // Native cooling applies the +0.15 health offset before its
                // configured pain multiplier. This same weapon removes pain,
                // so the final increase is not necessarily exactly +0.15.
                float coolingOffset = sources.Single(s => s.def.defName == "NeuralHeatRecoveryGain")
                    .CurStage.statOffsets.Single(m => m.stat == recovery).value;
                Require(Near(coolingOffset, .15f), "passive/native persona cooling definition declares offset +0.15", check);
                float expectedHumanRecovery = (humanRecoveryBefore / humanRecoveryPainBefore + coolingOffset) * recoveryPain.PainFactor(human);
                check(Near(humanRecovery, expectedHumanRecovery),
                    "passive/native persona human cooling oracle " + Pair(humanRecoveryBefore, humanRecovery)
                        + "; expected native offset/pain pipeline=" + Number(expectedHumanRecovery));
                check(NearRelative(humanHunger, humanHungerBefore * 1.5f),
                    "passive/native persona human hunger oracle " + Number(humanHungerBefore) + "->" + Number(humanHunger)
                        + "; ratio=" + Number(humanHunger / humanHungerBefore));
                body = Shift(map, human);
                Cut(body);
                check(Near(body.health.hediffSet.PainTotal, 0f),
                    "passive/persona NoPain disables real active-animal injury pain");
                check(Near(body.health.capacities.GetLevel(PawnCapacityDefOf.Moving), movingBefore + .15f),
                    "passive/persona SpeedBoost supplies native Moving +0.15 to the active animal");
                float actualRecovery = body.GetStatValue(recovery);
                float expectedRecovery = (recoveryBefore / recoveryPainBefore + coolingOffset) * recoveryPain.PainFactor(body);
                check(Near(actualRecovery, expectedRecovery),
                    "passive/persona NeuralHeatRecoveryGain follows native +0.15 offset and pain multiplier: form "
                        + Pair(recoveryBefore, actualRecovery) + "; expected=" + Number(expectedRecovery));
                check(NearRelative(body.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed), hungerBefore * 1.5f),
                    "passive/persona HungerMaker also retains the native food-consumption x1.5 downside");
                check(sources.All(s => s.pawn == human && human.health.hediffSet.hediffs.Contains(s)
                    && body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(p => ReferenceEquals(p.source, s)) == 1)
                    && weapon.ParentHolder == human.equipment && coding.CodedPawn == human,
                    "passive/persona weapon and all effect sources keep their original owners with one active-body view each");
                int tick = Find.TickManager.TicksGame;
                human.equipment.Remove(weapon);
                coding.UnCode();
                // Normal native queries realize the lazily invalidated views;
                // no production Refresh or cache clear is called by the test.
                float removedPain = body.health.hediffSet.PainTotal;
                float removedMoving = body.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                float removedRecovery = body.GetStatValue(recovery);
                float removedHunger = body.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                check(sources.All(s => !human.health.hediffSet.hediffs.Contains(s))
                    && !body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(p => sources.Contains(p.source)),
                    "passive/native persona unequip and unbond remove every canonical passive effect and view");
                check(Near(removedPain, painBefore)
                    && Near(removedMoving, movingBefore)
                    && Near(removedRecovery, recoveryBefore)
                    && NearRelative(removedHunger, hungerBefore)
                    && Find.TickManager.TicksGame == tick,
                    "passive/persona removal restores pain, movement, cooling and hunger immediately without another game tick");
            }
            finally
            {
                Pawn active = SharedFormIdentity.ActiveFormFor(human);
                if (active != null) TransformUtility.RevertToHuman(active, false);
                if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
                if (weapon != null && !weapon.Destroyed) weapon.Destroy(DestroyMode.Vanish);
            }
        }

        private static void PersonalStates(Map map, List<PawnCapacityDef> capacities, Action<bool, string> check)
        {
            Pawn human = Human(map), control = null;
            try
            {
                human.Name = new NameSingle("PersonalPassiveOriginal");
                Pawn body = Shift(map, human);
                control = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Warg"), Faction.OfPlayer);
                control.health.RemoveAllHediffs();
                GenSpawn.Spawn(control, CellFinder.StandableCellNear(map.Center, map, 10f), map);
                foreach (string name in new[] { "NeurosisPulse", "PleasurePulse", "PsychicHangover" })
                {
                    HediffDef def = DefDatabase<HediffDef>.GetNamed(name);
                    var statDefs = new List<StatDef>();
                    foreach (HediffStage stage in def.stages)
                    {
                        if (stage.statOffsets != null) statDefs.AddRange(stage.statOffsets.Select(m => m.stat));
                        if (stage.statFactors != null) statDefs.AddRange(stage.statFactors.Select(m => m.stat));
                    }
                    statDefs = statDefs.Distinct().ToList();
                    var before = Capacities(body, capacities);
                    var nativeBefore = Capacities(control, capacities);
                    var statsBefore = Stats(body, statDefs);
                    var nativeStatsBefore = Stats(control, statDefs);
                    float foodBefore = body.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                    float nativeFoodBefore = control.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                    Hediff source = human.health.AddHediff(def);
                    Hediff native = control.health.AddHediff(def);
                    // The later hangover stage contains its real penalties;
                    // set the same native severity on both test subjects.
                    source.Severity = native.Severity = Math.Max(def.initialSeverity, .5f);
                    var after = Capacities(body, capacities);
                    var nativeAfter = Capacities(control, capacities);
                    check(capacities.All(c => Near(after[c] - before[c], nativeAfter[c] - nativeBefore[c])),
                        "passive/" + name + " native capacity effects match the ordinary-animal control");
                    foreach (StatDef stat in statDefs)
                    {
                        float actual = body.GetStatValue(stat), expected = control.GetStatValue(stat);
                        bool factor = source.CurStage?.statFactors?.Any(m => m.stat == stat) == true;
                        bool equal = factor && statsBefore[stat] != 0f && nativeStatsBefore[stat] != 0f
                            ? Near(actual / statsBefore[stat], expected / nativeStatsBefore[stat])
                            : Near(actual - statsBefore[stat], expected - nativeStatsBefore[stat]);
                        check(equal, "passive/" + name + " stat " + stat.defName + " form " + Pair(statsBefore[stat], actual)
                            + "; native animal " + Pair(nativeStatsBefore[stat], expected));
                    }
                    float food = body.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                    float nativeFood = control.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
                    check(Near(food / foodBefore, nativeFood / nativeFoodBefore),
                        "passive/" + name + " retains its native hunger-rate benefit or penalty");
                    check(source.pawn == human && body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                        .Count(p => ReferenceEquals(p.source, source)) == 1,
                        "passive/" + name + " has one original source and one active-body effect view");
                    human.health.RemoveHediff(source);
                    control.health.RemoveHediff(native);
                    check(capacities.All(c => Near(body.health.capacities.GetLevel(c), before[c]))
                        && statDefs.All(s => Near(body.GetStatValue(s), statsBefore[s]))
                        && !body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(p => ReferenceEquals(p.source, source)),
                        "passive/" + name + " removal clears its passive values and source reference immediately");
                }
            }
            finally
            {
                Pawn active = SharedFormIdentity.ActiveFormFor(human);
                if (active != null) TransformUtility.RevertToHuman(active, false);
                if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
                if (control != null && !control.Destroyed) control.Destroy(DestroyMode.Vanish);
            }
        }

        private static void CheckOwner(Pawn human, Pawn body, Hediff source, string sourceId, Action<bool, string> check)
        {
            check(source.pawn == human && source.GetUniqueLoadID() == sourceId
                && human.health.hediffSet.hediffs.Count(h => h.def == source.def) == 1
                && !body.health.hediffSet.hediffs.Any(h => h.def == source.def)
                && body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(p => ReferenceEquals(p.source, source)) == 1,
                "passive/Luciferium keeps one canonical human source and exactly one non-owning animal view");
        }
        private static Dictionary<PawnCapacityDef, float> Capacities(Pawn pawn, IEnumerable<PawnCapacityDef> defs)
        { return defs.ToDictionary(d => d, d => pawn.health.capacities.GetLevel(d)); }
        private static Dictionary<StatDef, float> Stats(Pawn pawn, IEnumerable<StatDef> defs)
        { return defs.ToDictionary(d => d, d => pawn.GetStatValue(d)); }
        private static void Dose(Pawn pawn, string item)
        { ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(item)).Ingested(pawn, 0f); }
        private static Hediff Cut(Pawn pawn)
        { Hediff cut = pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart); cut.Severity = 4f; return cut; }
        private static bool Near(float a, float b) { return Math.Abs(a - b) < .00015f; }
        private static bool NearRelative(float a, float b) { return Math.Abs(a - b) < Math.Max(.000000001f, Math.Abs(b) * .00015f); }
        private static string Pair(float before, float after)
        { return before.ToString("F4", CultureInfo.InvariantCulture) + "->" + after.ToString("F4", CultureInfo.InvariantCulture); }
        private static string Number(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static void Require(bool value, string label, Action<bool, string> check)
        { check(value, label); if (!value) throw new InvalidOperationException(label); }
        private static Pawn Human(Map map)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedGender: Gender.Male,
                fixedBiologicalAge: 25f, forcedXenotype: XenotypeDefOf.Baseliner));
            pawn.Name = new NameSingle("PassiveHealthOriginal");
            pawn.health.RemoveAllHediffs();
            foreach (Trait trait in pawn.story.traits.allTraits.ToList()) pawn.story.traits.RemoveTrait(trait);
            foreach (Gene gene in pawn.genes.GenesListForReading.ToList()) pawn.genes.RemoveGene(gene);
            foreach (Apparel apparel in pawn.apparel.WornApparel.ToList()) pawn.apparel.Remove(apparel);
            pawn.equipment.DestroyAllEquipment();
            GenSpawn.Spawn(pawn, CellFinder.StandableCellNear(map.Center, map, 10f), map);
            pawn.drafter.Drafted = true;
            return pawn;
        }
        private static Pawn Shift(Map map, Pawn human)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
            TransformUtility.AddOrGetShapeshifter(human, kind);
            if (!TransformUtility.TransformToAnimal(human, kind)) throw new InvalidOperationException("Passive-health transformation failed");
            return map.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == human);
        }
    }
}
