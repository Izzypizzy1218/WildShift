using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // An explicit opt-in for a declarative, whole-body timed buff. This is not
    // permission to tick arbitrary custom components on the preserved human.
    public sealed class SharedFormBuffExtension : DefModExtension { }

    public static class SharedFormBuffs
    {
        // These are actual vanilla personal states, not generated animal
        // diseases or arbitrary mod state machines. Their original instances
        // remain the only owner of timers, links and component state.
        private static readonly HashSet<string> nativeStates = new HashSet<string>(StringComparer.Ordinal)
        {
            "NeuralSupercharge", "Hangover", "AlcoholHigh", "BrainShock",
            "BerserkTrance", "ImmunityDrive", "WorkFocus", "PsychicFocus",
            "Voidsight", "Metalblood", "JuggernautSerum", "MindNumbSerum",
            "PsychicInvisibility", "HoraxianInvisibility", "PsychicLove", "PsychicTrance",
            "Psychophage", "Regeneration", "RageSpeed", "SleepSuppression", "VoidTouched",
            "CombatCommand", "CombatCommandBuff", "FarmingCommand", "FarmingCommandBuff",
            "MarksmanCommand", "MarksmanCommandBuff", "MiningCommand", "MiningCommandBuff",
            "ProductionCommand", "ProductionCommandBuff", "ResearchCommand", "ResearchCommandBuff",
            "EntropyLink", "Deathrest", "PsychicBond", "PsychicBondTorn", "PollutionStimulus",
            "GlucosoidRush", "PsychofluidRush", "HemogenAmplified", "DeathRefusal", "DeathRefusalCreepJoiner",
            "CubeInterest", "CubeWithdrawal", "CubeRage", "FrenzyField",
            // Native personal states supplied by persona weapons, psychic
            // pulses and neural-heat recovery; retain their penalties too.
            "NoPain", "SpeedBoost", "NeuralHeatRecoveryGain", "HungerMaker",
            "NeurosisPulse", "PleasurePulse", "PsychicHangover"
        };
        private static readonly Dictionary<string, string> reviewed = new Dictionary<string, string>
        {
            { "Kiiro_AteCatmint", "ancot.kiirorace" },
            { "Miho_Sandevistan_Hediff", "miho.fortifiedoutremer" },
            { "RK_Hediff_LanceChargeFocus", "solaris.ratkinracemod" },
            { "RK_Hediff_LanceChargeExhaustion", "solaris.ratkinracemod" },
            { "WorkDrive", "ludeon.rimworld.ideology" },
            { "PreachHealth", "ludeon.rimworld.ideology" },
            { "PsychicAnesthesia", "ludeon.rimworld.royalty" }
        };
        private static readonly AccessTools.FieldRef<Hediff, float> savedSeverity
            = AccessTools.FieldRefAccess<Hediff, float>("severityInt");

        private sealed class DefClassification
        {
            public bool valid;
            public string defName;
            public Type hediffClass;
            public ModContentPack pack;
            public bool countsAsImplant;
            public BodyPartDef defaultInstallPart;
            public ThingDef spawnThingOnRemoved;
            public NeedDef chemicalNeed;
            public int compsCount;
            public int compsHash;
            public int extCount;
            public int extHash;
            public bool isNative;
            public bool isSystemicImplant;
            public bool isSupportedDef;
            public bool isNativeStateOrImplant;
        }

        private static readonly ConditionalWeakTable<HediffDef, DefClassification> defCache
            = new ConditionalWeakTable<HediffDef, DefClassification>();
        private static readonly ConditionalWeakTable<HediffDef, DefClassification>.CreateValueCallback createDefClassification
            = CreateDefClassification;
        private static DefClassification CreateDefClassification(HediffDef ignored) { return new DefClassification(); }

        private static DefClassification Classify(HediffDef def)
        {
            DefClassification entry = defCache.GetValue(def, createDefClassification);
            int compsCount = def.comps != null ? def.comps.Count : 0;
            int extCount = def.modExtensions != null ? def.modExtensions.Count : 0;
            int compsHash = 0;
            if (compsCount > 0)
            {
                unchecked
                {
                    compsHash = 17;
                    for (int i = 0; i < compsCount; i++)
                    {
                        HediffCompProperties p = def.comps[i];
                        compsHash = compsHash * 31 + (p?.compClass != null ? RuntimeHelpers.GetHashCode(p.compClass) : 0);
                        var d = p as HediffCompProperties_Disappears;
                        if (d != null)
                        {
                            compsHash = compsHash * 31 + (d.requiredMentalState != null ? RuntimeHelpers.GetHashCode(d.requiredMentalState) : 0);
                            compsHash = compsHash * 31 + (d.leaveFreshWounds ? 1 : 0);
                        }
                    }
                }
            }
            int extHash = 0;
            if (extCount > 0)
            {
                unchecked
                {
                    extHash = 17;
                    for (int i = 0; i < extCount; i++)
                        extHash = extHash * 31 + (def.modExtensions[i] != null ? RuntimeHelpers.GetHashCode(def.modExtensions[i]) : 0);
                }
            }
            if (entry.valid
                && entry.defName == def.defName
                && entry.hediffClass == def.hediffClass
                && ReferenceEquals(entry.pack, def.modContentPack)
                && entry.countsAsImplant == def.countsAsAddedPartOrImplant
                && entry.defaultInstallPart == def.defaultInstallPart
                && entry.spawnThingOnRemoved == def.spawnThingOnRemoved
                && entry.chemicalNeed == def.chemicalNeed
                && entry.compsCount == compsCount
                && entry.compsHash == compsHash
                && entry.extCount == extCount
                && entry.extHash == extHash)
            {
                return entry;
            }

            bool native = def.modContentPack != null && def.hediffClass != null
                && def.hediffClass.Assembly == typeof(Hediff).Assembly
                && def.modContentPack.PackageIdPlayerFacing.StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase);
            bool implant = native && (def.countsAsAddedPartOrImplant
                || typeof(Hediff_Implant).IsAssignableFrom(def.hediffClass)
                || typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass));
            bool nativeStateOrImplant = native && (nativeStates.Contains(def.defName) || implant);
            bool supported;
            if (nativeStateOrImplant)
            {
                supported = true;
                if (def.comps != null)
                    for (int i = 0; i < def.comps.Count; i++)
                        if (def.comps[i]?.compClass == null || def.comps[i].compClass.Assembly != typeof(Hediff).Assembly)
                        { supported = false; break; }
            }
            else if ((def.hediffClass != typeof(Hediff) && def.hediffClass != typeof(HediffWithComps))
                || def.defaultInstallPart != null || def.countsAsAddedPartOrImplant
                || def.spawnThingOnRemoved != null || def.chemicalNeed != null)
            {
                supported = false;
            }
            else
            {
                string package;
                bool approved = def.GetModExtension<SharedFormBuffExtension>() != null
                    || (reviewed.TryGetValue(def.defName ?? string.Empty, out package)
                        && def.modContentPack != null
                        && string.Equals(def.modContentPack.PackageIdPlayerFacing, package, StringComparison.OrdinalIgnoreCase));
                supported = approved;
                if (supported && def.comps != null)
                {
                    for (int i = 0; i < def.comps.Count; i++)
                    {
                        HediffCompProperties properties = def.comps[i];
                        Type type = properties != null ? properties.compClass : null;
                        if (type != typeof(HediffComp_SeverityPerDay) && type != typeof(HediffComp_Disappears)
                            && type != typeof(HediffComp_DisappearsOnDeath)) { supported = false; break; }
                        var disappearance = properties as HediffCompProperties_Disappears;
                        if (disappearance != null && (disappearance.requiredMentalState != null || !disappearance.leaveFreshWounds))
                        { supported = false; break; }
                    }
                }
            }

            entry.defName = def.defName;
            entry.hediffClass = def.hediffClass;
            entry.pack = def.modContentPack;
            entry.countsAsImplant = def.countsAsAddedPartOrImplant;
            entry.defaultInstallPart = def.defaultInstallPart;
            entry.spawnThingOnRemoved = def.spawnThingOnRemoved;
            entry.chemicalNeed = def.chemicalNeed;
            entry.compsCount = compsCount;
            entry.compsHash = compsHash;
            entry.extCount = extCount;
            entry.extHash = extHash;
            entry.isNative = native;
            entry.isSystemicImplant = implant;
            entry.isNativeStateOrImplant = nativeStateOrImplant;
            entry.isSupportedDef = supported;
            entry.valid = true;
            return entry;
        }

        public static bool IsNativeDefinition(HediffDef def)
        {
            return def != null && Classify(def).isNative;
        }

        public static bool IsSystemicImplant(HediffDef def)
        {
            return def != null && Classify(def).isSystemicImplant;
        }

        public static bool CanonicalizeAddition(Hediff effect)
        {
            // A prosthesis installed on the animal belongs to that physical
            // body. Only its human counterpart's systemic effects are views.
            return effect != null && !IsSystemicImplant(effect.def) && IsSupported(effect);
        }

        public static bool IsSupportedDef(HediffDef def)
        {
            return def != null && Classify(def).isSupportedDef;
        }

        public static bool IsSupported(Hediff effect)
        {
            if (effect == null || effect.def == null) return false;
            DefClassification info = Classify(effect.def);
            if (!info.isSupportedDef || !AllowsPart(effect.def, effect.pawn, effect.Part)) return false;
            Type type = effect.GetType();
            if (info.isNativeStateOrImplant)
            {
                if (type != effect.def.hediffClass) return false;
                HediffWithComps native = effect as HediffWithComps;
                if (native?.comps != null)
                    for (int i = 0; i < native.comps.Count; i++)
                        if (native.comps[i] == null || native.comps[i].GetType().Assembly != typeof(Hediff).Assembly) return false;
                return true;
            }
            if ((type != typeof(Hediff) && type != typeof(HediffWithComps)) || type != effect.def.hediffClass) return false;
            HediffWithComps withComps = effect as HediffWithComps;
            if ((withComps?.comps?.Count ?? 0) != (effect.def.comps?.Count ?? 0)) return false;
            if (withComps?.comps == null) return true;
            for (int i = 0; i < withComps.comps.Count; i++)
            {
                HediffComp comp = withComps.comps[i];
                if (comp == null) return false;
                Type compType = comp.GetType();
                if (compType != typeof(HediffComp_SeverityPerDay) && compType != typeof(HediffComp_Disappears)
                    && compType != typeof(HediffComp_DisappearsOnDeath)) return false;
            }
            return true;
        }

        private static bool IsBrainBuff(HediffDef def)
        {
            return def != null && (def.defName == "WorkDrive" || def.defName == "PreachHealth")
                && def.modContentPack != null
                && string.Equals(def.modContentPack.PackageIdPlayerFacing, "ludeon.rimworld.ideology", StringComparison.OrdinalIgnoreCase);
        }

        public static bool AllowsPart(HediffDef def, Pawn pawn, BodyPartRecord part)
        {
            return part == null || IsSystemicImplant(def)
                || ((IsBrainBuff(def) || (IsNativeDefinition(def) && nativeStates.Contains(def.defName)))
                    && pawn?.health != null && part == pawn.health.hediffSet.GetBrain());
        }

        public static BodyPartRecord SourcePart(HediffDef def, Pawn human, BodyPartRecord part)
        {
            return part != null && (IsBrainBuff(def) || (IsNativeDefinition(def) && nativeStates.Contains(def.defName)))
                ? human.health.hediffSet.GetBrain() : null;
        }

        public static Hediff SourceFor(Pawn body, HediffDef def, bool mustBeVisible)
        {
            HediffComp_Transformed carrier = TransformUtility.TryGetTransformedComp(body);
            if (carrier == null || !IsSupportedDef(def)) return null;
            Pawn human = SharedFormIdentity.GetStoredHumanFromActiveBody(body, carrier);
            if (human == null) return null;
            List<Hediff> hediffs = human.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff effect = hediffs[i];
                if (effect.def == def && IsSupported(effect) && (!mustBeVisible || effect.Visible)) return effect;
            }
            return null;
        }

        public static void AdoptExisting(Pawn animal, Pawn human)
        {
            // Older saves may already have a reviewed buff on the animal.
            // Native TryMergeWith is a new dose: it adds severity and resets
            // age. Reconcile saved state without replaying that ingestion.
            List<Hediff> animalEffects = animal?.health?.hediffSet?.hediffs;
            List<Hediff> humanEffects = human?.health?.hediffSet?.hediffs;
            if (animalEffects == null || humanEffects == null) return;
            var existing = new List<Hediff>(animalEffects);
            bool changed = false;
            foreach (Hediff effect in existing)
            {
                if (effect?.pawn != animal || !CanonicalizeAddition(effect) || !Finite(effect.Severity)) continue;
                BodyPartRecord part = SourcePart(effect.def, human, effect.Part);
                Hediff canonical = null;
                bool incompatible = false;
                foreach (Hediff source in humanEffects)
                {
                    if (source == null || source.def != effect.def || source.Part != part) continue;
                    if (canonical != null || !IsSupported(source) || !Finite(source.Severity))
                    {
                        incompatible = true;
                        break;
                    }
                    canonical = source;
                }
                // Never consume a custom source or guess which duplicate
                // original is authoritative in an ambiguous saved graph.
                if (incompatible) continue;
                animalEffects.Remove(effect);
                if (canonical == null)
                {
                    effect.Part = part;
                    effect.pawn = human;
                    humanEffects.Add(effect);
                }
                else MergeSavedBuff(canonical, effect);
                changed = true;
            }
            if (changed)
            {
                animal.health.hediffSet.DirtyCache();
                human.health.hediffSet.DirtyCache();
            }
        }

        private static void MergeSavedBuff(Hediff canonical, Hediff legacy)
        {
            savedSeverity(canonical) = Math.Max(canonical.Severity, legacy.Severity);
            canonical.ageTicks = Math.Max(canonical.ageTicks, legacy.ageTicks);
            HediffComp_Disappears target = canonical.TryGetComp<HediffComp_Disappears>();
            HediffComp_Disappears source = legacy.TryGetComp<HediffComp_Disappears>();
            if (target == null || source == null || source.ticksToDisappear <= target.ticksToDisappear) return;
            target.ticksToDisappear = source.ticksToDisappear;
            target.disappearsAfterTicks = Math.Max(target.disappearsAfterTicks, source.disappearsAfterTicks);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "AddHediff", new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_SharedBuffAdded
    {
        public static bool Prefix(Pawn ___pawn, Hediff hediff, BodyPartRecord part, DamageInfo? dinfo, DamageWorker.DamageResult result)
        {
            Pawn human = SharedFormIdentity.HumanFor(___pawn);
            BodyPartRecord actualPart = part ?? hediff?.Part;
            if (human == null || !SharedFormBuffs.CanonicalizeAddition(hediff)
                || !SharedFormBuffs.AllowsPart(hediff.def, ___pawn, actualPart)) return true;
            hediff.Part = SharedFormBuffs.SourcePart(hediff.def, human, actualPart);
            SharedFormHealth.EnterSource(___pawn);
            try { human.health.AddHediff(hediff, hediff.Part, dinfo, result); }
            finally { SharedFormHealth.ExitSource(); }
            SharedFormHealth.Refresh(___pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "RemoveHediff")]
    public static class Patch_SharedBuffRemoved
    {
        public static bool Prefix(Pawn ___pawn, Hediff hediff)
        {
            Pawn human = SharedFormIdentity.HumanFor(___pawn);
            if (human == null || hediff == null || !SharedFormBuffs.IsSupported(hediff)
                || !human.health.hediffSet.hediffs.Contains(hediff)) return true;
            human.health.RemoveHediff(hediff);
            SharedFormHealth.Refresh(___pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffSet), "GetFirstHediffOfDef")]
    public static class Patch_SharedBuffLookup
    {
        public static void Postfix(Pawn ___pawn, HediffDef def, bool mustBeVisible, ref Hediff __result)
        {
            if (__result != null || !TransformUtility.MightBeTransformed(___pawn)) return;
            List<Hediff> effects = ___pawn.health?.hediffSet?.hediffs;
            if (effects != null && effects.Count > 0)
                __result = SharedFormBuffs.SourceFor(___pawn, def, mustBeVisible);
        }
    }

    [HarmonyPatch(typeof(HediffSet), "HasHediff", new[] { typeof(HediffDef), typeof(bool) })]
    public static class Patch_SharedBuffPresence
    {
        public static void Postfix(Pawn ___pawn, HediffDef def, bool mustBeVisible, ref bool __result)
        {
            if (__result || !TransformUtility.MightBeTransformed(___pawn)) return;
            List<Hediff> effects = ___pawn.health?.hediffSet?.hediffs;
            if (effects != null && effects.Count > 0)
                __result = SharedFormBuffs.SourceFor(___pawn, def, mustBeVisible) != null;
        }
    }
}
