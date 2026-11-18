using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShift
{
    // The native row first measures its condition groups, then draws them.
    // Reuse work only within that invocation, with no cache between UI events.
    internal static class HealthCardRowLayout
    {
        internal static bool enabled = true;
        private const int MinimumHediffs = 24;
        private static readonly MethodInfo CalcHeight = AccessTools.DeclaredMethod(typeof(Text),
            nameof(Text.CalcHeight), new[] { typeof(string), typeof(float) });
        private static readonly MethodInfo FontStyle = AccessTools.PropertyGetter(typeof(Text), nameof(Text.CurFontStyle));
        [ThreadStatic] private static Listing current;

        internal struct Listing
        {
            internal Pawn Pawn;
            internal bool ReuseHeights;
        }

        private struct Measurement
        {
            internal string Label;
            internal float Width;
            internal float Height;
            internal GameFont Font;
            internal TextAnchor Anchor;
            internal bool WordWrap;

            internal bool Matches(string label, float width)
            {
                return Width == width && Font == Text.Font && Anchor == Text.Anchor
                    && WordWrap == Text.WordWrap && string.Equals(Label, label, StringComparison.Ordinal);
            }
        }

        private struct RowLayout
        {
            internal IEnumerable<Hediff> Source;
            internal ILookup<int, Hediff> Groups;
            internal bool ReuseHeights;
            internal Measurement First;
            internal Measurement[] Rest;
            internal int Measured;
            internal int Drawn;
        }

        internal static Listing Enter(Pawn pawn)
        {
            Listing previous = current;
            var next = default(Listing);
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (enabled && hediffs != null && hediffs.Count >= MinimumHediffs
                && (TransformUtility.TryGetShapeshifterComp(pawn) != null || SharedFormIdentity.HumanFor(pawn) != null))
            {
                next.Pawn = pawn;
                // Let other mods run their measurement callbacks for every
                // native lookup if they patched this method.
                next.ReuseHeights = CalcHeight != null && FontStyle != null
                    && Harmony.GetPatchInfo(CalcHeight) == null && Harmony.GetPatchInfo(FontStyle) == null;
            }
            current = next;
            return previous;
        }

        internal static void Leave(Listing previous)
        {
            current = previous;
        }

        private static IEnumerable<IGrouping<int, Hediff>> Group(IEnumerable<Hediff> hediffs,
            Func<Hediff, int> keySelector, ref RowLayout layout, Pawn pawn)
        {
            if (pawn == null || !ReferenceEquals(current.Pawn, pawn)) return hediffs.GroupBy(keySelector);
            var groups = hediffs.ToLookup(keySelector);
            layout.Source = hediffs;
            layout.Groups = groups;
            layout.ReuseHeights = current.ReuseHeights;
            if (layout.ReuseHeights && groups.Count > 1) layout.Rest = new Measurement[groups.Count - 1];
            return groups;
        }

        private static IEnumerable<IGrouping<int, Hediff>> ReuseGroups(IEnumerable<Hediff> hediffs,
            Func<Hediff, int> keySelector, ref RowLayout layout)
        {
            if (layout.Groups != null && ReferenceEquals(layout.Source, hediffs)) return layout.Groups;
            layout.ReuseHeights = false;
            return hediffs.GroupBy(keySelector);
        }

        private static float MeasureHeight(string label, float width, ref RowLayout layout)
        {
            float height = Text.CalcHeight(label, width);
            if (!layout.ReuseHeights || layout.Measured >= layout.Groups.Count) return height;

            var measurement = new Measurement
            {
                Label = label, Width = width, Height = height, Font = Text.Font,
                Anchor = Text.Anchor, WordWrap = Text.WordWrap
            };
            int index = layout.Measured++;
            if (index == 0) layout.First = measurement;
            else layout.Rest[index - 1] = measurement;
            return height;
        }

        private static float ReuseHeight(string label, float width, ref RowLayout layout)
        {
            if (layout.ReuseHeights && layout.Drawn < layout.Measured)
            {
                int index = layout.Drawn++;
                Measurement measurement = index == 0 ? layout.First : layout.Rest[index - 1];
                if (measurement.Matches(label, width)) return measurement.Height;
            }
            return Text.CalcHeight(label, width);
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            if (CalcHeight == null) return code;
            var groups = new List<int>();
            var heights = new List<int>();
            int partCondition = -1;
            var conditionLabel = AccessTools.DeclaredMethod(typeof(HealthUtility), nameof(HealthUtility.GetPartConditionLabel));
            for (int i = 0; i < code.Count; i++)
            {
                if (IsNativeGrouping(code[i])) groups.Add(i);
                if (code[i].Calls(CalcHeight)) heights.Add(i);
                if (code[i].Calls(conditionLabel)) partCondition = i;
            }

            // Both supported engines use this shape. A different row or a
            // foreign transpiler changing these operations keeps its own IL.
            if (groups.Count != 2 || heights.Count != 4 || heights[1] >= groups[0]
                || groups[0] >= heights[2] || heights[2] >= partCondition
                || partCondition >= groups[1] || groups[1] >= heights[3]
                || !EquivalentSelectors(code, groups[0], groups[1])
                || code[groups[0]].blocks.Count != 0 || code[groups[1]].blocks.Count != 0
                || code[heights[2]].blocks.Count != 0 || code[heights[3]].blocks.Count != 0) return code;

            var layout = generator.DeclareLocal(typeof(RowLayout));
            var rewritten = new List<CodeInstruction>(code.Count + 5);
            for (int i = 0; i < code.Count; i++)
            {
                string helper = null;
                if (i == groups[0]) helper = nameof(Group);
                else if (i == groups[1]) helper = nameof(ReuseGroups);
                else if (i == heights[2]) helper = nameof(MeasureHeight);
                else if (i == heights[3]) helper = nameof(ReuseHeight);
                if (helper == null)
                {
                    rewritten.Add(code[i]);
                    continue;
                }
                var load = new CodeInstruction(OpCodes.Ldloca, layout);
                load.labels.AddRange(code[i].labels);
                rewritten.Add(load);
                if (i == groups[0]) rewritten.Add(new CodeInstruction(OpCodes.Ldarg_1));
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(HealthCardRowLayout), helper)));
            }
            return rewritten;
        }

        private static bool IsNativeGrouping(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call || !(instruction.operand is MethodInfo method)
                || method.DeclaringType != typeof(Enumerable) || method.Name != nameof(Enumerable.GroupBy)
                || method.ReturnType != typeof(IEnumerable<IGrouping<int, Hediff>>)) return false;
            var arguments = method.GetParameters();
            return arguments.Length == 2 && arguments[0].ParameterType == typeof(IEnumerable<Hediff>)
                && arguments[1].ParameterType == typeof(Func<Hediff, int>);
        }

        private static bool EquivalentSelectors(List<CodeInstruction> code, int first, int second)
        {
            MethodInfo a = ReadSelector(code, first);
            MethodInfo b = ReadSelector(code, second);
            return a != null && b != null && a.DeclaringType == b.DeclaringType
                && a.GetMethodBody().GetILAsByteArray().SequenceEqual(b.GetMethodBody().GetILAsByteArray());
        }

        private static MethodInfo ReadSelector(List<CodeInstruction> code, int call)
        {
            int load = call - 4;
            if (load < 0 || code[load].opcode != OpCodes.Ldftn || !(code[load].operand is MethodInfo method)
                || method.DeclaringType?.DeclaringType != typeof(HealthCardUtility)
                || !method.Name.StartsWith("<DrawHediffRow>", StringComparison.Ordinal)
                || method.GetMethodBody() == null) return null;
            return method;
        }
    }

    [HarmonyPatch(typeof(HealthCardUtility), nameof(HealthCardUtility.DrawHediffListing))]
    internal static class Patch_HealthCardListingScope
    {
        internal struct Scope
        {
            internal bool Applied;
            internal HealthCardRowLayout.Listing Previous;
        }

        public static void Prefix(Pawn pawn, out Scope __state)
        {
            __state = new Scope { Previous = HealthCardRowLayout.Enter(pawn), Applied = true };
        }

        public static void Finalizer(Scope __state)
        {
            if (__state.Applied) HealthCardRowLayout.Leave(__state.Previous);
        }
    }

    [HarmonyPatch(typeof(HealthCardUtility), "DrawHediffRow")]
    internal static class Patch_HealthCardRowLayout
    {
        [HarmonyPriority(Priority.Last)]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            return HealthCardRowLayout.Rewrite(instructions, generator);
        }
    }
}
