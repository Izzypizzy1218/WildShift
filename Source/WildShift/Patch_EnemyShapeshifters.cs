using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WildShift
{
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryGenerateRaidInfo")]
    public static class Patch_EnemyRaidAssignment
    {
        public static void Postfix(IncidentWorker_Raid __instance, bool __result, List<Pawn> pawns, bool debugTest)
        {
            if (!__result || debugTest || !(__instance is IncidentWorker_RaidEnemy) || pawns == null) return;
            float chance = WildShiftMod.Settings != null ? WildShiftMod.Settings.enemyShapeshifterChance : 0.05f;
            foreach (Pawn pawn in pawns)
            {
                if (!EnemyShapeshifterUtility.IsHostile(pawn) || !pawn.RaceProps.Humanlike) continue;
                // Quest identity signals may refer to a particular human body;
                // leave those scripted actors intact until separately supported.
                if (pawn.questTags != null && pawn.questTags.Count > 0) continue;
                HediffComp_Shapeshifter comp = TransformUtility.TryGetShapeshifterComp(pawn);
                if (comp == null && chance > 0f && Rand.Chance(chance))
                    comp = TransformUtility.AddOrGetShapeshifter(pawn, null, true);
                if (comp == null) continue;
                comp.enemyRaidParticipant = true;
                comp.enemyShiftUsed = false;
            }
        }
    }

    [HarmonyPatch(typeof(LordToil_AssaultColony), "UpdateAllDuties")]
    public static class Patch_EnemyAssaultStart
    {
        public static void Postfix(LordToil_AssaultColony __instance)
        {
            Lord lord = __instance.lord;
            if (lord == null || lord.Map == null) return;
            MapComponent_EnemyShapeshifters queue = MapComponent_EnemyShapeshifters.For(lord.Map);
            if (queue == null) return;
            foreach (Pawn pawn in lord.ownedPawns) queue.QueueShift(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), "SpawnSetup")]
    public static class Patch_EnemyArrival
    {
        public static void Postfix(Pawn __instance)
        {
            // Drop-pod arrivals can receive their Lord before they are spawned.
            // Retry eligibility on arrival, not by polling all unspawned pawns.
            if (__instance.Spawned && __instance.lord != null
                && __instance.RaceProps.Humanlike)
                MapComponent_EnemyShapeshifters.For(__instance.Map)?.QueueShift(__instance);
        }
    }

    [HarmonyPatch(typeof(Map), "MapPostTick")]
    public static class Patch_EnemyShiftSafePoint
    {
        public static void Prefix(Map __instance)
        {
            // Prefix exceptions occur outside native MapPostTick's own guards.
            // A compatibility fault here must not abort power/plants/Lord ticks.
            try { MapComponent_EnemyShapeshifters.For(__instance)?.ProcessPending(); }
            catch (System.Exception ex) { Log.ErrorOnce("[WildShift] Enemy safe-point processing failed: " + ex, 81729173); }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "CheckForStateChange")]
    public static class Patch_EnemyDowningChance
    {
        public static void Prefix(Pawn_HealthTracker __instance, Pawn ___pawn, out float? __state)
        {
            __state = null;
            if (!EnemyShapeshifterUtility.IsCombatForm(___pawn)) return;
            // Bypass only the random enemy death-on-downing roll. Missing vital
            // organs/lethal damage still use the existing Pawn.Kill handling.
            __state = __instance.overrideDeathOnDownedChance;
            __instance.overrideDeathOnDownedChance = 0f;
        }

        public static void Finalizer(Pawn_HealthTracker __instance, float? __state)
        {
            if (__state.HasValue) __instance.overrideDeathOnDownedChance = __state.Value;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_EnemyDownedReturn
    {
        public static void Prefix(Pawn ___pawn)
        {
            if (___pawn.Downed || !EnemyShapeshifterUtility.IsCombatForm(___pawn)) return;
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(___pawn);
            comp.enemyReturnPending = true;
            MapComponent_EnemyShapeshifters.For(___pawn.Map)?.QueueReturn(___pawn);
        }
    }

    [HarmonyPatch(typeof(Lord), "Notify_PawnLost")]
    public static class Patch_EnemyPendingRaidLoss
    {
        public static bool Prefix(Pawn pawn, PawnLostCondition cond)
        {
            return cond != PawnLostCondition.Incapped || !EnemyShapeshifterUtility.IsReturnPending(pawn);
        }
    }

    [HarmonyPatch(typeof(Lord), "Notify_PawnDowned")]
    public static class Patch_EnemyPendingRaidDowned
    {
        public static bool Prefix(Pawn pawn)
        {
            return !EnemyShapeshifterUtility.IsReturnPending(pawn);
        }
    }
}
