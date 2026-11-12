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

        private sealed class State
        {
            public Pawn human;
            public int signature, version;
            public bool initialized, building;
            public readonly Dictionary<PawnCapacityDef, float> rawFactors = new Dictionary<PawnCapacityDef, float>();

            public void Refresh(Pawn body)
            {
                if (building) return;
                Pawn source = SharedFormIdentity.HumanFor(body);
                int next = Signature(source, body);
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

        public static int ModifierVersion(Pawn body)
        {
            if (body == null || neutralPawn == body) return 0;
            State state = states.GetOrCreateValue(body);
            state.Refresh(body);
            return state.version;
        }

        public static float ApplyRawCapacity(float value, HediffSet diffSet, PawnCapacityDef capacity)
        {
            Pawn body = diffSet?.pawn;
            // Counterfactual health sets and the neutral installation probes
            // must keep native worker results. Only the active body's actual
            // anatomy receives the original person's installed-part ratio.
            if (value <= 0f || body?.health == null || neutralPawn == body
                || !ReferenceEquals(diffSet, body.health.hediffSet)
                || SharedFormIdentity.HumanFor(body) == null) return value;
            State state = states.GetOrCreateValue(body);
            state.Refresh(body);
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

        private static int Signature(Pawn human, Pawn body)
        {
            unchecked { return PartSignature(human) * 397 ^ PartSignature(body); }
        }

        private static int PartSignature(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return 0;
            int hash = 17;
            List<Hediff> actual = pawn.health.hediffSet.hediffs;
            unchecked
            {
                for (int i = 0; i < actual.Count; i++)
                {
                    Hediff h = actual[i];
                    if (h?.Part == null || (!(h is Hediff_AddedPart) && !(h is Hediff_Implant)
                        && h.def?.countsAsAddedPartOrImplant != true)) continue;
                    hash = hash * 31 + RuntimeHelpers.GetHashCode(h);
                    hash = hash * 31 + RuntimeHelpers.GetHashCode(h.Part);
                    hash = hash * 31 + (h.def?.addedPartProps?.partEfficiency ?? 1f).GetHashCode();
                    hash = hash * 31 + (h.CurStage?.partEfficiencyOffset ?? 0f).GetHashCode();
                }
            }
            return hash;
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
