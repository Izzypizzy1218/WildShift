using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. This explicitly seeds a controlled pre-bridge ownership graph
    // using real native Hediffs/needs, then lets native save and fresh-process
    // production postload perform migration. It is not an actual old-colony save.
    public sealed class SharedLegacyDrugEngineChecks : IExposable
    {
        private Hediff high, tolerance, addiction;
        private string highId, toleranceId, addictionId, canonicalLuciferiumId, duplicateLuciferiumId;
        private int seedTick, highAge, toleranceAge, addictionAge;
        private float highSeverity, toleranceSeverity, addictionSeverity;
        private float highRate, toleranceRate, addictionRate;
        private const int PendingAnimalHeal = 731;
        private const float SavedChemicalLevel = .37f;

        public void ExposeData()
        {
            Scribe_References.Look(ref high, "legacyNativeHigh");
            Scribe_References.Look(ref tolerance, "legacyNativeTolerance");
            Scribe_References.Look(ref addiction, "legacyNativeAddiction");
            Scribe_Values.Look(ref highId, "legacyNativeHighId");
            Scribe_Values.Look(ref toleranceId, "legacyNativeToleranceId");
            Scribe_Values.Look(ref addictionId, "legacyNativeAddictionId");
            Scribe_Values.Look(ref canonicalLuciferiumId, "canonicalLuciferiumId");
            Scribe_Values.Look(ref duplicateLuciferiumId, "duplicateLuciferiumId");
            Scribe_Values.Look(ref seedTick, "legacySeedTick", -1);
            Scribe_Values.Look(ref highAge, "legacyHighAge", 0);
            Scribe_Values.Look(ref toleranceAge, "legacyToleranceAge", 0);
            Scribe_Values.Look(ref addictionAge, "legacyAddictionAge", 0);
            Scribe_Values.Look(ref highSeverity, "legacyHighSeverity", 0f);
            Scribe_Values.Look(ref toleranceSeverity, "legacyToleranceSeverity", 0f);
            Scribe_Values.Look(ref addictionSeverity, "legacyAddictionSeverity", 0f);
            Scribe_Values.Look(ref highRate, "legacyHighRate", 0f);
            Scribe_Values.Look(ref toleranceRate, "legacyToleranceRate", 0f);
            Scribe_Values.Look(ref addictionRate, "legacyAddictionRate", 0f);
        }

        public void Seed(Pawn original, Pawn animal, Action<bool, string> check)
        {
            Require(SharedFormIdentity.HumanFor(animal) == original,
                "legacy/full-engine seed targets the real active body holding its original", check);
            Require(!original.health.hediffSet.hediffs.Any(IsReplayDrug)
                && !animal.health.hediffSet.hediffs.Any(IsReplayDrug)
                && original.needs.TryGetNeed(WakeUpNeed()) == null,
                "legacy/full-engine seed starts without prior canonical wake-up or alcohol-tolerance state", check);
            // These native additions are the controlled *input*. No production
            // ingestion/adoption method is patched or called to fake migration.
            high = Add(animal, "WakeUpHigh", .4f, 67);
            // Alcohol really references this tolerance through its native
            // ChemicalDef. WakeUpTolerance is an unused vanilla definition in
            // 1.6/1.7, so it is deliberately outside production drug discovery.
            tolerance = Add(animal, "AlcoholTolerance", .18f, 11);
            addiction = Add(animal, "WakeUpAddiction", .5f, 31);
            Require(SharedFormHealth.IsSupported(tolerance),
                "legacy/full-engine native AlcoholTolerance is inside the production supported-drug gate", check);
            Need_Chemical need = animal.needs.TryGetNeed(WakeUpNeed()) as Need_Chemical;
            Require(need != null && need.GetType() == typeof(Need_Chemical),
                "legacy/full-engine native addiction creates a real animal-owned chemical need", check);
            need.CurLevel = SavedChemicalLevel;
            highId = high.GetUniqueLoadID(); toleranceId = tolerance.GetUniqueLoadID(); addictionId = addiction.GetUniqueLoadID();
            highAge = high.ageTicks; toleranceAge = tolerance.ageTicks; addictionAge = addiction.ageTicks;
            highSeverity = high.Severity; toleranceSeverity = tolerance.Severity; addictionSeverity = addiction.Severity;
            highRate = Rate(high); toleranceRate = Rate(tolerance); addictionRate = Rate(addiction);
            Hediff canonical = FindEffect(original, "LuciferiumHigh");
            Require(canonical?.TryGetComp<HediffComp_HealPermanentWounds>() != null,
                "legacy/full-engine original already owns the canonical native luciferium source", check);
            canonicalLuciferiumId = canonical.GetUniqueLoadID();
            SetHealing(canonical, 600000);
            Hediff duplicate = Add(animal, "LuciferiumHigh", canonical.Severity, canonical.ageTicks);
            duplicateLuciferiumId = duplicate.GetUniqueLoadID();
            SetHealing(duplicate, PendingAnimalHeal);
            seedTick = Find.TickManager.TicksGame;
            check(high.pawn == animal && tolerance.pawn == animal && addiction.pawn == animal
                && animal.health.hediffSet.hediffs.Contains(high) && !original.health.hediffSet.hediffs.Contains(high)
                && Math.Abs(need.CurLevel - SavedChemicalLevel) < .000001f,
                "legacy/full-engine save input really contains native animal drugs and chemical level .37");
            check(Healing(canonical) == 600000 && Healing(duplicate) == PendingAnimalHeal
                && duplicate.pawn == animal && canonical.pawn == original,
                "legacy/full-engine save input contains two native luciferium healing clocks (600000 and 731)");
        }

        public void CheckAfterLoad(Pawn original, Pawn animal, Action<bool, string> check)
        {
            check(seedTick >= 0 && Find.TickManager.TicksGame == seedTick,
                "legacy/full-engine postload migration is inspected before any native game tick");
            CheckEffect(original, animal, high, highId, highAge, highSeverity, highRate, "wake-up high", check);
            CheckEffect(original, animal, tolerance, toleranceId, toleranceAge, toleranceSeverity, toleranceRate, "alcohol tolerance", check);
            CheckEffect(original, animal, addiction, addictionId, addictionAge, addictionSeverity, addictionRate, "wake-up addiction", check);
            Need_Chemical need = original.needs.TryGetNeed(WakeUpNeed()) as Need_Chemical;
            check(need != null && need.GetType() == typeof(Need_Chemical)
                && ReferenceEquals(AccessTools.Field(typeof(Need), "pawn").GetValue(need), original)
                && Math.Abs(need.CurLevel - SavedChemicalLevel) < .000001f
                && original.needs.AllNeeds.Count(n => n.def == WakeUpNeed()) == 1
                && animal.needs.TryGetNeed(WakeUpNeed()) == null,
                "legacy/full-engine postload recreates exactly one original-owned chemical need without refilling .37");
            Hediff canonical = FindEffect(original, "LuciferiumHigh");
            check(canonical != null && canonical.GetUniqueLoadID() == canonicalLuciferiumId
                && canonical.pawn == original && original.health.hediffSet.hediffs.Count(h => h.def == canonical.def) == 1
                && !animal.health.hediffSet.hediffs.Any(h => h.def == canonical.def)
                && !original.health.hediffSet.hediffs.Any(h => h.GetUniqueLoadID() == duplicateLuciferiumId),
                "legacy/full-engine paired luciferium keeps the canonical source ID and removes the old physical duplicate");
            check(canonical != null && Healing(canonical) == PendingAnimalHeal,
                "legacy/full-engine paired luciferium preserves the nearer pending healing clock 731 after native reload");
            check(canonical != null && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                .Count(p => ReferenceEquals(p.source, canonical)) == 1,
                "legacy/full-engine paired luciferium resolves exactly one projection to the canonical source");
        }

        public void CheckAfterReturn(Pawn original, Pawn animal, Action<bool, string> check)
        {
            check(new[] { high, tolerance, addiction }.All(h => h != null && h.pawn == original
                && original.health.hediffSet.hediffs.Contains(h) && !animal.health.hediffSet.hediffs.Contains(h))
                && high.GetUniqueLoadID() == highId && tolerance.GetUniqueLoadID() == toleranceId && addiction.GetUniqueLoadID() == addictionId,
                "legacy/full-engine production return retains the exact migrated native sources on the original");
            Need_Chemical need = original.needs.TryGetNeed(WakeUpNeed()) as Need_Chemical;
            check(need != null && need.CurLevel > 0f && need.CurLevel <= SavedChemicalLevel
                && ReferenceEquals(AccessTools.Field(typeof(Need), "pawn").GetValue(need), original)
                && animal.needs.TryGetNeed(WakeUpNeed()) == null,
                "legacy/full-engine production return preserves the migrated chemical need after native ticking");
            Hediff canonical = FindEffect(original, "LuciferiumHigh");
            check(canonical != null && canonical.GetUniqueLoadID() == canonicalLuciferiumId
                && Healing(canonical) > 0 && Healing(canonical) <= PendingAnimalHeal
                && !animal.health.hediffSet.hediffs.Any(h => IsReplayDrug(h) || h.def.defName == "LuciferiumHigh")
                && !animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Any(),
                "legacy/full-engine return neither resets the pending heal nor leaves duplicate animal sources/projections");
        }

        public object EvidenceSummary()
        {
            return new { ControlledFixture = true, ActualOldSave = false, SeedTick = seedTick,
                WakeUpHighId = highId, AlcoholToleranceId = toleranceId, WakeUpAddictionId = addictionId,
                CanonicalLuciferiumId = canonicalLuciferiumId, RemovedDuplicateLuciferiumId = duplicateLuciferiumId,
                SavedChemicalLevel, PendingAnimalHeal };
        }

        private static void CheckEffect(Pawn original, Pawn animal, Hediff effect, string id, int age, float severity, float rate,
            string label, Action<bool, string> check)
        {
            check(effect != null && effect.GetUniqueLoadID() == id && effect.pawn == original
                && original.health.hediffSet.hediffs.Count(h => h.def == effect.def) == 1
                && original.health.hediffSet.hediffs.Contains(effect) && !animal.health.hediffSet.hediffs.Any(h => h.def == effect.def),
                "legacy/full-engine postload preserves exact " + label + " ID with one original owner");
            check(effect != null && effect.ageTicks == age && Math.Abs(effect.Severity - severity) < .000001f
                && Math.Abs(Rate(effect) - rate) < .000001f,
                "legacy/full-engine postload preserves " + label + " age, severity and native decay state without a new dose");
            check(effect != null && animal.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>()
                .Count(p => ReferenceEquals(p.source, effect)) == 1,
                "legacy/full-engine postload creates one separate " + label + " projection");
        }
        private static Hediff Add(Pawn pawn, string name, float severity, int age)
        {
            Hediff effect = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(name), pawn);
            effect.Severity = severity;
            pawn.health.AddHediff(effect);
            effect.ageTicks = age;
            return effect;
        }
        private static NeedDef WakeUpNeed() { return DefDatabase<NeedDef>.GetNamed("Chemical_WakeUp"); }
        private static Hediff FindEffect(Pawn pawn, string name) { return pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def.defName == name); }
        private static bool IsReplayDrug(Hediff effect) { return effect.def.defName == "WakeUpHigh" || effect.def.defName == "AlcoholTolerance" || effect.def.defName == "WakeUpAddiction"; }
        private static float Rate(Hediff effect) { return effect.TryGetComp<HediffComp_SeverityPerDay>().severityPerDay; }
        private static int Healing(Hediff effect) { return (int)AccessTools.Field(typeof(HediffComp_HealPermanentWounds), "ticksToHeal").GetValue(effect.TryGetComp<HediffComp_HealPermanentWounds>()); }
        private static void SetHealing(Hediff effect, int ticks) { AccessTools.Field(typeof(HediffComp_HealPermanentWounds), "ticksToHeal").SetValue(effect.TryGetComp<HediffComp_HealPermanentWounds>(), ticks); }
        private static void Require(bool ok, string label, Action<bool, string> check) { check(ok, label); if (!ok) throw new InvalidOperationException("Fixture prerequisite: " + label); }
    }
}
