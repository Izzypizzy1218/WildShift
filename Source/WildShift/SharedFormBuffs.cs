using System;
using System.Collections.Generic;
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

        public static bool IsSupportedDef(HediffDef def)
        {
            if (def == null || (def.hediffClass != typeof(Hediff) && def.hediffClass != typeof(HediffWithComps))
                || def.defaultInstallPart != null || def.countsAsAddedPartOrImplant
                || def.spawnThingOnRemoved != null || def.chemicalNeed != null) return false;
            string package;
            bool approved = def.GetModExtension<SharedFormBuffExtension>() != null
                || (reviewed.TryGetValue(def.defName ?? string.Empty, out package)
                    && def.modContentPack != null
                    && string.Equals(def.modContentPack.PackageIdPlayerFacing, package, StringComparison.OrdinalIgnoreCase));
            if (!approved) return false;
            if (def.comps == null) return true;
            for (int i = 0; i < def.comps.Count; i++)
            {
                HediffCompProperties properties = def.comps[i];
                Type type = properties != null ? properties.compClass : null;
                if (type != typeof(HediffComp_SeverityPerDay) && type != typeof(HediffComp_Disappears)
                    && type != typeof(HediffComp_DisappearsOnDeath)) return false;
                var disappearance = properties as HediffCompProperties_Disappears;
                if (disappearance != null && (disappearance.requiredMentalState != null || !disappearance.leaveFreshWounds)) return false;
            }
            return true;
        }

        public static bool IsSupported(Hediff effect)
        {
            if (effect == null || !IsSupportedDef(effect.def) || !AllowsPart(effect.def, effect.pawn, effect.Part)) return false;
            Type type = effect.GetType();
            if ((type != typeof(Hediff) && type != typeof(HediffWithComps)) || type != effect.def.hediffClass) return false;
            HediffWithComps withComps = effect as HediffWithComps;
            if ((withComps?.comps?.Count ?? 0) != (effect.def.comps?.Count ?? 0)) return false;
            if (withComps?.comps == null) return true;
            foreach (HediffComp comp in withComps.comps)
            {
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
            return part == null || (IsBrainBuff(def) && pawn?.health != null && part == pawn.health.hediffSet.GetBrain());
        }

        public static BodyPartRecord SourcePart(HediffDef def, Pawn human, BodyPartRecord part)
        {
            return part != null && IsBrainBuff(def) ? human.health.hediffSet.GetBrain() : null;
        }

        public static Hediff SourceFor(Pawn body, HediffDef def, bool mustBeVisible)
        {
            if (!IsSupportedDef(def)) return null;
            Pawn human = SharedFormIdentity.HumanFor(body);
            if (human?.health == null) return null;
            foreach (Hediff effect in human.health.hediffSet.hediffs)
                if (effect.def == def && IsSupported(effect) && (!mustBeVisible || effect.Visible)) return effect;
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
                if (effect?.pawn != animal || !IsSupported(effect) || !Finite(effect.Severity)) continue;
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
            if (human == null || !SharedFormBuffs.IsSupported(hediff)
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
            if (human == null || hediff == null || hediff.pawn != human || !SharedFormBuffs.IsSupported(hediff)
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
            if (__result == null) __result = SharedFormBuffs.SourceFor(___pawn, def, mustBeVisible);
        }
    }

    [HarmonyPatch(typeof(HediffSet), "HasHediff", new[] { typeof(HediffDef), typeof(bool) })]
    public static class Patch_SharedBuffPresence
    {
        public static void Postfix(Pawn ___pawn, HediffDef def, bool mustBeVisible, ref bool __result)
        {
            if (!__result) __result = SharedFormBuffs.SourceFor(___pawn, def, mustBeVisible) != null;
        }
    }
}
