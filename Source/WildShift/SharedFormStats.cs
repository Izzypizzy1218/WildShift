using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Project declarative stat and health modifiers, never a human's final
    // values or their Gene/Trait/Hediff objects. Native calculations retain
    // the animal's base, equipment, injuries, age, skills and capacities.
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

        private sealed class ConditionalState
        {
            public GeneDef gene;
            public ConditionalStatAffecter affecter;
            public bool applies;
        }

        // A personal thought query temporarily exposes the person's ideology
        // on the animal. Its identity can change without changing any stat
        // modifier. Observe actual declarative payloads only after the cheap
        // version changes; stable reads neither allocate nor re-evaluate them.
        private sealed class SystemsPayload
        {
            public bool known;
            private readonly List<StatModifier> projectedOffsets = new List<StatModifier>();
            private readonly List<StatModifier> projectedFactors = new List<StatModifier>();
            private readonly List<StatModifier> nativeOffsets = new List<StatModifier>();
            private readonly List<StatModifier> nativeFactors = new List<StatModifier>();
            private readonly List<StatModifier> inspirationOffsets = new List<StatModifier>();
            private readonly List<StatModifier> inspirationFactors = new List<StatModifier>();
            private readonly List<Apparel> sourceApparel = new List<Apparel>();
            private ThingWithComps sourceWeapon;
            private Ideo sourceIdeo;
            private InspirationDef sourceInspiration, nativeInspiration;
            private Precept_Role sourceRole, nativeRole;
            private readonly List<PreceptDef> sourcePrecepts = new List<PreceptDef>();

            public void Capture(Pawn body, Pawn human)
            {
                known = false;
                projectedOffsets.Clear(); projectedFactors.Clear();
                nativeOffsets.Clear(); nativeFactors.Clear();
                inspirationOffsets.Clear(); inspirationFactors.Clear();
                sourceApparel.Clear(); sourceWeapon = null;
                sourceIdeo = null; sourceInspiration = nativeInspiration = null;
                sourceRole = nativeRole = null; sourcePrecepts.Clear();
                if (human == null) { known = true; return; }
                // Do not evaluate a conditional affecter or fallback gear stat
                // an extra time. Retain conservative invalidation for those
                // consumers, including native-only physical render scopes.
                if (SharedFormGear.IsNativeOnly || HasConditions(human.Ideo) || HasConditions(body.Ideo)
                    || HasFallbackGear(body, human)) return;
                // Native-visible gear is absent from the projected stage, but
                // replacing an item can change a native equipped-stat bonus.
                // Preserve membership conservatively without evaluating gear
                // stat callbacks a second time. HP/quality-only changes stay
                // on the existing native-stat generation path.
                List<Apparel> apparel = human.apparel?.WornApparel;
                if (apparel != null) sourceApparel.AddRange(apparel);
                sourceWeapon = human.equipment?.Primary;
                // Limit equivalence to temporary body-ideology views. Actual
                // source belief, role, inspiration and item replacements keep
                // their conservative invalidation even with equal numbers.
                sourceIdeo = human.Ideo;
                sourceInspiration = human.mindState?.inspirationHandler?.CurStateDef;
                nativeInspiration = body.mindState?.inspirationHandler?.CurStateDef;
                sourceRole = sourceIdeo?.GetRole(human);
                nativeRole = body.Ideo?.GetRole(body);
                if (sourceIdeo != null)
                    for (int i = 0; i < sourceIdeo.PreceptsListForReading.Count; i++)
                        sourcePrecepts.Add(sourceIdeo.PreceptsListForReading[i].def);
                SharedFormSystems.AppendModifiers(body, projectedOffsets, projectedFactors);
                InspirationDef inspiration = body.mindState?.inspirationHandler?.CurStateDef;
                // Native inspiration offsets apply after gene/health factors;
                // ideology/role offsets apply before those factors. Keep the
                // categories separate even when their numbers are identical.
                Append(inspirationOffsets, inspiration?.statOffsets, false);
                Append(inspirationFactors, inspiration?.statFactors, true);
                Ideo ideo = body.Ideo;
                if (ideo != null)
                {
                    List<Precept> precepts = ideo.PreceptsListForReading;
                    for (int i = 0; i < precepts.Count; i++)
                    {
                        Append(nativeOffsets, precepts[i].def.statOffsets, false);
                        Append(nativeFactors, precepts[i].def.statFactors, true);
                    }
                    List<RoleEffect> effects = ideo.GetRole(body)?.def.roleEffects;
                    if (effects != null) for (int i = 0; i < effects.Count; i++)
                    {
                        RoleEffect_PawnStatOffset offset = effects[i] as RoleEffect_PawnStatOffset;
                        RoleEffect_PawnStatFactor factor = effects[i] as RoleEffect_PawnStatFactor;
                        if (offset?.statDef != null && offset.modifier != 0f)
                            nativeOffsets.Add(new StatModifier { stat = offset.statDef, value = offset.modifier });
                        if (factor?.statDef != null && factor.modifier != 1f)
                            nativeFactors.Add(new StatModifier { stat = factor.statDef, value = factor.modifier });
                    }
                }
                known = true;
            }

            public bool SameAs(SystemsPayload other)
            {
                return known && other.known
                    && Same(projectedOffsets, other.projectedOffsets) && Same(projectedFactors, other.projectedFactors)
                    && Same(nativeOffsets, other.nativeOffsets) && Same(nativeFactors, other.nativeFactors)
                    && Same(inspirationOffsets, other.inspirationOffsets) && Same(inspirationFactors, other.inspirationFactors)
                    && SameSources(other);
            }

            private bool SameSources(SystemsPayload other)
            {
                if (sourceIdeo != other.sourceIdeo || sourceInspiration != other.sourceInspiration
                    || nativeInspiration != other.nativeInspiration || sourceRole != other.sourceRole || nativeRole != other.nativeRole
                    || sourceWeapon != other.sourceWeapon || sourceApparel.Count != other.sourceApparel.Count
                    || sourcePrecepts.Count != other.sourcePrecepts.Count) return false;
                for (int i = 0; i < sourceApparel.Count; i++) if (sourceApparel[i] != other.sourceApparel[i]) return false;
                for (int i = 0; i < sourcePrecepts.Count; i++) if (sourcePrecepts[i] != other.sourcePrecepts[i]) return false;
                return true;
            }

            private static bool HasConditions(Ideo ideo)
            {
                List<Precept> precepts = ideo?.PreceptsListForReading;
                if (precepts != null) for (int i = 0; i < precepts.Count; i++)
                    if (precepts[i].def.conditionalStatAffecters?.Count > 0) return true;
                return false;
            }

            private static bool HasFallbackGear(Pawn body, Pawn human)
            {
                List<Apparel> source = human.apparel?.WornApparel, native = body.apparel?.WornApparel;
                if (source != null) for (int i = 0; i < source.Count; i++)
                    if (native == null || !native.Contains(source[i])) return true;
                ThingWithComps weapon = human.equipment?.Primary;
                return weapon != null && body.equipment?.Primary != weapon;
            }

            private static void Append(List<StatModifier> target, List<StatModifier> source, bool factor)
            {
                if (source == null) return;
                for (int i = 0; i < source.Count; i++)
                {
                    StatModifier modifier = source[i];
                    if (modifier?.stat == null || modifier.value == (factor ? 1f : 0f)) continue;
                    // Native precept/inspiration list lookups use the first
                    // entry, including a first entry with a neutral value.
                    bool duplicate = false;
                    for (int j = 0; j < i; j++) if (source[j]?.stat == modifier.stat) { duplicate = true; break; }
                    if (!duplicate) target.Add(new StatModifier { stat = modifier.stat, value = modifier.value });
                }
            }

            private static bool Same(List<StatModifier> first, List<StatModifier> second)
            {
                if (first.Count != second.Count) return false;
                for (int i = 0; i < first.Count; i++)
                    if (first[i].stat != second[i].stat || !first[i].value.Equals(second[i].value)) return false;
                return true;
            }
        }

        private sealed class State
        {
            public State() { }

            private Pawn original;
            private Pawn activeBody;
            private bool biotech, refreshing, dirty = true;
            private int generation;
            private int healthGeneration, preparedHealthGeneration = -1;
            private int systemsVersion = int.MinValue, projectedSystemsVersion = int.MinValue, anatomyVersion = int.MinValue;
            private SystemsPayload systemsPayload = new SystemsPayload(), nextSystemsPayload = new SystemsPayload();
            private List<GeneState> humanGenes, animalGenes;
            private List<TraitState> humanTraits, animalTraits;
            private List<ConditionalState> conditions = new List<ConditionalState>();
            private HediffStage nativeStage, sharedStage;
            private readonly Dictionary<StatWorker, int> workerGenerations = new Dictionary<StatWorker, int>();

            public void Refresh(Hediff carrier)
            {
                if (refreshing) return;
                refreshing = true;
                try
                {
                    Pawn animal = carrier.pawn;
                    activeBody = animal;
                    Pawn human = Original(animal);
                    bool activeBiotech = ModsConfig.BiotechActive;
                    List<Gene> hg = human != null && activeBiotech ? human.genes?.GenesListForReading : null;
                    List<Gene> ag = human != null && activeBiotech ? animal.genes?.GenesListForReading : null;
                    List<Trait> ht = human?.story?.traits?.allTraits;
                    List<Trait> at = human != null ? animal.story?.traits?.allTraits : null;
                    bool sourceChanged = !(original == human && biotech == activeBiotech
                        && GenesMatch(hg, humanGenes) && GenesMatch(ag, animalGenes)
                        && TraitsMatch(ht, humanTraits) && TraitsMatch(at, animalTraits));
                    int nextProjectedSystems=0;
                    int nextSystems = human != null ? SharedFormSystems.ModifierVersion(animal,out nextProjectedSystems) : 0;
                    int nextAnatomy = human != null ? SharedFormAnatomy.ModifierVersion(animal) : 0;
                    bool externalStatsChanged = systemsVersion != nextSystems;
                    bool systemsProjectionChanged = projectedSystemsVersion != nextProjectedSystems;
                    bool anatomyChanged = anatomyVersion != nextAnatomy;
                    bool systemsEffectsChanged = systemsProjectionChanged;
                    if (systemsProjectionChanged)
                    {
                        // Skip only a proven declarative no-op. Genuine stat
                        // changes retain full invalidation because a custom
                        // capacity worker can itself depend on those stats.
                        // In particular, do not clear capacity caches during
                        // a nested no-op thought query: native DirtyCache
                        // would reset its in-progress recursion guards.
                        nextSystemsPayload.Capture(animal, human);
                        systemsEffectsChanged = !nextSystemsPayload.SameAs(systemsPayload);
                        SystemsPayload previous = systemsPayload;
                        systemsPayload = nextSystemsPayload;
                        nextSystemsPayload = previous;
                    }
                    bool externalProjectionChanged = systemsProjectionChanged || anatomyChanged;
                    bool healthProjectionChanged = systemsEffectsChanged || anatomyChanged;
                    systemsVersion = nextSystems;
                    projectedSystemsVersion = nextProjectedSystems;
                    anatomyVersion = nextAnatomy;
                    if (sourceChanged)
                    {
                        original = human;
                        biotech = activeBiotech;
                        humanGenes = CaptureGenes(hg);
                        animalGenes = CaptureGenes(ag);
                        humanTraits = CaptureTraits(ht);
                        animalTraits = CaptureTraits(at);
                        CaptureConditions();
                        healthGeneration++;
                    }
                    // Conditions belong to the active body, not the stored
                    // human. Recheck only this form's relevant affecters on
                    // queries: movement, sunlight or clothes can change twice
                    // in one tick, without changing its gene membership.
                    bool conditionChanged = RefreshConditions(animal);
                    if (healthProjectionChanged) healthGeneration++;
                    if (sourceChanged || conditionChanged || healthProjectionChanged)
                    {
                        dirty = true;
                    }
                    // Native gear HP, quality and persona traits can change
                    // cached pawn stats without changing the shared stage or
                    // any capacity modifier. Keep their invalidation immediate
                    // without rebuilding that stage or dirtying all health.
                    if (sourceChanged || conditionChanged || externalProjectionChanged || externalStatsChanged)
                    {
                        generation++;
                    }
                }
                finally { refreshing = false; }
            }

            private void CaptureConditions()
            {
                conditions.Clear();
                var seen = new HashSet<GeneDef>();
                for (int i = 0; i < humanGenes.Count; i++)
                {
                    GeneState gene = humanGenes[i];
                    if (!gene.active || gene.def == null || !seen.Add(gene.def)
                        || gene.def.conditionalStatAffecters == null) continue;
                    for (int j = 0; j < gene.def.conditionalStatAffecters.Count; j++)
                    {
                        ConditionalStatAffecter affecter = gene.def.conditionalStatAffecters[j];
                        if (affecter == null || ((affecter.statOffsets == null || affecter.statOffsets.Count == 0)
                            && (affecter.statFactors == null || affecter.statFactors.Count == 0))) continue;
                        conditions.Add(new ConditionalState { gene = gene.def, affecter = affecter });
                    }
                }
            }

            private bool RefreshConditions(Pawn animal)
            {
                if (conditions.Count == 0) return false;
                StatRequest request = StatRequest.For(animal);
                bool changed = false;
                for (int i = 0; i < conditions.Count; i++)
                {
                    ConditionalState condition = conditions[i];
                    bool applies = condition.affecter.Applies(request);
                    if (condition.applies == applies) continue;
                    condition.applies = applies;
                    changed = true;
                }
                return changed;
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

            public void PrepareHealth(Hediff carrier)
            {
                Refresh(carrier);
                if (refreshing || preparedHealthGeneration == healthGeneration) return;
                // Pain and damage-factor queries may return native caches
                // before reading CurStage. Mark this generation first because
                // DirtyCache also refreshes other health consumers.
                preparedHealthGeneration = healthGeneration;
                carrier.pawn.health.hediffSet.DirtyCache();
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
                float painOffset = baseline?.painOffset ?? 0f;
                float painFactor = baseline?.painFactor ?? 1f;
                var damageFactors = CopyDamageFactors(baseline?.damageFactors);
                var capacities = CopyCapacities(baseline?.capMods);
                bool changed = false;
                var seenGenes = new HashSet<GeneDef>();
                for (int i = 0; i < humanGenes.Count; i++)
                {
                    GeneState gene = humanGenes[i];
                    if (!gene.active || gene.def == null || !seenGenes.Add(gene.def) || HasGene(animalGenes, gene.def)) continue;
                    // Native GeneDef list lookup uses the first modifier for
                    // each stat, both in unconditional and conditional lists.
                    changed |= Merge(offsets, gene.def.statOffsets, false, true);
                    changed |= Merge(factors, gene.def.statFactors, true, true);
                    changed |= MergePain(ref painOffset, ref painFactor, gene.def.painOffset, gene.def.painFactor);
                    changed |= AppendDamageFactors(damageFactors, gene.def.damageFactors);
                    changed |= AppendCapacities(capacities, gene.def.capMods);
                }
                for (int i = 0; i < conditions.Count; i++)
                {
                    ConditionalState condition = conditions[i];
                    if (!condition.applies || HasGene(animalGenes, condition.gene)) continue;
                    changed |= Merge(offsets, condition.affecter.statOffsets, false, true);
                    changed |= Merge(factors, condition.affecter.statFactors, true, true);
                }
                for (int i = 0; i < humanTraits.Count; i++)
                {
                    TraitState trait = humanTraits[i];
                    if (trait.data == null) continue;
                    // Native HediffSet.CalculatePain reads every trait's pain
                    // fields even when that trait's stat effects are suppressed.
                    if (!HasPainTrait(animalTraits, trait.def, trait.degree))
                        changed |= MergePain(ref painOffset, ref painFactor, trait.data.painOffset, trait.data.painFactor);
                    if (!trait.active || HasTrait(animalTraits, trait.def, trait.degree)) continue;
                    // Trait.OffsetOfStat/MultiplierOfStat combine every entry.
                    changed |= Merge(offsets, trait.data.statOffsets, false, false);
                    changed |= Merge(factors, trait.data.statFactors, true, false);
                }
                changed |= SharedFormSystems.AppendModifiers(activeBody, offsets, factors);
                if (!changed) return baseline;
                HediffStage result = baseline == null ? new HediffStage() : Gen.MemberwiseClone(baseline);
                result.statOffsets = offsets;
                result.statFactors = factors;
                result.painOffset = painOffset;
                result.painFactor = painFactor;
                result.damageFactors = damageFactors;
                result.capMods = capacities;
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

        private static bool HasPainTrait(List<TraitState> traits, TraitDef def, int degree)
        {
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].def == def && traits[i].degree == degree) return true;
            return false;
        }

        private static bool MergePain(ref float offset, ref float factor, float sourceOffset, float sourceFactor)
        {
            offset += sourceOffset;
            factor *= sourceFactor;
            return sourceOffset != 0f || sourceFactor != 1f;
        }

        private static List<DamageFactor> CopyDamageFactors(List<DamageFactor> source)
        {
            var result = new List<DamageFactor>(source?.Count ?? 0);
            AppendDamageFactors(result, source);
            return result;
        }

        private static List<PawnCapacityModifier> CopyCapacities(List<PawnCapacityModifier> source)
        {
            var result = new List<PawnCapacityModifier>(source?.Count ?? 0);
            AppendCapacities(result, source);
            return result;
        }

        private static bool AppendCapacities(List<PawnCapacityModifier> target, List<PawnCapacityModifier> source)
        {
            if (source == null) return false;
            bool changed = false;
            for (int i = 0; i < source.Count; i++)
            {
                PawnCapacityModifier modifier = source[i];
                if (modifier?.capacity == null) continue;
                target.Add(SharedFormAnatomy.Clone(modifier));
                changed = true;
            }
            return changed;
        }

        private static bool AppendDamageFactors(List<DamageFactor> target, List<DamageFactor> source)
        {
            if (source == null) return false;
            bool changed = false;
            // Native gene and HediffStage lists multiply every matching entry.
            for (int i = 0; i < source.Count; i++)
            {
                DamageFactor modifier = source[i];
                if (modifier?.damageDef == null || modifier.factor == 1f) continue;
                target.Add(new DamageFactor { damageDef = modifier.damageDef, factor = modifier.factor });
                changed = true;
            }
            return changed;
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

        public static void PrepareHealth(Pawn animal)
        {
            Hediff carrier = TransformUtility.TryGetTransformedComp(animal)?.parent;
            if (carrier != null) states.GetOrCreateValue(carrier).PrepareHealth(carrier);
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

    [HarmonyPatch(typeof(HediffSet), "get_PainTotal")]
    public static class Patch_SharedFormPainCache
    {
        public static void Prefix(Pawn ___pawn) { SharedFormStats.PrepareHealth(___pawn); }
    }

    [HarmonyPatch(typeof(HediffSet), "FactorForDamage", new[] { typeof(DamageInfo) })]
    public static class Patch_SharedFormDamageCache
    {
        public static void Prefix(Pawn ___pawn) { SharedFormStats.PrepareHealth(___pawn); }
    }
}
