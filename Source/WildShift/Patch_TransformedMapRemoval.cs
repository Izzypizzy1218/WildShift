using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Temporary sites retire their maps when this native predicate finds no
    // colony pawn. The preserved human is held by a HediffComp, outside the
    // native Pawn child-holder traversal, so its active form must stand in.
    [HarmonyPatch(typeof(MapPawns), "IsValidColonyPawn")]
    public static class Patch_TransformedMapRemoval
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null || pawn.Dead || pawn.Destroyed
                || pawn.Faction != Faction.OfPlayer || pawn.HostFaction != null
                || pawn.RaceProps == null || !pawn.RaceProps.Animal)
            {
                return;
            }

            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(pawn);
            Pawn human = comp != null ? comp.StoredPawn : null;
            // Downed and mentally broken colonists still retain their map in
            // the native rule. Preserve that behavior for their animal forms.
            if (human != null && !human.Dead && !human.Destroyed
                && human.Faction == Faction.OfPlayer && human.IsColonist)
            {
                __result = true;
            }
        }
    }
}
