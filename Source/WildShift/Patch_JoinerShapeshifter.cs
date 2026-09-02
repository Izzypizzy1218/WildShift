using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Normal colony joins, rather than a separate WildShift incident, have a small chance to reveal a shapeshifter.
    public static class JoinerShapeshifterUtility
    {
        public const float JoinerShapeshifterChance = 0.05f;

        public static void TryAssign(Pawn pawn)
        {
            if (pawn == null
                || pawn.Dead
                || pawn.RaceProps == null
                || !pawn.RaceProps.Humanlike
                || pawn.Faction != Faction.OfPlayer
                || pawn.IsPrisonerOfColony
                || pawn.IsSlaveOfColony
                || TransformUtility.IsShapeshifter(pawn)
                || TransformUtility.IsTransformedAnimal(pawn)
                || !Rand.Chance(JoinerShapeshifterChance))
            {
                return;
            }

            PawnKindDef animalKind = AnimalPool.RandomEligibleKind();
            if (animalKind != null)
            {
                TransformUtility.AddOrGetShapeshifter(pawn, animalKind);
            }
        }
    }

    // Covers the standard storyteller "wanderer joins" incidents.
    [HarmonyPatch(typeof(IncidentWorker_WandererJoin), "GeneratePawn")]
    public static class Patch_WandererJoinShapeshifter
    {
        public static void Postfix(Pawn __result)
        {
            JoinerShapeshifterUtility.TryAssign(__result);
        }
    }

    // Covers prisoner recruitment and other normal human recruitment flows.
    [HarmonyPatch(typeof(RecruitUtility), "Recruit")]
    public static class Patch_RecruitedJoinerShapeshifter
    {
        public static void Postfix(Pawn pawn)
        {
            JoinerShapeshifterUtility.TryAssign(pawn);
        }
    }

    // Some quest rewards join by setting their faction directly instead of using RecruitUtility.
    [HarmonyPatch(typeof(QuestPart_JoinPlayer), "Notify_QuestSignalReceived")]
    public static class Patch_QuestJoinerShapeshifter
    {
        public static void Prefix(QuestPart_JoinPlayer __instance, Signal signal, out List<Pawn> __state)
        {
            __state = null;
            if (__instance == null
                || !__instance.joinPlayer
                || signal.tag != __instance.inSignal
                || __instance.pawns == null)
            {
                return;
            }

            // QuestPart_JoinPlayer uses RecruitUtility for existing colonists,
            // prisoners, and slaves. Record only its direct SetFaction branch so
            // every joining pawn receives exactly one five-percent roll.
            __state = new List<Pawn>();
            for (int i = 0; i < __instance.pawns.Count; i++)
            {
                Pawn pawn = __instance.pawns[i];
                if (pawn != null
                    && !pawn.IsColonist
                    && !pawn.IsPrisonerOfColony
                    && !pawn.IsSlaveOfColony
                    && pawn.Faction != Faction.OfPlayer)
                {
                    __state.Add(pawn);
                }
            }
        }

        public static void Postfix(List<Pawn> __state)
        {
            if (__state == null)
            {
                return;
            }

            for (int i = 0; i < __state.Count; i++)
            {
                JoinerShapeshifterUtility.TryAssign(__state[i]);
            }
        }
    }
}
