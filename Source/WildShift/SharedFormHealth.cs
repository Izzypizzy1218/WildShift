using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WildShift
{
    // Native drug timers, chemical needs and dependency genes remain on their
    // original pawn. The animal only owns disposable views of their effects.
    public static class SharedFormHealth
    {
        public const string ProjectionDefName = "WildShift_SharedFormDrugEffect";

        private sealed class Clock
        {
            public Pawn human;
            public int lastTick;
            public int lastInterval;
            public bool advancing;
            public bool dirty = true;
            public int generation;
            public readonly List<Hediff> sources = new List<Hediff>();
            public readonly Dictionary<StatWorker, int> statGenerations = new Dictionary<StatWorker, int>();
        }

        private static readonly ConditionalWeakTable<Pawn, Clock> clocks = new ConditionalWeakTable<Pawn, Clock>();
        private static HashSet<HediffDef> drugDefs;
        [ThreadStatic] private static Stack<Pawn> lethalBodies;
        [ThreadStatic] private static Stack<Pawn> sourceBodies;

        private static int Now { get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; } }

        public static void Initialize(Pawn animal)
        {
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null) return;
            SharedFormBuffs.AdoptExisting(animal, human);
            clocks.Remove(animal);
            clocks.Add(animal, new Clock { human = human, lastTick = Now, lastInterval = Now });
            Refresh(animal);
        }

        private static Clock GetClock(Pawn animal)
        {
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null) return null;
            Clock clock;
            if (!clocks.TryGetValue(animal, out clock) || clock.human != human)
            {
                Initialize(animal);
                clocks.TryGetValue(animal, out clock);
            }
            return clock;
        }

        private static HashSet<HediffDef> DrugDefs
        {
            get
            {
                if (drugDefs != null) return drugDefs;
                drugDefs = new HashSet<HediffDef>();
                foreach (ChemicalDef chemical in DefDatabase<ChemicalDef>.AllDefsListForReading)
                {
                    if (chemical.addictionHediff != null) drugDefs.Add(chemical.addictionHediff);
                    if (chemical.toleranceHediff != null) drugDefs.Add(chemical.toleranceHediff);
                }
                foreach (ThingDef thing in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (!thing.IsDrug || thing.ingestible == null || thing.ingestible.outcomeDoers == null) continue;
                    foreach (IngestionOutcomeDoer outcome in thing.ingestible.outcomeDoers)
                    {
                        IngestionOutcomeDoer_GiveHediff give = outcome as IngestionOutcomeDoer_GiveHediff;
                        if (give != null && give.hediffDef != null) drugDefs.Add(give.hediffDef);
                    }
                }
                HediffDef overdose = DefDatabase<HediffDef>.GetNamedSilentFail("DrugOverdose");
                if (overdose != null) drugDefs.Add(overdose);
                return drugDefs;
            }
        }

        public static bool IsSupported(Hediff source)
        {
            if (source == null || source.def == null || source is Hediff_SharedFormDrugEffect)
                return false;
            if (SharedFormBuffs.IsSupported(source)) return true;
            if (source.Part != null) return false;
            Type type = source.GetType();
            bool dependency = type == typeof(Hediff_ChemicalDependency);
            bool hemogen = type == typeof(Hediff_HemogenCraving);
            if (!dependency && type != typeof(Hediff) && type != typeof(HediffWithComps)
                && type != typeof(Hediff_High) && type != typeof(Hediff_Addiction) && !hemogen) return false;
            if (!dependency && !hemogen && !DrugDefs.Contains(source.def)) return false;
            HediffWithComps withComps = source as HediffWithComps;
            if (withComps != null && withComps.comps != null)
            {
                for (int i = 0; i < withComps.comps.Count; i++)
                    if (withComps.comps[i].GetType().Assembly != typeof(Hediff).Assembly) return false;
            }
            return true;
        }

        public static void MarkDirty(Pawn human)
        {
            Pawn animal = SharedFormIdentity.ActiveFormFor(human);
            Clock clock;
            if (animal != null && clocks.TryGetValue(animal, out clock)) clock.dirty = true;
        }

        public static void Refresh(Pawn animal)
        {
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null || animal.health == null || human.health == null) return;
            Clock clock;
            if (!clocks.TryGetValue(animal, out clock))
            {
                clock = new Clock { human = human, lastTick = Now, lastInterval = Now };
                clocks.Add(animal, clock);
            }
            clock.sources.Clear();
            List<Hediff> originals = human.health.hediffSet.hediffs;
            for (int i = 0; i < originals.Count; i++)
                if (IsSupported(originals[i])) clock.sources.Add(originals[i]);

            List<Hediff> actual = animal.health.hediffSet.hediffs;
            for (int i = actual.Count - 1; i >= 0; i--)
            {
                Hediff_SharedFormDrugEffect projection = actual[i] as Hediff_SharedFormDrugEffect;
                if (projection != null && !clock.sources.Contains(projection.source))
                    animal.health.RemoveHediff(projection);
            }
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(ProjectionDefName);
            if (def == null) return;
            for (int i = 0; i < clock.sources.Count; i++)
            {
                Hediff source = clock.sources[i];
                bool present = false;
                for (int j = 0; j < actual.Count; j++)
                {
                    Hediff_SharedFormDrugEffect existing = actual[j] as Hediff_SharedFormDrugEffect;
                    if (existing != null && existing.source == source) { present = true; break; }
                }
                if (!present)
                {
                    var projection = (Hediff_SharedFormDrugEffect)HediffMaker.MakeHediff(def, animal);
                    projection.source = source;
                    animal.health.AddHediff(projection);
                }
            }
            clock.dirty = false;
            clock.generation++;
            animal.health.Notify_HediffChanged(null);
        }

        private static bool OriginalStillStored(Pawn animal, Clock clock)
        {
            return clock != null && SharedFormIdentity.HumanFor(animal) == clock.human;
        }

        public static void Tick(Pawn animal)
        {
            Clock clock = GetClock(animal);
            if (clock == null || clock.advancing || clock.lastTick >= Now) return;
            clock.lastTick = Now;
            if (clock.dirty) Refresh(animal);
            clock.advancing = true;
            EnterSource(animal);
            try
            {
                for (int i = clock.sources.Count - 1; i >= 0 && OriginalStillStored(animal, clock); i--)
                {
                    Hediff source = clock.sources[i];
                    source.PostTick();
                    if (source.ShouldRemove)
                    {
                        clock.human.health.RemoveHediff(source);
                        clock.dirty = true;
                    }
                }
            }
            finally
            {
                ExitSource();
                clock.advancing = false;
            }
            if (clock.dirty && OriginalStillStored(animal, clock)) Refresh(animal);
        }

        public static void TickInterval(Pawn animal, int delta)
        {
            Clock clock = GetClock(animal);
            if (clock == null || clock.advancing || clock.lastInterval >= Now || delta <= 0) return;
            int elapsed = Math.Min(delta, Now - clock.lastInterval);
            clock.lastInterval = Now;
            if (clock.dirty) Refresh(animal);
            clock.advancing = true;
            EnterSource(animal);
            try
            {
                // NeedInterval is natively a 150-tick step. Keep its original
                // pawn's hash phase, while visibility follows the active body.
                if (clock.human.IsHashIntervalTick(150, elapsed) && clock.human.needs != null)
                {
                    List<Need> needs = clock.human.needs.AllNeeds;
                    for (int i = 0; i < needs.Count; i++)
                        if (needs[i] is Need_Chemical) needs[i].NeedInterval();
                }
                for (int i = clock.sources.Count - 1; i >= 0 && OriginalStillStored(animal, clock); i--)
                {
                    Hediff source = clock.sources[i];
                    float previousSeverity = source.Severity;
                    source.ageTicks += elapsed;
                    // Native drug comps own severity, disappearance, scar
                    // healing and genetic-dependency progression. Stage events
                    // run once on the animal's projection, never on this body.
                    source.PostTickInterval(elapsed);
                    if (source.Severity != previousSeverity) clock.dirty = true;
                    if (source.ShouldRemove)
                    {
                        clock.human.health.RemoveHediff(source);
                        clock.dirty = true;
                    }
                }
            }
            finally
            {
                ExitSource();
                clock.advancing = false;
            }
            if (clock.dirty && OriginalStillStored(animal, clock)) Refresh(animal);
        }

        internal static void EnterSource(Pawn animal)
        {
            if (sourceBodies == null) sourceBodies = new Stack<Pawn>();
            sourceBodies.Push(animal);
        }
        internal static void ExitSource() { sourceBodies.Pop(); }

        internal static Pawn RedirectDrugRecipient(ref Pawn recipient)
        {
            Pawn human = SharedFormIdentity.HumanFor(recipient);
            if (human == null) return null;
            Pawn animal = recipient;
            recipient = human;
            EnterSource(animal);
            return animal;
        }

        public static void PrepareStatWorker(StatWorker worker, Thing thing)
        {
            Pawn animal = thing as Pawn;
            Clock clock;
            if (animal == null || !clocks.TryGetValue(animal, out clock)) return;
            if (clock.dirty) Refresh(animal);
            int previous;
            if (!clock.statGenerations.TryGetValue(worker, out previous) || previous != clock.generation)
            {
                worker.ClearCacheForThing(animal);
                clock.statGenerations[worker] = clock.generation;
            }
        }

        internal static bool ProcessingSource(Pawn human)
        {
            Pawn animal = SharedFormIdentity.ActiveFormFor(human);
            return animal != null && sourceBodies != null && sourceBodies.Contains(animal);
        }

        internal static void EnterProjection(Pawn animal)
        {
            if (lethalBodies == null) lethalBodies = new Stack<Pawn>();
            lethalBodies.Push(animal);
        }
        internal static void ExitProjection() { lethalBodies.Pop(); }

        public static bool IsSharedLethalDeath(Pawn animal)
        {
            if (lethalBodies != null && lethalBodies.Contains(animal)) return true;
            if (sourceBodies != null && sourceBodies.Contains(animal)) return true;
            Clock clock;
            if (animal == null || !clocks.TryGetValue(animal, out clock)) return false;
            for (int i = 0; i < clock.sources.Count; i++)
                if (clock.sources[i].CauseDeathNow()) return true;
            return false;
        }

        internal static bool IsStoredOriginal(Pawn human)
        {
            return SharedFormIdentity.ActiveFormFor(human) != null;
        }
    }

    // This object belongs only to the animal. It does not own or tick the
    // original hediff's comps, and cannot create another chemical need.
    public sealed class Hediff_SharedFormDrugEffect : Hediff
    {
        public Hediff source;
        public override string LabelBase { get { return source != null ? source.LabelBase : base.LabelBase; } }
        public override string LabelInBrackets { get { return source != null ? source.LabelInBrackets : base.LabelInBrackets; } }
        public override string SeverityLabel { get { return source != null ? source.SeverityLabel : base.SeverityLabel; } }
        public override UnityEngine.Color LabelColor { get { return source != null ? source.LabelColor : base.LabelColor; } }
        public override string Description { get { return source != null ? source.Description : base.Description; } }
        public override string TipStringExtra { get { return source != null ? source.TipStringExtra : null; } }
        public override HediffStage CurStage { get { return source != null ? source.CurStage : null; } }
        public override float Severity
        {
            get { return source != null ? source.Severity : base.Severity; }
            set { base.Severity = value; }
        }
        public override bool Visible { get { return source != null && source.Visible; } }
        public override bool ShouldRemove
        {
            get
            {
                Pawn human = SharedFormIdentity.HumanFor(pawn);
                return human == null || source == null || source.pawn != human
                    || !human.health.hediffSet.hediffs.Contains(source) || source.ShouldRemove;
            }
        }
        public override bool CauseDeathNow() { return source != null && source.CauseDeathNow(); }
        public override bool TryMergeWith(Hediff other) { return false; }
        public override void TickInterval(int delta)
        {
            if (ShouldRemove) return;
            SharedFormHealth.EnterProjection(pawn);
            try
            {
                // The proxy's neutral Def creates no second drug need. Forward
                // native Def-level side effects explicitly to the active body.
                if (source.def.hediffGivers != null && pawn.IsHashIntervalTick(60, delta))
                    for (int i = 0; i < source.def.hediffGivers.Count; i++)
                        source.def.hediffGivers[i].OnIntervalPassed(pawn, this);
                base.TickInterval(delta);
            }
            finally { SharedFormHealth.ExitProjection(); }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref source, "sharedDrugSource");
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "HealthTick")]
    public static class Patch_SharedDrugTick
    {
        public static bool Prefix(Pawn ___pawn)
        {
            if (SharedFormHealth.IsStoredOriginal(___pawn)) return false;
            bool transformed = SharedFormIdentity.HumanFor(___pawn) != null;
            if (transformed) SharedFormHealth.Tick(___pawn);
            return !transformed || SharedFormIdentity.HumanFor(___pawn) != null;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "HealthTickInterval")]
    public static class Patch_SharedDrugInterval
    {
        public static bool Prefix(Pawn ___pawn, int delta)
        {
            if (SharedFormHealth.IsStoredOriginal(___pawn)) return false;
            bool transformed = SharedFormIdentity.HumanFor(___pawn) != null;
            if (transformed) SharedFormHealth.TickInterval(___pawn, delta);
            return !transformed || SharedFormIdentity.HumanFor(___pawn) != null;
        }
    }

    [HarmonyPatch(typeof(HediffSet), "DirtyCache")]
    public static class Patch_SharedDrugChanged
    {
        public static void Postfix(Pawn ___pawn) { SharedFormHealth.MarkDirty(___pawn); }
    }

    [HarmonyPatch(typeof(Need), "get_IsPawnInteractableOrVisible")]
    public static class Patch_StoredChemicalNeedVisibility
    {
        public static void Postfix(Need __instance, Pawn ___pawn, ref bool __result)
        {
            if (__result || !(__instance is Need_Chemical)) return;
            Pawn animal = SharedFormIdentity.ActiveFormFor(___pawn);
            if (animal != null)
                __result = animal.SpawnedOrAnyParentSpawned || animal.IsCaravanMember()
                    || PawnUtility.IsTravelingInTransportPodWorldObject(animal);
        }
    }

    [HarmonyPatch(typeof(CompDrug), "PrePostIngested")]
    public static class Patch_SharedDrugBeforeIngestion
    {
        public static bool Prefix(ref Pawn ingester, out Pawn __state)
        {
            __state = null;
            if (AnimalFormStorage.IsStored(ingester)) return false;
            __state = SharedFormHealth.RedirectDrugRecipient(ref ingester);
            return true;
        }
        public static void Finalizer(Pawn __state) { if (__state != null) SharedFormHealth.ExitSource(); }
    }

    [HarmonyPatch(typeof(CompDrug), "PostIngested")]
    public static class Patch_SharedDrugAfterIngestion
    {
        public static bool Prefix(ref Pawn ingester, out Pawn __state)
        {
            __state = null;
            if (AnimalFormStorage.IsStored(ingester)) return false;
            __state = SharedFormHealth.RedirectDrugRecipient(ref ingester);
            return true;
        }
        public static void Finalizer(Pawn __state) { if (__state != null) SharedFormHealth.ExitSource(); }
        public static void Postfix(CompDrug __instance, Pawn ingester)
        {
            Pawn animal = SharedFormIdentity.ActiveFormFor(ingester);
            if (animal == null) return;
            if (ingester.genes != null)
            {
                List<Gene> genes = ingester.genes.GenesListForReading;
                for (int i = 0; i < genes.Count; i++)
                {
                    Gene_ChemicalDependency dependency = genes[i] as Gene_ChemicalDependency;
                    if (dependency != null && dependency.Active)
                        dependency.Notify_IngestedThing(__instance.parent, 1);
                }
            }
            SharedFormHealth.Refresh(animal);
        }
    }

    [HarmonyPatch(typeof(IngestionOutcomeDoer_GiveHediff), "DoIngestionOutcomeSpecial")]
    public static class Patch_SharedDrugOutcome
    {
        public static bool Prefix(IngestionOutcomeDoer_GiveHediff __instance, ref Pawn pawn, Thing ingested, out Pawn __state)
        {
            __state = null;
            if ((ingested == null || !ingested.def.IsDrug) && !SharedFormBuffs.IsSupportedDef(__instance.hediffDef)) return true;
            if (AnimalFormStorage.IsStored(pawn)) return false;
            __state = SharedFormHealth.RedirectDrugRecipient(ref pawn);
            return true;
        }
        public static void Finalizer(Pawn __state) { if (__state != null) SharedFormHealth.ExitSource(); }
    }

    [HarmonyPatch(typeof(IngestionOutcomeDoer), "DoIngestionOutcome")]
    public static class Patch_InactiveFormDrugOutcome
    {
        public static bool Prefix(Pawn pawn, Thing ingested)
        {
            // Thing.Ingested retains its original pawn local for every outcome
            // and PostIngested callback. A preceding lethal outcome may have
            // already returned and cached that animal. Do not mutate its now
            // inactive body with the remainder of the same ingestion.
            return ingested == null || ingested.def == null || !ingested.def.IsDrug
                || !AnimalFormStorage.IsStored(pawn);
        }
    }

    [HarmonyPatch(typeof(StatWorker), "GetValue", new[] { typeof(Thing), typeof(bool), typeof(int) })]
    public static class Patch_SharedDrugStatCache
    {
        public static void Prefix(StatWorker __instance, Thing thing)
        {
            SharedFormHealth.PrepareStatWorker(__instance, thing);
        }
    }

    [HarmonyPatch(typeof(HediffComp_HealPermanentWounds), "TryHealRandomPermanentWound")]
    public static class Patch_SharedDrugPermanentHealing
    {
        public static void Prefix(ref Pawn pawn)
        {
            if (!SharedFormHealth.ProcessingSource(pawn)) return;
            Pawn animal = SharedFormIdentity.ActiveFormFor(pawn);
            if (animal != null) pawn = animal;
        }
    }

    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Patch_StoredDrugDeath
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn __instance)
        {
            if (!SharedFormHealth.ProcessingSource(__instance)) return true;
            Pawn animal = SharedFormIdentity.ActiveFormFor(__instance);
            if (animal == null) return true;
            Pawn restored = TransformUtility.RevertToHuman(animal, false);
            // Failed placement keeps both bodies intact for a later retry.
            return restored == __instance && !__instance.Dead && !__instance.Destroyed;
        }
    }
}
