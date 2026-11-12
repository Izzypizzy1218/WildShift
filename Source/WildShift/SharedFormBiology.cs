using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WildShift
{
    // Mod authors may opt a reviewed gene callback into active-body execution.
    // Plain declarative GeneDef modifiers do not require this extension.
    public sealed class SharedFormGeneCallbacksExtension : DefModExtension { }

    // The source pawn owns every gene, trait and need. The active body only
    // exposes non-owning read views; its native gene lists remain untouched.
    // In particular, no copied Gene is added and no PostAdd is replayed.
    public static class SharedFormBiology
    {
        private sealed class State
        {
            public Pawn human;
            public Pawn_StoryTracker bodyStory;
            public Pawn_NeedsTracker bodyNeeds;
            public Pawn_InteractionsTracker bodyInteractions;
            public readonly List<Gene> genes = new List<Gene>();
            public readonly List<Gene> xenogenes = new List<Gene>();
            public readonly List<Gene> endogenes = new List<Gene>();
            public int signature = int.MinValue;
            public int lastTick = int.MinValue, lastInterval = int.MinValue, lastInteraction = int.MinValue;
            public int lastBindTick = int.MinValue;
            public bool binding, releasing, initialized;
        }

        private static readonly ConditionalWeakTable<Pawn, State> states = new ConditionalWeakTable<Pawn, State>();
        private static readonly FieldInfo geneXeno = AccessTools.Field(typeof(Pawn_GeneTracker), "xenogenes");
        private static readonly FieldInfo geneEndo = AccessTools.Field(typeof(Pawn_GeneTracker), "endogenes");
        private static readonly MethodInfo recacheGeneNeeds = AccessTools.Method(typeof(Pawn_GeneTracker), "RecacheNeeds");
        private static readonly FieldInfo needsPawn = AccessTools.Field(typeof(Pawn_NeedsTracker), "pawn");
        private static readonly FieldInfo needList = AccessTools.Field(typeof(Pawn_NeedsTracker), "needs");
        private static readonly FieldInfo interactionsPawn = AccessTools.Field(typeof(Pawn_InteractionsTracker), "pawn");
        private static readonly Dictionary<Type, FieldInfo> pawnFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, bool[]> nativeCallbacks = new Dictionary<Type, bool[]>();
        private sealed class NeedLevel { public float percentage; }
        private static readonly ConditionalWeakTable<Need, NeedLevel> savedNeedLevels = new ConditionalWeakTable<Need, NeedLevel>();
        [ThreadStatic] private static int nativeGenesOnly;

        public static bool SharesNeeds(Pawn animal)
        {
            return animal?.needs is SharedPawnNeedsTracker && SharedFormIdentity.HumanFor(animal) != null;
        }

        public static void Initialize(Pawn animal)
        {
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null) return;
            State state = states.GetOrCreateValue(animal);
            state.releasing = false;
            state.signature = int.MinValue;
            state.lastBindTick = int.MinValue;
            if (!state.initialized)
            {
                state.initialized = true;
                state.bodyStory = animal.story;
                state.bodyNeeds = (animal.needs as SharedPawnNeedsTracker)?.BodyNeeds ?? animal.needs;
                state.bodyInteractions = animal.interactions;
            }
            state.human = human;
            if (ModsConfig.BiotechActive && animal.genes == null)
                animal.genes = new Pawn_GeneTracker(animal);
            if (human.story?.traits != null)
            {
                if (animal.story == null)
                {
                    animal.story = new Pawn_StoryTracker(animal);
                    animal.story.bodyType = human.story.bodyType;
                    animal.story.headType = human.story.headType;
                    animal.story.hairDef = human.story.hairDef;
                }
                SharedPawnTraitSet traits = animal.story.traits as SharedPawnTraitSet;
                if (traits == null)
                    animal.story.traits = new SharedPawnTraitSet(animal, animal.story.traits);
            }
            if (human.needs != null && !(animal.needs is SharedPawnNeedsTracker))
                animal.needs = new SharedPawnNeedsTracker(animal, state.bodyNeeds);
            if (human.interactions != null)
            {
                animal.interactions = human.interactions;
                interactionsPawn.SetValue(human.interactions, animal);
            }
            Bind(animal);
        }

        public static void Bind(Pawn animal)
        {
            if (animal == null) return;
            State state;
            if (!states.TryGetValue(animal, out state) || state.releasing || state.binding) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (state.lastBindTick == now) return;
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null) { Release(animal); return; }
            state.binding = true;
            try
            {
                state.human = human;
                state.lastBindTick = now;
                int signature = Signature(human, animal);
                if (signature != state.signature)
                {
                    state.signature = signature;
                    (animal.story?.traits as SharedPawnTraitSet)?.Bind();
                    if (animal.genes != null)
                    {
                        BuildGenes(state.genes, animal.genes, human.genes, 0);
                        BuildGenes(state.xenogenes, animal.genes, human.genes, 1);
                        BuildGenes(state.endogenes, animal.genes, human.genes, 2);
                    }
                    ClearGeneCaches(animal.genes);
                    (animal.story?.traits as SharedPawnTraitSet)?.Recache();
                    animal.health?.hediffSet.DirtyCache();
                    animal.Notify_DisabledWorkTypesChanged();
                    human.needs?.AddOrRemoveNeedsAsAppropriate();
                }
                (animal.needs as SharedPawnNeedsTracker)?.Bind();
            }
            finally { state.binding = false; }
        }

        private static Pawn ActiveBodyFor(Pawn pawn)
        {
            return SharedFormIdentity.ActiveFormFor(pawn)
                ?? (SharedFormIdentity.HumanFor(pawn) != null ? pawn : null);
        }

        public static void InvalidateSource(Pawn pawn)
        {
            Pawn animal = ActiveBodyFor(pawn);
            State state;
            if (animal != null && states.TryGetValue(animal, out state))
            { state.signature = int.MinValue; state.lastBindTick = int.MinValue; }
        }

        public static void SourceMutationCompleted(Pawn pawn)
        {
            Pawn animal = ActiveBodyFor(pawn);
            State state;
            if (animal == null || !states.TryGetValue(animal, out state) || state.releasing) return;
            InvalidateSource(pawn);
            if (state.binding) return;
            // Native mutation notifications may themselves query and cache
            // stats before the canonical operation has finished. Rebuild the
            // final non-owning view before clearing this body's entries, so
            // the next same-tick query cannot reuse an intermediate result.
            Bind(animal);
            foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
                stat.Worker.ClearCacheForThing(animal);
        }

        public static void Release(Pawn animal)
        {
            State state;
            if (animal == null || !states.TryGetValue(animal, out state) || state.releasing) return;
            state.releasing = true;
            Pawn human = state.human;
            (animal.needs as SharedPawnNeedsTracker)?.RestoreOwners();
            if (human?.interactions != null) interactionsPawn.SetValue(human.interactions, human);
            if (animal.interactions == human?.interactions) animal.interactions = state.bodyInteractions;
            SharedPawnNeedsTracker needs = animal.needs as SharedPawnNeedsTracker;
            if (needs != null) animal.needs = needs.BodyNeeds;
            if (state.bodyStory == null) animal.story = null;
            else if (animal.story?.traits is SharedPawnTraitSet)
                animal.story.traits = ((SharedPawnTraitSet)animal.story.traits).BodyTraits;
            state.genes.Clear(); state.xenogenes.Clear(); state.endogenes.Clear();
            ClearGeneCaches(animal.genes);
        }

        public static List<Gene> ReadGenes(Pawn_GeneTracker tracker, List<Gene> native, int kind)
        {
            if (nativeGenesOnly != 0 || tracker?.pawn == null) return native;
            State state;
            if (!states.TryGetValue(tracker.pawn, out state) || state.releasing
                || SharedFormIdentity.HumanFor(tracker.pawn) == null) return native;
            if (!state.binding) Bind(tracker.pawn);
            return kind == 1 ? state.xenogenes : kind == 2 ? state.endogenes : state.genes;
        }

        public static List<Gene> RawBodyGenesFor(Pawn animal)
        {
            var result = new List<Gene>();
            if (animal?.genes == null) return result;
            List<Gene> x = geneXeno.GetValue(animal.genes) as List<Gene>;
            List<Gene> e = geneEndo.GetValue(animal.genes) as List<Gene>;
            if (x != null) result.AddRange(x);
            if (e != null) result.AddRange(e);
            return result;
        }

        private static void BuildGenes(List<Gene> target, Pawn_GeneTracker body, Pawn_GeneTracker source, int kind)
        {
            target.Clear();
            HashSet<GeneDef> seen = new HashSet<GeneDef>();
            AddGenes(target, source, kind, seen);
            // Xeno/endo identity and reimplantation belong to the person.
            // The combined mechanical view also includes actual body genes.
            if (kind == 0 || source == null) AddGenes(target, body, kind, seen);
        }

        private static void AddGenes(List<Gene> target, Pawn_GeneTracker tracker, int kind, HashSet<GeneDef> seen)
        {
            if (tracker == null) return;
            if (kind != 2) AddGenes(target, geneXeno.GetValue(tracker) as List<Gene>, seen);
            if (kind != 1) AddGenes(target, geneEndo.GetValue(tracker) as List<Gene>, seen);
        }

        private static void AddGenes(List<Gene> target, List<Gene> source, HashSet<GeneDef> seen)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                Gene gene = source[i];
                if (gene?.def == null) continue;
                if (seen.Add(gene.def)) target.Add(gene);
                else if (gene.Active)
                    for (int j = 0; j < target.Count; j++)
                        if (target[j].def == gene.def && !target[j].Active) { target[j] = gene; break; }
            }
        }

        private static int Signature(Pawn human, Pawn animal)
        {
            unchecked
            {
                int value = 17;
                List<Gene> genes = human.genes?.GenesListForReading;
                if (genes != null) for (int i = 0; i < genes.Count; i++)
                {
                    value = value * 31 + RuntimeHelpers.GetHashCode(genes[i]);
                    value = value * 31 + (genes[i].Active ? 1 : 0);
                }
                List<Trait> traits = human.story?.traits?.allTraits;
                if (traits != null) for (int i = 0; i < traits.Count; i++)
                {
                    value = value * 31 + RuntimeHelpers.GetHashCode(traits[i]);
                    value = value * 31 + (traits[i].Suppressed ? 1 : 0);
                }
                if (animal.genes != null)
                {
                    List<Gene> native = geneXeno.GetValue(animal.genes) as List<Gene>;
                    if (native != null) for (int i = 0; i < native.Count; i++)
                        value = value * 31 + RuntimeHelpers.GetHashCode(native[i]);
                    native = geneEndo.GetValue(animal.genes) as List<Gene>;
                    if (native != null) for (int i = 0; i < native.Count; i++)
                        value = value * 31 + RuntimeHelpers.GetHashCode(native[i]);
                }
                return value;
            }
        }

        private static void ClearGeneCaches(Pawn_GeneTracker tracker)
        {
            if (tracker == null) return;
            foreach (string name in new[] { "cachedDamageFactors", "cachedAddictionChanceFactors" })
                (AccessTools.Field(typeof(Pawn_GeneTracker), name)?.GetValue(tracker) as IDictionary)?.Clear();
            foreach (string name in new[] { "cachedEnabledNeeds", "cachedDisabledNeeds", "cachedGenesAffectAge", "cachedWaterCellCost" })
                AccessTools.Field(typeof(Pawn_GeneTracker), name)?.SetValue(tracker, null);
            AccessTools.Field(typeof(Pawn_GeneTracker), "hasCachedWaterCost")?.SetValue(tracker, false);
            recacheGeneNeeds.Invoke(tracker, null);
        }

        public static bool SharesPersonalClock(Pawn animal)
        {
            return SharesNeeds(animal) && animal.needs.mood != null;
        }

        public static bool CanRunCallback(Gene gene, bool interval)
        {
            if (gene?.def == null) return false;
            if (gene.def.GetModExtension<SharedFormGeneCallbacksExtension>() != null) return true;
            Type type = gene.GetType();
            bool[] allowed;
            if (!nativeCallbacks.TryGetValue(type, out allowed))
            {
                MethodInfo tick = AccessTools.Method(type, "Tick");
                MethodInfo tickInterval = AccessTools.Method(type, "TickInterval", new[] { typeof(int) });
                allowed = new[] { tick?.DeclaringType?.Assembly == typeof(Gene).Assembly,
                    tickInterval?.DeclaringType?.Assembly == typeof(Gene).Assembly };
                nativeCallbacks[type] = allowed;
            }
            return allowed[interval ? 1 : 0];
        }

        public static void TickInterval(Pawn animal, int delta)
        {
            State state;
            if (animal == null || delta <= 0 || !states.TryGetValue(animal, out state)
                || state.releasing || state.lastInterval == Find.TickManager?.TicksGame) return;
            Bind(animal);
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null) return;
            state.lastInterval = Find.TickManager?.TicksGame ?? 0;
            List<Gene> genes = human.genes?.GenesListForReading;
            if (genes != null) for (int i = 0; i < genes.Count; i++)
            {
                Gene gene = genes[i];
                if (!gene.Active || !CanRunCallback(gene, true) || gene is Gene_Hemogen || gene is Gene_HemogenDrain || gene is Gene_ChemicalDependency) continue;
                Pawn owner = gene.pawn;
                try { gene.pawn = animal; gene.TickInterval(delta); }
                finally { gene.pawn = owner; }
                if (SharedFormIdentity.HumanFor(animal) != human) break;
            }
            if (SharedFormIdentity.HumanFor(animal) == human)
                human.ageTracker?.AgeTickInterval(delta);
            if (!animal.RaceProps.Humanlike && animal.interactions != null && state.lastInteraction != state.lastInterval)
            {
                state.lastInteraction = state.lastInterval;
                animal.interactions.InteractionsTrackerTickInterval(delta);
            }
        }

        public static void Tick(Pawn animal)
        {
            State state;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (animal == null || !states.TryGetValue(animal, out state) || state.releasing || state.lastTick == now) return;
            state.lastTick = now;
            Pawn human = SharedFormIdentity.HumanFor(animal);
            List<Gene> genes = human?.genes?.GenesListForReading;
            if (genes == null) return;
            for (int i = 0; i < genes.Count; i++)
            {
                Gene gene = genes[i];
                if (!gene.Active || !CanRunCallback(gene, false) || gene is Gene_Hemogen || gene is Gene_HemogenDrain || gene is Gene_ChemicalDependency) continue;
                Pawn owner = gene.pawn;
                try { gene.pawn = animal; gene.Tick(); }
                finally { gene.pawn = owner; }
            }
        }

        internal static void RebindOwner(object target, Pawn owner)
        {
            if (target == null) return;
            FieldInfo field;
            Type type = target.GetType();
            if (!pawnFields.TryGetValue(type, out field))
            {
                field = AccessTools.Field(type, "pawn");
                if (field?.FieldType != typeof(Pawn)) field = null;
                pawnFields[type] = field;
            }
            field?.SetValue(target, owner);
        }

        internal static void RebindNeed(Need need, Pawn owner)
        {
            FieldInfo ownerField = AccessTools.Field(need.GetType(), "pawn");
            Pawn previous = ownerField?.GetValue(need) as Pawn;
            bool preserveLevel = previous != owner && (need is Need_Food || need is Need_Rest);
            float percentage = preserveLevel ? need.CurLevelPercentage : 0f;
            RebindOwner(need, owner);
            if (preserveLevel) need.CurLevelPercentage = percentage;
            Need_Mood mood = need as Need_Mood;
            if (mood == null) return;
            RebindOwner(mood.thoughts, owner);
            RebindOwner(mood.thoughts?.memories, owner);
            RebindOwner(mood.thoughts?.situational, owner);
            RebindOwner(mood.observer, owner);
            RebindOwner(mood.recentMemory, owner);
            if (previous != owner) mood.thoughts?.situational?.Notify_SituationalThoughtsDirty();
            if (mood.thoughts?.memories?.Memories != null)
                for (int i = 0; i < mood.thoughts.memories.Memories.Count; i++) RebindOwner(mood.thoughts.memories.Memories[i], owner);
        }

        public static void ExposeNeedLevel(Need need)
        {
            if (!(need is Need_Food) && !(need is Need_Rest)) return;
            float percentage = -1f;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Pawn owner = AccessTools.Field(need.GetType(), "pawn")?.GetValue(need) as Pawn;
                if (SharedFormIdentity.HumanFor(owner) != null) percentage = need.CurLevelPercentage;
            }
            Scribe_Values.Look(ref percentage, "wildShiftSharedLevelPercentage", -1f);
            if (Scribe.mode == LoadSaveMode.LoadingVars && percentage >= 0f)
                savedNeedLevels.GetOrCreateValue(need).percentage = percentage;
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NeedLevel saved;
                if (savedNeedLevels.TryGetValue(need, out saved))
                { need.CurLevelPercentage = saved.percentage; savedNeedLevels.Remove(need); }
            }
        }

        internal static void SetNeeds(Pawn_NeedsTracker tracker, List<Need> needs)
        {
            needList.SetValue(tracker, needs);
            tracker.BindDirectNeedFields();
        }

        public static void BeginNativeAppearance() { nativeGenesOnly++; }
        public static void EndNativeAppearance() { if (nativeGenesOnly > 0) nativeGenesOnly--; }

        public static Pawn_InteractionsTracker BodyInteractionsForSave(Pawn pawn)
        {
            State state;
            return states.TryGetValue(pawn, out state) ? state.bodyInteractions : pawn.interactions;
        }

        public static float MatingChance(float nativeChance, Pawn male, Pawn female)
        {
            float chance = nativeChance * MatingFertilityFactor(male) * MatingFertilityFactor(female);
            return float.IsNaN(chance) ? 0f : Math.Min(1f, Math.Max(0f, chance));
        }

        private static float MatingFertilityFactor(Pawn pawn)
        {
            if (SharedFormIdentity.HumanFor(pawn) == null || !ModsConfig.BiotechActive || StatDefOf.Fertility == null) return 1f;
            float factor = pawn.GetStatValue(StatDefOf.Fertility, true, 0);
            return float.IsNaN(factor) ? 0f : Math.Max(0f, factor);
        }
    }

    public sealed class SharedPawnTraitSet : TraitSet, IExposable
    {
        private readonly Pawn activeBody;
        private TraitSet bodyTraits;
        private readonly List<Trait> merged = new List<Trait>();
        private static readonly MethodInfo recache = AccessTools.Method(typeof(TraitSet), "RecacheTraits");
        public SharedPawnTraitSet(Pawn pawn) : this(pawn, null) { }
        public SharedPawnTraitSet(Pawn pawn, TraitSet previous) : base(pawn)
        {
            activeBody = pawn;
            bodyTraits = (previous as SharedPawnTraitSet)?.BodyTraits ?? previous ?? new TraitSet(pawn);
            Bind();
        }
        public TraitSet BodyTraits { get { return bodyTraits; } }
        public TraitSet OriginalTraits { get { return SharedFormIdentity.HumanFor(activeBody)?.story?.traits; } }
        public void Bind()
        {
            TraitSet source = OriginalTraits;
            if (source != null && bodyTraits.allTraits.Count == 0)
            {
                allTraits = source.allTraits;
                return;
            }
            merged.Clear();
            if (source != null) merged.AddRange(source.allTraits);
            if (bodyTraits != null) for (int i = 0; i < bodyTraits.allTraits.Count; i++)
                if (!merged.Any(t => t.def == bodyTraits.allTraits[i].def)) merged.Add(bodyTraits.allTraits[i]);
            allTraits = merged;
        }
        public void Recache() { recache.Invoke(this, null); }
        public new void ExposeData()
        {
            Scribe_Deep.Look(ref bodyTraits, "bodyTraits", activeBody);
            if (bodyTraits == null || bodyTraits is SharedPawnTraitSet) bodyTraits = new TraitSet(activeBody);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { Bind(); Recache(); }
        }
    }

    public sealed class SharedPawnNeedsTracker : Pawn_NeedsTracker, IExposable
    {
        private readonly Pawn activeBody;
        private Pawn_NeedsTracker bodyNeeds;
        private Pawn sourceOwner;
        private Pawn_NeedsTracker sourceNeeds;
        private int boundCount = -1;
        public SharedPawnNeedsTracker(Pawn pawn) : this(pawn, null) { }
        public SharedPawnNeedsTracker(Pawn pawn, Pawn_NeedsTracker previous) : base()
        {
            activeBody = pawn;
            AccessTools.Field(typeof(Pawn_NeedsTracker), "pawn").SetValue(this, pawn);
            bodyNeeds = (previous as SharedPawnNeedsTracker)?.BodyNeeds ?? previous ?? new Pawn_NeedsTracker(pawn);
            Bind();
        }
        public Pawn_NeedsTracker BodyNeeds { get { return bodyNeeds; } }
        public Pawn_NeedsTracker OriginalNeeds { get { return sourceNeeds; } }
        public void Bind(bool force = false)
        {
            Pawn human = SharedFormIdentity.HumanFor(activeBody);
            Pawn_NeedsTracker source = human?.needs;
            if (source is SharedPawnNeedsTracker) source = null;
            if (sourceOwner != human || sourceNeeds != source) RestoreOwners();
            sourceOwner = human;
            sourceNeeds = source;
            List<Need> list = (source ?? bodyNeeds).AllNeeds;
            if (!force && ReferenceEquals(AllNeeds, list) && boundCount == list.Count) return;
            boundCount = list.Count;
            SharedFormBiology.SetNeeds(this, list);
            if (source != null) for (int i = 0; i < list.Count; i++) SharedFormBiology.RebindNeed(list[i], activeBody);
        }
        public void RestoreOwners()
        {
            if (sourceOwner != null && sourceNeeds != null)
                for (int i = 0; i < sourceNeeds.AllNeeds.Count; i++) SharedFormBiology.RebindNeed(sourceNeeds.AllNeeds[i], sourceOwner);
            sourceOwner = null; sourceNeeds = null;
            boundCount = -1;
        }
        public new void ExposeData()
        {
            Scribe_Deep.Look(ref bodyNeeds, "bodyNeeds", activeBody);
            if (bodyNeeds == null || bodyNeeds is SharedPawnNeedsTracker) bodyNeeds = new Pawn_NeedsTracker(activeBody);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Bind();
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "get_GenesListForReading")]
    public static class Patch_SharedBiologicalGeneView
    {
        public static void Postfix(Pawn_GeneTracker __instance, ref List<Gene> __result)
        { __result = SharedFormBiology.ReadGenes(__instance, __result, 0); }
    }
    [HarmonyPatch(typeof(Pawn_GeneTracker), "get_Xenogenes")]
    public static class Patch_SharedBiologicalXenoView
    {
        public static void Postfix(Pawn_GeneTracker __instance, ref List<Gene> __result)
        { __result = SharedFormBiology.ReadGenes(__instance, __result, 1); }
    }
    [HarmonyPatch(typeof(Pawn_GeneTracker), "get_Endogenes")]
    public static class Patch_SharedBiologicalEndoView
    {
        public static void Postfix(Pawn_GeneTracker __instance, ref List<Gene> __result)
        { __result = SharedFormBiology.ReadGenes(__instance, __result, 2); }
    }
    [HarmonyPatch(typeof(TraitSet), "GainTrait")]
    public static class Patch_SharedBiologicalTraitGain
    {
        public static bool Prefix(TraitSet __instance, Trait trait, bool suppressConflicts)
        {
            TraitSet source = (__instance as SharedPawnTraitSet)?.OriginalTraits;
            if (source == null) return true;
            source.GainTrait(trait, suppressConflicts); return false;
        }
        public static void Postfix(Pawn ___pawn) { SharedFormBiology.SourceMutationCompleted(___pawn); }
    }
    [HarmonyPatch(typeof(TraitSet), "RemoveTrait")]
    public static class Patch_SharedBiologicalTraitRemove
    {
        public static bool Prefix(TraitSet __instance, Trait trait, bool unsuppressConflicts)
        {
            TraitSet source = (__instance as SharedPawnTraitSet)?.OriginalTraits;
            if (source == null) return true;
            source.RemoveTrait(trait, unsuppressConflicts); return false;
        }
        public static void Postfix(Pawn ___pawn) { SharedFormBiology.SourceMutationCompleted(___pawn); }
    }
    [HarmonyPatch(typeof(TraitSet), "RecalculateSuppression")]
    public static class Patch_SharedBiologicalTraitSuppression
    {
        public static void Postfix(Pawn ___pawn) { SharedFormBiology.SourceMutationCompleted(___pawn); }
    }
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "AddOrRemoveNeedsAsAppropriate")]
    public static class Patch_SharedBiologicalNeedMembership
    {
        public static bool Prefix(Pawn_NeedsTracker __instance)
        {
            SharedPawnNeedsTracker proxy = __instance as SharedPawnNeedsTracker;
            if (proxy?.OriginalNeeds == null) return true;
            proxy.OriginalNeeds.AddOrRemoveNeedsAsAppropriate(); proxy.Bind(); return false;
        }
        public static void Postfix(Pawn ___pawn)
        {
            Pawn animal = SharedFormIdentity.ActiveFormFor(___pawn);
            (animal?.needs as SharedPawnNeedsTracker)?.Bind(true);
        }
    }
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "NeedsTrackerTickInterval")]
    public static class Patch_SharedBiologicalNeedClock
    {
        public static void Prefix(Pawn_NeedsTracker __instance) { (__instance as SharedPawnNeedsTracker)?.Bind(); }
    }
    [HarmonyPatch(typeof(Pawn_GeneTracker), "GeneTrackerTick")]
    public static class Patch_SharedBiologicalGeneClock
    {
        public static void Postfix(Pawn_GeneTracker __instance) { SharedFormBiology.Tick(__instance.pawn); }
    }
    [HarmonyPatch(typeof(Pawn_GeneTracker), "GeneTrackerTickInterval")]
    public static class Patch_SharedBiologicalGeneInterval
    {
        public static void Postfix(Pawn_GeneTracker __instance, int delta) { SharedFormBiology.TickInterval(__instance.pawn, delta); }
    }
    [HarmonyPatch(typeof(Pawn), "ExposeData")]
    public static class Patch_SharedBiologicalInteractionSave
    {
        public static void Prefix(Pawn __instance, out Pawn_InteractionsTracker __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving || SharedFormIdentity.HumanFor(__instance) == null) return;
            __state = __instance.interactions;
            __instance.interactions = SharedFormBiology.BodyInteractionsForSave(__instance);
        }
        public static void Postfix(Pawn __instance, Pawn_InteractionsTracker __state)
        { if (__state != null) __instance.interactions = __state; }
        public static Exception Finalizer(Pawn __instance, Pawn_InteractionsTracker __state, Exception __exception)
        { if (__state != null) __instance.interactions = __state; return __exception; }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "AddGene", new[] { typeof(GeneDef), typeof(bool) })]
    public static class Patch_SharedBiologicalGeneAdd
    {
        public static bool Prefix(Pawn_GeneTracker __instance, GeneDef geneDef, bool xenogene, ref Gene __result)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance.pawn);
            if (human?.genes == null) return true;
            __result = human.genes.AddGene(geneDef, xenogene);
            SharedFormBiology.InvalidateSource(human);
            SharedFormBiology.Bind(__instance.pawn);
            return false;
        }
        public static void Postfix(Pawn_GeneTracker __instance)
        { SharedFormBiology.SourceMutationCompleted(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "RemoveGene")]
    public static class Patch_SharedBiologicalGeneRemove
    {
        public static bool Prefix(Pawn_GeneTracker __instance, Gene gene)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance.pawn);
            if (human?.genes == null || gene?.pawn != human) return true;
            human.genes.RemoveGene(gene);
            SharedFormBiology.InvalidateSource(human);
            SharedFormBiology.Bind(__instance.pawn);
            return false;
        }
        public static void Postfix(Pawn_GeneTracker __instance)
        { SharedFormBiology.SourceMutationCompleted(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "ClearXenogenes")]
    public static class Patch_SharedBiologicalGeneClear
    {
        public static bool Prefix(Pawn_GeneTracker __instance)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance.pawn);
            if (human?.genes == null) return true;
            human.genes.ClearXenogenes(); SharedFormBiology.InvalidateSource(human); SharedFormBiology.Bind(__instance.pawn); return false;
        }
        public static void Postfix(Pawn_GeneTracker __instance)
        { SharedFormBiology.SourceMutationCompleted(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "get_Xenotype")]
    public static class Patch_SharedBiologicalXenotype
    {
        public static void Postfix(Pawn_GeneTracker __instance, ref XenotypeDef __result)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance.pawn);
            if (human?.genes != null) __result = human.genes.Xenotype;
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "get_BiologicalAgeTickFactor")]
    public static class Patch_SharedBiologicalAgeFactor
    {
        public static bool Prefix(Pawn_GeneTracker __instance, ref float __result)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance.pawn);
            if (human?.genes == null) return true;
            __result = 1f;
            HashSet<GeneDef> seen = new HashSet<GeneDef>();
            foreach (Gene gene in human.genes.GenesListForReading)
                if (gene.Active && seen.Add(gene.def) && gene.def.biologicalAgeTickFactorFromAgeCurve != null)
                    __result *= gene.def.biologicalAgeTickFactorFromAgeCurve.Evaluate(human.ageTracker.AgeBiologicalYearsFloat);
            foreach (Gene gene in SharedFormBiology.RawBodyGenesFor(__instance.pawn))
                if (gene.Active && seen.Add(gene.def) && gene.def.biologicalAgeTickFactorFromAgeCurve != null)
                    __result *= gene.def.biologicalAgeTickFactorFromAgeCurve.Evaluate(__instance.pawn.ageTracker.AgeBiologicalYearsFloat);
            return false;
        }
    }

    [HarmonyPatch(typeof(Gene), "get_Active")]
    public static class Patch_SharedBiologicalGeneActivationOwner
    {
        public static void Prefix(Gene __instance, out Pawn __state)
        {
            __state = null;
            Pawn human = SharedFormIdentity.HumanFor(__instance.pawn);
            if (human?.genes == null) return;
            if (human.genes.GenesListForReading.Contains(__instance))
            {
                __state = __instance.pawn;
                __instance.pawn = human;
            }
        }
        public static void Postfix(Gene __instance, Pawn __state) { if (__state != null) __instance.pawn = __state; }
        public static Exception Finalizer(Gene __instance, Pawn __state, Exception __exception)
        { if (__state != null) __instance.pawn = __state; return __exception; }
    }

    [HarmonyPatch]
    public static class Patch_SharedBiologicalDeathrestContext
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Gene_Deathrest)))
                if (!method.IsStatic && method.Name != "ExposeData" && method.Name != "PostAdd" && method.Name != "PostRemove")
                    yield return method;
        }
        public static void Prefix(Gene_Deathrest __instance, out Pawn __state)
        {
            __state = null;
            Pawn animal = SharedFormIdentity.ActiveFormFor(__instance.pawn);
            if (animal == null) return;
            __state = __instance.pawn; __instance.pawn = animal;
        }
        public static void Postfix(Gene_Deathrest __instance, Pawn __state) { if (__state != null) __instance.pawn = __state; }
        public static Exception Finalizer(Gene_Deathrest __instance, Pawn __state, Exception __exception)
        { if (__state != null) __instance.pawn = __state; return __exception; }
    }

    [HarmonyPatch]
    public static class Patch_SharedBiologicalAppearanceScope
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type setup = AccessTools.TypeByName("Verse.DynamicPawnRenderNodeSetup_Genes");
            foreach (Type nested in setup.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                MethodInfo moveNext = AccessTools.Method(nested, "MoveNext");
                if (moveNext != null && nested.Name.Contains("GetDynamicNodes")) yield return moveNext;
            }
        }
        public static void Prefix() { SharedFormBiology.BeginNativeAppearance(); }
        public static Exception Finalizer(Exception __exception) { SharedFormBiology.EndNativeAppearance(); return __exception; }
    }

    [HarmonyPatch]
    public static class Patch_SharedBiologicalJoyousPresence
    {
        public static MethodBase TargetMethod() { return AccessTools.Method("RimWorld.ThoughtWorker_JoyousPresence:CurrentStateInternal"); }
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (__result.Active || p?.MapHeld == null || (!p.IsPlayerControlled && !p.IsPrisonerOfColony)) return;
            var pawns = p.MapHeld.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other != p && SharedFormIdentity.HumanFor(other)?.IsFreeColonist == true
                    && other.story?.traits?.HasTrait(TraitDefOf.Joyous) == true && !other.HostileTo(p))
                { __result = ThoughtState.ActiveDefault; return; }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "Notify_GenesChanged")]
    public static class Patch_SharedBiologicalGeneInvalidation
    {
        public static void Postfix(Pawn_GeneTracker __instance) { SharedFormBiology.SourceMutationCompleted(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Gene), "OverrideBy")]
    public static class Patch_SharedBiologicalGeneOverrideInvalidation
    {
        public static void Postfix(Gene __instance) { SharedFormBiology.InvalidateSource(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Need), "ExposeData")]
    public static class Patch_SharedBiologicalNeedLevelSave
    {
        public static void Postfix(Need __instance) { SharedFormBiology.ExposeNeedLevel(__instance); }
    }

    [HarmonyPatch(typeof(SocialInteractionUtility), "CanInitiateInteraction")]
    public static class Patch_SharedBiologicalInteractionSpeech
    {
        public static void Postfix(Pawn pawn, InteractionDef interactionDef, ref bool __result)
        {
            if (__result) return;
            Pawn human = SharedFormIdentity.HumanFor(pawn);
            if (human == null || pawn.interactions == null || pawn.Downed || pawn.Dead || !pawn.Awake() || pawn.IsBurning()
                || (pawn.IsMutant && pawn.mutant.Def.incapableOfSocialInteractions)
                || pawn.IsInteractionBlocked(interactionDef, isInitiator: true, isRandom: false)) return;
            __result = human.health.capacities.CapableOf(PawnCapacityDefOf.Talking);
        }
    }

    [HarmonyPatch(typeof(SocialInteractionUtility), "CanInitiateRandomInteraction")]
    public static class Patch_SharedBiologicalRandomInteraction
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (__result) return;
            Pawn human = SharedFormIdentity.HumanFor(p);
            if (human == null || !SocialInteractionUtility.CanInitiateInteraction(p)
                || p.Downed || p.InAggroMentalState || p.Faction == null
                || !human.ageTracker.CurLifeStage.canInitiateSocialInteraction || p.Inhumanized()
                || p.IsInteractionBlocked(null, isInitiator: true, isRandom: true)) return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(SocialInteractionUtility), "CanReceiveRandomInteraction")]
    public static class Patch_SharedBiologicalReceiveRandomInteraction
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (!__result && SharedFormIdentity.HumanFor(p) != null && SocialInteractionUtility.CanReceiveInteraction(p)
                && !p.Downed && !p.InAggroMentalState) __result = true;
        }
    }

    [HarmonyPatch(typeof(MentalBreaker), "get_CanDoRandomMentalBreaks")]
    public static class Patch_SharedBiologicalMentalBreakEligibility
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            Pawn human = SharedFormIdentity.HumanFor(___pawn);
            if (human == null) return;
            LifeStageDef stage = human.ageTracker?.CurLifeStage;
            __result = stage != null ? stage.canDoRandomMentalBreaks
                : human.RaceProps.Humanlike && (___pawn.Spawned || ___pawn.IsCaravanMember());
        }
    }

    [HarmonyPatch(typeof(Pawn), "get_CombinedDisabledWorkTags")]
    public static class Patch_SharedBiologicalWorkRestrictions
    {
        public static void Postfix(Pawn __instance, ref WorkTags __result)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance);
            if (human != null) __result |= human.CombinedDisabledWorkTags;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetDisabledWorkTypes")]
    public static class Patch_SharedBiologicalWorkTypeRestrictions
    {
        public static void Postfix(Pawn __instance, bool permanentOnly, ref List<WorkTypeDef> __result)
        {
            Pawn human = SharedFormIdentity.HumanFor(__instance);
            if (human == null) return;
            List<WorkTypeDef> source = human.GetDisabledWorkTypes(permanentOnly);
            List<WorkTypeDef> combined = null;
            for (int i = 0; i < source.Count; i++)
            {
                if ((__result != null && __result.Contains(source[i])) || (combined != null && combined.Contains(source[i]))) continue;
                if (combined == null) combined = __result == null ? new List<WorkTypeDef>() : new List<WorkTypeDef>(__result);
                combined.Add(source[i]);
            }
            if (combined != null) __result = combined;
        }
    }

    [HarmonyPatch(typeof(StatPart_FertilityByGenderAge), "AgeFactor")]
    public static class Patch_SharedBiologicalFertilityAge
    {
        public static void Prefix(ref Pawn pawn)
        {
            Pawn human = SharedFormIdentity.HumanFor(pawn);
            if (human != null) pawn = human;
        }
    }

    [HarmonyPatch(typeof(PawnUtility), "Mated")]
    public static class Patch_SharedBiologicalMating
    {
        public static bool Prefix(Pawn male, Pawn female)
        {
            if (SharedFormIdentity.HumanFor(male) == null && SharedFormIdentity.HumanFor(female) == null) return true;
            if (male.Sterile() || female.Sterile()) return false;
            if (female.TryGetComp<CompEggLayer>() == null) return true;
            float chance = SharedFormBiology.MatingChance(1f, male, female);
            return chance >= 1f || (chance > 0f && Rand.Value < chance);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo scale = AccessTools.Method(typeof(SharedFormBiology), "MatingChance");
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float && (float)instruction.operand == .5f)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, scale);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PawnUtility), "FertileMateTarget")]
    public static class Patch_SharedBiologicalFertileMateTarget
    {
        public static void Postfix(Pawn male, Pawn female, ref bool __result)
        {
            if (__result && (SharedFormIdentity.HumanFor(male) != null || SharedFormIdentity.HumanFor(female) != null)
                && SharedFormBiology.MatingChance(1f, male, female) <= 0f) __result = false;
        }
    }
}
