using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WildShift
{
    internal static class TransformedCaravanOwnerUtility
    {
        public static bool IsLivingFormOwner(Caravan caravan, Pawn form)
        {
            // Every caller iterates this caravan's actual members. Avoid a
            // redundant linear ContainsPawn lookup for each animal checked.
            if (caravan.Faction != Faction.OfPlayer || form == null
                || form.Dead || form.Destroyed || form.Faction != Faction.OfPlayer
                || form.HostFaction != null || form.RaceProps == null || !form.RaceProps.Animal)
            {
                return false;
            }

            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(form);
            Pawn human = comp != null ? comp.StoredPawn : null;
            // Native ownership retains all faction, host, slave and mutant
            // restrictions. Global animal ownership/trade rules stay native.
            return human != null && !human.Dead && !human.Destroyed
                && CaravanUtility.IsOwner(human, caravan.Faction);
        }
    }

    [HarmonyPatch(typeof(Caravan), "Notify_MemberDied")]
    public static class Patch_TransformedCaravanMemberDied
    {
        public static bool Prefix(Caravan __instance, Pawn member)
        {
            if (!__instance.Spawned || __instance.Faction != Faction.OfPlayer) return true;

            bool hasLivingFormOwner = false;
            var pawns = __instance.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (candidate == member) continue;
                // Let the original method handle every case it already knows.
                if (__instance.IsOwner(candidate)) return true;
                if (TransformedCaravanOwnerUtility.IsLivingFormOwner(__instance, candidate))
                    hasLivingFormOwner = true;
            }
            if (!hasLivingFormOwner) return true;

            // This is the native surviving-owner branch in both 1.6 and 1.7.
            // Only its missed human-backed animal case needs this substitution.
            member.Strip();
            __instance.RemovePawn(member);
            return false;
        }
    }

    [HarmonyPatch(typeof(Caravan), "get_AllOwnersDowned")]
    public static class Patch_TransformedCaravanOwnersDowned
    {
        public static void Postfix(Caravan __instance, ref bool __result)
        {
            if (!__result || __instance.Faction != Faction.OfPlayer) return;
            var pawns = __instance.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn form = pawns[i];
                if (TransformedCaravanOwnerUtility.IsLivingFormOwner(__instance, form) && !form.Downed)
                {
                    __result = false;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), "get_AllOwnersHaveMentalBreak")]
    public static class Patch_TransformedCaravanOwnersMentalBreak
    {
        public static void Postfix(Caravan __instance, ref bool __result)
        {
            if (!__result || __instance.Faction != Faction.OfPlayer) return;
            var pawns = __instance.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn form = pawns[i];
                if (TransformedCaravanOwnerUtility.IsLivingFormOwner(__instance, form) && !form.InMentalState)
                {
                    __result = false;
                    return;
                }
            }
        }
    }
}
