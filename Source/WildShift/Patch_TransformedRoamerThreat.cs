using HarmonyLib;
using Verse;

namespace WildShift
{
    // Native 1.6/1.7 Pawn.ThreatDisabledBecauseNonAggressiveRoamer(Pawn)
    // recognizes combat jobs, but does not recognize drafting itself. A newly
    // drafted herd-animal form can therefore be ignored until its first native
    // Wait_Combat job begins. Its preserved person is already an active fighter.
    // Change only this roamer exemption; the outer ThreatDisabled method keeps
    // every other native gate, including downing, dormancy and invisibility.
    [HarmonyPatch(typeof(Pawn), "ThreatDisabledBecauseNonAggressiveRoamer", new[] { typeof(Pawn) })]
    public static class Patch_TransformedRoamerThreat
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!__result || __instance == null || __instance.Destroyed || __instance.Dead
                || !__instance.Spawned || __instance.health == null
                || __instance.Faction?.IsPlayer != true || __instance.drafter?.Drafted != true
                || __instance.MentalStateDef != null || SharedFormIdentity.HumanFor(__instance) == null)
                return;

            __result = false;
        }
    }
}
