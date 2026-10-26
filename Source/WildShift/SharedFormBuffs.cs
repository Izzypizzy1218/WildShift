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
            // Transfer the exact native state without replaying PostAdd (which
            // could reroll severity/timers). Both lists have one owner only.
            var existing = new List<Hediff>(animal.health.hediffSet.hediffs);
            bool changed = false;
            foreach (Hediff effect in existing)
            {
                if (!IsSupported(effect)) continue;
                animal.health.hediffSet.hediffs.Remove(effect);
                effect.Part = SourcePart(effect.def, human, effect.Part);
                effect.pawn = human;
                bool merged = false;
                foreach (Hediff source in human.health.hediffSet.hediffs)
                    if (source.TryMergeWith(effect)) { merged = true; break; }
                if (!merged) human.health.hediffSet.hediffs.Add(effect);
                changed = true;
            }
            if (changed)
            {
                animal.health.hediffSet.DirtyCache();
                human.health.hediffSet.DirtyCache();
            }
        }
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
