using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShift
{
    internal static class SharedPsycastUiStats
    {
        internal struct DrawScope
        {
            internal bool Entered;
            internal int Depth;
            internal bool CacheEnabled;
        }

        private struct PsychicStats
        {
            internal bool HasGain;
            internal float Gain;
            internal Pawn_PsychicEntropyTracker LimitTracker;
            internal float Limit;
        }

        private sealed class PawnReferenceComparer : IEqualityComparer<Pawn>
        {
            internal static readonly PawnReferenceComparer Instance = new PawnReferenceComparer();

            public bool Equals(Pawn left, Pawn right) { return ReferenceEquals(left, right); }
            public int GetHashCode(Pawn pawn) { return RuntimeHelpers.GetHashCode(pawn); }
        }

        [ThreadStatic] private static int drawDepth;
        [ThreadStatic] private static bool cacheEnabled;
        [ThreadStatic] private static Dictionary<Pawn, PsychicStats> byPawn;

        internal static DrawScope EnterDraw()
        {
            var previous = new DrawScope { Entered = true, Depth = drawDepth, CacheEnabled = cacheEnabled };
            Event guiEvent = Event.current;
            // Input runs native stat reads. Nested draws stay independent of the outer cache.
            cacheEnabled = drawDepth == 0 && guiEvent != null
                && (guiEvent.type == EventType.Layout || guiEvent.type == EventType.Repaint);
            drawDepth++;
            return previous;
        }

        internal static void ExitDraw(DrawScope previous)
        {
            if (!previous.Entered) return;
            if (previous.Depth == 0) byPawn?.Clear();
            drawDepth = previous.Depth;
            cacheEnabled = previous.CacheEnabled;
        }

        internal static float ReadGain(Thing thing, StatDef stat, bool applyPostProcess, int cacheStaleAfterTicks)
        {
            Pawn pawn = thing as Pawn;
            if (!cacheEnabled || pawn == null || !pawn.Spawned || stat != StatDefOf.PsychicEntropyGain
                || !applyPostProcess || cacheStaleAfterTicks != -1)
                return thing.GetStatValue(stat, applyPostProcess, cacheStaleAfterTicks);

            if (byPawn == null) byPawn = new Dictionary<Pawn, PsychicStats>(PawnReferenceComparer.Instance);
            if (byPawn.TryGetValue(pawn, out PsychicStats current) && current.HasGain) return current.Gain;

            float gain = thing.GetStatValue(stat, applyPostProcess, cacheStaleAfterTicks);
            byPawn.TryGetValue(pawn, out current);
            current.HasGain = true;
            current.Gain = gain;
            byPawn[pawn] = current;
            return gain;
        }

        internal static float ReadLimit(Pawn_PsychicEntropyTracker tracker)
        {
            if (!cacheEnabled) return tracker.MaxEntropy;
            Pawn pawn = tracker.Pawn;
            if (pawn == null || !pawn.Spawned) return tracker.MaxEntropy;

            if (byPawn == null) byPawn = new Dictionary<Pawn, PsychicStats>(PawnReferenceComparer.Instance);
            if (byPawn.TryGetValue(pawn, out PsychicStats current) && current.LimitTracker == tracker) return current.Limit;

            float limit = tracker.MaxEntropy;
            byPawn.TryGetValue(pawn, out current);
            current.LimitTracker = tracker;
            current.Limit = limit;
            byPawn[pawn] = current;
            return limit;
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
    internal static class Patch_PsycastUiStatDrawScope
    {
        public static void Prefix(out SharedPsycastUiStats.DrawScope __state)
        {
            __state = SharedPsycastUiStats.EnterDraw();
        }

        public static void Finalizer(SharedPsycastUiStats.DrawScope __state)
        {
            SharedPsycastUiStats.ExitDraw(__state);
        }
    }

    [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.WouldOverflowEntropy))]
    internal static class Patch_PsycastUiStatReads
    {
        [HarmonyPriority(Priority.Last)]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo gain = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue),
                new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
            MethodInfo limit = AccessTools.PropertyGetter(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.MaxEntropy));
            if (gain == null || limit == null) return code;

            int gainIndex = code.FindIndex(instruction => instruction.Calls(gain));
            int limitIndex = code.FindIndex(instruction => instruction.Calls(limit));
            if (gainIndex < 0 || limitIndex <= gainIndex
                || code.FindIndex(gainIndex + 1, instruction => instruction.Calls(gain)) >= 0
                || code.FindIndex(limitIndex + 1, instruction => instruction.Calls(limit)) >= 0) return code;

            // Keep the limiter flag, queued cost and current heat in the native check.
            code[gainIndex].opcode = OpCodes.Call;
            code[gainIndex].operand = AccessTools.Method(typeof(SharedPsycastUiStats), nameof(SharedPsycastUiStats.ReadGain));
            code[limitIndex].opcode = OpCodes.Call;
            code[limitIndex].operand = AccessTools.Method(typeof(SharedPsycastUiStats), nameof(SharedPsycastUiStats.ReadLimit));
            return code;
        }
    }
}
