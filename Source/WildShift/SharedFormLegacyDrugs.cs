using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Before the shared-state bridge, an active animal could own its own native
    // drug state. Reconcile that saved state only when the bridge initializes;
    // never synthesize another dose or scan bodies from a per-tick callback.
    internal static class SharedFormLegacyDrugs
    {
        private static readonly AccessTools.FieldRef<Hediff, float> severity
            = AccessTools.FieldRefAccess<Hediff, float>("severityInt");
        private static readonly AccessTools.FieldRef<Need, float> needLevel
            = AccessTools.FieldRefAccess<Need, float>("curLevelInt");
        private static readonly AccessTools.FieldRef<HediffComp_HealPermanentWounds, int> healingTicks
            = AccessTools.FieldRefAccess<HediffComp_HealPermanentWounds, int>("ticksToHeal");
        private static readonly HashSet<Type> supportedComps = new HashSet<Type>
        {
            typeof(HediffComp_Disappears), typeof(HediffComp_SeverityPerDay),
            typeof(HediffComp_DrugEffectFactor), typeof(HediffComp_HealPermanentWounds)
        };

        public static void AdoptExisting(Pawn animal, Pawn human)
        {
            List<Hediff> animalEffects = animal.health?.hediffSet?.hediffs;
            List<Hediff> humanEffects = human.health?.hediffSet?.hediffs;
            if (animalEffects == null || humanEffects == null) return;
            bool changed = false;
            for (int i = animalEffects.Count - 1; i >= 0; i--)
            {
                Hediff effect = animalEffects[i];
                if (effect == null || effect.pawn != animal || !CanAdopt(effect)) continue;
                Hediff canonical = null;
                bool incompatible = false;
                foreach (Hediff existing in humanEffects)
                {
                    if (existing == null || existing.def != effect.def || existing.Part != null) continue;
                    if (existing.GetType() != effect.GetType() || !CanAdopt(existing)) { incompatible = true; break; }
                    if (canonical != null) { incompatible = true; break; }
                    canonical = existing;
                }
                if (incompatible || !AdoptChemicalNeed(effect, animal, human)) continue;
                if (canonical != null) MergeSavedState(canonical, effect);
                animalEffects.RemoveAt(i);
                if (canonical == null)
                {
                    // Direct list ownership transfer preserves the native ID,
                    // comp fields and age. PostAdd would be a new ingestion.
                    effect.pawn = human;
                    humanEffects.Add(effect);
                }
                changed = true;
            }
            if (!changed) return;
            animal.health.hediffSet.DirtyCache();
            human.health.hediffSet.DirtyCache();
            RefreshNeedFields(PhysicalNeeds(animal));
            RefreshNeedFields(human.needs);
            (animal.needs as SharedPawnNeedsTracker)?.Bind();
        }

        private static bool CanAdopt(Hediff effect)
        {
            if (effect == null || effect.def == null || effect.Part != null
                || effect.def.countsAsAddedPartOrImplant || effect.def.defaultInstallPart != null
                || effect.def.spawnThingOnRemoved != null) return false;
            Type type = effect.GetType();
            // Gene-backed dependency/craving may cache a body-specific gene.
            // The original human's own genes already have a separate bridge.
            if (type != typeof(Hediff) && type != typeof(HediffWithComps)
                && type != typeof(Hediff_High) && type != typeof(Hediff_Addiction)) return false;
            if (effect.def.hediffClass != type || !Finite(effect.Severity)) return false;
            HediffWithComps comps = effect as HediffWithComps;
            if ((comps?.comps?.Count ?? 0) != (effect.def.comps?.Count ?? 0)) return false;
            if (comps?.comps != null)
                for (int i = 0; i < comps.comps.Count; i++)
                    if (comps.comps[i] == null || effect.def.comps[i]?.compClass != comps.comps[i].GetType()
                        || !supportedComps.Contains(comps.comps[i].GetType())) return false;
            return !SharedFormBuffs.IsSupported(effect) && SharedFormHealth.IsSupported(effect);
        }

        private static void MergeSavedState(Hediff canonical, Hediff legacy)
        {
            // Native TryMergeWith adds a fresh dose and resets age. Reconcile
            // two saved doses conservatively instead: no sum, no rejuvenation,
            // and no stage-event callback on the hidden original during load.
            severity(canonical) = Math.Max(canonical.Severity, legacy.Severity);
            canonical.ageTicks = Math.Max(canonical.ageTicks, legacy.ageTicks);
            HediffWithComps target = canonical as HediffWithComps;
            HediffWithComps source = legacy as HediffWithComps;
            if (target?.comps == null || source?.comps == null) return;
            for (int i = 0; i < target.comps.Count; i++)
            {
                if (target.comps[i].GetType() == typeof(HediffComp_HealPermanentWounds)
                    && source.comps[i].GetType() == typeof(HediffComp_HealPermanentWounds))
                {
                    // Two old luciferium states have only one future healing
                    // interval after adoption. Keep the nearer pending heal;
                    // never fire it here or replay the random interval setup.
                    var pending = (HediffComp_HealPermanentWounds)target.comps[i];
                    var oldPending = (HediffComp_HealPermanentWounds)source.comps[i];
                    healingTicks(pending) = Math.Min(healingTicks(pending), healingTicks(oldPending));
                    continue;
                }
                if (target.comps[i].GetType() != typeof(HediffComp_Disappears)
                    || source.comps[i].GetType() != typeof(HediffComp_Disappears)) continue;
                var remaining = (HediffComp_Disappears)target.comps[i];
                var old = (HediffComp_Disappears)source.comps[i];
                if (old.ticksToDisappear <= remaining.ticksToDisappear) continue;
                remaining.ticksToDisappear = old.ticksToDisappear;
                remaining.disappearsAfterTicks = Math.Max(remaining.disappearsAfterTicks, old.disappearsAfterTicks);
            }
        }

        private static bool AdoptChemicalNeed(Hediff effect, Pawn animal, Pawn human)
        {
            NeedDef def = effect.def.chemicalNeed;
            if (def == null) return true;
            Pawn_NeedsTracker animalNeeds = PhysicalNeeds(animal);
            if (def.needClass != typeof(Need_Chemical) || animalNeeds == null || human.needs == null) return false;
            // Do not steal a need still used by an unsupported custom effect.
            foreach (Hediff other in animal.health.hediffSet.hediffs)
                if (other?.def?.chemicalNeed == def && !CanAdopt(other)) return false;
            foreach (Hediff other in human.health.hediffSet.hediffs)
                if (other?.def?.chemicalNeed == def && !CanAdopt(other)) return false;
            Need old = animalNeeds.TryGetNeed(def);
            Need existing = human.needs.TryGetNeed(def);
            if ((old != null && (old.GetType() != typeof(Need_Chemical) || !Finite(old.CurLevel)))
                || (existing != null && (existing.GetType() != typeof(Need_Chemical) || !Finite(existing.CurLevel)))) return false;
            if (old == null) return existing != null; // No saved level to guess.
            if (existing == null)
            {
                // Native Need.pawn is readonly and caches body components. A
                // new exact-native need keeps those references valid. Its only
                // saved fields are Def and level (1.6/1.7); restore both without
                // consuming random state or firing a withdrawal notification.
                Rand.PushState();
                try { existing = new Need_Chemical(human) { def = def }; }
                finally { Rand.PopState(); }
                needLevel(existing) = old.CurLevel;
                human.needs.AllNeeds.Add(existing);
            }
            else needLevel(existing) = Math.Min(existing.CurLevel, old.CurLevel);
            animalNeeds.AllNeeds.Remove(old);
            return true;
        }

        private static Pawn_NeedsTracker PhysicalNeeds(Pawn animal)
        {
            return (animal?.needs as SharedPawnNeedsTracker)?.BodyNeeds ?? animal?.needs;
        }

        private static void RefreshNeedFields(Pawn_NeedsTracker tracker)
        {
            if (tracker == null) return;
            tracker.BindDirectNeedFields();
            tracker.MiscNeeds?.Clear();
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
