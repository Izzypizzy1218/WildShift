using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(StockGenerator_Animals), "GenerateThings")]
    public static class Patch_TraderShapeshifterTestMode
    {
        public static void Postfix(ref IEnumerable<Thing> __result)
        {
            if (WildShiftMod.Settings != null && WildShiftMod.Settings.forceTraderShapeshifters)
                __result = MarkGeneratedStock(__result);
        }

        private static IEnumerable<Thing> MarkGeneratedStock(IEnumerable<Thing> stock)
        {
            // GenerateThings is lazy: mark each generated animal before handing
            // it to the trader, preserving enumeration order and stock quantity.
            if (stock == null) yield break;
            foreach (Thing thing in stock)
            {
                Pawn animal = thing as Pawn;
                Faction player = Find.FactionManager != null ? Find.FactionManager.OfPlayer : null;
                if (WildShiftMod.Settings != null && WildShiftMod.Settings.forceTraderShapeshifters
                    && player != null && animal != null && !animal.Dead && !animal.Destroyed
                    && animal.Faction != player && AnimalPool.IsEligible(animal)
                    && !TransformUtility.IsTransformedAnimal(animal) && animal.health != null
                    && animal.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker) == null)
                {
                    animal.health.AddHediff(HediffMaker.MakeHediff(WildShiftDefOf.WildShift_FieldShapeshifterMarker, animal));
                }
                yield return thing;
            }
        }
    }
}
