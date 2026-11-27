using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace WildShift
{
    // Transfer installed-part performance, not the preserved body's wounds or
    // its final capacities. Native workers compare healthy anatomy with and
    // without those exact installations; dependent capacities are neutral in
    // that comparison and are evaluated normally on the active animal.
    public static class SharedFormAnatomy
    {
        [ThreadStatic] private static Pawn neutralPawn;
        private static readonly ConditionalWeakTable<Pawn, State> states = new ConditionalWeakTable<Pawn, State>();

        private sealed class PartSignatureCache
        {
            public HediffSet set;
            public SharedFormHealth.DirtyStamp stamp;
            public int dirtyVersion = int.MinValue;
            public int count = -1;
            public int hash;
            public bool hasDynamicStage;
            public readonly List<Hediff> installed = new List<Hediff>();

            public void Reset()
            {
                set = null;
                stamp = null;
                dirtyVersion = int.MinValue;
                count = -1;
                installed.Clear();
                hasDynamicStage = false;
                hash = 0;
            }

            public int Compute(Pawn pawn)
            {
                HediffSet currentSet = pawn?.health?.hediffSet;
                if (currentSet == null)
                {
                    Reset();
                    return 0;
                }

                List<Hediff> actual = currentSet.hediffs;
                int actualCount = actual.Count;
                if (ReferenceEquals(set, currentSet) && stamp != null
                    && stamp.version == dirtyVersion && actualCount == count)
                {
                    if (!hasDynamicStage) return hash;
                    int dynamicHash = 17;
                    unchecked
                    {
                        for (int i = 0; i < installed.Count; i++)
                        {
                            Hediff h = installed[i];
                            dynamicHash = dynamicHash * 31 + RuntimeHelpers.GetHashCode(h);
                            dynamicHash = dynamicHash * 31 + RuntimeHelpers.GetHashCode(h.Part);
                            dynamicHash = dynamicHash * 31 + (h.def?.addedPartProps?.partEfficiency ?? 1f).GetHashCode();
                            dynamicHash = dynamicHash * 31 + (h.CurStage?.partEfficiencyOffset ?? 0f).GetHashCode();
                        }
                    }
                    hash = dynamicHash;
                    return dynamicHash;
                }

                set = currentSet;
                stamp = SharedFormHealth.WatchHealthSet(currentSet);
                dirtyVersion = stamp != null ? stamp.version : 0;
                count = actualCount;
                installed.Clear();
                hasDynamicStage = false;
                int result = 17;
                unchecked
                {
                    for (int i = 0; i < actualCount; i++)
                    {
                        Hediff h = actual[i];
                        if (h?.Part == null || (!(h is Hediff_AddedPart) && !(h is Hediff_Implant)
                            && h.def?.countsAsAddedPartOrImplant != true)) continue;
                        installed.Add(h);
                        Type ht = h.GetType();
                        if ((h.def?.stages != null && (h.def.stages.Count > 1
                                || (h.def.stages.Count == 1 && h.def.stages[0].minSeverity > 0f)))
                            || (ht != typeof(Hediff) && ht != typeof(HediffWithComps)
                                && ht != typeof(Hediff_AddedPart) && ht != typeof(Hediff_Implant)))
                            hasDynamicStage = true;
                        result = result * 31 + RuntimeHelpers.GetHashCode(h);
                        result = result * 31 + RuntimeHelpers.GetHashCode(h.Part);
                        result = result * 31 + (h.def?.addedPartProps?.partEfficiency ?? 1f).GetHashCode();
                        result = result * 31 + (h.CurStage?.partEfficiencyOffset ?? 0f).GetHashCode();
                    }
                }
                hash = result;
                return result;
            }
        }

        private sealed class State
        {
            public Pawn ownerBody;
            public Pawn human;
            public int signature, version;
            public bool initialized, building;
            public readonly PartSignatureCache humanParts = new PartSignatureCache();
            public readonly PartSignatureCache bodyParts = new PartSignatureCache();
            public readonly Dictionary<PawnCapacityDef, float> rawFactors = new Dictionary<PawnCapacityDef, float>();

            public void Refresh(Pawn body, Pawn source = null)
            {
                if (building) return;
                // A raw-capacity query has just validated the source. Reuse
                // that result only within this call, never across queries.
                source = source ?? SharedFormIdentity.HumanFor(body);
                int next = unchecked(humanParts.Compute(source) * 397 ^ bodyParts.Compute(body));
                if (initialized && source == human && next == signature) return;
                human = source;
                signature = next;
                initialized = true;
                version++;
                building = true;
                try
                {
                    rawFactors.Clear();
                    if (source?.RaceProps?.body == null || body?.RaceProps?.body == null) return;
                    List<Hediff> sourceParts = InstalledParts(source);
                    if (sourceParts.Count == 0) return;
                    List<Hediff> bodyParts = InstalledParts(body);
                    var sourceNatural = new HediffSet(source);
                    var sourceInstalled = new HediffSet(source);
                    sourceInstalled.hediffs.AddRange(sourceParts);
                    var bodyNatural = new HediffSet(body);
                    var bodyInstalled = new HediffSet(body);
                    bodyInstalled.hediffs.AddRange(bodyParts);
                    foreach (PawnCapacityDef capacity in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
                    {
                        if (!capacity.Worker.CanHaveCapacity(source.RaceProps.body)
                            || !capacity.Worker.CanHaveCapacity(body.RaceProps.body)) continue;
                        float sourceFactor = Ratio(capacity, sourceNatural, sourceInstalled);
                        if (!Finite(sourceFactor) || Math.Abs(sourceFactor - 1f) < .00001f) continue;
                        float ownFactor = bodyParts.Count == 0 ? 1f : Ratio(capacity, bodyNatural, bodyInstalled);
                        if (!Finite(ownFactor) || ownFactor <= 0f) ownFactor = 1f;
                        // Two bodies' replacement limbs do not multiply the
                        // same improvement twice. Keep the stronger installed
                        // performance; actual source drawbacks still apply.
                        float factor = sourceFactor > 1f ? Math.Max(1f, sourceFactor / Math.Max(1f, ownFactor)) : sourceFactor;
                        if (Math.Abs(factor - 1f) > .00001f)
                            rawFactors.Add(capacity, factor);
                    }
                }
                finally { building = false; }
            }
        }

        private static State GetState(Pawn body, HediffComp_Transformed marker = null)
        {
            State state = marker?.anatomyState as State;
            if (state != null && ReferenceEquals(state.ownerBody, body)) return state;
            if (!states.TryGetValue(body, out state)) state = states.GetOrCreateValue(body);
            state.ownerBody = body;
            if (marker != null) marker.anatomyState = state;
            return state;
        }

        internal static void Invalidate(Pawn body)
        {
            if (body == null) return;
            State state;
            if (states.TryGetValue(body, out state))
            {
                state.humanParts.Reset();
                state.bodyParts.Reset();
                state.initialized = false;
            }
        }

        public static int ModifierVersion(Pawn body)
        {
            return ModifierVersionForSource(body, null, null);
        }

        internal static int ModifierVersionForSource(Pawn body, Pawn source)
        {
            return ModifierVersionForSource(body, source, null);
        }

        internal static int ModifierVersionForSource(Pawn body, Pawn source, HediffComp_Transformed marker)
        {
            if (body == null || neutralPawn == body) return 0;
            State state = GetState(body, marker);
            state.Refresh(body, source);
            return state.version;
        }

        public static float ApplyRawCapacity(float value, HediffSet diffSet, PawnCapacityDef capacity)
        {
            Pawn body = diffSet?.pawn;
            // Counterfactual health sets and the neutral installation probes
            // must keep native worker results. Only the active body's actual
            // anatomy receives the original person's installed-part ratio.
            if (value <= 0f || body?.health == null || neutralPawn == body
                || !ReferenceEquals(diffSet, body.health.hediffSet)) return value;
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(body);
            Pawn source = marker == null ? null : SharedFormIdentity.GetStoredHumanFromActiveBody(body, marker);
            if (source == null) return value;
            State state = GetState(body, marker);
            state.Refresh(body, source);
            float factor;
            return state.rawFactors.TryGetValue(capacity, out factor) ? value * factor : value;
        }

        public static bool IsNeutral(Pawn pawn) { return neutralPawn != null && neutralPawn == pawn; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private static float Ratio(PawnCapacityDef capacity, HediffSet natural, HediffSet installed)
        {
            Pawn previous = neutralPawn;
            neutralPawn = natural.pawn;
            try
            {
                float baseline = capacity.Worker.CalculateCapacityLevel(natural);
                if (baseline <= .00001f) return 1f;
                return capacity.Worker.CalculateCapacityLevel(installed) / baseline;
            }
            finally { neutralPawn = previous; }
        }

        private static List<Hediff> InstalledParts(Pawn pawn)
        {
            var result = new List<Hediff>();
            if (pawn?.health?.hediffSet == null) return result;
            List<Hediff> actual = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < actual.Count; i++)
            {
                Hediff h = actual[i];
                if (h?.Part == null) continue;
                if (h is Hediff_AddedPart && h.def?.addedPartProps != null) result.Add(h);
                else if ((h is Hediff_Implant || h.def?.countsAsAddedPartOrImplant == true)
                    && h.CurStage != null && h.CurStage.partEfficiencyOffset != 0f) result.Add(h);
            }
            return result;
        }

        public static PawnCapacityModifier Clone(PawnCapacityModifier source)
        {
            return source == null ? null : Gen.MemberwiseClone(source);
        }
    }

    [HarmonyPatch(typeof(PawnCapacityUtility), nameof(PawnCapacityUtility.CalculateCapacityLevel))]
    public static class Patch_SharedAnatomyRawCapacity
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var worker = AccessTools.Method(typeof(PawnCapacityWorker), nameof(PawnCapacityWorker.CalculateCapacityLevel),
                new[] { typeof(HediffSet), typeof(List<PawnCapacityUtility.CapacityImpactor>) });
            int index = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(worker)) continue;
                if (index >= 0)
                {
                    Log.Error("[WildShift] Ambiguous raw capacity calculation; leaving native anatomy unchanged.");
                    return code;
                }
                index = i;
            }
            if (index < 0)
            {
                Log.Error("[WildShift] Raw capacity calculation not found; leaving native anatomy unchanged.");
                return code;
            }
            // Installed parts affect the worker's raw anatomy. Native hediff
            // and gene offsets, factors and limits must run afterward once.
            code.InsertRange(index + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SharedFormAnatomy), nameof(SharedFormAnatomy.ApplyRawCapacity)))
            });
            return code;
        }
    }

    [HarmonyPatch(typeof(PawnCapacitiesHandler), "GetLevel")]
    public static class Patch_SharedAnatomyCapacityContext
    {
        public static bool Prefix(Pawn ___pawn, ref float __result)
        {
            if (SharedFormAnatomy.IsNeutral(___pawn)) { __result = 1f; return false; }
            SharedFormStats.PrepareHealth(___pawn);
            return true;
        }
    }
}
