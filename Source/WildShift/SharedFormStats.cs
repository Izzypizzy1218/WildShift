using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Project only declarative stat modifiers, never a human's final stat or
    // their Gene/Trait/Hediff objects. Native StatWorker supplies the animal's
    // base, equipment, injuries, age, skills, capacities and final processing.
    public static class SharedFormStats
    {
        private static readonly ConditionalWeakTable<Hediff, State> states = new ConditionalWeakTable<Hediff, State>();

        private struct GeneState
        {
            public Gene gene;
            public GeneDef def;
            public bool active;
        }

        private struct TraitState
        {
            public Trait trait;
            public TraitDef def;
            public TraitDegreeData data;
            public int degree;
            public bool active;
        }

        private sealed class State
        {
            public State() { }

            private Pawn original;
            private bool biotech, refreshing, dirty = true;
            private int generation;
            private List<GeneState> humanGenes, animalGenes;
            private List<TraitState> humanTraits, animalTraits;
            private HediffStage nativeStage, sharedStage;
            private readonly Dictionary<StatWorker, int> workerGenerations = new Dictionary<StatWorker, int>();

            public void Refresh(Hediff carrier)
            {
                if (refreshing) return;
                refreshing = true;
                try
                {
                    Pawn animal = carrier.pawn;
                    Pawn human = Original(animal);
                    bool activeBiotech = ModsConfig.BiotechActive;
                    List<Gene> hg = human != null && activeBiotech ? human.genes?.GenesListForReading : null;
                    List<Gene> ag = human != null && activeBiotech ? animal.genes?.GenesListForReading : null;
                    List<Trait> ht = human?.story?.traits?.allTraits;
                    List<Trait> at = human != null ? animal.story?.traits?.allTraits : null;
                    if (original == human && biotech == activeBiotech
                        && GenesMatch(hg, humanGenes) && GenesMatch(ag, animalGenes)
                        && TraitsMatch(ht, humanTraits) && TraitsMatch(at, animalTraits)) return;
                    original = human;
                    biotech = activeBiotech;
                    humanGenes = CaptureGenes(hg);
                    animalGenes = CaptureGenes(ag);
                    humanTraits = CaptureTraits(ht);
                    animalTraits = CaptureTraits(at);
                    dirty = true;
                    generation++;
                }
                finally { refreshing = false; }
            }

            public HediffStage Stage(Hediff carrier, HediffStage baseline)
            {
                Refresh(carrier);
                // A modded Gene.Active may itself read this animal's stats.
                // The incomplete projection must never recursively build itself.
                if (refreshing) return baseline;
                if (dirty || nativeStage != baseline)
                {
                    nativeStage = baseline;
                    sharedStage = Build(baseline);
                    dirty = false;
                }
                return sharedStage;
            }

            public void PrepareWorker(Hediff carrier, StatWorker worker)
            {
                Refresh(carrier);
                if (refreshing) return;
                int previous;
                if (!workerGenerations.TryGetValue(worker, out previous) || previous != generation)
                {
                    // Clear only this pawn and this worker, including native
                    // immutable/temporary caches, after its source changes.
                    worker.ClearCacheForThing(carrier.pawn);
                    workerGenerations[worker] = generation;
                }
            }

            private HediffStage Build(HediffStage baseline)
            {
                if (original == null) return baseline;
                // The vanilla WildShift carrier has no stage. Preserve a plain
                // stage added by another mod; do not silently scale inherited
                // genes by an unrelated stage's severity/stat multipliers.
                if (baseline != null && (baseline.multiplyStatChangesBySeverity
                    || baseline.statOffsetEffectMultiplier != null || baseline.statFactorEffectMultiplier != null)) return baseline;
                var offsets = Copy(baseline?.statOffsets);
                var factors = Copy(baseline?.statFactors);
                bool changed = false;
                var seenGenes = new HashSet<GeneDef>();
                for (int i = 0; i < humanGenes.Count; i++)
                {
                    GeneState gene = humanGenes[i];
                    if (!gene.active || gene.def == null || !seenGenes.Add(gene.def) || HasGene(animalGenes, gene.def)) continue;
                    // Native GeneDef list lookup uses the first modifier for
                    // each stat. ConditionalStatAffecters and custom gene
                    // behavior deliberately remain outside this bridge.
                    changed |= Merge(offsets, gene.def.statOffsets, false, true);
                    changed |= Merge(factors, gene.def.statFactors, true, true);
                }
                for (int i = 0; i < humanTraits.Count; i++)
                {
                    TraitState trait = humanTraits[i];
                    if (!trait.active || trait.data == null || HasTrait(animalTraits, trait.def, trait.degree)) continue;
                    // Trait.OffsetOfStat/MultiplierOfStat combine every entry.
                    changed |= Merge(offsets, trait.data.statOffsets, false, false);
                    changed |= Merge(factors, trait.data.statFactors, true, false);
                }
                if (!changed) return baseline;
                HediffStage result = baseline == null ? new HediffStage() : Gen.MemberwiseClone(baseline);
                result.statOffsets = offsets;
                result.statFactors = factors;
                return result;
            }
        }

        private static Pawn Original(Pawn animal)
        {
            if (animal == null || animal.Destroyed || animal.Dead) return null;
            // Sapient Animals can replace an active form with a Humanlike
            // pawn. The transformed carrier and its one-way ownership, rather
            // than the current race's intelligence, identify the active body.
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(animal);
            Pawn human = comp?.StoredPawn;
            return human != null && human != animal && !human.Destroyed && !human.Dead
                && human.holdingOwner == comp.GetDirectlyHeldThings()
                && human.RaceProps != null && human.RaceProps.Humanlike ? human : null;
        }

        private static bool GenesMatch(List<Gene> genes, List<GeneState> saved)
        {
            if ((genes?.Count ?? 0) != (saved?.Count ?? 0)) return false;
            if (genes == null) return true;
            for (int i = 0; i < genes.Count; i++)
                if (genes[i] != saved[i].gene || genes[i]?.def != saved[i].def || (genes[i] != null && genes[i].Active) != saved[i].active) return false;
            return true;
        }

        private static bool TraitsMatch(List<Trait> traits, List<TraitState> saved)
        {
            if ((traits?.Count ?? 0) != (saved?.Count ?? 0)) return false;
            if (traits == null) return true;
            for (int i = 0; i < traits.Count; i++)
            {
                Trait trait = traits[i];
                if (trait != saved[i].trait || trait?.def != saved[i].def || trait?.CurrentData != saved[i].data
                    || (trait?.Degree ?? 0) != saved[i].degree || (trait != null && !trait.Suppressed) != saved[i].active) return false;
            }
            return true;
        }

        private static List<GeneState> CaptureGenes(List<Gene> genes)
        {
            var result = new List<GeneState>(genes?.Count ?? 0);
            if (genes != null) for (int i = 0; i < genes.Count; i++)
                result.Add(new GeneState { gene = genes[i], def = genes[i]?.def, active = genes[i] != null && genes[i].Active });
            return result;
        }

        private static List<TraitState> CaptureTraits(List<Trait> traits)
        {
            var result = new List<TraitState>(traits?.Count ?? 0);
            if (traits != null) for (int i = 0; i < traits.Count; i++)
            {
                Trait trait = traits[i];
                result.Add(new TraitState { trait = trait, def = trait?.def, data = trait?.CurrentData,
                    degree = trait?.Degree ?? 0, active = trait != null && !trait.Suppressed });
            }
            return result;
        }

        private static bool HasGene(List<GeneState> genes, GeneDef def)
        {
            for (int i = 0; i < genes.Count; i++) if (genes[i].active && genes[i].def == def) return true;
            return false;
        }

        private static bool HasTrait(List<TraitState> traits, TraitDef def, int degree)
        {
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].active && traits[i].def == def && traits[i].degree == degree) return true;
            return false;
        }

        private static List<StatModifier> Copy(List<StatModifier> modifiers)
        {
            var result = new List<StatModifier>(modifiers?.Count ?? 0);
            if (modifiers != null) for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] != null) result.Add(new StatModifier { stat = modifiers[i].stat, value = modifiers[i].value });
            return result;
        }

        private static bool Merge(List<StatModifier> target, List<StatModifier> source, bool factor, bool firstOnly)
        {
            if (source == null) return false;
            bool changed = false;
            HashSet<StatDef> seen = firstOnly ? new HashSet<StatDef>() : null;
            for (int i = 0; i < source.Count; i++)
            {
                StatModifier modifier = source[i];
                if (modifier?.stat == null || (seen != null && !seen.Add(modifier.stat))
                    || modifier.value == (factor ? 1f : 0f)) continue;
                StatModifier existing = null;
                for (int j = 0; j < target.Count; j++) if (target[j].stat == modifier.stat) { existing = target[j]; break; }
                if (existing == null) target.Add(new StatModifier { stat = modifier.stat, value = modifier.value });
                else existing.value = factor ? existing.value * modifier.value : existing.value + modifier.value;
                changed = true;
            }
            return changed;
        }

        public static HediffStage StageFor(Hediff carrier, HediffStage baseline)
        {
            if (carrier == null || carrier.def != WildShiftDefOf.WildShift_Transformed) return baseline;
            return states.GetOrCreateValue(carrier).Stage(carrier, baseline);
        }

        public static void PrepareWorker(StatWorker worker, Thing thing)
        {
            Pawn animal = thing as Pawn;
            if (animal == null) return;
            Hediff carrier = TransformUtility.TryGetTransformedComp(animal)?.parent;
            if (carrier != null) states.GetOrCreateValue(carrier).PrepareWorker(carrier, worker);
        }
    }

    [HarmonyPatch(typeof(Hediff), "get_CurStage")]
    public static class Patch_SharedFormStatStage
    {
        public static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            __result = SharedFormStats.StageFor(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(StatWorker), "GetValue", new[] { typeof(Thing), typeof(bool), typeof(int) })]
    public static class Patch_SharedFormStatCache
    {
        public static void Prefix(StatWorker __instance, Thing thing)
        {
            SharedFormStats.PrepareWorker(__instance, thing);
        }
    }
}
