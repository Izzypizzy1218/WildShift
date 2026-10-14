using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [StaticConstructorOnStartup]
    public static class GeneticSapientDefinitions
    {
        static GeneticSapientDefinitions()
        {
            LongEventHandler.ExecuteWhenFinished(GeneticsCompatibility.RegisterSapientHybridComps);
        }
    }

    // Only scalar hybrid state crosses bodies. Never serialize a third-party
    // component or copy arbitrary implants, production timers or pawn references.
    public sealed class GeneticFormState : IExposable
    {
        public ThingDef race;
        public int quality = 2, ageTicks, diseaseThreshold = 10800000;
        public bool qualityRead;
        public List<string> diseases = new List<string>();
        public List<float> severities = new List<float>();
        public void ExposeData()
        {
            Scribe_Defs.Look(ref race, "race");
            Scribe_Values.Look(ref quality, "quality", 2);
            Scribe_Values.Look(ref ageTicks, "ageTicks", 0);
            Scribe_Values.Look(ref diseaseThreshold, "diseaseThreshold", 10800000);
            Scribe_Values.Look(ref qualityRead, "qualityRead", false);
            Scribe_Collections.Look(ref diseases, "diseases", LookMode.Value);
            Scribe_Collections.Look(ref severities, "severities", LookMode.Value);
        }
    }

    public static class GeneticsCompatibility
    {
        private static readonly Type Hybrid = AccessTools.TypeByName("GeneticRim.CompHybrid");
        private static readonly Type Age = AccessTools.TypeByName("GeneticRim.CompApplyAgeDiseases");
        private static readonly FieldInfo Quality = Hybrid == null ? null : AccessTools.Field(Hybrid, "quality");
        private static readonly FieldInfo Counter = Age == null ? null : AccessTools.Field(Age, "tickCounter");
        private static readonly FieldInfo Threshold = Age == null ? null : AccessTools.Field(Age, "ticksToApply");
        private static readonly FieldInfo Read = Age == null ? null : AccessTools.Field(Age, "readQualityOnce");
        private static readonly HashSet<string> DiseaseNames = new HashSet<string>
        {
            "GR_MuscleNecrosis", "GR_AnimalTuberculosis", "GR_AnimalAbasia", "GR_SargSyndrome"
        };

        public static bool Excluded(ThingDef race)
        {
            if (Hybrid == null || race == null) return false;
            if (race.tradeTags != null && race.tradeTags.Contains("AnimalGeneticMechanoid")) return true;
            if (race.comps != null) foreach (var comp in race.comps)
            {
                string name = comp.compClass?.FullName;
                if (name == "GeneticRim.CompGeneticFailure" || name == "GeneticRim.CompFleshGrowth"
                    || name == "GeneticRim.CompDieUnlessReset") return true;
            }
            return false;
        }

        public static void RegisterSapientHybridComps()
        {
            if (Hybrid == null || Age == null) return;
            int count = 0;
            // VGE attaches these two comps in its startup constructor, after
            // B&S has generated sapient races. Complete that late definition
            // setup once so native comp serialization also works after reload.
            foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                ThingDef source = SapientAnimalsCompatibility.AnimalSource(race);
                if (source?.comps == null || Excluded(source)) continue;
                foreach (CompProperties properties in source.comps)
                {
                    if (properties.compClass != Hybrid && properties.compClass != Age) continue;
                    if (race.comps == null) race.comps = new List<CompProperties>();
                    if (!race.comps.Exists(p => p.compClass == properties.compClass)) { race.comps.Add(properties); count++; }
                }
            }
            if (count > 0) Log.Message("[WildShift] Registered " + count + " VGE state components on B&S sapient race definitions.");
        }

        private static ThingComp FindComp(Pawn pawn, Type type)
        {
            if (pawn == null || type == null) return null;
            foreach (ThingComp comp in pawn.AllComps) if (type.IsInstanceOfType(comp)) return comp;
            return null;
        }

        public static GeneticFormState Capture(Pawn pawn)
        {
            ThingComp hybrid = FindComp(pawn, Hybrid);
            if (hybrid == null || Quality == null) return null;
            try
            {
                var state = new GeneticFormState { race = SapientAnimalsCompatibility.AnimalSource(pawn.def) ?? pawn.def,
                    quality = Convert.ToInt32(Quality.GetValue(hybrid)) };
                ThingComp age = FindComp(pawn, Age);
                if (age != null && Counter != null && Threshold != null && Read != null)
                {
                    state.ageTicks = (int)Counter.GetValue(age);
                    state.diseaseThreshold = (int)Threshold.GetValue(age);
                    state.qualityRead = (bool)Read.GetValue(age);
                }
                foreach (Hediff h in pawn.health.hediffSet.hediffs)
                    if (h.Part == null && DiseaseNames.Contains(h.def.defName))
                    { state.diseases.Add(h.def.defName); state.severities.Add(h.Severity); }
                return state;
            }
            catch (Exception ex) { Log.WarningOnce("[WildShift] VGE state capture failed: " + ex, 197521801); return null; }
        }

        public static void Remember(Pawn animal, Pawn human)
        {
            var owner = TransformUtility.TryGetShapeshifterComp(human);
            var state = Capture(animal);
            if (owner != null && state != null) owner.geneticForm = state;
        }

        public static void Restore(Pawn pawn, GeneticFormState state)
        {
            if (state == null || state.race != (SapientAnimalsCompatibility.AnimalSource(pawn.def) ?? pawn.def)) return;
            ThingComp hybrid = FindComp(pawn, Hybrid);
            if (hybrid == null || Quality == null) return;
            try
            {
                int quality = Math.Max(0, Math.Min(6, state.quality));
                Quality.SetValue(hybrid, Enum.ToObject(Quality.FieldType, quality));
                ThingComp age = FindComp(pawn, Age);
                if (age != null && Counter != null && Threshold != null && Read != null)
                {
                    Counter.SetValue(age, Math.Max(0, state.ageTicks));
                    Threshold.SetValue(age, Math.Max(1, state.diseaseThreshold));
                    Read.SetValue(age, state.qualityRead);
                }
                // Same mapping as VGE Core.ApplyQualityHediff, without adding duplicates.
                HediffDef hunger = DefDatabase<HediffDef>.GetNamedSilentFail("GR_HungerByQuality");
                if (hunger != null)
                {
                    Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(hunger) ?? pawn.health.AddHediff(hunger);
                    h.Severity = quality * 0.1f;
                }
                if (state.diseases != null && state.severities != null)
                    for (int i = 0; i < Math.Min(state.diseases.Count, state.severities.Count); i++)
                    {
                        if (!DiseaseNames.Contains(state.diseases[i])) continue;
                        var def = DefDatabase<HediffDef>.GetNamedSilentFail(state.diseases[i]);
                        if (def == null) continue;
                        var h = pawn.health.hediffSet.GetFirstHediffOfDef(def) ?? pawn.health.AddHediff(def);
                        h.Severity = state.severities[i];
                    }
            }
            catch (Exception ex) { Log.WarningOnce("[WildShift] VGE state restore failed: " + ex, 197521802); }
        }
    }
}
