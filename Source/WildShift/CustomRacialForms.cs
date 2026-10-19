using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WildShift
{
    public sealed class CustomRacialFormRule : IExposable
    {
        public bool xenotype;
        public string targetDefName;
        public float chance = 1f;
        public List<string> animalKindDefNames = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref xenotype, "xenotype", false);
            Scribe_Values.Look(ref targetDefName, "targetDefName");
            Scribe_Values.Look(ref chance, "chance", 1f);
            Scribe_Collections.Look(ref animalKindDefNames, "animalKindDefNames", LookMode.Value);
            Normalize();
        }

        public void Normalize()
        {
            targetDefName = targetDefName == null ? null : targetDefName.Trim();
            chance = float.IsNaN(chance) || float.IsInfinity(chance) ? 1f : Math.Max(0f, Math.Min(1f, chance));
            if (animalKindDefNames == null) animalKindDefNames = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < animalKindDefNames.Count;)
            {
                string name = animalKindDefNames[i];
                name = name == null ? null : name.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) animalKindDefNames.RemoveAt(i);
                else { animalKindDefNames[i] = name; i++; }
            }
            // Keep names from absent mods, so temporarily disabling a mod does
            // not erase a player's saved rules.
        }
    }

    public static class CustomRacialForms
    {
        public static void NormalizeRules(List<CustomRacialFormRule> rules)
        {
            if (rules == null) return;
            var races = new HashSet<string>(StringComparer.Ordinal);
            var xenotypes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rules.Count;)
            {
                CustomRacialFormRule rule = rules[i];
                if (rule != null) rule.Normalize();
                if (rule == null || string.IsNullOrEmpty(rule.targetDefName)
                    || !(rule.xenotype ? xenotypes : races).Add(rule.targetDefName)) rules.RemoveAt(i);
                else i++;
            }
        }

        public static CustomRacialFormRule FindMatchingRule(Pawn pawn)
        {
            if (WildShiftMod.Settings == null || !WildShiftMod.Settings.customRacialFormsEnabled
                || pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return null;
            List<CustomRacialFormRule> rules = WildShiftMod.Settings.customRacialForms;
            if (rules == null) return null;
            string xenotypeName = pawn.genes != null && pawn.genes.Xenotype != null ? pawn.genes.Xenotype.defName : null;
            CustomRacialFormRule raceRule = null;
            for (int i = 0; i < rules.Count; i++)
            {
                CustomRacialFormRule rule = rules[i];
                if (rule == null || string.IsNullOrEmpty(rule.targetDefName)) continue;
                if (rule.xenotype)
                {
                    // An explicit xenotype rule wins even when its chance
                    // fails or all of its optional animal definitions are gone.
                    if (rule.targetDefName == xenotypeName) return rule;
                }
                else if (raceRule == null && pawn.def != null && rule.targetDefName == pawn.def.defName) raceRule = rule;
            }
            return raceRule;
        }

        // Called only for a new assignment. Existing pawns retain their saved
        // form and never roll again when the settings window changes.
        public static bool TryChoose(Pawn pawn, out PawnKindDef kind)
        {
            kind = null;
            CustomRacialFormRule rule = FindMatchingRule(pawn);
            if (rule == null || rule.animalKindDefNames == null) return false;
            float chance = rule.chance;
            if (float.IsNaN(chance) || float.IsInfinity(chance) || chance <= 0f) return false;
            var choices = new List<PawnKindDef>();
            var seen = new HashSet<PawnKindDef>();
            for (int i = 0; i < rule.animalKindDefNames.Count; i++)
            {
                string name = rule.animalKindDefNames[i];
                PawnKindDef candidate = string.IsNullOrEmpty(name) ? null : DefDatabase<PawnKindDef>.GetNamedSilentFail(name);
                if (IsSelectable(candidate) && seen.Add(candidate)) choices.Add(candidate);
            }
            if (choices.Count == 0 || (chance < 1f && !Rand.Chance(chance))) return false;
            kind = choices.Count == 1 ? choices[0] : choices[Rand.Range(0, choices.Count)];
            return true;
        }

        // This is shared by the settings selector and assignment validation.
        // Custom rules can use the normal pool and supported small racial
        // forms, but cannot bypass insect settings or unsafe hybrid exclusions.
        public static bool IsSelectable(PawnKindDef kind)
        {
            RaceProperties race = kind != null && kind.race != null ? kind.race.race : null;
            if (race == null || !race.Animal || race.Humanlike || race.IsMechanoid || race.IsAnomalyEntity || race.IsDrone
                || GeneticsCompatibility.Excluded(kind.race)) return false;
            if (race.FleshType == FleshTypeDefOf.Insectoid
                && (WildShiftMod.Settings == null || !WildShiftMod.Settings.allowInsectoids)) return false;
            return AnimalPool.IsEligible(kind) || RacialAnimalForms.IsPresetForm(kind);
        }
    }
}
