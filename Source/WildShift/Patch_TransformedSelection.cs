using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch]
    public static class Patch_TransformedDragSelection
    {
        // Patch only the native drag-box priority predicate. Keep the original
        // rectangle, portrait, Shift-add, ordering and selection-limit logic.
        public static MethodInfo FindTarget()
        {
            MethodInfo found = null;
            foreach (MethodInfo method in typeof(Selector).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!method.Name.StartsWith("<SelectInsideDragBox>g__IsColonist|", StringComparison.Ordinal)
                    || method.ReturnType != typeof(bool)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Thing)) continue;
                if (found != null) return null;
                found = method;
            }
            return found;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo target = FindTarget();
            if (target != null) yield return target;
            else Log.Warning("[WildShift] Native drag-selection predicate was not found; mixed map selection is unavailable on this game build.");
        }

        public static void Postfix(Thing __0, ref bool __result)
        {
            if (__result || !(__0 is Pawn pawn) || pawn.Faction != Faction.OfPlayer
                || pawn.RaceProps == null || !pawn.RaceProps.Animal) return;
            __result = TransformUtility.TryGetTransformedComp(pawn)?.HasStoredPawn == true;
        }
    }
}
