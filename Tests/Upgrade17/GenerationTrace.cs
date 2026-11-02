using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

namespace WildShiftUpgradeValidation
{
    // TEST ONLY: observe native generation without replacing any request,
    // result, relation, RNG or production transformation method.
    public sealed class UpgradeProbeMod : Mod
    {
        public UpgradeProbeMod(ModContentPack pack) : base(pack)
        {
            new Harmony("wildshift.tests.upgrade.generation-observer").Patch(
                AccessTools.Method(typeof(PawnGenerator), "GeneratePawn", new[] { typeof(PawnGenerationRequest) }),
                postfix: new HarmonyMethod(typeof(GenerationTrace), nameof(GenerationTrace.Record)));
        }
    }

    public static class GenerationTrace
    {
        private static readonly List<XElement> entries = new List<XElement>();
        public static void Record(PawnGenerationRequest request, Pawn __result)
        {
            if (__result == null) return;
            lock (entries)
                entries.Add(new XElement("Generation", new XAttribute("id", __result.GetUniqueLoadID()),
                    new XAttribute("requestedKind", request.KindDef?.defName ?? ""),
                    new XAttribute("actualDef", __result.def?.defName ?? ""),
                    new XAttribute("dead", __result.Dead),
                    new XElement("Stack", Environment.StackTrace)));
        }

        public static void Save(string path)
        {
            lock (entries)
                new XDocument(new XElement("NativeGenerations", entries.Select(e => new XElement(e)))).Save(path);
        }
    }
}
